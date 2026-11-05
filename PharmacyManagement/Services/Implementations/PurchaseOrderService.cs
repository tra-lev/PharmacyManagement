using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PharmacyManagement.Configuration;
using PharmacyManagement.Data;
using PharmacyManagement.Helpers;
using PharmacyManagement.Models.Entities;
using PharmacyManagement.Models.Enums;
using PharmacyManagement.Models.ViewModels.Catalog;
using PharmacyManagement.Models.ViewModels.Inventory;
using PharmacyManagement.Services.Interfaces;

namespace PharmacyManagement.Services.Implementations;

public sealed class PurchaseOrderService : IPurchaseOrderService
{
    private const string CodePrefix = "DH";
    private const int NotesMax = 1000;
    private const decimal MaxQuantity = 1_000_000m;
    private const decimal MaxPrice = 1_000_000_000m;
    private const string SendAction = "PURCHASE_ORDER_SEND";
    private const string EmailAction = "PURCHASE_ORDER_EMAIL";
    private const string Entity = "PurchaseOrders";

    private readonly PharmacyDbContext _db;
    private readonly IAuditLogService _audit;
    private readonly IEmailService _email;
    private readonly string _pharmacyName;

    public PurchaseOrderService(PharmacyDbContext db, IAuditLogService audit, IEmailService email, IOptions<PharmacyOptions> pharmacy)
    {
        _db = db;
        _audit = audit;
        _email = email;
        _pharmacyName = string.IsNullOrWhiteSpace(pharmacy.Value.Name) ? "Nhà thuốc" : pharmacy.Value.Name.Trim();
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    // ================================================================== 2.1.3 Theo dõi: danh sách, chi tiết

    public async Task<PurchaseOrderListViewModel> GetListAsync(PurchaseOrderFilter filter, CancellationToken ct = default)
    {
        var query = _db.PurchaseOrders.AsNoTracking().AsQueryable();
        byte? status = filter.Status switch
        {
            "draft" => (byte)PurchaseOrderStatus.Draft,
            "awaiting" => (byte)PurchaseOrderStatus.AwaitingDelivery,
            "partial" => (byte)PurchaseOrderStatus.PartiallyDelivered,
            "delivered" => (byte)PurchaseOrderStatus.Delivered,
            "cancelled" => (byte)PurchaseOrderStatus.Cancelled,
            _ => null
        };
        if (status is null) filter.Status = null;
        else query = query.Where(p => p.Status == status);
        if (filter.SupplierId is not null) query = query.Where(p => p.SupplierId == filter.SupplierId);

        var q = AccountHelpers.NormalizeFullName(filter.Q);
        filter.Q = q.Length == 0 ? null : q;
        if (filter.Q is not null)
        {
            var text = filter.Q;
            var folded = SearchText.Fold(text);
            query = query.Where(p => p.PurchaseOrderCode.Contains(text)
                                     || EF.Functions.Collate(p.Supplier.SupplierName.Replace("đ", "d").Replace("Đ", "D"), SearchText.Collation).Contains(folded));
        }

        var total = await query.CountAsync(ct);
        var pageCount = Math.Max(1, (int)Math.Ceiling(total / (double)PurchaseOrderFilter.PageSize));
        filter.Page = Math.Clamp(filter.Page, 1, pageCount);
        var rows = await query
            .OrderByDescending(p => p.OrderDate).ThenByDescending(p => p.PurchaseOrderId)
            .Skip((filter.Page - 1) * PurchaseOrderFilter.PageSize).Take(PurchaseOrderFilter.PageSize)
            .Select(p => new PurchaseOrderRow(p.PurchaseOrderId, p.PurchaseOrderCode, p.OrderDate, p.ExpectedDeliveryDate, p.Supplier.SupplierName,
                p.PurchaseOrderDetails.Count(), p.PurchaseOrderDetails.Count(d => d.ReceivedQuantity >= d.OrderedQuantity), p.TotalAmount, p.Status))
            .ToListAsync(ct);

        return new PurchaseOrderListViewModel
        {
            Filter = filter,
            Rows = rows,
            Total = total,
            GrandTotal = await _db.PurchaseOrders.CountAsync(ct),
            PageCount = pageCount,
            Suppliers = await _db.Suppliers.AsNoTracking().OrderBy(s => s.SupplierName).Select(s => new LookupItem(s.SupplierId, s.SupplierName)).ToListAsync(ct),
            LowStockCount = (await SuggestionsAsync(ct)).Count
        };
    }

    public async Task<PurchaseOrderDetailsViewModel?> GetDetailsAsync(int purchaseOrderId, CancellationToken ct = default)
    {
        var p = await _db.PurchaseOrders.AsNoTracking().Where(x => x.PurchaseOrderId == purchaseOrderId)
            .Select(x => new
            {
                x.PurchaseOrderId, x.PurchaseOrderCode, x.Status, x.OrderDate, x.ExpectedDeliveryDate, x.Notes, x.TotalAmount,
                CreatedBy = x.CreatedByEmployee.FullName, x.SupplierId, x.Supplier.SupplierCode, x.Supplier.SupplierName, x.Supplier.Phone,
                x.Supplier.Email, x.Supplier.Address, x.Supplier.ContactPerson, SupplierActive = x.Supplier.IsActive
            })
            .FirstOrDefaultAsync(ct);
        if (p is null) return null;

        var lines = await _db.PurchaseOrderDetails.AsNoTracking()
            .Where(d => d.PurchaseOrderId == purchaseOrderId)
            .OrderBy(d => d.PurchaseOrderDetailId)
            .Select(d => new
            {
                d.Medicine.MedicineCode, d.Medicine.MedicineName, UnitName = d.Unit.UnitName, d.UnitId, d.Medicine.BaseUnitId,
                BaseUnitName = d.Medicine.BaseUnit.UnitName, d.MedicineId, d.OrderedQuantity, d.ReceivedQuantity, d.ExpectedUnitPrice
            })
            .ToListAsync(ct);
        var medicineIds = lines.Select(l => l.MedicineId).Distinct().ToList();
        var factors = await _db.MedicineUnitConversions.AsNoTracking()
            .Where(c => medicineIds.Contains(c.MedicineId))
            .Select(c => new { c.MedicineId, c.UnitId, c.ConversionToBase })
            .ToListAsync(ct);

        var receipts = await _db.GoodsReceipts.AsNoTracking()
            .Where(r => r.PurchaseOrderId == purchaseOrderId)
            .OrderBy(r => r.ReceiptDate).ThenBy(r => r.GoodsReceiptId)
            .Select(r => new PurchaseOrderReceiptInfo(r.GoodsReceiptId, r.GoodsReceiptCode, r.ReceiptDate, r.TotalAmount, r.Status))
            .ToListAsync(ct);

        var key = purchaseOrderId.ToString(CultureInfo.InvariantCulture);
        var logs = await _db.AuditLogs.AsNoTracking()
            .Where(a => a.EntityName == Entity && a.EntityId == key && (a.ActionType == SendAction || a.ActionType == EmailAction))
            .OrderBy(a => a.AuditLogId)
            .Select(a => new { a.ActionType, a.CreatedAt })
            .ToListAsync(ct);

        return new PurchaseOrderDetailsViewModel
        {
            PurchaseOrderId = p.PurchaseOrderId,
            Code = p.PurchaseOrderCode,
            Status = p.Status,
            OrderDate = p.OrderDate,
            ExpectedDeliveryDate = p.ExpectedDeliveryDate,
            Notes = p.Notes,
            TotalAmount = p.TotalAmount,
            CreatedByName = p.CreatedBy,
            SupplierId = p.SupplierId,
            SupplierCode = p.SupplierCode,
            SupplierName = p.SupplierName,
            SupplierPhone = ContactRules.DisplayPhone(p.Phone),
            SupplierEmail = p.Email,
            SupplierAddress = p.Address,
            SupplierContact = p.ContactPerson,
            SupplierActive = p.SupplierActive,
            Lines = lines.Select(l => new PurchaseOrderLineInfo(l.MedicineCode, l.MedicineName, l.UnitName,
                l.UnitId == l.BaseUnitId ? 1m : factors.FirstOrDefault(f => f.MedicineId == l.MedicineId && f.UnitId == l.UnitId)?.ConversionToBase ?? 1m,
                l.BaseUnitName, l.OrderedQuantity, l.ReceivedQuantity, l.ExpectedUnitPrice)).ToList(),
            Receipts = receipts,
            SentAt = logs.FirstOrDefault(l => l.ActionType == SendAction)?.CreatedAt,
            EmailedAt = logs.Where(l => l.ActionType == EmailAction).Select(l => l.CreatedAt).ToList(),
            PharmacyName = _pharmacyName
        };
    }

    // ================================================================== 2.1.1 Lập / sửa đơn

    public async Task<PurchaseOrderFormViewModel> NewFormAsync(int? supplierId, bool suggest, CancellationToken ct = default)
    {
        var form = new PurchaseOrderFormViewModel { SupplierId = supplierId };
        if (suggest)
        {
            var suggestions = await SuggestionsAsync(ct);
            var prices = await LastPricesAsync(ct);
            form.Lines = suggestions.Select(s => new PurchaseOrderLineForm
            {
                MedicineId = s.MedicineId,
                UnitId = s.BaseUnitId,
                Quantity = Math.Ceiling(s.SuggestBase),
                UnitPrice = PriceFor(prices, s.MedicineId, s.BaseUnitId, 1m, s.DefaultPurchasePrice)
            }).ToList();
            form.SuggestedCount = form.Lines.Count;
        }
        if (form.Lines.Count == 0) form.Lines.Add(new PurchaseOrderLineForm());
        await PrepareFormAsync(form, ct);
        return form;
    }

    public async Task<PurchaseOrderFormViewModel?> EditFormAsync(int purchaseOrderId, CancellationToken ct = default)
    {
        var p = await _db.PurchaseOrders.AsNoTracking().Where(x => x.PurchaseOrderId == purchaseOrderId)
            .Select(x => new { x.PurchaseOrderId, x.PurchaseOrderCode, x.Status, x.SupplierId, x.ExpectedDeliveryDate, x.Notes })
            .FirstOrDefaultAsync(ct);
        if (p is null || p.Status != (byte)PurchaseOrderStatus.Draft) return null;
        var lines = await _db.PurchaseOrderDetails.AsNoTracking().Where(d => d.PurchaseOrderId == purchaseOrderId)
            .OrderBy(d => d.PurchaseOrderDetailId)
            .Select(d => new PurchaseOrderLineForm { MedicineId = d.MedicineId, UnitId = d.UnitId, Quantity = d.OrderedQuantity, UnitPrice = d.ExpectedUnitPrice })
            .ToListAsync(ct);
        var form = new PurchaseOrderFormViewModel
        {
            PurchaseOrderId = p.PurchaseOrderId,
            Code = p.PurchaseOrderCode,
            SupplierId = p.SupplierId,
            ExpectedDeliveryDate = p.ExpectedDeliveryDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Notes = p.Notes,
            Lines = lines
        };
        await PrepareFormAsync(form, ct);
        return form;
    }

    public async Task PrepareFormAsync(PurchaseOrderFormViewModel form, CancellationToken ct = default)
    {
        if (form.PurchaseOrderId is not null && form.Code is null)
        {
            var id = form.PurchaseOrderId.Value;
            form.Code = await _db.PurchaseOrders.AsNoTracking().Where(p => p.PurchaseOrderId == id).Select(p => p.PurchaseOrderCode).FirstOrDefaultAsync(ct);
        }
        form.Code ??= await NextCodeAsync(DateTime.Now, ct);
        var current = form.SupplierId;
        form.SupplierOptions = await _db.Suppliers.AsNoTracking()
            .Where(s => s.IsActive || s.SupplierId == current)
            .OrderBy(s => s.SupplierName)
            .Select(s => new LookupItem(s.SupplierId, s.SupplierName + " (" + s.SupplierCode + ")"))
            .ToListAsync(ct);
        form.MedicineOptions = await MedicineOptionsAsync(form.Lines.Where(l => l.MedicineId is not null).Select(l => l.MedicineId!.Value).ToList(), ct);
        form.Today = Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        form.LowStockCount = (await SuggestionsAsync(ct)).Count;
        if (form.Lines.Count == 0) form.Lines.Add(new PurchaseOrderLineForm());
    }

    public async Task<OperationResult<int>> CreateAsync(PurchaseOrderFormViewModel form, string userId, CancellationToken ct = default)
    {
        var employeeId = await EmployeeIdAsync(userId, ct);
        if (employeeId is null) return OperationResult<int>.Fail("Tài khoản chưa gắn hồ sơ nhân viên đang làm việc nên chưa lập đơn được.");
        var clean = await ValidateAsync(form, ct);
        if (clean.Error is not null) return OperationResult<int>.Fail(clean.Error, clean.Field);

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var order = new PurchaseOrder
            {
                PurchaseOrderCode = await NextCodeAsync(DateTime.Now, ct),
                SupplierId = clean.SupplierId,
                CreatedByEmployeeId = employeeId.Value,
                OrderDate = DateTime.Now,
                ExpectedDeliveryDate = clean.ExpectedDate,
                Status = (byte)PurchaseOrderStatus.Draft,
                Notes = clean.Notes,
                TotalAmount = clean.Lines.Sum(l => l.OrderedQuantity * l.ExpectedUnitPrice)
            };
            foreach (var l in clean.Lines) order.PurchaseOrderDetails.Add(l);
            _db.PurchaseOrders.Add(order);
            await _db.SaveChangesAsync(ct);
            await _audit.WriteAsync("PURCHASE_ORDER_CREATE", Entity, order.PurchaseOrderId.ToString(CultureInfo.InvariantCulture), null,
                new { order.PurchaseOrderCode, order.SupplierId, order.ExpectedDeliveryDate, order.TotalAmount, Lines = clean.Lines.Count }, userId, ct);
            await tx.CommitAsync(ct);
            return OperationResult<int>.Ok(order.PurchaseOrderId, $"Đã lưu đơn nháp {order.PurchaseOrderCode} — {Format.Money(order.TotalAmount)}.");
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            return OperationResult<int>.Fail("Chưa lưu được. Bấm Lưu lại.");
        }
    }

    public async Task<OperationResult> UpdateAsync(int purchaseOrderId, PurchaseOrderFormViewModel form, string userId, CancellationToken ct = default)
    {
        var clean = await ValidateAsync(form, ct);
        if (clean.Error is not null) return OperationResult.Fail(clean.Error, clean.Field);

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var order = await _db.PurchaseOrders.Include(p => p.PurchaseOrderDetails).FirstOrDefaultAsync(p => p.PurchaseOrderId == purchaseOrderId, ct);
            if (order is null) return OperationResult.Fail("Không tìm thấy đơn đặt hàng.");
            if (order.Status != (byte)PurchaseOrderStatus.Draft) return OperationResult.Fail($"Đơn {order.PurchaseOrderCode} đã gửi nên không sửa được.");
            var old = new { order.SupplierId, order.ExpectedDeliveryDate, order.TotalAmount, Lines = order.PurchaseOrderDetails.Count };

            _db.PurchaseOrderDetails.RemoveRange(order.PurchaseOrderDetails);
            order.PurchaseOrderDetails.Clear();
            order.SupplierId = clean.SupplierId;
            order.ExpectedDeliveryDate = clean.ExpectedDate;
            order.Notes = clean.Notes;
            order.TotalAmount = clean.Lines.Sum(l => l.OrderedQuantity * l.ExpectedUnitPrice);
            foreach (var l in clean.Lines) order.PurchaseOrderDetails.Add(l);
            await _db.SaveChangesAsync(ct);
            await _audit.WriteAsync("PURCHASE_ORDER_UPDATE", Entity, purchaseOrderId.ToString(CultureInfo.InvariantCulture), old,
                new { order.SupplierId, order.ExpectedDeliveryDate, order.TotalAmount, Lines = clean.Lines.Count }, userId, ct);
            await tx.CommitAsync(ct);
            return OperationResult.Ok($"Đã lưu đơn nháp {order.PurchaseOrderCode} — {Format.Money(order.TotalAmount)}.");
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            return OperationResult.Fail("Chưa lưu được. Bấm Lưu lại.");
        }
    }

    // ================================================================== 2.1.2 Gửi đơn, gửi email

    public async Task<OperationResult> SendAsync(int purchaseOrderId, string userId, CancellationToken ct = default)
    {
        var p = await _db.PurchaseOrders.AsNoTracking().Where(x => x.PurchaseOrderId == purchaseOrderId)
            .Select(x => new { x.PurchaseOrderCode, x.Status, SupplierActive = x.Supplier.IsActive, x.Supplier.SupplierName, Lines = x.PurchaseOrderDetails.Count() })
            .FirstOrDefaultAsync(ct);
        if (p is null) return OperationResult.Fail("Không tìm thấy đơn đặt hàng.");
        if (p.Status != (byte)PurchaseOrderStatus.Draft) return OperationResult.Fail($"Đơn {p.PurchaseOrderCode} đã gửi trước đó.");
        if (!p.SupplierActive) return OperationResult.Fail($"Nhà cung cấp {p.SupplierName} đã ngừng giao dịch. Đổi nhà cung cấp rồi gửi lại.");
        if (p.Lines == 0) return OperationResult.Fail("Đơn chưa có thuốc nào.");

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var updated = await _db.PurchaseOrders
            .Where(x => x.PurchaseOrderId == purchaseOrderId && x.Status == (byte)PurchaseOrderStatus.Draft)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, (byte)PurchaseOrderStatus.AwaitingDelivery), ct);
        if (updated == 0) return OperationResult.Fail("Đơn vừa được gửi hoặc hủy ở nơi khác. Tải lại trang.");
        await _audit.WriteAsync(SendAction, Entity, purchaseOrderId.ToString(CultureInfo.InvariantCulture),
            new { Status = (byte)PurchaseOrderStatus.Draft }, new { Status = (byte)PurchaseOrderStatus.AwaitingDelivery, p.PurchaseOrderCode }, userId, ct);
        await tx.CommitAsync(ct);
        return OperationResult.Ok($"Đã gửi đơn {p.PurchaseOrderCode}, chờ nhà cung cấp giao hàng. In đơn hoặc gửi email cho nhà cung cấp.");
    }

    public async Task<OperationResult> EmailSupplierAsync(int purchaseOrderId, string userId, CancellationToken ct = default)
    {
        var details = await GetDetailsAsync(purchaseOrderId, ct);
        if (details is null) return OperationResult.Fail("Không tìm thấy đơn đặt hàng.");
        if (!details.IsOpen) return OperationResult.Fail("Chỉ gửi email cho đơn đã gửi, đang chờ giao.");
        if (string.IsNullOrWhiteSpace(details.SupplierEmail) || !ContactRules.IsValidEmail(details.SupplierEmail))
            return OperationResult.Fail($"Nhà cung cấp {details.SupplierName} chưa có email. Thêm email ở Danh mục nhà cung cấp.");
        if (!_email.IsConfigured) return OperationResult.Fail("Hệ thống chưa cài đặt tài khoản gửi email. Báo chủ nhà thuốc.");

        var employeeId = await EmployeeIdAsync(userId, ct) ?? 0;
        var message = new EmailMessage(details.SupplierEmail.Trim(), details.SupplierContact ?? details.SupplierName,
            $"[{EmailTemplates.PharmacyNameToken}] Đơn đặt hàng {details.Code}", BuildEmail(details));
        var sent = await _email.SendAsync(message, "purchase-order", employeeId, ct);
        if (!sent.Succeeded) return OperationResult.Fail(sent.ErrorMessage ?? "Chưa gửi được email. Thử lại sau.");
        await _audit.WriteAsync(EmailAction, Entity, purchaseOrderId.ToString(CultureInfo.InvariantCulture), null,
            new { details.Code, To = details.SupplierEmail }, userId, ct);
        return OperationResult.Ok($"Đã gửi email đơn {details.Code} tới {details.SupplierEmail}.");
    }

    // ================================================================== Hủy đơn

    public async Task<OperationResult> CancelAsync(int purchaseOrderId, string? reason, string userId, CancellationToken ct = default)
    {
        var why = AccountHelpers.NormalizeFullName(reason);
        if (why.Length == 0) return OperationResult.Fail("Nhập lý do hủy đơn.");
        if (why.Length > 300) return OperationResult.Fail("Lý do hủy tối đa 300 ký tự.");

        var p = await _db.PurchaseOrders.AsNoTracking().Where(x => x.PurchaseOrderId == purchaseOrderId)
            .Select(x => new { x.PurchaseOrderCode, x.Status, x.Notes, Received = x.PurchaseOrderDetails.Sum(d => (decimal?)d.ReceivedQuantity) ?? 0 })
            .FirstOrDefaultAsync(ct);
        if (p is null) return OperationResult.Fail("Không tìm thấy đơn đặt hàng.");
        if (p.Status is not ((byte)PurchaseOrderStatus.Draft or (byte)PurchaseOrderStatus.AwaitingDelivery) || p.Received > 0)
            return OperationResult.Fail($"Đơn {p.PurchaseOrderCode} {PurchaseOrderStatusText.Of(p.Status).ToLowerInvariant()} nên không hủy được.");
        var receipt = await _db.GoodsReceipts.AsNoTracking()
            .Where(r => r.PurchaseOrderId == purchaseOrderId && r.Status != (byte)GoodsReceiptStatus.Cancelled)
            .Select(r => r.GoodsReceiptCode).FirstOrDefaultAsync(ct);
        if (receipt is not null) return OperationResult.Fail($"Đơn đang có phiếu nhập {receipt}. Hủy phiếu nhập đó trước.");

        var note = "Hủy đơn: " + why;
        var notes = string.IsNullOrEmpty(p.Notes) ? note : p.Notes + "\n" + note;
        if (notes.Length > NotesMax) notes = notes[..NotesMax];
        var from = p.Status;

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var updated = await _db.PurchaseOrders
            .Where(x => x.PurchaseOrderId == purchaseOrderId && x.Status == from)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, (byte)PurchaseOrderStatus.Cancelled).SetProperty(x => x.Notes, notes), ct);
        if (updated == 0) return OperationResult.Fail("Đơn vừa thay đổi ở nơi khác. Tải lại trang.");
        await _audit.WriteAsync("PURCHASE_ORDER_CANCEL", Entity, purchaseOrderId.ToString(CultureInfo.InvariantCulture),
            new { Status = from }, new { Status = (byte)PurchaseOrderStatus.Cancelled, p.PurchaseOrderCode, Reason = why }, userId, ct);
        await tx.CommitAsync(ct);
        return OperationResult.Ok($"Đã hủy đơn {p.PurchaseOrderCode}.");
    }

    // ================================================================== Kiểm tra form

    private sealed class CleanOrder
    {
        public string? Error { get; private set; }
        public string? Field { get; private set; }
        public int SupplierId { get; set; }
        public DateOnly? ExpectedDate { get; set; }
        public string? Notes { get; set; }
        public List<PurchaseOrderDetail> Lines { get; } = new();

        public CleanOrder Fail(string error, string? field = null)
        {
            Error = error;
            Field = field;
            return this;
        }
    }

    private async Task<CleanOrder> ValidateAsync(PurchaseOrderFormViewModel form, CancellationToken ct)
    {
        var data = new CleanOrder();
        if (form.SupplierId is null) return data.Fail("Chọn nhà cung cấp.", nameof(form.SupplierId));
        var supplierId = form.SupplierId.Value;
        var active = await _db.Suppliers.AsNoTracking().Where(s => s.SupplierId == supplierId).Select(s => (bool?)s.IsActive).FirstOrDefaultAsync(ct);
        if (active is null) return data.Fail("Không tìm thấy nhà cung cấp.", nameof(form.SupplierId));
        if (active == false) return data.Fail("Nhà cung cấp đã ngừng giao dịch — chọn nhà cung cấp khác.", nameof(form.SupplierId));
        data.SupplierId = supplierId;

        if (!string.IsNullOrWhiteSpace(form.ExpectedDeliveryDate))
        {
            if (!DateOnly.TryParseExact(form.ExpectedDeliveryDate.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                return data.Fail("Ngày dự kiến giao chưa đúng.", nameof(form.ExpectedDeliveryDate));
            if (date < Today) return data.Fail("Ngày dự kiến giao không được trước hôm nay.", nameof(form.ExpectedDeliveryDate));
            data.ExpectedDate = date;
        }
        var notes = form.Notes?.Trim().Replace("\r\n", "\n");
        if (notes is { Length: > NotesMax }) return data.Fail("Ghi chú tối đa 1000 ký tự.", nameof(form.Notes));
        data.Notes = string.IsNullOrEmpty(notes) ? null : notes;

        var lines = form.Lines.Where(l => l.MedicineId is not null || l.UnitId is not null || l.Quantity is not null || l.UnitPrice is not null).ToList();
        if (lines.Count == 0) return data.Fail("Thêm ít nhất một thuốc vào đơn.");
        if (lines.Count > PurchaseOrderFormViewModel.MaxLines) return data.Fail($"Một đơn tối đa {PurchaseOrderFormViewModel.MaxLines} dòng thuốc.");

        var options = (await MedicineOptionsAsync(Array.Empty<int>(), ct)).ToDictionary(m => m.MedicineId);
        var seen = new HashSet<int>();
        for (var i = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            var label = $"Dòng {i + 1}";
            if (l.MedicineId is null) return data.Fail($"{label}: chọn thuốc.");
            if (!options.TryGetValue(l.MedicineId.Value, out var med)) return data.Fail($"{label}: thuốc đã ngừng kinh doanh.");
            if (!seen.Add(med.MedicineId)) return data.Fail($"{med.Label} bị nhập ở 2 dòng — gộp lại một dòng.");
            var unit = l.UnitId is null ? null : med.Units.FirstOrDefault(u => u.UnitId == l.UnitId);
            if (unit is null) return data.Fail($"{label} ({med.Label}): chọn đơn vị đặt.");
            if (l.Quantity is null || l.Quantity <= 0 || l.Quantity > MaxQuantity || l.Quantity * 1000 != decimal.Truncate(l.Quantity.Value * 1000))
                return data.Fail($"{label} ({med.Label}): số lượng lớn hơn 0, tối đa 3 chữ số thập phân.");
            if (l.UnitPrice is null) return data.Fail($"{label} ({med.Label}): nhập đơn giá dự kiến.");
            if (l.UnitPrice < 0 || l.UnitPrice > MaxPrice) return data.Fail($"{label} ({med.Label}): đơn giá không hợp lệ.");
            data.Lines.Add(new PurchaseOrderDetail
            {
                MedicineId = med.MedicineId,
                UnitId = unit.UnitId,
                OrderedQuantity = l.Quantity.Value,
                ExpectedUnitPrice = l.UnitPrice.Value,
                ReceivedQuantity = 0
            });
        }
        return data;
    }

    // ================================================================== Dữ liệu chọn, gợi ý đặt hàng

    /// <summary>Thuốc đang kinh doanh (+ thuốc đã có trong đơn) kèm đơn vị, giá nhập gần nhất, tồn khả dụng, mức tối thiểu.</summary>
    private async Task<List<OrderMedicineOption>> MedicineOptionsAsync(IReadOnlyCollection<int> include, CancellationToken ct)
    {
        var ids = include.ToList();
        var medicines = await _db.Medicines.AsNoTracking()
            .Where(m => m.IsActive || ids.Contains(m.MedicineId))
            .OrderBy(m => m.MedicineName).ThenBy(m => m.MedicineCode)
            .Select(m => new { m.MedicineId, m.MedicineCode, m.MedicineName, m.BaseUnitId, BaseUnitName = m.BaseUnit.UnitName, m.DefaultPurchasePrice, m.IsActive })
            .ToListAsync(ct);
        var conversions = (await _db.MedicineUnitConversions.AsNoTracking()
                .Select(c => new { c.MedicineId, c.UnitId, c.Unit.UnitName, c.ConversionToBase })
                .ToListAsync(ct))
            .GroupBy(c => c.MedicineId).ToDictionary(g => g.Key, g => g.OrderBy(c => c.ConversionToBase).ToList());
        var keywords = (await _db.MedicineIngredients.AsNoTracking().Select(i => new { i.MedicineId, i.Ingredient.IngredientName }).ToListAsync(ct))
            .GroupBy(i => i.MedicineId).ToDictionary(g => g.Key, g => string.Join(", ", g.Select(i => i.IngredientName).OrderBy(n => n)));
        var prices = await LastPricesAsync(ct);
        var stock = await StockAsync(ct);
        var minimum = await MinimumAsync(ct);

        return medicines.Where(m => m.IsActive || ids.Contains(m.MedicineId)).Select(m =>
        {
            var units = new List<OrderUnitOption> { new(m.BaseUnitId, m.BaseUnitName, 1m, PriceFor(prices, m.MedicineId, m.BaseUnitId, 1m, m.DefaultPurchasePrice)) };
            if (conversions.TryGetValue(m.MedicineId, out var list))
                units.AddRange(list.Where(c => c.UnitId != m.BaseUnitId).Select(c =>
                    new OrderUnitOption(c.UnitId, c.UnitName, c.ConversionToBase, PriceFor(prices, m.MedicineId, c.UnitId, c.ConversionToBase, m.DefaultPurchasePrice))));
            return new OrderMedicineOption(m.MedicineId, $"{m.MedicineName} ({m.MedicineCode})", m.BaseUnitName, units,
                keywords.GetValueOrDefault(m.MedicineId, string.Empty), stock.GetValueOrDefault(m.MedicineId),
                minimum.TryGetValue(m.MedicineId, out var min) ? min : null);
        }).ToList();
    }

    private sealed record Suggestion(int MedicineId, int BaseUnitId, decimal SuggestBase, decimal DefaultPurchasePrice);

    /// <summary>
    /// Thuốc đang kinh doanh có tồn khả dụng + số đang chờ giao (đơn Chờ giao / Giao thiếu) dưới mức tối thiểu (InventoryPolicies).
    /// Số gợi ý (đơn vị cơ sở) = mức tối thiểu × 2 − tồn − đang chờ giao.
    /// </summary>
    private async Task<List<Suggestion>> SuggestionsAsync(CancellationToken ct)
    {
        var minimum = await MinimumAsync(ct);
        if (minimum.Count == 0) return new List<Suggestion>();
        var stock = await StockAsync(ct);
        var onOrder = await OnOrderAsync(ct);
        var ids = minimum.Keys.ToList();
        var medicines = await _db.Medicines.AsNoTracking()
            .Where(m => m.IsActive && ids.Contains(m.MedicineId))
            .OrderBy(m => m.MedicineName)
            .Select(m => new { m.MedicineId, m.BaseUnitId, m.DefaultPurchasePrice })
            .ToListAsync(ct);
        var list = new List<Suggestion>();
        foreach (var m in medicines)
        {
            var min = minimum[m.MedicineId];
            var have = stock.GetValueOrDefault(m.MedicineId) + onOrder.GetValueOrDefault(m.MedicineId);
            if (min <= 0 || have >= min) continue;
            list.Add(new Suggestion(m.MedicineId, m.BaseUnitId, min * 2 - have, m.DefaultPurchasePrice));
        }
        return list.Take(PurchaseOrderFormViewModel.MaxLines).ToList();
    }

    /// <summary>Tồn khả dụng theo đơn vị cơ sở: lô Khả dụng, còn hạn.</summary>
    private async Task<Dictionary<int, decimal>> StockAsync(CancellationToken ct)
    {
        var today = Today;
        return await _db.MedicineBatches.AsNoTracking()
            .Where(b => b.Status == (byte)BatchStatus.Available && b.ExpiryDate > today && b.QuantityOnHand > 0)
            .GroupBy(b => b.MedicineId)
            .Select(g => new { MedicineId = g.Key, Quantity = g.Sum(b => b.QuantityOnHand) })
            .ToDictionaryAsync(x => x.MedicineId, x => x.Quantity, ct);
    }

    private async Task<Dictionary<int, decimal>> MinimumAsync(CancellationToken ct)
        => (await _db.InventoryPolicies.AsNoTracking().Select(p => new { p.MedicineId, p.MinimumQuantity }).ToListAsync(ct))
            .GroupBy(p => p.MedicineId).ToDictionary(g => g.Key, g => g.Max(p => p.MinimumQuantity));

    /// <summary>Số đang chờ giao (đơn vị cơ sở) của các đơn Chờ giao / Giao thiếu.</summary>
    private async Task<Dictionary<int, decimal>> OnOrderAsync(CancellationToken ct)
    {
        var rows = await _db.PurchaseOrderDetails.AsNoTracking()
            .Where(d => (d.PurchaseOrder.Status == (byte)PurchaseOrderStatus.AwaitingDelivery || d.PurchaseOrder.Status == (byte)PurchaseOrderStatus.PartiallyDelivered)
                        && d.OrderedQuantity > d.ReceivedQuantity)
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

    /// <summary>Đơn giá nhập gần nhất (phiếu đã nhập kho) theo (thuốc, đơn vị).</summary>
    private async Task<Dictionary<(int, int), decimal>> LastPricesAsync(CancellationToken ct)
        => (await _db.GoodsReceiptDetails.AsNoTracking()
                .Where(d => d.GoodsReceipt.Status == (byte)GoodsReceiptStatus.Posted)
                .Select(d => new { d.MedicineId, d.UnitId, d.UnitCost, d.GoodsReceipt.ReceiptDate, d.GoodsReceiptDetailId })
                .ToListAsync(ct))
            .GroupBy(d => (d.MedicineId, d.UnitId))
            .ToDictionary(g => g.Key, g => g.OrderByDescending(d => d.ReceiptDate).ThenByDescending(d => d.GoodsReceiptDetailId).First().UnitCost);

    /// <summary>Giá nhập gần nhất của đơn vị; chưa nhập lần nào thì giá nhập mặc định × hệ số (làm tròn đồng).</summary>
    private static decimal PriceFor(Dictionary<(int, int), decimal> prices, int medicineId, int unitId, decimal factor, decimal defaultPerBase)
        => prices.TryGetValue((medicineId, unitId), out var price)
            ? Math.Round(price, 0, MidpointRounding.AwayFromZero)
            : Math.Round(defaultPerBase * factor, 0, MidpointRounding.AwayFromZero);

    // ================================================================== Dùng chung

    private Task<int?> EmployeeIdAsync(string userId, CancellationToken ct)
        => _db.Employees.AsNoTracking().Where(e => e.UserId == userId && e.IsActive).Select(e => (int?)e.EmployeeId).FirstOrDefaultAsync(ct);

    /// <summary>DH + yyMMdd + 3 số nối tiếp trong ngày (giống dữ liệu mẫu DH260427001).</summary>
    private async Task<string> NextCodeAsync(DateTime at, CancellationToken ct)
    {
        var prefix = CodePrefix + at.ToString("yyMMdd", CultureInfo.InvariantCulture);
        var codes = await _db.PurchaseOrders.AsNoTracking().Where(p => p.PurchaseOrderCode.StartsWith(prefix)).Select(p => p.PurchaseOrderCode).ToListAsync(ct);
        var max = 0;
        foreach (var code in codes)
            if (int.TryParse(code.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n > max) max = n;
        return prefix + (max + 1).ToString("D3", CultureInfo.InvariantCulture);
    }

    /// <summary>Nội dung email đơn đặt hàng (HTML đơn giản, style nội tuyến để hiện đúng trên Gmail).</summary>
    private static string BuildEmail(PurchaseOrderDetailsViewModel o)
    {
        static string E(string? s) => WebUtility.HtmlEncode(s ?? string.Empty);
        var rows = new StringBuilder();
        var i = 0;
        foreach (var l in o.Lines)
        {
            i++;
            rows.Append("<tr>")
                .Append($"<td style=\"padding:8px;border-bottom:1px solid #E3EDEB\">{i}</td>")
                .Append($"<td style=\"padding:8px;border-bottom:1px solid #E3EDEB\">{E(l.MedicineName)}<br><span style=\"color:#4E6664;font-size:12px\">{E(l.MedicineCode)}</span></td>")
                .Append($"<td style=\"padding:8px;border-bottom:1px solid #E3EDEB;text-align:right\">{E(Format.Number(l.OrderedQuantity))} {E(l.UnitName)}</td>")
                .Append($"<td style=\"padding:8px;border-bottom:1px solid #E3EDEB;text-align:right\">{E(Format.Money(l.UnitPrice))}</td>")
                .Append($"<td style=\"padding:8px;border-bottom:1px solid #E3EDEB;text-align:right\">{E(Format.Money(l.SubTotal))}</td>")
                .Append("</tr>");
        }
        var expected = o.ExpectedDeliveryDate is null ? "" :
            $"<p style=\"margin:0 0 8px\">Ngày mong muốn nhận hàng: <b>{o.ExpectedDeliveryDate.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}</b></p>";
        var notes = string.IsNullOrWhiteSpace(o.Notes) ? "" : $"<p style=\"margin:0 0 16px\">Ghi chú: {E(o.Notes).Replace("\n", "<br>")}</p>";
        return $@"<!doctype html>
<html lang=""vi""><body style=""margin:0;padding:24px;background:#E3EDEB;font-family:Segoe UI,Arial,sans-serif;color:#13302F"">
<div style=""max-width:680px;margin:0 auto;background:#ffffff;border-radius:20px;padding:32px"">
<p style=""margin:0 0 20px;font-size:18px;font-weight:700"">{EmailTemplates.PharmacyNameToken}</p>
<p style=""margin:0 0 12px"">Kính gửi {E(o.SupplierContact ?? o.SupplierName)} — {E(o.SupplierName)},</p>
<p style=""margin:0 0 16px;line-height:1.6"">Nhà thuốc gửi đơn đặt hàng <b>{E(o.Code)}</b> lập ngày {o.OrderDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}. Mong quý công ty xác nhận và giao hàng.</p>
{expected}
<table style=""width:100%;border-collapse:collapse;font-size:14px;margin:0 0 16px"">
<thead><tr style=""background:#F1F6F5"">
<th style=""padding:8px;text-align:left"">#</th><th style=""padding:8px;text-align:left"">Thuốc</th>
<th style=""padding:8px;text-align:right"">Số lượng</th><th style=""padding:8px;text-align:right"">Đơn giá dự kiến</th><th style=""padding:8px;text-align:right"">Thành tiền</th>
</tr></thead>
<tbody>{rows}</tbody>
<tfoot><tr><td colspan=""4"" style=""padding:8px;text-align:right;font-weight:700"">Tổng cộng</td><td style=""padding:8px;text-align:right;font-weight:700"">{E(Format.Money(o.TotalAmount))}</td></tr></tfoot>
</table>
{notes}
<p style=""margin:0;color:#4E6664;font-size:12px"">Email gửi từ hệ thống quản lý nhà thuốc. Vui lòng liên hệ nhà thuốc nếu cần trao đổi thêm.</p>
</div></body></html>";
    }
}
