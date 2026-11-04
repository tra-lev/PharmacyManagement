using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PharmacyManagement.Data;
using PharmacyManagement.Helpers;
using PharmacyManagement.Models.Entities;
using PharmacyManagement.Models.Enums;
using PharmacyManagement.Models.ViewModels.Catalog;
using PharmacyManagement.Models.ViewModels.Inventory;
using PharmacyManagement.Services.Interfaces;

namespace PharmacyManagement.Services.Implementations;

public sealed class GoodsReceiptService : IGoodsReceiptService
{
    private const string CodePrefix = "PN";
    private const int MaxLines = 100;
    private const decimal MaxQuantity = 1_000_000m;
    private const string ExpiryWarningKey = "ExpiryWarningDays";
    private const int DefaultExpiryWarningDays = 90;

    private static readonly Regex BatchPattern = new(@"^[A-Za-z0-9][A-Za-z0-9./_\- ]*$", RegexOptions.CultureInvariant);
    private static readonly Regex PrintableAscii = new(@"^[\x20-\x7E]*$", RegexOptions.CultureInvariant);

    private static readonly byte[] ReceivableOrderStatuses =
    {
        (byte)PurchaseOrderStatus.AwaitingDelivery, (byte)PurchaseOrderStatus.PartiallyDelivered
    };

    private readonly PharmacyDbContext _db;
    private readonly IAuditLogService _audit;

    public GoodsReceiptService(PharmacyDbContext db, IAuditLogService audit)
    {
        _db = db;
        _audit = audit;
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    // ================================================================== Danh sách

    public async Task<ReceiptListViewModel> GetListAsync(ReceiptFilter filter, CancellationToken ct = default)
    {
        var query = _db.GoodsReceipts.AsNoTracking().AsQueryable();

        byte? status = filter.Status switch
        {
            "inspecting" => (byte)GoodsReceiptStatus.Inspecting,
            "posted" => (byte)GoodsReceiptStatus.Posted,
            "cancelled" => (byte)GoodsReceiptStatus.Cancelled,
            _ => null
        };
        if (status is null) filter.Status = null;
        else query = query.Where(r => r.Status == status);
        if (filter.SupplierId is not null) query = query.Where(r => r.SupplierId == filter.SupplierId);

        var q = AccountHelpers.NormalizeFullName(filter.Q);
        filter.Q = q.Length == 0 ? null : q;
        if (filter.Q is not null)
        {
            var text = filter.Q;
            var folded = SearchText.Fold(text);
            query = query.Where(r => r.GoodsReceiptCode.Contains(text)
                                     || (r.SupplierInvoiceNumber != null && r.SupplierInvoiceNumber.Contains(text))
                                     || (r.PurchaseOrder != null && r.PurchaseOrder.PurchaseOrderCode.Contains(text))
                                     || EF.Functions.Collate(r.Supplier.SupplierName.Replace("đ", "d").Replace("Đ", "D"), SearchText.Collation).Contains(folded));
        }

        var total = await query.CountAsync(ct);
        var pageCount = Math.Max(1, (int)Math.Ceiling(total / (double)ReceiptFilter.PageSize));
        filter.Page = Math.Clamp(filter.Page, 1, pageCount);

        var rows = await query
            .OrderByDescending(r => r.ReceiptDate).ThenByDescending(r => r.GoodsReceiptId)
            .Skip((filter.Page - 1) * ReceiptFilter.PageSize).Take(ReceiptFilter.PageSize)
            .Select(r => new ReceiptRow(r.GoodsReceiptId, r.GoodsReceiptCode, r.ReceiptDate, r.Supplier.SupplierName,
                r.SupplierInvoiceNumber, r.PurchaseOrder != null ? r.PurchaseOrder.PurchaseOrderCode : null,
                r.GoodsReceiptDetails.Count(), r.TotalAmount, r.Status))
            .ToListAsync(ct);

        return new ReceiptListViewModel
        {
            Filter = filter,
            Rows = rows,
            Total = total,
            GrandTotal = await _db.GoodsReceipts.CountAsync(ct),
            PageCount = pageCount,
            Suppliers = await _db.Suppliers.AsNoTracking().OrderBy(s => s.SupplierName)
                .Select(s => new LookupItem(s.SupplierId, s.SupplierName)).ToListAsync(ct)
        };
    }

    // ================================================================== Chi tiết (2.2.1.1 đối chiếu đơn đặt hàng)

    public async Task<ReceiptDetailsViewModel?> GetDetailsAsync(int receiptId, CancellationToken ct = default)
    {
        var r = await _db.GoodsReceipts.AsNoTracking()
            .Where(x => x.GoodsReceiptId == receiptId)
            .Select(x => new
            {
                x.GoodsReceiptId, x.GoodsReceiptCode, x.Status, x.ReceiptDate, x.SupplierInvoiceNumber, x.Notes, x.TotalAmount,
                x.Supplier.SupplierCode, x.Supplier.SupplierName,
                PurchaseOrderCode = x.PurchaseOrder != null ? x.PurchaseOrder.PurchaseOrderCode : null,
                ReceivedBy = x.ReceivedByEmployee.FullName
            })
            .FirstOrDefaultAsync(ct);
        if (r is null) return null;

        var lines = await _db.GoodsReceiptDetails.AsNoTracking()
            .Where(d => d.GoodsReceiptId == receiptId)
            .OrderBy(d => d.GoodsReceiptDetailId)
            .Select(d => new
            {
                d.Medicine.MedicineCode, d.Medicine.MedicineName, UnitName = d.Unit.UnitName, d.ConversionToBase,
                BaseUnitName = d.Medicine.BaseUnit.UnitName, d.QuantityReceived, d.DamagedQuantity, d.UnitCost, d.QualityNote,
                d.BatchNumber, d.ManufactureDate, d.ExpiryDate,
                LocationName = d.Location != null ? d.Location.LocationName : null,
                ParentName = d.Location != null && d.Location.ParentLocation != null ? d.Location.ParentLocation.LocationName : null,
                Ordered = d.PurchaseOrderDetail != null ? (decimal?)d.PurchaseOrderDetail.OrderedQuantity : null,
                OrderReceived = d.PurchaseOrderDetail != null ? (decimal?)d.PurchaseOrderDetail.ReceivedQuantity : null,
                BatchId = d.MedicineBatch != null ? (int?)d.MedicineBatch.BatchId : null,
                BatchOnHand = d.MedicineBatch != null ? (decimal?)d.MedicineBatch.QuantityOnHand : null
            })
            .ToListAsync(ct);

        var today = Today;
        return new ReceiptDetailsViewModel
        {
            GoodsReceiptId = r.GoodsReceiptId,
            Code = r.GoodsReceiptCode,
            Status = r.Status,
            ReceiptDate = r.ReceiptDate,
            SupplierCode = r.SupplierCode,
            SupplierName = r.SupplierName,
            SupplierInvoiceNumber = r.SupplierInvoiceNumber,
            PurchaseOrderCode = r.PurchaseOrderCode,
            ReceivedByName = r.ReceivedBy,
            Notes = r.Notes,
            TotalAmount = r.TotalAmount,
            ExpiryWarningDays = await ExpiryWarningDaysAsync(ct),
            Lines = lines.Select(l => new ReceiptLineInfo(
                l.MedicineCode, l.MedicineName, l.UnitName, l.ConversionToBase, l.BaseUnitName,
                l.QuantityReceived, l.DamagedQuantity, l.UnitCost, (l.QuantityReceived - l.DamagedQuantity) * l.UnitCost, l.QualityNote,
                l.BatchNumber, l.ManufactureDate, l.ExpiryDate, LocationLabel(l.ParentName, l.LocationName),
                l.Ordered, l.OrderReceived, l.BatchId, l.BatchOnHand,
                l.ExpiryDate is null ? null : l.ExpiryDate.Value.DayNumber - today.DayNumber)).ToList()
        };
    }

    public Task<byte?> GetStatusAsync(int receiptId, CancellationToken ct = default)
        => _db.GoodsReceipts.AsNoTracking().Where(r => r.GoodsReceiptId == receiptId).Select(r => (byte?)r.Status).FirstOrDefaultAsync(ct);

    // ================================================================== Form

    public async Task<ReceiptFormViewModel> NewFormAsync(int? supplierId, int? purchaseOrderId, CancellationToken ct = default)
    {
        var form = new ReceiptFormViewModel { SupplierId = supplierId };
        if (purchaseOrderId is not null)
        {
            var po = await _db.PurchaseOrders.AsNoTracking()
                .Where(p => p.PurchaseOrderId == purchaseOrderId && ReceivableOrderStatuses.Contains(p.Status))
                .Select(p => new { p.PurchaseOrderId, p.SupplierId })
                .FirstOrDefaultAsync(ct);
            if (po is not null)
            {
                form.SupplierId = po.SupplierId;
                form.PurchaseOrderId = po.PurchaseOrderId;
                form.Lines = await _db.PurchaseOrderDetails.AsNoTracking()
                    .Where(d => d.PurchaseOrderId == po.PurchaseOrderId && d.ReceivedQuantity < d.OrderedQuantity)
                    .OrderBy(d => d.PurchaseOrderDetailId)
                    .Select(d => new ReceiptLineForm
                    {
                        PurchaseOrderDetailId = d.PurchaseOrderDetailId,
                        MedicineId = d.MedicineId,
                        UnitId = d.UnitId,
                        QuantityReceived = d.OrderedQuantity - d.ReceivedQuantity,
                        DamagedQuantity = 0,
                        UnitCost = d.ExpectedUnitPrice
                    })
                    .ToListAsync(ct);
            }
        }
        await PrepareFormAsync(form, ct);
        return form;
    }

    public async Task<ReceiptFormViewModel?> EditFormAsync(int receiptId, CancellationToken ct = default)
    {
        var r = await _db.GoodsReceipts.AsNoTracking()
            .Where(x => x.GoodsReceiptId == receiptId)
            .Select(x => new { x.GoodsReceiptId, x.SupplierId, x.PurchaseOrderId, x.SupplierInvoiceNumber, x.Notes })
            .FirstOrDefaultAsync(ct);
        if (r is null) return null;

        var details = await _db.GoodsReceiptDetails.AsNoTracking()
            .Where(d => d.GoodsReceiptId == receiptId)
            .OrderBy(d => d.GoodsReceiptDetailId)
            .ToListAsync(ct);
        var form = new ReceiptFormViewModel
        {
            GoodsReceiptId = r.GoodsReceiptId,
            SupplierId = r.SupplierId,
            PurchaseOrderId = r.PurchaseOrderId,
            SupplierInvoiceNumber = r.SupplierInvoiceNumber,
            Notes = r.Notes,
            Lines = details.Select(d => new ReceiptLineForm
            {
                PurchaseOrderDetailId = d.PurchaseOrderDetailId,
                MedicineId = d.MedicineId,
                UnitId = d.UnitId,
                QuantityReceived = d.QuantityReceived,
                DamagedQuantity = d.DamagedQuantity,
                UnitCost = d.UnitCost,
                BatchNumber = d.BatchNumber,
                ManufactureDate = d.ManufactureDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ExpiryDate = d.ExpiryDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                LocationId = d.LocationId,
                QualityNote = d.QualityNote
            }).ToList()
        };
        await PrepareFormAsync(form, ct);
        return form;
    }

    public async Task PrepareFormAsync(ReceiptFormViewModel form, CancellationToken ct = default)
    {
        if (form.GoodsReceiptId is null)
        {
            form.Code = await NextCodeAsync(DateTime.Now, ct);
        }
        else
        {
            // Phiếu đã lập: mã và đơn đặt hàng lấy từ CSDL (không đổi được)
            var saved = await _db.GoodsReceipts.AsNoTracking().Where(r => r.GoodsReceiptId == form.GoodsReceiptId)
                .Select(r => new { r.GoodsReceiptCode, r.PurchaseOrderId }).FirstOrDefaultAsync(ct);
            form.Code = saved?.GoodsReceiptCode;
            form.PurchaseOrderId = saved?.PurchaseOrderId;
        }

        if (form.PurchaseOrderId is not null)
        {
            var po = await _db.PurchaseOrders.AsNoTracking().Where(p => p.PurchaseOrderId == form.PurchaseOrderId)
                .Select(p => new { p.PurchaseOrderCode, p.SupplierId, p.Supplier.SupplierName }).FirstOrDefaultAsync(ct);
            form.PurchaseOrderCode = po?.PurchaseOrderCode;
            if (po is not null)
            {
                form.SupplierId = po.SupplierId;
                form.SupplierName = po.SupplierName;
            }
        }

        var currentSupplier = form.SupplierId;
        form.SupplierOptions = await _db.Suppliers.AsNoTracking()
            .Where(s => s.IsActive || s.SupplierId == currentSupplier)
            .OrderBy(s => s.SupplierName)
            .Select(s => new LookupItem(s.SupplierId, s.SupplierName + " (" + s.SupplierCode + ")"))
            .ToListAsync(ct);

        form.PurchaseOrderOptions = (await _db.PurchaseOrders.AsNoTracking()
                .Where(p => ReceivableOrderStatuses.Contains(p.Status))
                .OrderByDescending(p => p.OrderDate)
                .Select(p => new { p.PurchaseOrderId, p.PurchaseOrderCode, p.SupplierId, p.Supplier.SupplierName, p.OrderDate, p.Status })
                .ToListAsync(ct))
            .Select(p => new PurchaseOrderOption(p.PurchaseOrderId,
                $"{p.PurchaseOrderCode} · {p.SupplierName} · {p.OrderDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}"
                + (p.Status == (byte)PurchaseOrderStatus.PartiallyDelivered ? " (giao thiếu)" : ""), p.SupplierId))
            .ToList();

        // Thuốc đang kinh doanh + thuốc đã có trong phiếu (kể cả vừa ngừng, để hiện đúng tên)
        var lineMedicineIds = form.Lines.Where(l => l.MedicineId is not null).Select(l => l.MedicineId!.Value).Distinct().ToList();
        var medicines = await _db.Medicines.AsNoTracking()
            .Where(m => m.IsActive || lineMedicineIds.Contains(m.MedicineId))
            .OrderBy(m => m.MedicineName).ThenBy(m => m.MedicineCode)
            .Select(m => new { m.MedicineId, m.MedicineCode, m.MedicineName, m.BaseUnitId, BaseUnitName = m.BaseUnit.UnitName, m.DefaultPurchasePrice })
            .ToListAsync(ct);
        var conversions = await _db.MedicineUnitConversions.AsNoTracking()
            .Select(c => new { c.MedicineId, c.UnitId, c.Unit.UnitName, c.ConversionToBase })
            .ToListAsync(ct);
        var convByMedicine = conversions.GroupBy(c => c.MedicineId).ToDictionary(g => g.Key, g => g.OrderBy(c => c.ConversionToBase).ToList());
        // Tên hoạt chất để tìm thuốc theo hoạt chất ở ô chọn thuốc
        var ingredients = await _db.MedicineIngredients.AsNoTracking()
            .Select(i => new { i.MedicineId, i.Ingredient.IngredientName })
            .ToListAsync(ct);
        var keywordsByMedicine = ingredients.GroupBy(i => i.MedicineId)
            .ToDictionary(g => g.Key, g => string.Join(", ", g.Select(i => i.IngredientName).OrderBy(n => n)));
        form.MedicineOptions = medicines.Select(m =>
        {
            var units = new List<UnitOption> { new(m.BaseUnitId, m.BaseUnitName, 1m) };
            if (convByMedicine.TryGetValue(m.MedicineId, out var list))
                units.AddRange(list.Where(c => c.UnitId != m.BaseUnitId).Select(c => new UnitOption(c.UnitId, c.UnitName, c.ConversionToBase)));
            return new MedicineOption(m.MedicineId, $"{m.MedicineName} ({m.MedicineCode})", m.BaseUnitName, m.DefaultPurchasePrice, units,
                keywordsByMedicine.GetValueOrDefault(m.MedicineId, string.Empty));
        }).ToList();

        form.LocationOptions = (await _db.StorageLocations.AsNoTracking()
                .Where(l => l.LocationType == (byte)LocationType.Shelf)
                .Select(l => new
                {
                    l.LocationId, l.LocationCode, l.LocationName,
                    ParentCode = l.ParentLocation != null ? l.ParentLocation.LocationCode : "",
                    ParentName = l.ParentLocation != null ? l.ParentLocation.LocationName : null
                })
                .ToListAsync(ct))
            .OrderBy(l => l.ParentCode).ThenBy(l => l.LocationCode)
            .Select(l => new LookupItem(l.LocationId, LocationLabel(l.ParentName, l.LocationName)!))
            .ToList();

        // Thông tin hiển thị cho từng dòng
        var byId = form.MedicineOptions.ToDictionary(m => m.MedicineId);
        var poDetailIds = form.Lines.Where(l => l.PurchaseOrderDetailId is not null).Select(l => l.PurchaseOrderDetailId!.Value).Distinct().ToList();
        var poDetails = await _db.PurchaseOrderDetails.AsNoTracking()
            .Where(d => poDetailIds.Contains(d.PurchaseOrderDetailId))
            .Select(d => new { d.PurchaseOrderDetailId, d.MedicineId, d.UnitId, d.OrderedQuantity, d.ReceivedQuantity })
            .ToDictionaryAsync(d => d.PurchaseOrderDetailId, ct);
        foreach (var line in form.Lines)
        {
            if (line.PurchaseOrderDetailId is not null && poDetails.TryGetValue(line.PurchaseOrderDetailId.Value, out var pd))
            {
                line.MedicineId = pd.MedicineId;
                line.UnitId = pd.UnitId;
                line.OrderedQuantity = pd.OrderedQuantity;
                line.AlreadyReceived = pd.ReceivedQuantity;
            }
            if (line.MedicineId is not null && byId.TryGetValue(line.MedicineId.Value, out var med))
            {
                line.MedicineLabel = med.Label;
                line.UnitName = med.Units.FirstOrDefault(u => u.UnitId == line.UnitId)?.UnitName;
            }
        }

        form.ExpiryWarningDays = await ExpiryWarningDaysAsync(ct);
        form.Today = Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    // ================================================================== Lập / sửa phiếu (2.2.1 kiểm nhận)

    public async Task<OperationResult<int>> CreateAsync(ReceiptFormViewModel form, string actorUserId, CancellationToken ct = default)
    {
        var employeeId = await EmployeeIdOfAsync(actorUserId, ct);
        if (employeeId is null) return OperationResult<int>.Fail("Tài khoản chưa gắn hồ sơ nhân viên nên chưa lập phiếu nhập được.");

        var clean = await ValidateFormAsync(form, form.PurchaseOrderId, ct);
        if (clean.Error is not null) return OperationResult<int>.Fail(clean.Error, clean.Field);

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var receipt = new GoodsReceipt
            {
                GoodsReceiptCode = await NextCodeAsync(DateTime.Now, ct),
                PurchaseOrderId = clean.PurchaseOrderId,
                SupplierId = clean.SupplierId,
                ReceivedByEmployeeId = employeeId.Value,
                ReceiptDate = DateTime.Now,
                SupplierInvoiceNumber = clean.InvoiceNumber,
                Status = (byte)GoodsReceiptStatus.Inspecting,
                TotalAmount = clean.Total,
                Notes = clean.Notes
            };
            foreach (var line in clean.Lines) receipt.GoodsReceiptDetails.Add(ToDetail(line));
            _db.GoodsReceipts.Add(receipt);
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return OperationResult<int>.Ok(receipt.GoodsReceiptId,
                $"Đã lưu phiếu {receipt.GoodsReceiptCode} ở trạng thái đang kiểm nhận. Kiểm tra đủ số lô, hạn dùng, kệ rồi bấm Nhập kho.");
        }
        catch (DbUpdateException)
        {
            return OperationResult<int>.Fail("Không lưu được: mã phiếu vừa bị trùng do người khác lập cùng lúc. Bấm Lưu lại.");
        }
    }

    public async Task<OperationResult> UpdateAsync(ReceiptFormViewModel form, string actorUserId, CancellationToken ct = default)
    {
        var receipt = await _db.GoodsReceipts.Include(r => r.GoodsReceiptDetails)
            .FirstOrDefaultAsync(r => r.GoodsReceiptId == form.GoodsReceiptId, ct);
        if (receipt is null) return OperationResult.Fail("Không tìm thấy phiếu nhập.");
        if (receipt.Status != (byte)GoodsReceiptStatus.Inspecting)
            return OperationResult.Fail($"Phiếu {receipt.GoodsReceiptCode} {StatusText(receipt.Status).ToLowerInvariant()} nên không sửa được.");

        // Đơn đặt hàng của phiếu không đổi sau khi lập
        var clean = await ValidateFormAsync(form, receipt.PurchaseOrderId, ct);
        if (clean.Error is not null) return OperationResult.Fail(clean.Error, clean.Field);

        receipt.SupplierId = clean.SupplierId;
        receipt.SupplierInvoiceNumber = clean.InvoiceNumber;
        receipt.Notes = clean.Notes;
        receipt.TotalAmount = clean.Total;
        // Phiếu đang kiểm nhận chưa sinh lô, chưa ghi sổ: thay toàn bộ dòng
        _db.GoodsReceiptDetails.RemoveRange(receipt.GoodsReceiptDetails);
        foreach (var line in clean.Lines) receipt.GoodsReceiptDetails.Add(ToDetail(line));

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return OperationResult.Ok($"Đã lưu phiếu {receipt.GoodsReceiptCode}.");
    }

    // ================================================================== 2.2.2–2.2.4 Nhập kho

    public async Task<OperationResult> PostAsync(int receiptId, string actorUserId, CancellationToken ct = default)
    {
        var employeeId = await EmployeeIdOfAsync(actorUserId, ct);
        if (employeeId is null) return OperationResult.Fail("Tài khoản chưa gắn hồ sơ nhân viên nên chưa nhập kho được.");

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // Giành quyền nhập kho phiếu này (khóa dòng tới khi commit): bấm hai lần / hai máy cùng lúc chỉ một lần thành công
        var claimed = await _db.GoodsReceipts
            .Where(r => r.GoodsReceiptId == receiptId && r.Status == (byte)GoodsReceiptStatus.Inspecting)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, (byte)GoodsReceiptStatus.Posted), ct);
        if (claimed == 0)
        {
            var status = await GetStatusAsync(receiptId, ct);
            return OperationResult.Fail(status is null ? "Không tìm thấy phiếu nhập." : $"Phiếu {StatusText(status.Value).ToLowerInvariant()} nên không nhập kho lại được.");
        }

        var receipt = await _db.GoodsReceipts.AsNoTracking()
            .Where(r => r.GoodsReceiptId == receiptId)
            .Select(r => new { r.GoodsReceiptCode, r.PurchaseOrderId, SupplierActive = r.Supplier.IsActive, r.Supplier.SupplierName })
            .FirstAsync(ct);
        if (!receipt.SupplierActive)
            return OperationResult.Fail($"Nhà cung cấp {receipt.SupplierName} đã ngừng giao dịch — mở lại giao dịch ở Danh mục rồi nhập kho.");

        var lines = await _db.GoodsReceiptDetails.AsNoTracking()
            .Where(d => d.GoodsReceiptId == receiptId)
            .OrderBy(d => d.GoodsReceiptDetailId)
            .Select(d => new
            {
                Detail = d,
                d.Medicine.MedicineName,
                MedicineActive = d.Medicine.IsActive,
                LocationType = d.Location != null ? (byte?)d.Location.LocationType : null,
                LocationWarehouseId = d.Location != null ? (int?)d.Location.WarehouseId : null
            })
            .ToListAsync(ct);
        if (lines.Count == 0) return OperationResult.Fail("Phiếu chưa có dòng thuốc nào.");

        var warehouseId = await _db.Warehouses.AsNoTracking().Where(w => w.IsActive).OrderBy(w => w.WarehouseId)
            .Select(w => (int?)w.WarehouseId).FirstOrDefaultAsync(ct);
        if (warehouseId is null) return OperationResult.Fail("Chưa có kho đang hoạt động để nhập hàng.");

        // ---- Kiểm tra đủ thông tin lô (2.2.3), vị trí (2.2.4), hạn dùng (2.2.1.2) ----
        var today = Today;
        var warnDays = await ExpiryWarningDaysAsync(ct);
        var warnings = new List<string>();
        for (var i = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            var d = l.Detail;
            var label = $"Dòng {i + 1} ({l.MedicineName})";
            if (!l.MedicineActive) return OperationResult.Fail($"{label}: thuốc đã ngừng kinh doanh — mở lại ở Danh mục thuốc hoặc bỏ dòng này.");
            if (d.DamagedQuantity > 0 && string.IsNullOrWhiteSpace(d.QualityNote))
                return OperationResult.Fail($"{label}: có hàng hỏng — ghi chú chất lượng trước khi nhập kho.");
            if (d.QuantityReceived - d.DamagedQuantity <= 0) continue; // hỏng toàn bộ: không sinh lô
            if (string.IsNullOrWhiteSpace(d.BatchNumber)) return OperationResult.Fail($"{label}: chưa ghi số lô.");
            if (d.ExpiryDate is null) return OperationResult.Fail($"{label}: chưa ghi hạn dùng.");
            if (d.ExpiryDate <= today) return OperationResult.Fail($"{label}: lô {d.BatchNumber} đã hết hạn — không nhập kho.");
            if (d.ManufactureDate is not null && d.ManufactureDate >= d.ExpiryDate)
                return OperationResult.Fail($"{label}: ngày sản xuất phải trước hạn dùng.");
            if (d.LocationId is null) return OperationResult.Fail($"{label}: chưa chọn kệ xếp thuốc.");
            if (l.LocationType != (byte)LocationType.Shelf || l.LocationWarehouseId != warehouseId)
                return OperationResult.Fail($"{label}: vị trí xếp thuốc phải là một Kệ của kho.");
            var days = d.ExpiryDate.Value.DayNumber - today.DayNumber;
            if (days <= warnDays) warnings.Add($"{l.MedicineName} lô {d.BatchNumber} còn {days} ngày hạn dùng");
        }

        // ---- Sinh lô (2.2.3, 2.2.4) ----
        var batches = new List<(MedicineBatch Batch, GoodsReceiptDetail Detail)>();
        foreach (var l in lines)
        {
            var d = l.Detail;
            var good = d.QuantityReceived - d.DamagedQuantity;
            if (good <= 0) continue;
            var batch = new MedicineBatch
            {
                MedicineId = d.MedicineId,
                GoodsReceiptId = receiptId,
                GoodsReceiptDetailId = d.GoodsReceiptDetailId,
                WarehouseId = warehouseId.Value,
                LocationId = d.LocationId,
                BatchNumber = d.BatchNumber!.Trim(),
                ManufactureDate = d.ManufactureDate,
                ExpiryDate = d.ExpiryDate!.Value,
                ReceivedDate = today,
                // Giá vốn trên đơn vị cơ sở (cột decimal(18,2) — xem Q-18)
                UnitCost = Math.Round(d.UnitCost / d.ConversionToBase, 2, MidpointRounding.AwayFromZero),
                QuantityOnHand = Math.Round(good * d.ConversionToBase, 3, MidpointRounding.AwayFromZero),
                Status = (byte)BatchStatus.Available
            };
            _db.MedicineBatches.Add(batch);
            batches.Add((batch, d));
        }

        try
        {
            await _db.SaveChangesAsync(ct);

            // ---- Sổ kho: mỗi lô một dòng nhập mua (loại 1, tham chiếu dòng phiếu nhập) ----
            var now = DateTime.Now;
            foreach (var (batch, detail) in batches)
            {
                _db.InventoryTransactions.Add(new InventoryTransaction
                {
                    MedicineId = batch.MedicineId,
                    BatchId = batch.BatchId,
                    WarehouseId = batch.WarehouseId,
                    TransactionType = (byte)InventoryTransactionType.PurchaseReceipt,
                    QuantityChange = batch.QuantityOnHand,
                    ReferenceType = InventoryReferenceTypes.GoodsReceiptDetail,
                    ReferenceId = detail.GoodsReceiptDetailId,
                    TransactionDate = now,
                    CreatedByEmployeeId = employeeId,
                    Notes = "Nhập kho — " + receipt.GoodsReceiptCode
                });
            }
            await _db.SaveChangesAsync(ct);

            // ---- Đơn đặt hàng: cộng số đã nhận (theo đơn vị của đơn), không vượt số đặt ----
            var goodByOrderLine = lines.Where(l => l.Detail.PurchaseOrderDetailId is not null)
                .GroupBy(l => l.Detail.PurchaseOrderDetailId!.Value)
                .Select(g => new { Id = g.Key, Good = g.Sum(l => l.Detail.QuantityReceived - l.Detail.DamagedQuantity) })
                .Where(x => x.Good > 0)
                .ToList();
            foreach (var x in goodByOrderLine)
            {
                var id = x.Id;
                var good = x.Good;
                var updated = await _db.PurchaseOrderDetails
                    .Where(p => p.PurchaseOrderDetailId == id && p.ReceivedQuantity + good <= p.OrderedQuantity)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.ReceivedQuantity, p => p.ReceivedQuantity + good), ct);
                if (updated == 0)
                {
                    _db.ChangeTracker.Clear();
                    return OperationResult.Fail("Số lượng nhận vượt số còn thiếu của đơn đặt hàng (có thể vừa có phiếu khác nhập cho đơn này). Mở lại phiếu, sửa số lượng rồi nhập kho.");
                }
            }
            if (receipt.PurchaseOrderId is not null)
            {
                var orderId = receipt.PurchaseOrderId.Value;
                var complete = !await _db.PurchaseOrderDetails.AnyAsync(p => p.PurchaseOrderId == orderId && p.ReceivedQuantity < p.OrderedQuantity, ct);
                var newStatus = complete ? (byte)PurchaseOrderStatus.Delivered : (byte)PurchaseOrderStatus.PartiallyDelivered;
                await _db.PurchaseOrders
                    .Where(p => p.PurchaseOrderId == orderId && ReceivableOrderStatuses.Contains(p.Status))
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, newStatus), ct);
            }

            // Tổng tiền phiếu = Σ (nhận − hỏng) × đơn giá
            var total = lines.Sum(l => (l.Detail.QuantityReceived - l.Detail.DamagedQuantity) * l.Detail.UnitCost);
            await _db.GoodsReceipts.Where(r => r.GoodsReceiptId == receiptId)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.TotalAmount, total), ct);

            await _audit.WriteAsync("GOODS_RECEIPT_POST", "GoodsReceipts", receiptId.ToString(CultureInfo.InvariantCulture),
                new { Status = (byte)GoodsReceiptStatus.Inspecting },
                new
                {
                    Status = (byte)GoodsReceiptStatus.Posted, receipt.GoodsReceiptCode, receipt.PurchaseOrderId, TotalAmount = total,
                    Batches = batches.Select(b => new { b.Batch.BatchId, b.Batch.MedicineId, b.Batch.BatchNumber, b.Batch.QuantityOnHand }).ToList()
                }, actorUserId, ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            return OperationResult.Fail("Không nhập kho được do dữ liệu vừa thay đổi (lô của dòng phiếu đã tồn tại hoặc vi phạm ràng buộc). Tải lại trang rồi thử lại.");
        }

        var message = $"Đã nhập kho phiếu {receipt.GoodsReceiptCode}: {batches.Count} lô, cộng tồn và ghi sổ kho.";
        return warnings.Count == 0
            ? OperationResult.Ok(message)
            : OperationResult.Ok(message, message + " Lưu ý hạn dùng ngắn (≤ " + warnDays + " ngày): " + string.Join("; ", warnings) + ".");
    }

    // ================================================================== Hủy phiếu đang kiểm nhận

    public async Task<OperationResult> CancelAsync(int receiptId, string? reason, string actorUserId, CancellationToken ct = default)
    {
        var why = AccountHelpers.NormalizeFullName(reason);
        if (why.Length == 0) return OperationResult.Fail("Nhập lý do hủy phiếu.");
        if (why.Length > 300) return OperationResult.Fail("Lý do hủy tối đa 300 ký tự.");

        var current = await _db.GoodsReceipts.AsNoTracking().Where(r => r.GoodsReceiptId == receiptId)
            .Select(r => new { r.GoodsReceiptCode, r.Status, r.Notes }).FirstOrDefaultAsync(ct);
        if (current is null) return OperationResult.Fail("Không tìm thấy phiếu nhập.");
        if (current.Status != (byte)GoodsReceiptStatus.Inspecting)
            return OperationResult.Fail($"Phiếu {current.GoodsReceiptCode} {StatusText(current.Status).ToLowerInvariant()} nên không hủy được. Hàng đã nhập kho thì lập phiếu trả nhà cung cấp (2.3.1).");

        var note = "Hủy phiếu: " + why;
        var notes = string.IsNullOrEmpty(current.Notes) ? note : current.Notes + "\n" + note;
        if (notes.Length > 1000) notes = notes[..1000];

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var updated = await _db.GoodsReceipts
            .Where(r => r.GoodsReceiptId == receiptId && r.Status == (byte)GoodsReceiptStatus.Inspecting)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, (byte)GoodsReceiptStatus.Cancelled).SetProperty(r => r.Notes, notes), ct);
        if (updated == 0) return OperationResult.Fail("Phiếu vừa được nhập kho hoặc hủy ở nơi khác. Tải lại trang.");
        await _audit.WriteAsync("GOODS_RECEIPT_CANCEL", "GoodsReceipts", receiptId.ToString(CultureInfo.InvariantCulture),
            new { Status = (byte)GoodsReceiptStatus.Inspecting }, new { Status = (byte)GoodsReceiptStatus.Cancelled, Reason = why }, actorUserId, ct);
        await tx.CommitAsync(ct);
        return OperationResult.Ok($"Đã hủy phiếu {current.GoodsReceiptCode}.");
    }

    // ================================================================== Kiểm tra form

    private sealed class CleanLine
    {
        public int? PurchaseOrderDetailId { get; init; }
        public int MedicineId { get; init; }
        public int UnitId { get; init; }
        public decimal Factor { get; init; }
        public decimal Quantity { get; init; }
        public decimal Damaged { get; init; }
        public decimal UnitCost { get; init; }
        public string? BatchNumber { get; init; }
        public DateOnly? ManufactureDate { get; init; }
        public DateOnly? ExpiryDate { get; init; }
        public int? LocationId { get; init; }
        public string? QualityNote { get; init; }
    }

    private sealed class CleanReceipt
    {
        public string? Error { get; private set; }
        public string? Field { get; private set; }
        public int SupplierId { get; set; }
        public int? PurchaseOrderId { get; set; }
        public string? InvoiceNumber { get; set; }
        public string? Notes { get; set; }
        public List<CleanLine> Lines { get; } = new();
        public decimal Total => Lines.Sum(l => (l.Quantity - l.Damaged) * l.UnitCost);

        public CleanReceipt Fail(string message, string? field = null)
        {
            Error = message;
            Field = field;
            return this;
        }
    }

    /// <summary>
    /// Kiểm tra khi lưu phiếu đang kiểm nhận. Số lô, hạn dùng, kệ được để trống (bổ sung trước khi nhập kho) nhưng nếu đã nhập thì phải đúng.
    /// <paramref name="purchaseOrderId"/>: đơn đặt hàng của phiếu (phiếu đã lập thì lấy từ CSDL).
    /// </summary>
    private async Task<CleanReceipt> ValidateFormAsync(ReceiptFormViewModel form, int? purchaseOrderId, CancellationToken ct)
    {
        var data = new CleanReceipt { PurchaseOrderId = purchaseOrderId };
        var today = Today;

        // ---- Nhà cung cấp, đơn đặt hàng ----
        if (purchaseOrderId is not null)
        {
            var po = await _db.PurchaseOrders.AsNoTracking().Where(p => p.PurchaseOrderId == purchaseOrderId)
                .Select(p => new { p.PurchaseOrderCode, p.SupplierId, p.Status }).FirstOrDefaultAsync(ct);
            if (po is null) return data.Fail("Không tìm thấy đơn đặt hàng.", nameof(form.PurchaseOrderId));
            if (!ReceivableOrderStatuses.Contains(po.Status))
                return data.Fail($"Đơn {po.PurchaseOrderCode} không ở trạng thái chờ giao hoặc giao thiếu nên không nhập theo đơn được.", nameof(form.PurchaseOrderId));
            data.SupplierId = po.SupplierId; // NCC luôn theo đơn
        }
        else
        {
            if (form.SupplierId is null) return data.Fail("Chọn nhà cung cấp.", nameof(form.SupplierId));
            data.SupplierId = form.SupplierId.Value;
        }
        var supplier = await _db.Suppliers.AsNoTracking().Where(s => s.SupplierId == data.SupplierId)
            .Select(s => new { s.SupplierName, s.IsActive }).FirstOrDefaultAsync(ct);
        if (supplier is null) return data.Fail("Không tìm thấy nhà cung cấp.", nameof(form.SupplierId));
        if (!supplier.IsActive) return data.Fail($"Nhà cung cấp {supplier.SupplierName} đã ngừng giao dịch.", nameof(form.SupplierId));

        var invoice = form.SupplierInvoiceNumber?.Trim();
        data.InvoiceNumber = string.IsNullOrEmpty(invoice) ? null : invoice;
        if (data.InvoiceNumber is { Length: > 50 }) return data.Fail("Số hóa đơn tối đa 50 ký tự.", nameof(form.SupplierInvoiceNumber));
        if (data.InvoiceNumber is not null && !PrintableAscii.IsMatch(data.InvoiceNumber))
            return data.Fail("Số hóa đơn chỉ gồm chữ không dấu, chữ số và ký hiệu.", nameof(form.SupplierInvoiceNumber));
        data.Notes = CleanText(form.Notes);
        if (data.Notes?.Length > 1000) return data.Fail("Ghi chú tối đa 1000 ký tự.", nameof(form.Notes));

        // ---- Dòng thuốc ----
        var lines = form.Lines.Where(l => l.MedicineId is not null || l.PurchaseOrderDetailId is not null || l.QuantityReceived is not null).ToList();
        if (lines.Count == 0) return data.Fail("Thêm ít nhất một dòng thuốc.", nameof(form.Lines));
        if (lines.Count > MaxLines) return data.Fail($"Một phiếu tối đa {MaxLines} dòng.", nameof(form.Lines));

        var poDetails = purchaseOrderId is null
            ? new Dictionary<int, (int MedicineId, int UnitId, decimal Ordered, decimal Received)>()
            : (await _db.PurchaseOrderDetails.AsNoTracking().Where(d => d.PurchaseOrderId == purchaseOrderId)
                    .Select(d => new { d.PurchaseOrderDetailId, d.MedicineId, d.UnitId, d.OrderedQuantity, d.ReceivedQuantity }).ToListAsync(ct))
                .ToDictionary(d => d.PurchaseOrderDetailId, d => (MedicineId: d.MedicineId, UnitId: d.UnitId, Ordered: d.OrderedQuantity, Received: d.ReceivedQuantity));

        var medicineIds = lines.Select(l => l.PurchaseOrderDetailId is not null && poDetails.TryGetValue(l.PurchaseOrderDetailId.Value, out var p) ? p.MedicineId : l.MedicineId)
            .Where(id => id is not null).Select(id => id!.Value).Distinct().ToList();
        var medicines = await _db.Medicines.AsNoTracking().Where(m => medicineIds.Contains(m.MedicineId))
            .Select(m => new { m.MedicineId, m.MedicineName, m.IsActive, m.BaseUnitId }).ToDictionaryAsync(m => m.MedicineId, ct);
        var factors = (await _db.MedicineUnitConversions.AsNoTracking().Where(c => medicineIds.Contains(c.MedicineId))
                .Select(c => new { c.MedicineId, c.UnitId, c.ConversionToBase }).ToListAsync(ct))
            .ToDictionary(c => (c.MedicineId, c.UnitId), c => c.ConversionToBase);
        var shelves = (await _db.StorageLocations.AsNoTracking().Where(l => l.LocationType == (byte)LocationType.Shelf)
            .Select(l => l.LocationId).ToListAsync(ct)).ToHashSet();

        var goodByOrderLine = new Dictionary<int, decimal>();
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var n = i + 1;
            int medicineId, unitId;
            if (line.PurchaseOrderDetailId is not null)
            {
                if (!poDetails.TryGetValue(line.PurchaseOrderDetailId.Value, out var pd))
                    return data.Fail($"Dòng {n}: không thuộc đơn đặt hàng của phiếu.", nameof(form.Lines));
                medicineId = pd.MedicineId;
                unitId = pd.UnitId;
            }
            else
            {
                if (line.MedicineId is null) return data.Fail($"Dòng {n}: chọn thuốc.", nameof(form.Lines));
                if (line.UnitId is null) return data.Fail($"Dòng {n}: chọn đơn vị nhập.", nameof(form.Lines));
                medicineId = line.MedicineId.Value;
                unitId = line.UnitId.Value;
            }
            if (!medicines.TryGetValue(medicineId, out var med)) return data.Fail($"Dòng {n}: không tìm thấy thuốc.", nameof(form.Lines));
            var label = $"Dòng {n} ({med.MedicineName})";
            if (!med.IsActive) return data.Fail($"{label}: thuốc đã ngừng kinh doanh.", nameof(form.Lines));

            decimal factor;
            if (unitId == med.BaseUnitId) factor = 1m;
            else if (!factors.TryGetValue((medicineId, unitId), out factor))
                return data.Fail($"{label}: đơn vị nhập không có trong quy đổi của thuốc — khai báo quy đổi ở Danh mục thuốc (1.1.4).", nameof(form.Lines));

            var qty = line.QuantityReceived;
            if (qty is null or <= 0) return data.Fail($"{label}: nhập số lượng nhận lớn hơn 0.", nameof(form.Lines));
            if (qty > MaxQuantity || decimal.Round(qty.Value, 3) != qty) return data.Fail($"{label}: số lượng nhận không hợp lệ (tối đa 3 chữ số lẻ).", nameof(form.Lines));
            var damaged = line.DamagedQuantity ?? 0;
            if (damaged < 0 || damaged > qty || decimal.Round(damaged, 3) != damaged)
                return data.Fail($"{label}: số lượng hỏng từ 0 đến số lượng nhận.", nameof(form.Lines));
            if (line.UnitCost is null) return data.Fail($"{label}: nhập đơn giá nhập.", nameof(form.Lines));

            var note = AccountHelpers.NormalizeFullName(line.QualityNote);
            if (note.Length > 500) return data.Fail($"{label}: ghi chú chất lượng tối đa 500 ký tự.", nameof(form.Lines));
            if (damaged > 0 && note.Length == 0)
                return data.Fail($"{label}: có hàng hỏng — ghi chú chất lượng (hỏng thế nào, đã báo nhà cung cấp chưa).", nameof(form.Lines));

            var batch = line.BatchNumber?.Trim();
            if (string.IsNullOrEmpty(batch)) batch = null;
            if (batch is not null && (batch.Length > 60 || !BatchPattern.IsMatch(batch)))
                return data.Fail($"{label}: số lô chỉ gồm chữ không dấu, chữ số và . / - _ (tối đa 60 ký tự).", nameof(form.Lines));

            var mfgError = ParseDate(line.ManufactureDate, out var mfg);
            if (mfgError) return data.Fail($"{label}: ngày sản xuất chưa đúng dạng.", nameof(form.Lines));
            var expError = ParseDate(line.ExpiryDate, out var exp);
            if (expError) return data.Fail($"{label}: hạn dùng chưa đúng dạng.", nameof(form.Lines));
            if (mfg is not null && mfg > today) return data.Fail($"{label}: ngày sản xuất không được sau hôm nay.", nameof(form.Lines));
            if (exp is not null && exp <= today) return data.Fail($"{label}: hàng đã hết hạn dùng — không nhập kho.", nameof(form.Lines));
            if (mfg is not null && exp is not null && mfg >= exp) return data.Fail($"{label}: ngày sản xuất phải trước hạn dùng.", nameof(form.Lines));

            if (line.LocationId is not null && !shelves.Contains(line.LocationId.Value))
                return data.Fail($"{label}: vị trí xếp thuốc phải là một Kệ.", nameof(form.Lines));

            if (line.PurchaseOrderDetailId is not null)
            {
                var key = line.PurchaseOrderDetailId.Value;
                goodByOrderLine[key] = goodByOrderLine.GetValueOrDefault(key) + (qty.Value - damaged);
                var pd = poDetails[key];
                var remaining = pd.Ordered - pd.Received;
                if (goodByOrderLine[key] > remaining)
                    return data.Fail($"{label}: số nhận tốt vượt số còn thiếu trên đơn ({Format.Number(remaining)}). Hàng giao dư thêm thành dòng ngoài đơn.", nameof(form.Lines));
            }

            data.Lines.Add(new CleanLine
            {
                PurchaseOrderDetailId = line.PurchaseOrderDetailId,
                MedicineId = medicineId,
                UnitId = unitId,
                Factor = factor,
                Quantity = qty.Value,
                Damaged = damaged,
                UnitCost = line.UnitCost.Value,
                BatchNumber = batch,
                ManufactureDate = mfg,
                ExpiryDate = exp,
                LocationId = line.LocationId,
                QualityNote = note.Length == 0 ? null : note
            });
        }
        return data;
    }

    // ================================================================== Dùng chung

    private static GoodsReceiptDetail ToDetail(CleanLine l) => new()
    {
        PurchaseOrderDetailId = l.PurchaseOrderDetailId,
        MedicineId = l.MedicineId,
        UnitId = l.UnitId,
        ConversionToBase = l.Factor,
        QuantityReceived = l.Quantity,
        DamagedQuantity = l.Damaged,
        UnitCost = l.UnitCost,
        BatchNumber = l.BatchNumber,
        ManufactureDate = l.ManufactureDate,
        ExpiryDate = l.ExpiryDate,
        LocationId = l.LocationId,
        QualityNote = l.QualityNote
    };

    /// <summary>true = có giá trị nhưng sai dạng.</summary>
    private static bool ParseDate(string? text, out DateOnly? value)
    {
        value = null;
        var t = text?.Trim();
        if (string.IsNullOrEmpty(t)) return false;
        if (!DateOnly.TryParseExact(t, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return true;
        value = d;
        return false;
    }

    private async Task<int> ExpiryWarningDaysAsync(CancellationToken ct)
    {
        var raw = await _db.PharmacySettings.AsNoTracking().Where(s => s.SettingKey == ExpiryWarningKey)
            .Select(s => s.SettingValue).FirstOrDefaultAsync(ct);
        return int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var days) ? days : DefaultExpiryWarningDays;
    }

    private Task<int?> EmployeeIdOfAsync(string userId, CancellationToken ct)
        => _db.Employees.AsNoTracking().Where(e => e.UserId == userId && e.IsActive).Select(e => (int?)e.EmployeeId).FirstOrDefaultAsync(ct);

    /// <summary>PN + yyMMdd + 3 số nối tiếp trong ngày (giống dữ liệu mẫu PN261031002).</summary>
    private async Task<string> NextCodeAsync(DateTime at, CancellationToken ct)
    {
        var prefix = CodePrefix + at.ToString("yyMMdd", CultureInfo.InvariantCulture);
        var codes = await _db.GoodsReceipts.AsNoTracking().Where(r => r.GoodsReceiptCode.StartsWith(prefix))
            .Select(r => r.GoodsReceiptCode).ToListAsync(ct);
        var max = 0;
        foreach (var code in codes)
            if (int.TryParse(code.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n > max) max = n;
        return prefix + (max + 1).ToString("D3", CultureInfo.InvariantCulture);
    }

    private static string StatusText(byte status) => ReceiptStatusText.Of(status);

    private static string? LocationLabel(string? parentName, string? name)
        => name is null ? null : parentName is null ? name : parentName + " › " + name;

    private static string? CleanText(string? value)
    {
        var text = value?.Trim();
        return string.IsNullOrEmpty(text) ? null : text.Replace("\r\n", "\n");
    }
}
