using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PharmacyManagement.Configuration;
using PharmacyManagement.Data;
using PharmacyManagement.Models.Enums;
using PharmacyManagement.Models.ViewModels.Catalog;
using PharmacyManagement.Models.ViewModels.Reports;
using PharmacyManagement.Services.Interfaces;

namespace PharmacyManagement.Services.Implementations;

/// <summary>Báo cáo doanh thu, lợi nhuận (DEC-46). Chỉ đọc.</summary>
public sealed class RevenueReportService : IRevenueReportService
{
    private const int TopMedicines = 15;

    private static readonly CultureInfo Vietnamese = CultureInfo.GetCultureInfo("vi-VN");
    private static readonly StringComparer NameOrder = StringComparer.Create(Vietnamese, ignoreCase: true);

    private readonly PharmacyDbContext _db;
    private readonly string _pharmacyName;

    public RevenueReportService(PharmacyDbContext db, IOptions<PharmacyOptions> pharmacy)
    {
        _db = db;
        _pharmacyName = pharmacy.Value.Name;
    }

    public async Task<(int EmployeeId, string Name)?> EmployeeOfAsync(string userId, CancellationToken ct = default)
    {
        var e = await _db.Employees.AsNoTracking().Where(x => x.UserId == userId)
            .Select(x => new { x.EmployeeId, x.FullName }).FirstOrDefaultAsync(ct);
        return e is null ? null : (e.EmployeeId, e.FullName);
    }

    public async Task<RevenueReportViewModel> BuildAsync(RevenueFilter filter, int? onlyEmployeeId, bool includeProfit, CancellationToken ct = default)
    {
        var (from, to, monthly, forced, error) = Normalize(filter);
        var start = from.ToDateTime(TimeOnly.MinValue);
        var end = to.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var employeeId = onlyEmployeeId ?? filter.EmployeeId;

        // ---------------- Hóa đơn đã thanh toán, theo ngày thanh toán đủ tiền ----------------
        var paidStatus = (byte)InvoiceStatus.Paid;
        var okPayment = (byte)PaymentStatus.Succeeded;
        var paidQuery = _db.SalesInvoices.AsNoTracking()
            .Where(i => i.Status == paidStatus)
            .Select(i => new
            {
                i.InvoiceId, i.EmployeeId, i.SubTotal, i.DiscountAmount, i.TotalAmount,
                PaidAt = i.Payments.Where(p => p.Status == okPayment).Max(p => (DateTime?)p.PaidAt) ?? i.InvoiceDate
            })
            .Where(x => x.PaidAt >= start && x.PaidAt < end);
        if (employeeId is not null) paidQuery = paidQuery.Where(x => x.EmployeeId == employeeId);
        var invoices = await paidQuery.ToListAsync(ct);
        var ids = invoices.Select(i => i.InvoiceId).ToList();

        var costByInvoice = new Dictionary<int, decimal>();
        if (includeProfit && ids.Count > 0)
        {
            costByInvoice = await _db.SalesIssueAllocations.AsNoTracking()
                .Where(a => ids.Contains(a.InvoiceDetail.InvoiceId))
                .GroupBy(a => a.InvoiceDetail.InvoiceId)
                .Select(g => new { InvoiceId = g.Key, Cost = g.Sum(a => a.QuantityBase * a.UnitCostAtSale) })
                .ToDictionaryAsync(g => g.InvoiceId, g => g.Cost, ct);
        }

        // ---------------- Hoàn trả khách, theo ngày đổi trả ----------------
        var cancelledReturn = (byte)ReturnStatus.Cancelled;
        var returnQuery = _db.Returns.AsNoTracking()
            .Where(r => r.Status != cancelledReturn && r.ReturnDate >= start && r.ReturnDate < end);
        if (employeeId is not null) returnQuery = returnQuery.Where(r => r.Invoice.EmployeeId == employeeId);
        var returns = await returnQuery.Select(r => new { r.ReturnDate, r.RefundAmount, r.Invoice.EmployeeId }).ToListAsync(ct);

        // ---------------- Giá vốn hàng khách trả đã nhập lại kho ----------------
        var restocks = new List<(DateTime When, decimal Value)>();
        if (includeProfit)
        {
            var restockType = (byte)InventoryTransactionType.CustomerReturnRestock;
            var restockQuery = _db.InventoryTransactions.AsNoTracking()
                .Where(t => t.TransactionType == restockType && t.TransactionDate >= start && t.TransactionDate < end);
            if (employeeId is not null)
                restockQuery = restockQuery.Where(t => _db.ReturnDetails.Any(d => (long)d.ReturnDetailId == t.ReferenceId && d.Return.Invoice.EmployeeId == employeeId));
            restocks = (await restockQuery.Select(t => new { t.TransactionDate, Value = t.QuantityChange * t.Batch.UnitCost }).ToListAsync(ct))
                .Select(t => (t.TransactionDate, t.Value)).ToList();
        }

        // ---------------- Theo ngày / tháng ----------------
        var buckets = BuildBuckets(from, to, monthly);
        RevenueBucket BucketOf(DateTime when)
        {
            var d = DateOnly.FromDateTime(when);
            var key = monthly ? new DateOnly(d.Year, d.Month, 1) : d;
            var index = monthly
                ? (key.Year - buckets[0].Start.Year) * 12 + key.Month - buckets[0].Start.Month
                : key.DayNumber - buckets[0].Start.DayNumber;
            return buckets[Math.Clamp(index, 0, buckets.Count - 1)];
        }
        foreach (var i in invoices)
        {
            var b = BucketOf(i.PaidAt);
            b.Invoices++;
            b.Revenue += i.TotalAmount;
            b.Cost += costByInvoice.GetValueOrDefault(i.InvoiceId);
        }
        foreach (var r in returns) BucketOf(r.ReturnDate).Refunds += r.RefundAmount;
        foreach (var r in restocks) BucketOf(r.When).RestockedCost += r.Value;

        // ---------------- Thuốc bán chạy ----------------
        var topMedicines = new List<RevenueMedicineRow>();
        if (ids.Count > 0)
        {
            var lines = await _db.SalesInvoiceDetails.AsNoTracking()
                .Where(d => ids.Contains(d.InvoiceId))
                .GroupBy(d => d.MedicineId)
                .Select(g => new { MedicineId = g.Key, Quantity = g.Sum(d => d.Quantity * d.ConversionToBase), Revenue = g.Sum(d => d.LineTotal) })
                .OrderByDescending(g => g.Revenue)
                .Take(TopMedicines)
                .ToListAsync(ct);
            var medicineIds = lines.Select(l => l.MedicineId).ToList();
            var medicines = await _db.Medicines.AsNoTracking().Where(m => medicineIds.Contains(m.MedicineId))
                .Select(m => new { m.MedicineId, m.MedicineCode, m.MedicineName, BaseUnit = m.BaseUnit.UnitName })
                .ToDictionaryAsync(m => m.MedicineId, ct);
            var lineCost = includeProfit
                ? await _db.SalesIssueAllocations.AsNoTracking()
                    .Where(a => ids.Contains(a.InvoiceDetail.InvoiceId) && medicineIds.Contains(a.InvoiceDetail.MedicineId))
                    .GroupBy(a => a.InvoiceDetail.MedicineId)
                    .Select(g => new { MedicineId = g.Key, Cost = g.Sum(a => a.QuantityBase * a.UnitCostAtSale) })
                    .ToDictionaryAsync(g => g.MedicineId, g => g.Cost, ct)
                : new Dictionary<int, decimal>();
            topMedicines = lines
                .Where(l => medicines.ContainsKey(l.MedicineId))
                .Select(l =>
                {
                    var m = medicines[l.MedicineId];
                    return new RevenueMedicineRow(l.MedicineId, m.MedicineCode, m.MedicineName, m.BaseUnit, l.Quantity, l.Revenue,
                        lineCost.GetValueOrDefault(l.MedicineId));
                })
                .ToList();
        }

        // ---------------- Theo người bán ----------------
        var employees = new List<RevenueEmployeeRow>();
        if (employeeId is null && invoices.Count + returns.Count > 0)
        {
            var sellerIds = invoices.Select(i => i.EmployeeId).Concat(returns.Select(r => r.EmployeeId)).Distinct().ToList();
            var names = await _db.Employees.AsNoTracking().Where(e => sellerIds.Contains(e.EmployeeId))
                .ToDictionaryAsync(e => e.EmployeeId, e => e.FullName, ct);
            employees = sellerIds
                .Select(id => new RevenueEmployeeRow(id, names.GetValueOrDefault(id) ?? "—",
                    invoices.Count(i => i.EmployeeId == id),
                    invoices.Where(i => i.EmployeeId == id).Sum(i => i.TotalAmount),
                    returns.Where(r => r.EmployeeId == id).Sum(r => r.RefundAmount),
                    invoices.Where(i => i.EmployeeId == id).Sum(i => costByInvoice.GetValueOrDefault(i.InvoiceId))))
                .OrderByDescending(r => r.NetRevenue).ThenBy(r => r.Name, NameOrder)
                .ToList();
        }

        // ---------------- Hình thức thanh toán ----------------
        var methods = new List<RevenueMethodRow>();
        if (ids.Count > 0)
        {
            methods = (await _db.Payments.AsNoTracking()
                    .Where(p => ids.Contains(p.InvoiceId) && p.Status == okPayment)
                    .GroupBy(p => p.PaymentMethod)
                    .Select(g => new { Method = g.Key, Count = g.Count(), Amount = g.Sum(p => p.Amount) })
                    .ToListAsync(ct))
                .OrderByDescending(m => m.Amount)
                .Select(m => new RevenueMethodRow(m.Method, MethodName(m.Method), m.Count, m.Amount))
                .ToList();
        }

        // ---------------- Tổn thất hàng tồn kho (chỉ toàn nhà thuốc) ----------------
        InventoryLossSummary? losses = null;
        decimal expiredValue = 0;
        var expiredLots = 0;
        if (includeProfit && employeeId is null)
        {
            var lossTypes = new[]
            {
                (byte)InventoryTransactionType.Disposal, (byte)InventoryTransactionType.Recall, (byte)InventoryTransactionType.StocktakeAdjustment
            };
            var byType = await _db.InventoryTransactions.AsNoTracking()
                .Where(t => lossTypes.Contains(t.TransactionType) && t.TransactionDate >= start && t.TransactionDate < end)
                .GroupBy(t => t.TransactionType)
                .Select(g => new { Type = g.Key, Value = g.Sum(t => -t.QuantityChange * t.Batch.UnitCost) })
                .ToDictionaryAsync(g => g.Type, g => g.Value, ct);

            var supplierReturnType = (byte)InventoryTransactionType.SupplierReturn;
            var supplierLoss = await (
                    from t in _db.InventoryTransactions.AsNoTracking()
                    where t.TransactionType == supplierReturnType && t.TransactionDate >= start && t.TransactionDate < end
                    join d in _db.SupplierReturnDetails.AsNoTracking() on t.ReferenceId equals (long)d.SupplierReturnDetailId
                    select -t.QuantityChange * t.Batch.UnitCost - (d.LineTotal ?? 0m))
                .SumAsync(v => (decimal?)v, ct) ?? 0m;

            losses = new InventoryLossSummary
            {
                Disposal = byType.GetValueOrDefault((byte)InventoryTransactionType.Disposal),
                Recall = byType.GetValueOrDefault((byte)InventoryTransactionType.Recall),
                Stocktake = byType.GetValueOrDefault((byte)InventoryTransactionType.StocktakeAdjustment),
                SupplierReturn = supplierLoss
            };

            var today = DateOnly.FromDateTime(DateTime.Now);
            var expired = _db.MedicineBatches.AsNoTracking().Where(b => b.QuantityOnHand > 0 && b.ExpiryDate < today);
            expiredLots = await expired.CountAsync(ct);
            expiredValue = expiredLots == 0 ? 0m : await expired.SumAsync(b => b.QuantityOnHand * b.UnitCost, ct);
        }

        string? employeeName = null;
        if (employeeId is not null)
            employeeName = await _db.Employees.AsNoTracking().Where(e => e.EmployeeId == employeeId).Select(e => e.FullName).FirstOrDefaultAsync(ct);

        var sellers = onlyEmployeeId is not null
            ? new List<LookupItem>()
            : (await _db.Employees.AsNoTracking().Where(e => e.SalesInvoiceEmployees.Any())
                    .Select(e => new { e.EmployeeId, e.FullName }).ToListAsync(ct))
                .OrderBy(e => e.FullName, NameOrder)
                .Select(e => new LookupItem(e.EmployeeId, e.FullName))
                .ToList();

        filter.From = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        filter.To = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        filter.By = monthly ? "month" : "day";
        filter.EmployeeId = onlyEmployeeId is null ? employeeId : null;

        return new RevenueReportViewModel
        {
            Filter = filter,
            From = from,
            To = to,
            Monthly = monthly,
            ForcedMonthly = forced,
            FilterError = error,
            PharmacyName = _pharmacyName,
            EmployeeName = employeeName,
            Invoices = invoices.Count,
            Goods = invoices.Sum(i => i.SubTotal),
            Discount = invoices.Sum(i => i.DiscountAmount),
            Revenue = invoices.Sum(i => i.TotalAmount),
            RefundCount = returns.Count,
            Refunds = returns.Sum(r => r.RefundAmount),
            Cost = costByInvoice.Values.Sum(),
            RestockedCost = restocks.Sum(r => r.Value),
            Losses = losses,
            ExpiredStockValue = expiredValue,
            ExpiredLots = expiredLots,
            Buckets = buckets,
            TopMedicines = topMedicines,
            Employees = employees,
            Methods = methods,
            Sellers = sellers
        };
    }

    // ================================================================== Dùng chung

    private static (DateOnly From, DateOnly To, bool Monthly, bool Forced, string? Error) Normalize(RevenueFilter filter)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        string? error = null;
        var from = Parse(filter.From, ref error) ?? new DateOnly(today.Year, today.Month, 1);
        var to = Parse(filter.To, ref error) ?? today;
        if (from > to)
        {
            (from, to) = (to, from);
            error ??= "Ngày bắt đầu sau ngày kết thúc nên đã đổi chỗ hai ngày.";
        }
        if (to.DayNumber - from.DayNumber + 1 > RevenueFilter.MaxDays)
        {
            from = to.AddDays(-(RevenueFilter.MaxDays - 1));
            error ??= "Mỗi lần xem tối đa 3 năm. Đã rút ngắn khoảng thời gian.";
        }
        var monthly = filter.By == "month";
        var forced = false;
        if (!monthly && to.DayNumber - from.DayNumber + 1 > RevenueFilter.MaxDailyDays)
        {
            monthly = true;
            forced = true;
        }
        return (from, to, monthly, forced, error);
    }

    private static DateOnly? Parse(string? text, ref string? error)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (DateOnly.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return d;
        error ??= "Ngày không hợp lệ nên đã dùng khoảng mặc định.";
        return null;
    }

    private static List<RevenueBucket> BuildBuckets(DateOnly from, DateOnly to, bool monthly)
    {
        var list = new List<RevenueBucket>();
        if (monthly)
        {
            for (var m = new DateOnly(from.Year, from.Month, 1); m <= to; m = m.AddMonths(1))
                list.Add(new RevenueBucket
                {
                    Start = m,
                    Label = "Tháng " + m.ToString("MM/yyyy", Vietnamese),
                    ShortLabel = m.ToString("MM/yy", Vietnamese)
                });
        }
        else
        {
            for (var d = from; d <= to; d = d.AddDays(1))
                list.Add(new RevenueBucket
                {
                    Start = d,
                    Label = d.ToString("dd/MM/yyyy", Vietnamese),
                    ShortLabel = d.ToString("dd/MM", Vietnamese)
                });
        }
        return list;
    }

    private static string MethodName(byte method) => method switch
    {
        (byte)PaymentMethod.Cash => "Tiền mặt",
        (byte)PaymentMethod.BankTransfer => "Chuyển khoản",
        (byte)PaymentMethod.Card => "Thẻ",
        _ => "Khác"
    };
}
