using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PharmacyManagement.Data;
using PharmacyManagement.Helpers;
using PharmacyManagement.Models.Entities;
using PharmacyManagement.Models.Enums;
using PharmacyManagement.Models.ViewModels.Catalog;
using PharmacyManagement.Services.Interfaces;

namespace PharmacyManagement.Services.Implementations;

public sealed class SupplierService : ISupplierService
{
    private const string CodePrefix = "NCC";

    private static readonly byte[] OpenOrderStatuses =
    {
        (byte)PurchaseOrderStatus.Draft, (byte)PurchaseOrderStatus.AwaitingDelivery, (byte)PurchaseOrderStatus.PartiallyDelivered
    };

    private readonly PharmacyDbContext _db;
    private readonly IAuditLogService _audit;

    public SupplierService(PharmacyDbContext db, IAuditLogService audit)
    {
        _db = db;
        _audit = audit;
    }

    // ================================================================== Danh sách

    public async Task<SupplierListViewModel> GetListAsync(SupplierFilter filter, CancellationToken ct = default)
    {
        var query = _db.Suppliers.AsNoTracking().AsQueryable();

        filter.Status = filter.Status is "inactive" or "all" ? filter.Status : "active";
        query = filter.Status switch
        {
            "inactive" => query.Where(s => !s.IsActive),
            "all" => query,
            _ => query.Where(s => s.IsActive)
        };

        var q = AccountHelpers.NormalizeFullName(filter.Q);
        filter.Q = q.Length == 0 ? null : q;
        var folded = string.Empty;
        if (filter.Q is not null)
        {
            // Tên NCC, người liên hệ: không phân biệt dấu (SearchText); mã, MST, số điện thoại: so bình thường
            var text = filter.Q;
            folded = SearchText.Fold(text);
            var digits = ContactRules.NormalizePhone(text) ?? string.Empty;
            var byPhone = digits.Length >= 3;
            query = query.Where(s =>
                EF.Functions.Collate(s.SupplierName.Replace("đ", "d").Replace("Đ", "D"), SearchText.Collation).Contains(folded)
                || s.SupplierCode.Contains(text)
                || (s.TaxCode != null && s.TaxCode.Contains(text))
                || (s.ContactPerson != null
                    && EF.Functions.Collate(s.ContactPerson.Replace("đ", "d").Replace("Đ", "D"), SearchText.Collation).Contains(folded))
                || (byPhone && s.Phone != null && s.Phone.Contains(digits)));
        }

        var total = await query.CountAsync(ct);
        var pageCount = Math.Max(1, (int)Math.Ceiling(total / (double)SupplierFilter.PageSize));
        filter.Page = Math.Clamp(filter.Page, 1, pageCount);

        // Đang tìm: tên BẮT ĐẦU bằng từ khóa lên trước
        var ordered = folded.Length == 0
            ? query.OrderBy(s => s.SupplierName)
            : query.OrderBy(s => EF.Functions.Collate(s.SupplierName.Replace("đ", "d").Replace("Đ", "D"), SearchText.Collation).StartsWith(folded) ? 0 : 1)
                .ThenBy(s => s.SupplierName);
        var rows = await ordered.ThenBy(s => s.SupplierCode)
            .Skip((filter.Page - 1) * SupplierFilter.PageSize).Take(SupplierFilter.PageSize)
            .Select(s => new SupplierRow(s.SupplierId, s.SupplierCode, s.SupplierName, s.Phone, s.Email, s.TaxCode, s.ContactPerson, s.IsActive))
            .ToListAsync(ct);

        return new SupplierListViewModel { Filter = filter, Rows = rows, Total = total, PageCount = pageCount };
    }

    // ================================================================== Chi tiết

    public async Task<SupplierDetailsViewModel?> GetDetailsAsync(int supplierId, CancellationToken ct = default)
    {
        var s = await _db.Suppliers.AsNoTracking().FirstOrDefaultAsync(x => x.SupplierId == supplierId, ct);
        if (s is null) return null;

        var orders = _db.PurchaseOrders.AsNoTracking().Where(o => o.SupplierId == supplierId);
        var posted = _db.GoodsReceipts.AsNoTracking()
            .Where(r => r.SupplierId == supplierId && r.Status == (byte)GoodsReceiptStatus.Posted);

        return new SupplierDetailsViewModel
        {
            SupplierId = s.SupplierId,
            Code = s.SupplierCode,
            Name = s.SupplierName,
            TaxCode = s.TaxCode,
            Phone = s.Phone,
            Email = s.Email,
            ContactPerson = s.ContactPerson,
            Address = s.Address,
            IsActive = s.IsActive,
            PurchaseOrderCount = await orders.CountAsync(ct),
            OpenPurchaseOrderCount = await orders.CountAsync(o => OpenOrderStatuses.Contains(o.Status), ct),
            PostedReceiptCount = await posted.CountAsync(ct),
            LastReceiptDate = await posted.MaxAsync(r => (DateTime?)r.ReceiptDate, ct)
        };
    }

    // ================================================================== Form

    public async Task<SupplierFormViewModel> NewFormAsync(CancellationToken ct = default)
        => new() { SupplierCode = await NextCodeAsync(ct) };

    public async Task<SupplierFormViewModel?> EditFormAsync(int supplierId, CancellationToken ct = default)
    {
        var s = await _db.Suppliers.AsNoTracking().FirstOrDefaultAsync(x => x.SupplierId == supplierId, ct);
        if (s is null) return null;
        return new SupplierFormViewModel
        {
            SupplierId = s.SupplierId,
            SupplierCode = s.SupplierCode,
            SupplierName = s.SupplierName,
            TaxCode = s.TaxCode,
            Phone = s.Phone,
            Email = s.Email,
            ContactPerson = s.ContactPerson,
            Address = s.Address
        };
    }

    // ================================================================== Thêm

    public async Task<OperationResult<int>> CreateAsync(SupplierFormViewModel form, string actorUserId, CancellationToken ct = default)
    {
        var clean = await ValidateAsync(form, supplierId: null, ct);
        if (clean.Error is not null) return OperationResult<int>.Fail(clean.Error, clean.Field);

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var supplier = new Supplier
            {
                SupplierCode = await NextCodeAsync(ct),
                SupplierName = clean.Name,
                TaxCode = clean.TaxCode,
                Phone = clean.Phone,
                Email = clean.Email,
                ContactPerson = clean.ContactPerson,
                Address = clean.Address,
                IsActive = true
            };
            _db.Suppliers.Add(supplier);
            await _db.SaveChangesAsync(ct);
            await _audit.WriteAsync("SUPPLIER_CREATE", "Suppliers", supplier.SupplierId.ToString(CultureInfo.InvariantCulture),
                null, Snapshot(supplier), actorUserId, ct);
            await tx.CommitAsync(ct);
            return OperationResult<int>.Ok(supplier.SupplierId, $"Đã thêm nhà cung cấp {supplier.SupplierCode} · {supplier.SupplierName}.");
        }
        catch (DbUpdateException)
        {
            return OperationResult<int>.Fail("Không lưu được: mã nhà cung cấp vừa được người khác dùng cùng lúc. Bấm Lưu lại.");
        }
    }

    // ================================================================== Sửa

    public async Task<OperationResult> UpdateAsync(SupplierFormViewModel form, string actorUserId, CancellationToken ct = default)
    {
        var supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.SupplierId == form.SupplierId, ct);
        if (supplier is null) return OperationResult.Fail("Không tìm thấy nhà cung cấp.");

        var clean = await ValidateAsync(form, supplier.SupplierId, ct);
        if (clean.Error is not null) return OperationResult.Fail(clean.Error, clean.Field);

        var before = Snapshot(supplier);
        supplier.SupplierName = clean.Name;
        supplier.TaxCode = clean.TaxCode;
        supplier.Phone = clean.Phone;
        supplier.Email = clean.Email;
        supplier.ContactPerson = clean.ContactPerson;
        supplier.Address = clean.Address;

        if (!_db.ChangeTracker.HasChanges()) return OperationResult.Ok("Không có thay đổi nào.");

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("SUPPLIER_UPDATE", "Suppliers", supplier.SupplierId.ToString(CultureInfo.InvariantCulture),
            before, Snapshot(supplier), actorUserId, ct);
        await tx.CommitAsync(ct);
        return OperationResult.Ok($"Đã lưu thông tin {supplier.SupplierName}.");
    }

    // ================================================================== Ngừng / mở lại giao dịch

    public async Task<OperationResult> SetActiveAsync(int supplierId, bool active, string actorUserId, CancellationToken ct = default)
    {
        var supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.SupplierId == supplierId, ct);
        if (supplier is null) return OperationResult.Fail("Không tìm thấy nhà cung cấp.");
        if (supplier.IsActive == active)
            return OperationResult.Ok(active ? "Nhà cung cấp đang giao dịch." : "Nhà cung cấp đã ngừng giao dịch từ trước.");

        supplier.IsActive = active;
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(active ? "SUPPLIER_ACTIVATE" : "SUPPLIER_DEACTIVATE", "Suppliers",
            supplierId.ToString(CultureInfo.InvariantCulture), new { isActive = !active }, new { isActive = active }, actorUserId, ct);
        await tx.CommitAsync(ct);

        if (active) return OperationResult.Ok($"Đã mở lại giao dịch với {supplier.SupplierName}.");
        var open = await _db.PurchaseOrders.CountAsync(o => o.SupplierId == supplierId && OpenOrderStatuses.Contains(o.Status), ct);
        return open > 0
            ? OperationResult.Ok($"Đã ngừng giao dịch với {supplier.SupplierName}.",
                $"Đã ngừng giao dịch với {supplier.SupplierName}. Nhà cung cấp còn {open} đơn đặt hàng đang mở (nháp, chờ giao hoặc giao thiếu) — xử lý ở mục Đặt hàng.")
            : OperationResult.Ok($"Đã ngừng giao dịch với {supplier.SupplierName}.");
    }

    // ================================================================== Kiểm tra dữ liệu

    private sealed record CleanSupplier(string? Error, string? Field, string Name, string? TaxCode, string? Phone, string? Email,
        string? ContactPerson, string? Address)
    {
        public static CleanSupplier Fail(string message, string? field = null) => new(message, field, string.Empty, null, null, null, null, null);
    }

    private async Task<CleanSupplier> ValidateAsync(SupplierFormViewModel form, int? supplierId, CancellationToken ct)
    {
        var name = AccountHelpers.NormalizeFullName(form.SupplierName);
        if (name.Length == 0) return CleanSupplier.Fail("Nhập tên nhà cung cấp.", nameof(form.SupplierName));
        if (name.Length > 200) return CleanSupplier.Fail("Tên nhà cung cấp tối đa 200 ký tự.", nameof(form.SupplierName));

        var taxCode = ContactRules.NormalizeTaxCode(form.TaxCode);
        if (!ContactRules.IsValidTaxCode(taxCode)) return CleanSupplier.Fail(ContactRules.TaxCodeMessage, nameof(form.TaxCode));

        var phone = ContactRules.NormalizePhone(form.Phone);
        if (!ContactRules.IsValidPhone(phone)) return CleanSupplier.Fail(ContactRules.PhoneMessage, nameof(form.Phone));

        var email = ContactRules.NormalizeEmail(form.Email);
        if (!ContactRules.IsValidEmail(email)) return CleanSupplier.Fail(ContactRules.EmailMessage + " Email chỉ dùng chữ không dấu.", nameof(form.Email));

        var contact = Clean(form.ContactPerson);
        if (contact?.Length > 150) return CleanSupplier.Fail("Tên người liên hệ tối đa 150 ký tự.", nameof(form.ContactPerson));
        var address = Clean(form.Address);
        if (address?.Length > 300) return CleanSupplier.Fail("Địa chỉ tối đa 300 ký tự.", nameof(form.Address));

        // Chặn trùng tên (so sánh không phân biệt hoa/thường theo collation của CSDL) và trùng mã số thuế
        var sameName = await _db.Suppliers.AsNoTracking()
            .Where(s => s.SupplierName == name && s.SupplierId != supplierId)
            .Select(s => s.SupplierCode).FirstOrDefaultAsync(ct);
        if (sameName is not null)
            return CleanSupplier.Fail($"Đã có nhà cung cấp tên “{name}” ({sameName}). Mở hồ sơ đó để cập nhật thay vì tạo mới.", nameof(form.SupplierName));

        if (taxCode is not null)
        {
            var sameTax = await _db.Suppliers.AsNoTracking()
                .Where(s => s.TaxCode == taxCode && s.SupplierId != supplierId)
                .Select(s => new { s.SupplierCode, s.SupplierName }).FirstOrDefaultAsync(ct);
            if (sameTax is not null)
                return CleanSupplier.Fail($"Mã số thuế {taxCode} đã dùng cho {sameTax.SupplierCode} · {sameTax.SupplierName}.", nameof(form.TaxCode));
        }

        return new CleanSupplier(null, null, name, taxCode, phone, email, contact, address);
    }

    private async Task<string> NextCodeAsync(CancellationToken ct)
    {
        var codes = await _db.Suppliers.AsNoTracking().Where(s => s.SupplierCode.StartsWith(CodePrefix))
            .Select(s => s.SupplierCode).ToListAsync(ct);
        var max = 0;
        foreach (var code in codes)
            if (int.TryParse(code.AsSpan(CodePrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n > max) max = n;
        return CodePrefix + (max + 1).ToString("D2", CultureInfo.InvariantCulture);
    }

    /// <summary>Ảnh chụp dữ liệu để ghi nhật ký (SYS-09).</summary>
    private static object Snapshot(Supplier s) => new
    {
        s.SupplierCode, s.SupplierName, s.TaxCode, s.Phone, s.Email, s.ContactPerson, s.Address, s.IsActive
    };

    private static string? Clean(string? value)
    {
        var text = AccountHelpers.NormalizeFullName(value);
        return text.Length == 0 ? null : text;
    }
}
