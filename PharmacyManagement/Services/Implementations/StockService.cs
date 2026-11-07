using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PharmacyManagement.Data;
using PharmacyManagement.Helpers;
using PharmacyManagement.Models.Enums;
using PharmacyManagement.Models.ViewModels.Catalog;
using PharmacyManagement.Models.ViewModels.Inventory;
using PharmacyManagement.Services.Interfaces;

namespace PharmacyManagement.Services.Implementations;

/// <summary>
/// Tồn kho &amp; lô (DEC-39). Ngưỡng cận hạn khác nhau theo thuốc nên việc phân loại lô làm trong bộ nhớ sau khi lọc thô bằng SQL —
/// quy mô một nhà thuốc (vài trăm thuốc, vài nghìn lô) đọc một lần vẫn nhanh. Không ghi gì vào CSDL ngoài đổi vị trí lô.
/// </summary>
public sealed class StockService : IStockService
{
    private const string ExpiryWarningKey = "ExpiryWarningDays";
    private const int DefaultExpiryWarningDays = 90;
    private const int HistoryPageSize = 20;

    private static readonly CultureInfo Vietnamese = CultureInfo.GetCultureInfo("vi-VN");
    private static readonly StringComparer NameOrder = StringComparer.Create(Vietnamese, ignoreCase: true);

    private readonly PharmacyDbContext _db;
    private readonly IAuditLogService _audit;

    public StockService(PharmacyDbContext db, IAuditLogService audit)
    {
        _db = db;
        _audit = audit;
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    // ================================================================== Cảnh báo (2.5.1, 2.5.2)

    public async Task<StockAlertCounts> GetAlertCountsAsync(CancellationToken ct = default)
    {
        var today = Today;
        var thresholds = await LoadThresholdsAsync(ct);
        var facts = await InStockFactsAsync(null, ct);

        int near = 0, expired = 0, blocked = 0;
        var available = new Dictionary<int, decimal>();
        foreach (var f in facts)
        {
            var condition = Classify(f.Quantity, f.Status, f.ExpiryDate, thresholds.DaysFor(f.MedicineId, f.WarehouseId), today);
            switch (condition)
            {
                case BatchCondition.NearExpiry: near++; break;
                case BatchCondition.Expired: expired++; break;
                case BatchCondition.Blocked: blocked++; break;
            }
            if (IsSellable(condition)) available[f.MedicineId] = available.GetValueOrDefault(f.MedicineId) + f.Quantity;
        }

        var withMinimum = thresholds.Minimum.Where(p => p.Value > 0).Select(p => p.Key).ToList();
        var activeIds = withMinimum.Count == 0
            ? new List<int>()
            : await _db.Medicines.AsNoTracking().Where(m => m.IsActive && withMinimum.Contains(m.MedicineId)).Select(m => m.MedicineId).ToListAsync(ct);
        var below = activeIds.Count(id => available.GetValueOrDefault(id) < thresholds.Minimum[id]);

        return new StockAlertCounts(below, near, expired, blocked);
    }

    // ================================================================== Tồn theo thuốc

    public async Task<StockListViewModel> GetMedicineListAsync(StockFilter filter, CancellationToken ct = default)
    {
        var today = Today;
        var query = _db.Medicines.AsNoTracking().AsQueryable();

        filter.Show = filter.Show == "all" ? "all" : null;
        if (filter.Show is null) query = query.Where(m => m.IsActive);
        if (filter.CategoryId is not null) query = query.Where(m => m.CategoryId == filter.CategoryId);
        filter.Status = filter.Status is "below" or "out" or "near" or "expired" or "blocked" ? filter.Status : null;

        var q = AccountHelpers.NormalizeFullName(filter.Q);
        filter.Q = q.Length == 0 ? null : q;
        if (filter.Q is not null)
        {
            var text = filter.Q;
            var folded = SearchText.Fold(text);
            query = query.Where(m => m.MedicineCode.Contains(text)
                                     || (m.Barcode != null && m.Barcode.Contains(text))
                                     || EF.Functions.Collate(m.MedicineName.Replace("đ", "d").Replace("Đ", "D"), SearchText.Collation).Contains(folded)
                                     || m.MedicineIngredients.Any(i => EF.Functions.Collate(i.Ingredient.IngredientName.Replace("đ", "d").Replace("Đ", "D"), SearchText.Collation).Contains(folded)));
        }

        var medicines = await query
            .Select(m => new { m.MedicineId, m.MedicineCode, m.MedicineName, BaseUnit = m.BaseUnit.UnitName, m.IsActive, m.IsPrescription })
            .ToListAsync(ct);

        var thresholds = await LoadThresholdsAsync(ct);
        var summaries = Summarize(await InStockFactsAsync(null, ct), thresholds, today);
        var onOrder = await OnOrderAsync(null, ct);

        var all = medicines.Select(m =>
        {
            var s = summaries.GetValueOrDefault(m.MedicineId) ?? Summary.Empty;
            var minimum = thresholds.Minimum.GetValueOrDefault(m.MedicineId);
            return new
            {
                m.MedicineId, m.MedicineCode, m.MedicineName, m.BaseUnit, m.IsActive, m.IsPrescription, Summary = s, Minimum = minimum,
                Below = minimum > 0 && s.Available < minimum
            };
        });

        all = filter.Status switch
        {
            "below" => all.Where(r => r.Below),
            "out" => all.Where(r => r.Summary.Available == 0),
            "near" => all.Where(r => r.Summary.HasNearExpiry),
            "expired" => all.Where(r => r.Summary.HasExpired),
            "blocked" => all.Where(r => r.Summary.HasBlocked),
            _ => all
        };

        var ordered = filter.Q is null
            ? all.OrderBy(r => r.MedicineName, NameOrder).ToList()
            : all.OrderByDescending(r => StartsWithFolded(r.MedicineName, filter.Q)).ThenBy(r => r.MedicineName, NameOrder).ToList();

        var total = ordered.Count;
        var pageCount = Math.Max(1, (int)Math.Ceiling(total / (double)StockFilter.PageSize));
        filter.Page = Math.Clamp(filter.Page, 1, pageCount);
        var page = ordered.Skip((filter.Page - 1) * StockFilter.PageSize).Take(StockFilter.PageSize).ToList();

        var pageIds = page.Select(r => r.MedicineId).ToList();
        var ingredients = (await _db.MedicineIngredients.AsNoTracking()
                .Where(i => pageIds.Contains(i.MedicineId))
                .Select(i => new { i.MedicineId, i.Ingredient.IngredientName })
                .ToListAsync(ct))
            .GroupBy(i => i.MedicineId)
            .ToDictionary(g => g.Key, g => string.Join(", ", g.Select(i => i.IngredientName).OrderBy(n => n, NameOrder)));
        var largest = await LargestUnitsAsync(pageIds, ct);

        var rows = page.Select(r => new StockMedicineRow(
                r.MedicineId, r.MedicineCode, r.MedicineName, ingredients.GetValueOrDefault(r.MedicineId), r.BaseUnit, r.IsActive, r.IsPrescription,
                r.Summary.Available, StockText.Breakdown(r.Summary.Available, r.BaseUnit, largest.GetValueOrDefault(r.MedicineId)),
                r.Summary.Unsellable, r.Summary.BatchCount, r.Summary.NearestExpiry,
                r.Summary.NearestExpiry is null ? null : r.Summary.NearestExpiry.Value.DayNumber - today.DayNumber,
                r.Minimum, onOrder.GetValueOrDefault(r.MedicineId), r.Below,
                r.Summary.HasNearExpiry, r.Summary.HasExpired, r.Summary.HasBlocked))
            .ToList();

        return new StockListViewModel
        {
            Filter = filter,
            Rows = rows,
            Total = total,
            GrandTotal = await _db.Medicines.CountAsync(ct),
            PageCount = pageCount,
            Categories = await _db.Categories.AsNoTracking().OrderBy(c => c.CategoryName)
                .Select(c => new LookupItem(c.CategoryId, c.CategoryName)).ToListAsync(ct),
            Alerts = await GetAlertCountsAsync(ct),
            WarningDays = thresholds.DefaultDays
        };
    }

    // ================================================================== Tồn theo lô

    public async Task<BatchListViewModel> GetBatchListAsync(BatchFilter filter, CancellationToken ct = default)
    {
        var today = Today;
        var thresholds = await LoadThresholdsAsync(ct);
        var query = _db.MedicineBatches.AsNoTracking().AsQueryable();

        filter.Show = filter.Show == "all" ? "all" : null;
        if (filter.Show is null) query = query.Where(b => b.QuantityOnHand > 0);
        if (filter.MedicineId is not null) query = query.Where(b => b.MedicineId == filter.MedicineId);
        if (filter.LocationId is not null)
        {
            var locationId = filter.LocationId;
            query = query.Where(b => b.LocationId == locationId || (b.Location != null && b.Location.ParentLocationId == locationId));
        }

        var q = AccountHelpers.NormalizeFullName(filter.Q);
        filter.Q = q.Length == 0 ? null : q;
        if (filter.Q is not null)
        {
            var text = filter.Q;
            var folded = SearchText.Fold(text);
            query = query.Where(b => b.BatchNumber.Contains(text)
                                     || b.Medicine.MedicineCode.Contains(text)
                                     || EF.Functions.Collate(b.Medicine.MedicineName.Replace("đ", "d").Replace("Đ", "D"), SearchText.Collation).Contains(folded));
        }

        // Lọc thô bằng SQL; ngưỡng cận hạn chính xác theo từng thuốc xét lại bên dưới
        filter.Status = filter.Status is "sellable" or "near" or "expired" or "blocked" ? filter.Status : null;
        var available = (byte)BatchStatus.Available;
        var nearLimit = today.AddDays(thresholds.MaxDays);
        query = filter.Status switch
        {
            "sellable" => query.Where(b => b.QuantityOnHand > 0 && b.Status == available && b.ExpiryDate > today),
            "near" => query.Where(b => b.QuantityOnHand > 0 && b.Status == available && b.ExpiryDate > today && b.ExpiryDate <= nearLimit),
            "expired" => query.Where(b => b.QuantityOnHand > 0 && b.ExpiryDate <= today),
            "blocked" => query.Where(b => b.QuantityOnHand > 0 && b.Status != available && b.ExpiryDate > today),
            _ => query
        };

        var items = await query
            .Select(b => new
            {
                b.BatchId, b.MedicineId, b.Medicine.MedicineCode, b.Medicine.MedicineName, BaseUnit = b.Medicine.BaseUnit.UnitName,
                b.BatchNumber, b.ExpiryDate, b.QuantityOnHand, b.Status, b.WarehouseId, b.ReceivedDate, b.GoodsReceiptId,
                LocationName = b.Location != null ? b.Location.LocationName : null,
                ParentName = b.Location != null && b.Location.ParentLocation != null ? b.Location.ParentLocation.LocationName : null,
                ReceiptCode = b.GoodsReceipt != null ? b.GoodsReceipt.GoodsReceiptCode : null,
                SupplierName = b.GoodsReceipt != null ? b.GoodsReceipt.Supplier.SupplierName : null
            })
            .ToListAsync(ct);

        var classified = items
            .Select(b => new
            {
                Item = b,
                Condition = Classify(b.QuantityOnHand, b.Status, b.ExpiryDate, thresholds.DaysFor(b.MedicineId, b.WarehouseId), today)
            })
            .Where(x => filter.Status switch
            {
                "sellable" => IsSellable(x.Condition),
                "near" => x.Condition == BatchCondition.NearExpiry,
                "expired" => x.Condition == BatchCondition.Expired,
                "blocked" => x.Condition == BatchCondition.Blocked,
                _ => true
            })
            .OrderBy(x => x.Item.ExpiryDate).ThenBy(x => x.Item.BatchId)
            .ToList();

        var total = classified.Count;
        var pageCount = Math.Max(1, (int)Math.Ceiling(total / (double)BatchFilter.PageSize));
        filter.Page = Math.Clamp(filter.Page, 1, pageCount);
        var page = classified.Skip((filter.Page - 1) * BatchFilter.PageSize).Take(BatchFilter.PageSize).ToList();
        var largest = await LargestUnitsAsync(page.Select(x => x.Item.MedicineId).Distinct().ToList(), ct);

        var rows = page.Select(x => new StockBatchRow(
                x.Item.BatchId, x.Item.MedicineId, x.Item.MedicineCode, x.Item.MedicineName, x.Item.BatchNumber, x.Item.ExpiryDate,
                x.Item.ExpiryDate.DayNumber - today.DayNumber, x.Item.QuantityOnHand, x.Item.BaseUnit,
                StockText.Breakdown(x.Item.QuantityOnHand, x.Item.BaseUnit, largest.GetValueOrDefault(x.Item.MedicineId)),
                LocationLabel(x.Item.ParentName, x.Item.LocationName), x.Item.ReceivedDate, x.Item.GoodsReceiptId, x.Item.ReceiptCode,
                x.Item.SupplierName, x.Item.Status, x.Condition))
            .ToList();

        string? medicineName = null;
        if (filter.MedicineId is not null)
        {
            medicineName = await _db.Medicines.AsNoTracking().Where(m => m.MedicineId == filter.MedicineId)
                .Select(m => m.MedicineName).FirstOrDefaultAsync(ct);
            if (medicineName is null) filter.MedicineId = null;
        }

        return new BatchListViewModel
        {
            Filter = filter,
            Rows = rows,
            Total = total,
            GrandTotal = await _db.MedicineBatches.CountAsync(ct),
            PageCount = pageCount,
            Locations = await LocationOptionsAsync(ct),
            Alerts = await GetAlertCountsAsync(ct),
            WarningDays = thresholds.DefaultDays,
            MedicineName = medicineName
        };
    }

    // ================================================================== Chi tiết thuốc (tra cứu 3.1.1)

    public async Task<StockMedicineViewModel?> GetMedicineAsync(int medicineId, bool includeHistory, int historyPage, CancellationToken ct = default)
    {
        var today = Today;
        var m = await _db.Medicines.AsNoTracking()
            .Where(x => x.MedicineId == medicineId)
            .Select(x => new
            {
                x.MedicineId, x.MedicineCode, x.MedicineName, x.Barcode, x.Category.CategoryName,
                ManufacturerName = x.Manufacturer != null ? x.Manufacturer.ManufacturerName : null,
                x.Strength, x.DosageForm, x.RegistrationNumber, x.IsPrescription, x.IsControlledDrug, x.IsActive,
                x.UsageInstructions, x.Contraindications, x.Interactions, x.DefaultSellingPrice, BaseUnit = x.BaseUnit.UnitName
            })
            .FirstOrDefaultAsync(ct);
        if (m is null) return null;

        var ingredients = await _db.MedicineIngredients.AsNoTracking()
            .Where(i => i.MedicineId == medicineId)
            .OrderBy(i => i.Ingredient.IngredientName)
            .Select(i => new StockIngredient(i.Ingredient.IngredientName, i.IngredientStrength))
            .ToListAsync(ct);

        var conversions = await _db.MedicineUnitConversions.AsNoTracking()
            .Where(c => c.MedicineId == medicineId)
            .OrderBy(c => c.ConversionToBase)
            .Select(c => new { c.Unit.UnitName, c.ConversionToBase, c.SellingPrice })
            .ToListAsync(ct);
        var saleUnits = new List<StockSaleUnit> { new(m.BaseUnit, 1m, RoundPrice(m.DefaultSellingPrice), true) };
        saleUnits.AddRange(conversions.Select(c => new StockSaleUnit(c.UnitName, c.ConversionToBase,
            RoundPrice(c.SellingPrice ?? m.DefaultSellingPrice * c.ConversionToBase), false))); // DB-13
        var largest = conversions.Where(c => c.ConversionToBase > 1).OrderByDescending(c => c.ConversionToBase)
            .Select(c => new UnitFactor(c.UnitName, c.ConversionToBase)).FirstOrDefault();

        var thresholds = await LoadThresholdsAsync(ct);
        var batches = await _db.MedicineBatches.AsNoTracking()
            .Where(b => b.MedicineId == medicineId)
            .Select(b => new
            {
                b.BatchId, b.BatchNumber, b.ExpiryDate, b.QuantityOnHand, b.Status, b.WarehouseId, b.ReceivedDate, b.GoodsReceiptId, b.UnitCost,
                LocationName = b.Location != null ? b.Location.LocationName : null,
                ParentName = b.Location != null && b.Location.ParentLocation != null ? b.Location.ParentLocation.LocationName : null,
                ReceiptCode = b.GoodsReceipt != null ? b.GoodsReceipt.GoodsReceiptCode : null
            })
            .ToListAsync(ct);

        // Thứ tự xuất bán: hạn dùng gần trước, cùng hạn thì lô nhập trước
        var inStock = batches.Where(b => b.QuantityOnHand > 0)
            .OrderBy(b => b.ExpiryDate).ThenBy(b => b.BatchId)
            .Select(b => new
            {
                Batch = b,
                Condition = Classify(b.QuantityOnHand, b.Status, b.ExpiryDate, thresholds.DaysFor(medicineId, b.WarehouseId), today)
            })
            .ToList();
        var sellsNextId = inStock.Where(x => IsSellable(x.Condition)).Select(x => (int?)x.Batch.BatchId).FirstOrDefault();
        var lines = inStock.Select(x => new StockBatchLine(
                x.Batch.BatchId, x.Batch.BatchNumber, x.Batch.ExpiryDate, x.Batch.ExpiryDate.DayNumber - today.DayNumber,
                x.Batch.QuantityOnHand, StockText.Breakdown(x.Batch.QuantityOnHand, m.BaseUnit, largest),
                LocationLabel(x.Batch.ParentName, x.Batch.LocationName), x.Batch.ReceivedDate, x.Batch.GoodsReceiptId, x.Batch.ReceiptCode,
                x.Batch.UnitCost, x.Batch.Status, x.Condition, x.Batch.BatchId == sellsNextId))
            .ToList();
        var availableQty = inStock.Where(x => IsSellable(x.Condition)).Sum(x => x.Batch.QuantityOnHand);

        var history = new List<StockLedgerRow>();
        var historyTotal = 0;
        var historyPageCount = 1;
        if (includeHistory)
        {
            var ledger = _db.InventoryTransactions.AsNoTracking().Where(t => t.MedicineId == medicineId);
            historyTotal = await ledger.CountAsync(ct);
            historyPageCount = Math.Max(1, (int)Math.Ceiling(historyTotal / (double)HistoryPageSize));
            historyPage = Math.Clamp(historyPage, 1, historyPageCount);
            var entries = await ledger
                .OrderByDescending(t => t.TransactionDate).ThenByDescending(t => t.InventoryTransactionId)
                .Skip((historyPage - 1) * HistoryPageSize).Take(HistoryPageSize)
                .Select(t => new LedgerEntry(t.InventoryTransactionId, t.TransactionDate, t.TransactionType, t.BatchId, t.Batch.BatchNumber,
                    t.QuantityChange, t.ReferenceType, t.ReferenceId,
                    t.CreatedByEmployee != null ? t.CreatedByEmployee.FullName : null, t.Notes))
                .ToListAsync(ct);
            history = await ToLedgerRowsAsync(entries, null, ct);
        }

        return new StockMedicineViewModel
        {
            MedicineId = m.MedicineId,
            Code = m.MedicineCode,
            Name = m.MedicineName,
            Barcode = m.Barcode,
            CategoryName = m.CategoryName,
            ManufacturerName = m.ManufacturerName,
            Strength = m.Strength,
            DosageForm = m.DosageForm,
            RegistrationNumber = m.RegistrationNumber,
            IsPrescription = m.IsPrescription,
            IsControlledDrug = m.IsControlledDrug,
            IsActive = m.IsActive,
            UsageInstructions = m.UsageInstructions,
            Contraindications = m.Contraindications,
            Interactions = m.Interactions,
            BaseUnit = m.BaseUnit,
            Ingredients = ingredients,
            SaleUnits = saleUnits,
            Available = availableQty,
            AvailableBreakdown = StockText.Breakdown(availableQty, m.BaseUnit, largest),
            Unsellable = inStock.Where(x => !IsSellable(x.Condition)).Sum(x => x.Batch.QuantityOnHand),
            Minimum = thresholds.Minimum.GetValueOrDefault(medicineId),
            OnOrder = (await OnOrderAsync(medicineId, ct)).GetValueOrDefault(medicineId),
            WarningDays = thresholds.DaysForMedicine(medicineId),
            Batches = lines,
            EmptyBatchCount = batches.Count(b => b.QuantityOnHand <= 0),
            History = history,
            HistoryPage = historyPage,
            HistoryPageCount = historyPageCount,
            HistoryTotal = historyTotal
        };
    }

    // ================================================================== Chi tiết lô (2.3.3.2 tra cứu lô đang tồn, đã bán)

    public async Task<StockBatchViewModel?> GetBatchAsync(int batchId, bool includeSales, CancellationToken ct = default)
    {
        var today = Today;
        var b = await _db.MedicineBatches.AsNoTracking()
            .Where(x => x.BatchId == batchId)
            .Select(x => new
            {
                x.BatchId, x.BatchNumber, x.MedicineId, x.Medicine.MedicineCode, x.Medicine.MedicineName, BaseUnit = x.Medicine.BaseUnit.UnitName,
                x.ManufactureDate, x.ExpiryDate, x.ReceivedDate, x.GoodsReceiptId, x.UnitCost, x.WarehouseId, x.Warehouse.WarehouseName,
                x.LocationId, x.Status, x.Notes, x.QuantityOnHand,
                LocationName = x.Location != null ? x.Location.LocationName : null,
                ParentName = x.Location != null && x.Location.ParentLocation != null ? x.Location.ParentLocation.LocationName : null,
                ReceiptCode = x.GoodsReceipt != null ? x.GoodsReceipt.GoodsReceiptCode : null,
                SupplierName = x.GoodsReceipt != null ? x.GoodsReceipt.Supplier.SupplierName : null
            })
            .FirstOrDefaultAsync(ct);
        if (b is null) return null;

        var thresholds = await LoadThresholdsAsync(ct);
        var warningDays = thresholds.DaysFor(b.MedicineId, b.WarehouseId);
        var largest = (await LargestUnitsAsync(new List<int> { b.MedicineId }, ct)).GetValueOrDefault(b.MedicineId);

        // Sổ kho của lô: cộng dồn từ đầu để có số tồn sau từng biến động, hiện các dòng mới nhất
        var entries = await _db.InventoryTransactions.AsNoTracking()
            .Where(t => t.BatchId == batchId)
            .OrderBy(t => t.TransactionDate).ThenBy(t => t.InventoryTransactionId)
            .Select(t => new LedgerEntry(t.InventoryTransactionId, t.TransactionDate, t.TransactionType, t.BatchId, b.BatchNumber,
                t.QuantityChange, t.ReferenceType, t.ReferenceId,
                t.CreatedByEmployee != null ? t.CreatedByEmployee.FullName : null, t.Notes))
            .ToListAsync(ct);
        var balances = new Dictionary<long, decimal>();
        var running = 0m;
        foreach (var e in entries)
        {
            running += e.Change;
            balances[e.Id] = running;
        }
        var latest = entries.AsEnumerable().Reverse().Take(StockBatchViewModel.LedgerLimit).ToList();
        var ledger = await ToLedgerRowsAsync(latest, balances, ct);

        var recall = await _db.RecallBatches.AsNoTracking()
            .Where(r => r.BatchId == batchId)
            .OrderByDescending(r => r.RecallId)
            .Select(r => new { r.RecallId, r.Recall.RecallCode })
            .FirstOrDefaultAsync(ct);

        var sales = new List<BatchSaleRow>();
        var salesCount = 0;
        var soldQuantity = 0m;
        if (includeSales)
        {
            var allocations = _db.SalesIssueAllocations.AsNoTracking().Where(a => a.BatchId == batchId);
            salesCount = await allocations.CountAsync(ct);
            soldQuantity = salesCount == 0 ? 0m : await allocations.SumAsync(a => a.QuantityBase, ct);
            sales = await allocations
                .OrderByDescending(a => a.AllocatedAt).ThenByDescending(a => a.AllocationId)
                .Take(StockBatchViewModel.SalesLimit)
                .Select(a => new BatchSaleRow(a.AllocatedAt, a.InvoiceDetail.InvoiceId, a.InvoiceDetail.Invoice.InvoiceCode,
                    a.InvoiceDetail.Invoice.Customer != null ? a.InvoiceDetail.Invoice.Customer.FullName : "Khách lẻ",
                    a.InvoiceDetail.Invoice.Customer != null ? a.InvoiceDetail.Invoice.Customer.Phone : null,
                    a.QuantityBase))
                .ToListAsync(ct);
        }

        return new StockBatchViewModel
        {
            BatchId = b.BatchId,
            BatchNumber = b.BatchNumber,
            MedicineId = b.MedicineId,
            MedicineCode = b.MedicineCode,
            MedicineName = b.MedicineName,
            BaseUnit = b.BaseUnit,
            ManufactureDate = b.ManufactureDate,
            ExpiryDate = b.ExpiryDate,
            DaysLeft = b.ExpiryDate.DayNumber - today.DayNumber,
            ReceivedDate = b.ReceivedDate,
            GoodsReceiptId = b.GoodsReceiptId,
            GoodsReceiptCode = b.ReceiptCode,
            SupplierName = b.SupplierName,
            UnitCost = b.UnitCost,
            WarehouseName = b.WarehouseName,
            LocationId = b.LocationId,
            Location = LocationLabel(b.ParentName, b.LocationName),
            Status = b.Status,
            Condition = Classify(b.QuantityOnHand, b.Status, b.ExpiryDate, warningDays, today),
            Notes = b.Notes,
            RecallId = recall?.RecallId,
            RecallCode = recall?.RecallCode,
            Quantity = b.QuantityOnHand,
            Breakdown = StockText.Breakdown(b.QuantityOnHand, b.BaseUnit, largest),
            WarningDays = warningDays,
            Ledger = ledger,
            LedgerTotal = entries.Count,
            Sales = sales,
            SalesCount = salesCount,
            SoldQuantity = soldQuantity,
            ShelfOptions = await ShelfOptionsAsync(b.WarehouseId, ct)
        };
    }

    // ================================================================== Đổi vị trí lô (2.2.4)

    public async Task<OperationResult> MoveBatchAsync(int batchId, int? locationId, string actorUserId, CancellationToken ct = default)
    {
        var b = await _db.MedicineBatches.AsNoTracking()
            .Where(x => x.BatchId == batchId)
            .Select(x => new
            {
                x.BatchId, x.BatchNumber, x.WarehouseId, x.LocationId, x.QuantityOnHand, x.Status,
                LocationName = x.Location != null ? x.Location.LocationName : null,
                ParentName = x.Location != null && x.Location.ParentLocation != null ? x.Location.ParentLocation.LocationName : null
            })
            .FirstOrDefaultAsync(ct);
        if (b is null) return OperationResult.Fail("Không tìm thấy lô thuốc.");
        if (b.QuantityOnHand <= 0) return OperationResult.Fail("Lô đã hết hàng, không cần đổi vị trí.");
        if (b.Status == (byte)BatchStatus.Discontinued) return OperationResult.Fail("Lô đã ngừng sử dụng, không đổi vị trí được.");
        if (locationId is null) return OperationResult.Fail("Chọn kệ mới cho lô.", "locationId");

        var shelf = await _db.StorageLocations.AsNoTracking()
            .Where(l => l.LocationId == locationId && l.WarehouseId == b.WarehouseId && l.LocationType == (byte)LocationType.Shelf)
            .Select(l => new { l.LocationId, l.LocationName, ParentName = l.ParentLocation != null ? l.ParentLocation.LocationName : null })
            .FirstOrDefaultAsync(ct);
        if (shelf is null) return OperationResult.Fail("Chọn một kệ trong kho của lô.", "locationId");
        if (shelf.LocationId == b.LocationId) return OperationResult.Fail("Lô đang ở kệ này.", "locationId");

        var oldLocationId = b.LocationId;
        var newLocationId = (int?)shelf.LocationId;
        var updated = await _db.MedicineBatches
            .Where(x => x.BatchId == batchId && x.LocationId == oldLocationId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.LocationId, newLocationId), ct);
        if (updated == 0) return OperationResult.Fail("Vị trí lô vừa được đổi ở máy khác. Tải lại trang rồi thử lại.");

        var oldLabel = LocationLabel(b.ParentName, b.LocationName);
        var newLabel = LocationLabel(shelf.ParentName, shelf.LocationName)!;
        await _audit.WriteAsync("BATCH_MOVE", "MedicineBatches", batchId.ToString(CultureInfo.InvariantCulture),
            new { LocationId = oldLocationId, Location = oldLabel },
            new { LocationId = newLocationId, Location = newLabel, b.BatchNumber },
            actorUserId, ct);

        return OperationResult.Ok($"Đã chuyển lô {b.BatchNumber} sang {newLabel}.");
    }

    // ================================================================== Dùng chung

    private sealed record BatchFact(int BatchId, int MedicineId, int WarehouseId, DateOnly ExpiryDate, decimal Quantity, byte Status);

    private sealed record Summary(decimal Available, decimal Unsellable, int BatchCount, DateOnly? NearestExpiry,
        bool HasNearExpiry, bool HasExpired, bool HasBlocked)
    {
        public static readonly Summary Empty = new(0m, 0m, 0, null, false, false, false);
    }

    private sealed record LedgerEntry(long Id, DateTime When, byte Type, int BatchId, string BatchNumber, decimal Change,
        string ReferenceType, long ReferenceId, string? Actor, string? Notes);

    /// <summary>Ngưỡng cận hạn (mặc định + theo thuốc, kho) và mức tồn tối thiểu theo thuốc (lấy lớn nhất nếu nhiều kho).</summary>
    private sealed record Thresholds(int DefaultDays, Dictionary<(int MedicineId, int WarehouseId), int?> PolicyDays, Dictionary<int, decimal> Minimum)
    {
        public int DaysFor(int medicineId, int warehouseId)
            => PolicyDays.TryGetValue((medicineId, warehouseId), out var days) && days is not null ? days.Value : DefaultDays;

        public int DaysForMedicine(int medicineId)
            => PolicyDays.Where(p => p.Key.MedicineId == medicineId && p.Value is not null).Select(p => p.Value!.Value).DefaultIfEmpty(DefaultDays).Max();

        public int MaxDays => PolicyDays.Values.Where(v => v is not null).Select(v => v!.Value).DefaultIfEmpty(0).Append(DefaultDays).Max();
    }

    private async Task<Thresholds> LoadThresholdsAsync(CancellationToken ct)
    {
        var raw = await _db.PharmacySettings.AsNoTracking().Where(s => s.SettingKey == ExpiryWarningKey)
            .Select(s => s.SettingValue).FirstOrDefaultAsync(ct);
        var days = int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ? parsed : DefaultExpiryWarningDays;
        var policies = await _db.InventoryPolicies.AsNoTracking()
            .Select(p => new { p.MedicineId, p.WarehouseId, p.MinimumQuantity, p.ExpiryWarningDays })
            .ToListAsync(ct);
        return new Thresholds(
            days,
            policies.ToDictionary(p => (p.MedicineId, p.WarehouseId), p => p.ExpiryWarningDays),
            policies.GroupBy(p => p.MedicineId).ToDictionary(g => g.Key, g => g.Max(p => p.MinimumQuantity)));
    }

    /// <summary>
    /// Hết hàng → Empty; HSD ≤ hôm nay → Expired (dù trạng thái nào); lô không Khả dụng → Blocked;
    /// còn ≤ <paramref name="warningDays"/> ngày → NearExpiry; còn lại Available.
    /// </summary>
    private static BatchCondition Classify(decimal quantity, byte status, DateOnly expiry, int warningDays, DateOnly today)
    {
        if (quantity <= 0) return BatchCondition.Empty;
        if (expiry <= today) return BatchCondition.Expired;
        if (status != (byte)BatchStatus.Available) return BatchCondition.Blocked;
        return expiry.DayNumber - today.DayNumber <= warningDays ? BatchCondition.NearExpiry : BatchCondition.Available;
    }

    private static bool IsSellable(BatchCondition condition) => condition is BatchCondition.Available or BatchCondition.NearExpiry;

    private Task<List<BatchFact>> InStockFactsAsync(IReadOnlyCollection<int>? medicineIds, CancellationToken ct)
    {
        var query = _db.MedicineBatches.AsNoTracking().Where(b => b.QuantityOnHand > 0);
        if (medicineIds is not null) query = query.Where(b => medicineIds.Contains(b.MedicineId));
        return query.Select(b => new BatchFact(b.BatchId, b.MedicineId, b.WarehouseId, b.ExpiryDate, b.QuantityOnHand, b.Status)).ToListAsync(ct);
    }

    private static Dictionary<int, Summary> Summarize(IEnumerable<BatchFact> facts, Thresholds thresholds, DateOnly today)
        => facts.GroupBy(f => f.MedicineId).ToDictionary(g => g.Key, g =>
        {
            var lots = g.Select(f => new { f.Quantity, f.ExpiryDate, Condition = Classify(f.Quantity, f.Status, f.ExpiryDate, thresholds.DaysFor(f.MedicineId, f.WarehouseId), today) })
                .ToList();
            var sellable = lots.Where(l => IsSellable(l.Condition)).ToList();
            return new Summary(
                sellable.Sum(l => l.Quantity),
                lots.Where(l => !IsSellable(l.Condition)).Sum(l => l.Quantity),
                lots.Count,
                sellable.Count == 0 ? null : sellable.Min(l => l.ExpiryDate),
                lots.Any(l => l.Condition == BatchCondition.NearExpiry),
                lots.Any(l => l.Condition == BatchCondition.Expired),
                lots.Any(l => l.Condition == BatchCondition.Blocked));
        });

    /// <summary>Số đang chờ giao (đơn vị cơ sở) của các đơn đặt hàng Chờ giao / Giao thiếu — cùng cách tính với gợi ý đặt hàng.</summary>
    private async Task<Dictionary<int, decimal>> OnOrderAsync(int? medicineId, CancellationToken ct)
    {
        var query = _db.PurchaseOrderDetails.AsNoTracking()
            .Where(d => (d.PurchaseOrder.Status == (byte)PurchaseOrderStatus.AwaitingDelivery || d.PurchaseOrder.Status == (byte)PurchaseOrderStatus.PartiallyDelivered)
                        && d.OrderedQuantity > d.ReceivedQuantity);
        if (medicineId is not null) query = query.Where(d => d.MedicineId == medicineId);
        var rows = await query
            .Select(d => new
            {
                d.MedicineId, Remaining = d.OrderedQuantity - d.ReceivedQuantity,
                Factor = d.UnitId == d.Medicine.BaseUnitId
                    ? 1m
                    : d.Medicine.MedicineUnitConversions.Where(c => c.UnitId == d.UnitId).Select(c => (decimal?)c.ConversionToBase).FirstOrDefault() ?? 1m
            })
            .ToListAsync(ct);
        return rows.GroupBy(r => r.MedicineId).ToDictionary(g => g.Key, g => g.Sum(r => r.Remaining * r.Factor));
    }

    /// <summary>Đơn vị quy đổi lớn nhất (hệ số &gt; 1) của từng thuốc — để quy số tồn ra hộp, chai…</summary>
    private async Task<Dictionary<int, UnitFactor>> LargestUnitsAsync(List<int> medicineIds, CancellationToken ct)
    {
        if (medicineIds.Count == 0) return new Dictionary<int, UnitFactor>();
        var rows = await _db.MedicineUnitConversions.AsNoTracking()
            .Where(c => medicineIds.Contains(c.MedicineId) && c.ConversionToBase > 1)
            .Select(c => new { c.MedicineId, c.Unit.UnitName, c.ConversionToBase })
            .ToListAsync(ct);
        return rows.GroupBy(r => r.MedicineId).ToDictionary(g => g.Key,
            g => g.OrderByDescending(r => r.ConversionToBase).Select(r => new UnitFactor(r.UnitName, r.ConversionToBase)).First());
    }

    /// <summary>Tủ và kệ cho ô lọc: "Tủ 1", "Tủ 1 › Kệ A".</summary>
    private async Task<IReadOnlyList<LookupItem>> LocationOptionsAsync(CancellationToken ct)
    {
        var rows = await _db.StorageLocations.AsNoTracking()
            .Select(l => new
            {
                l.LocationId, l.LocationCode, l.LocationName, l.LocationType,
                ParentCode = l.ParentLocation != null ? l.ParentLocation.LocationCode : null,
                ParentName = l.ParentLocation != null ? l.ParentLocation.LocationName : null
            })
            .ToListAsync(ct);
        return rows
            .OrderBy(l => l.ParentCode ?? l.LocationCode, StringComparer.Ordinal)
            .ThenBy(l => l.LocationType)
            .ThenBy(l => l.LocationCode, StringComparer.Ordinal)
            .Select(l => new LookupItem(l.LocationId, LocationLabel(l.ParentName, l.LocationName)!))
            .ToList();
    }

    private async Task<IReadOnlyList<LookupItem>> ShelfOptionsAsync(int warehouseId, CancellationToken ct)
    {
        var rows = await _db.StorageLocations.AsNoTracking()
            .Where(l => l.WarehouseId == warehouseId && l.LocationType == (byte)LocationType.Shelf)
            .Select(l => new
            {
                l.LocationId, l.LocationCode, l.LocationName,
                ParentCode = l.ParentLocation != null ? l.ParentLocation.LocationCode : "",
                ParentName = l.ParentLocation != null ? l.ParentLocation.LocationName : null
            })
            .ToListAsync(ct);
        return rows
            .OrderBy(l => l.ParentCode, StringComparer.Ordinal).ThenBy(l => l.LocationCode, StringComparer.Ordinal)
            .Select(l => new LookupItem(l.LocationId, LocationLabel(l.ParentName, l.LocationName)!))
            .ToList();
    }

    /// <summary>Đổi dòng sổ kho thành dòng hiển thị, kèm mã chứng từ gốc (phiếu nhập, hóa đơn, phiếu trả…).</summary>
    private async Task<List<StockLedgerRow>> ToLedgerRowsAsync(List<LedgerEntry> entries, Dictionary<long, decimal>? balances, CancellationToken ct)
    {
        var documents = await ResolveDocumentsAsync(entries.Select(e => (e.ReferenceType, e.ReferenceId)).ToList(), ct);
        return entries.Select(e =>
        {
            var found = documents.TryGetValue((e.ReferenceType, e.ReferenceId), out var doc);
            return new StockLedgerRow(e.When, e.Type, e.BatchId, e.BatchNumber, e.Change,
                balances is not null && balances.TryGetValue(e.Id, out var balance) ? balance : null,
                found ? doc.Code : null, found ? doc.DocumentId : null,
                e.Actor ?? "Hệ thống", e.Notes);
        }).ToList();
    }

    private async Task<Dictionary<(string Type, long Id), (string Code, int DocumentId)>> ResolveDocumentsAsync(
        List<(string Type, long Id)> references, CancellationToken ct)
    {
        var result = new Dictionary<(string Type, long Id), (string Code, int DocumentId)>();
        foreach (var group in references.Distinct().GroupBy(r => r.Type))
        {
            var ids = group.Select(r => r.Id).ToList();
            var intIds = ids.Where(i => i is > 0 and <= int.MaxValue).Select(i => (int)i).ToList();
            switch (group.Key)
            {
                case InventoryReferenceTypes.GoodsReceiptDetail:
                    foreach (var d in await _db.GoodsReceiptDetails.AsNoTracking().Where(d => intIds.Contains(d.GoodsReceiptDetailId))
                                 .Select(d => new { d.GoodsReceiptDetailId, d.GoodsReceiptId, d.GoodsReceipt.GoodsReceiptCode }).ToListAsync(ct))
                        result[(group.Key, d.GoodsReceiptDetailId)] = (d.GoodsReceiptCode, d.GoodsReceiptId);
                    break;
                case InventoryReferenceTypes.SalesIssueAllocation:
                    foreach (var a in await _db.SalesIssueAllocations.AsNoTracking().Where(a => ids.Contains(a.AllocationId))
                                 .Select(a => new { a.AllocationId, a.InvoiceDetail.InvoiceId, a.InvoiceDetail.Invoice.InvoiceCode }).ToListAsync(ct))
                        result[(group.Key, a.AllocationId)] = (a.InvoiceCode, a.InvoiceId);
                    break;
                case InventoryReferenceTypes.SupplierReturnDetail:
                    foreach (var d in await _db.SupplierReturnDetails.AsNoTracking().Where(d => intIds.Contains(d.SupplierReturnDetailId))
                                 .Select(d => new { d.SupplierReturnDetailId, d.SupplierReturnId, d.SupplierReturn.SupplierReturnCode }).ToListAsync(ct))
                        result[(group.Key, d.SupplierReturnDetailId)] = (d.SupplierReturnCode, d.SupplierReturnId);
                    break;
                case InventoryReferenceTypes.StockDisposalDetail:
                    foreach (var d in await _db.StockDisposalDetails.AsNoTracking().Where(d => intIds.Contains(d.DisposalDetailId))
                                 .Select(d => new { d.DisposalDetailId, d.DisposalId, d.Disposal.DisposalCode }).ToListAsync(ct))
                        result[(group.Key, d.DisposalDetailId)] = (d.DisposalCode, d.DisposalId);
                    break;
                case InventoryReferenceTypes.RecallBatch:
                    foreach (var r in await _db.RecallBatches.AsNoTracking().Where(r => intIds.Contains(r.RecallBatchId))
                                 .Select(r => new { r.RecallBatchId, r.RecallId, r.Recall.RecallCode }).ToListAsync(ct))
                        result[(group.Key, r.RecallBatchId)] = (r.RecallCode, r.RecallId);
                    break;
                case InventoryReferenceTypes.StocktakeDetail:
                    foreach (var d in await _db.StocktakeDetails.AsNoTracking().Where(d => intIds.Contains(d.StocktakeDetailId))
                                 .Select(d => new { d.StocktakeDetailId, d.StocktakeId, d.Stocktake.StocktakeCode }).ToListAsync(ct))
                        result[(group.Key, d.StocktakeDetailId)] = (d.StocktakeCode, d.StocktakeId);
                    break;
                case InventoryReferenceTypes.ReturnDetail:
                    foreach (var d in await _db.ReturnDetails.AsNoTracking().Where(d => intIds.Contains(d.ReturnDetailId))
                                 .Select(d => new { d.ReturnDetailId, d.ReturnId, d.Return.ReturnCode }).ToListAsync(ct))
                        result[(group.Key, d.ReturnDetailId)] = (d.ReturnCode, d.ReturnId);
                    break;
            }
        }
        return result;
    }

    private static bool StartsWithFolded(string name, string term)
        => CultureInfo.InvariantCulture.CompareInfo.IsPrefix(SearchText.Fold(name), SearchText.Fold(term),
            CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace);

    private static decimal RoundPrice(decimal price) => Math.Round(price, 0, MidpointRounding.AwayFromZero);

    private static string? LocationLabel(string? parentName, string? name)
        => name is null ? null : parentName is null ? name : parentName + " › " + name;
}
