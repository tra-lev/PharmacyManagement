using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PharmacyManagement.Configuration;
using PharmacyManagement.Data;
using PharmacyManagement.Helpers;
using PharmacyManagement.Models.Entities;
using PharmacyManagement.Models.Enums;
using PharmacyManagement.Models.ViewModels.Catalog;
using PharmacyManagement.Models.ViewModels.Reports;
using PharmacyManagement.Services.Interfaces;

namespace PharmacyManagement.Services.Implementations;

/// <summary>Báo cáo nhập – xuất – tồn và thuốc cận hạn (DEC-49).</summary>
public sealed class StockReportService : IStockReportService
{
    private const string ExpiryWarningKey = "ExpiryWarningDays";
    private const int DefaultExpiryWarningDays = 90;

    private static readonly CultureInfo Vietnamese = CultureInfo.GetCultureInfo("vi-VN");
    private static readonly StringComparer NameOrder = StringComparer.Create(Vietnamese, ignoreCase: true);

    private readonly PharmacyDbContext _db;
    private readonly string _pharmacyName;

    public StockReportService(PharmacyDbContext db, IOptions<PharmacyOptions> pharmacy)
    {
        _db = db;
        _pharmacyName = pharmacy.Value.Name;
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    // ================================================================== Nhập – xuất – tồn

    public async Task<StockMovementViewModel> GetMovementAsync(StockMovementFilter filter, bool all, CancellationToken ct = default)
    {
        string? error = null;
        var today = Today;
        var from = ParseDate(filter.From, ref error) ?? new DateOnly(today.Year, today.Month, 1);
        var to = ParseDate(filter.To, ref error) ?? today;
        if (from > to)
        {
            (from, to) = (to, from);
            error ??= "Ngày bắt đầu sau ngày kết thúc nên đã đổi chỗ hai ngày.";
        }
        filter.From = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        filter.To = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        filter.Show = filter.Show == "moved" ? "moved" : null;
        var start = from.ToDateTime(TimeOnly.MinValue);
        var end = to.AddDays(1).ToDateTime(TimeOnly.MinValue);

        var medicines = MedicineQuery(filter.Q, filter.CategoryId, out var q);
        filter.Q = q;
        var medicineIds = medicines.Select(m => m.MedicineId);

        const byte purchase = (byte)InventoryTransactionType.PurchaseReceipt;
        const byte sale = (byte)InventoryTransactionType.SaleIssue;
        const byte supplierReturn = (byte)InventoryTransactionType.SupplierReturn;
        const byte disposal = (byte)InventoryTransactionType.Disposal;
        const byte recall = (byte)InventoryTransactionType.Recall;
        const byte stocktake = (byte)InventoryTransactionType.StocktakeAdjustment;
        const byte restock = (byte)InventoryTransactionType.CustomerReturnRestock;

        var ledger = _db.InventoryTransactions.AsNoTracking()
            .Where(t => t.TransactionDate < end && medicineIds.Contains(t.MedicineId))
            .Select(t => new { t.MedicineId, t.TransactionType, t.TransactionDate, Q = t.QuantityChange, V = t.QuantityChange * t.Batch.UnitCost });

        var sums = await ledger
            .GroupBy(x => x.MedicineId)
            .Select(g => new
            {
                MedicineId = g.Key,
                OpenQ = g.Sum(x => x.TransactionDate < start ? x.Q : 0m),
                OpenV = g.Sum(x => x.TransactionDate < start ? x.V : 0m),
                PurQ = g.Sum(x => x.TransactionDate >= start && x.TransactionType == purchase ? x.Q : 0m),
                PurV = g.Sum(x => x.TransactionDate >= start && x.TransactionType == purchase ? x.V : 0m),
                InQ = g.Sum(x => x.TransactionDate >= start && (x.TransactionType == restock || (x.TransactionType == stocktake && x.Q > 0)) ? x.Q : 0m),
                InV = g.Sum(x => x.TransactionDate >= start && (x.TransactionType == restock || (x.TransactionType == stocktake && x.Q > 0)) ? x.V : 0m),
                SoldQ = g.Sum(x => x.TransactionDate >= start && x.TransactionType == sale ? -x.Q : 0m),
                SoldV = g.Sum(x => x.TransactionDate >= start && x.TransactionType == sale ? -x.V : 0m),
                OutQ = g.Sum(x => x.TransactionDate >= start
                                  && (x.TransactionType == supplierReturn || x.TransactionType == disposal || x.TransactionType == recall
                                      || (x.TransactionType == stocktake && x.Q < 0)) ? -x.Q : 0m),
                OutV = g.Sum(x => x.TransactionDate >= start
                                  && (x.TransactionType == supplierReturn || x.TransactionType == disposal || x.TransactionType == recall
                                      || (x.TransactionType == stocktake && x.Q < 0)) ? -x.V : 0m)
            })
            .ToListAsync(ct);

        var ids = sums.Select(s => s.MedicineId).ToList();
        var info = await _db.Medicines.AsNoTracking().Where(m => ids.Contains(m.MedicineId))
            .Select(m => new { m.MedicineId, m.MedicineCode, m.MedicineName, BaseUnit = m.BaseUnit.UnitName })
            .ToDictionaryAsync(m => m.MedicineId, ct);

        var rows = sums
            .Where(s => info.ContainsKey(s.MedicineId))
            .Select(s => new StockMovementRow
            {
                MedicineId = s.MedicineId,
                Code = info[s.MedicineId].MedicineCode,
                Name = info[s.MedicineId].MedicineName,
                BaseUnit = info[s.MedicineId].BaseUnit,
                Opening = new QtyValue(s.OpenQ, s.OpenV),
                Purchased = new QtyValue(s.PurQ, s.PurV),
                OtherIn = new QtyValue(s.InQ, s.InV),
                Sold = new QtyValue(s.SoldQ, s.SoldV),
                OtherOut = new QtyValue(s.OutQ, s.OutV)
            })
            .Where(r => filter.Show == "moved" ? r.Moved : r.Moved || !r.Opening.IsZero)
            .OrderBy(r => r.Name, NameOrder)
            .ToList();

        var total = rows.Count;
        var pageCount = Math.Max(1, (int)Math.Ceiling(total / (double)StockMovementFilter.PageSize));
        filter.Page = Math.Clamp(filter.Page, 1, pageCount);

        // Tổng theo loại biến động trong kỳ
        var byTypeRaw = await ledger
            .Where(x => x.TransactionDate >= start)
            .GroupBy(x => x.TransactionType)
            .Select(g => new { Type = g.Key, In = g.Sum(x => x.Q > 0 ? x.V : 0m), Out = g.Sum(x => x.Q < 0 ? -x.V : 0m) })
            .ToListAsync(ct);
        var byType = new List<MovementTypeRow>();
        void Add(string name, bool isIn, byte type)
        {
            var row = byTypeRaw.FirstOrDefault(r => r.Type == type);
            byType.Add(new MovementTypeRow(name, isIn, row is null ? 0m : isIn ? row.In : row.Out));
        }
        Add("Nhập mua", true, purchase);
        Add("Khách trả nhập lại", true, restock);
        Add("Kiểm kê thừa", true, stocktake);
        Add("Xuất bán", false, sale);
        Add("Trả nhà cung cấp", false, supplierReturn);
        Add("Hủy thuốc", false, disposal);
        Add("Thu hồi", false, recall);
        Add("Kiểm kê thiếu", false, stocktake);

        return new StockMovementViewModel
        {
            Filter = filter,
            From = from,
            To = to,
            FilterError = error,
            PharmacyName = _pharmacyName,
            Rows = all ? rows : rows.Skip((filter.Page - 1) * StockMovementFilter.PageSize).Take(StockMovementFilter.PageSize).ToList(),
            Total = total,
            PageCount = pageCount,
            OpeningValue = rows.Sum(r => r.Opening.Value),
            InValue = rows.Sum(r => r.In.Value),
            OutValue = rows.Sum(r => r.Out.Value),
            ByType = byType,
            Categories = await CategoriesAsync(ct),
            CategoryName = await CategoryNameAsync(filter.CategoryId, ct)
        };
    }

    // ================================================================== Thuốc cận hạn

    public async Task<ExpiryReportViewModel> GetExpiryAsync(ExpiryFilter filter, CancellationToken ct = default)
    {
        var today = Today;
        var raw = await _db.PharmacySettings.AsNoTracking().Where(s => s.SettingKey == ExpiryWarningKey)
            .Select(s => s.SettingValue).FirstOrDefaultAsync(ct);
        var defaultDays = int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ? parsed : DefaultExpiryWarningDays;
        var policyDays = (await _db.InventoryPolicies.AsNoTracking().Where(p => p.ExpiryWarningDays != null)
                .Select(p => new { p.MedicineId, p.WarehouseId, p.ExpiryWarningDays }).ToListAsync(ct))
            .ToDictionary(p => (p.MedicineId, p.WarehouseId), p => p.ExpiryWarningDays!.Value);
        var maxDays = policyDays.Values.Append(defaultDays).Max();
        var horizon = today.AddDays(maxDays);

        var medicines = MedicineQuery(filter.Q, filter.CategoryId, out var q);
        filter.Q = q;
        filter.Show = filter.Show is "expired" or "near" ? filter.Show : null;
        var medicineIds = medicines.Select(m => m.MedicineId);

        var lots = await _db.MedicineBatches.AsNoTracking()
            .Where(b => b.QuantityOnHand > 0 && b.ExpiryDate <= horizon && medicineIds.Contains(b.MedicineId))
            .Select(b => new
            {
                b.BatchId, b.MedicineId, b.WarehouseId, b.Medicine.MedicineCode, b.Medicine.MedicineName, BaseUnit = b.Medicine.BaseUnit.UnitName,
                b.BatchNumber, b.ExpiryDate, b.QuantityOnHand, b.UnitCost, b.Status,
                LocationName = b.Location != null ? b.Location.LocationName : null,
                ParentName = b.Location != null && b.Location.ParentLocation != null ? b.Location.ParentLocation.LocationName : null,
                SupplierName = b.GoodsReceipt != null ? b.GoodsReceipt.Supplier.SupplierName : null
            })
            .ToListAsync(ct);

        var rows = lots
            .Select(b =>
            {
                var days = policyDays.TryGetValue((b.MedicineId, b.WarehouseId), out var d) ? d : defaultDays;
                var location = b.LocationName is null ? null : b.ParentName is null ? b.LocationName : b.ParentName + " › " + b.LocationName;
                return new ExpiryRow(b.BatchId, b.MedicineId, b.MedicineCode, b.MedicineName, b.BaseUnit, b.BatchNumber, b.ExpiryDate,
                    b.ExpiryDate.DayNumber - today.DayNumber, days, b.QuantityOnHand, b.UnitCost, b.Status, location, b.SupplierName);
            })
            .Where(r => r.DaysLeft <= r.WarningDays)
            .Where(r => filter.Show switch { "expired" => r.Expired, "near" => !r.Expired, _ => true })
            .OrderBy(r => r.ExpiryDate).ThenBy(r => r.MedicineName, NameOrder)
            .ToList();

        return new ExpiryReportViewModel
        {
            Filter = filter,
            Today = today,
            DefaultWarningDays = defaultDays,
            PharmacyName = _pharmacyName,
            Rows = rows,
            Categories = await CategoriesAsync(ct),
            CategoryName = await CategoryNameAsync(filter.CategoryId, ct)
        };
    }

    // ================================================================== Dùng chung

    private IQueryable<Medicine> MedicineQuery(string? q, int? categoryId, out string? normalized)
    {
        var query = _db.Medicines.AsNoTracking().AsQueryable();
        if (categoryId is not null) query = query.Where(m => m.CategoryId == categoryId);
        var text = AccountHelpers.NormalizeFullName(q);
        normalized = text.Length == 0 ? null : text;
        if (normalized is not null)
        {
            var raw = normalized;
            var folded = SearchText.Fold(raw);
            query = query.Where(m => m.MedicineCode.Contains(raw)
                                     || EF.Functions.Collate(m.MedicineName.Replace("đ", "d").Replace("Đ", "D"), SearchText.Collation).Contains(folded));
        }
        return query;
    }

    private async Task<IReadOnlyList<LookupItem>> CategoriesAsync(CancellationToken ct)
        => (await _db.Categories.AsNoTracking().Select(c => new { c.CategoryId, c.CategoryName }).ToListAsync(ct))
            .OrderBy(c => c.CategoryName, NameOrder)
            .Select(c => new LookupItem(c.CategoryId, c.CategoryName))
            .ToList();

    private Task<string?> CategoryNameAsync(int? categoryId, CancellationToken ct)
        => categoryId is null
            ? Task.FromResult<string?>(null)
            : _db.Categories.AsNoTracking().Where(c => c.CategoryId == categoryId).Select(c => (string?)c.CategoryName).FirstOrDefaultAsync(ct);

    private static DateOnly? ParseDate(string? text, ref string? error)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (DateOnly.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return d;
        error ??= "Ngày không hợp lệ nên đã dùng khoảng mặc định.";
        return null;
    }
}
