using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PharmacyManagement.Data;
using PharmacyManagement.Helpers;
using PharmacyManagement.Models.Entities;
using PharmacyManagement.Models.Enums;
using PharmacyManagement.Models.ViewModels.Catalog;
using PharmacyManagement.Models.ViewModels.Inventory;
using PharmacyManagement.Services.Interfaces;

namespace PharmacyManagement.Services.Implementations;

/// <summary>Định mức tồn 1.5.2 + ngưỡng cận hạn 1.5.3 (DEC-42) cho kho đang hoạt động đầu tiên (KHO01).</summary>
public sealed class InventoryPolicyService : IInventoryPolicyService
{
    private const string ExpiryWarningKey = "ExpiryWarningDays";
    private const int DefaultExpiryWarningDays = 90;
    private const int MaxRowsPerSave = 200;

    private static readonly StringComparer NameOrder = StringComparer.Create(CultureInfo.GetCultureInfo("vi-VN"), ignoreCase: true);

    private readonly PharmacyDbContext _db;
    private readonly IAuditLogService _audit;

    public InventoryPolicyService(PharmacyDbContext db, IAuditLogService audit)
    {
        _db = db;
        _audit = audit;
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    // ================================================================== Danh sách

    public async Task<PolicyListViewModel> GetListAsync(PolicyFilter filter, CancellationToken ct = default)
    {
        var warehouseId = await DefaultWarehouseAsync(ct) ?? 0;
        var query = _db.Medicines.AsNoTracking().AsQueryable();

        filter.Show = filter.Show == "all" ? "all" : null;
        if (filter.Show is null) query = query.Where(m => m.IsActive);
        if (filter.CategoryId is not null) query = query.Where(m => m.CategoryId == filter.CategoryId);
        filter.Status = filter.Status is "unset" or "custom" or "below" ? filter.Status : null;

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
            .Select(m => new { m.MedicineId, m.MedicineCode, m.MedicineName, BaseUnit = m.BaseUnit.UnitName, m.IsActive })
            .ToListAsync(ct);
        var policies = await _db.InventoryPolicies.AsNoTracking()
            .Where(p => p.WarehouseId == warehouseId)
            .Select(p => new { p.MedicineId, p.MinimumQuantity, p.ExpiryWarningDays })
            .ToDictionaryAsync(p => p.MedicineId, ct);

        // Tồn bán được: lô Khả dụng, HSD sau hôm nay (cùng điều kiện xuất bán)
        var today = Today;
        var available = await _db.MedicineBatches.AsNoTracking()
            .Where(b => b.WarehouseId == warehouseId && b.Status == (byte)BatchStatus.Available && b.ExpiryDate > today && b.QuantityOnHand > 0)
            .GroupBy(b => b.MedicineId)
            .Select(g => new { MedicineId = g.Key, Quantity = g.Sum(b => b.QuantityOnHand) })
            .ToDictionaryAsync(x => x.MedicineId, x => x.Quantity, ct);

        var rows = medicines.Select(m =>
        {
            policies.TryGetValue(m.MedicineId, out var p);
            return new PolicyRow(m.MedicineId, m.MedicineCode, m.MedicineName, m.BaseUnit, m.IsActive,
                available.GetValueOrDefault(m.MedicineId), p?.MinimumQuantity ?? 0m, p?.ExpiryWarningDays);
        });
        rows = filter.Status switch
        {
            "unset" => rows.Where(r => r.Minimum == 0),
            "custom" => rows.Where(r => r.WarningDays is not null),
            "below" => rows.Where(r => r.Minimum > 0 && r.Available < r.Minimum),
            _ => rows
        };
        var ordered = rows.OrderBy(r => r.Name, NameOrder).ThenBy(r => r.Code, StringComparer.Ordinal).ToList();

        var total = ordered.Count;
        var pageCount = Math.Max(1, (int)Math.Ceiling(total / (double)PolicyFilter.PageSize));
        filter.Page = Math.Clamp(filter.Page, 1, pageCount);

        return new PolicyListViewModel
        {
            Filter = filter,
            Rows = ordered.Skip((filter.Page - 1) * PolicyFilter.PageSize).Take(PolicyFilter.PageSize).ToList(),
            Total = total,
            GrandTotal = await _db.Medicines.CountAsync(ct),
            PageCount = pageCount,
            Categories = await _db.Categories.AsNoTracking().OrderBy(c => c.CategoryName)
                .Select(c => new LookupItem(c.CategoryId, c.CategoryName)).ToListAsync(ct),
            DefaultWarningDays = await DefaultWarningDaysAsync(ct)
        };
    }

    // ================================================================== Lưu định mức / ngưỡng riêng từng thuốc

    public async Task<OperationResult> SaveAsync(IReadOnlyList<PolicyRowInput> rows, bool canEditMinimum, bool canEditWarning,
        string actorUserId, CancellationToken ct = default)
    {
        if (!canEditMinimum && !canEditWarning) return OperationResult.Fail("Tài khoản không có quyền sửa thiết lập này.");
        if (rows.Count == 0) return OperationResult.Ok("Không có thay đổi nào để lưu.");
        if (rows.Count > MaxRowsPerSave) return OperationResult.Fail("Mỗi lần lưu tối đa " + MaxRowsPerSave + " thuốc.");
        var warehouseId = await DefaultWarehouseAsync(ct);
        if (warehouseId is null) return OperationResult.Fail("Chưa có kho nào đang hoạt động.");

        var ids = rows.Select(r => r.MedicineId).Distinct().ToList();
        var names = await _db.Medicines.AsNoTracking().Where(m => ids.Contains(m.MedicineId))
            .Select(m => new { m.MedicineId, m.MedicineName }).ToDictionaryAsync(m => m.MedicineId, m => m.MedicineName, ct);
        var existing = await _db.InventoryPolicies
            .Where(p => p.WarehouseId == warehouseId && ids.Contains(p.MedicineId))
            .ToDictionaryAsync(p => p.MedicineId, ct);

        var now = DateTime.Now;
        var changes = new List<object>();
        foreach (var row in rows.GroupBy(r => r.MedicineId).Select(g => g.Last()))
        {
            if (!names.TryGetValue(row.MedicineId, out var name))
                return OperationResult.Fail("Có thuốc không còn trong danh mục. Tải lại trang rồi nhập lại.");
            existing.TryGetValue(row.MedicineId, out var policy);

            var minimum = canEditMinimum ? row.Minimum ?? 0m : policy?.MinimumQuantity ?? 0m;
            if (minimum < 0) return OperationResult.Fail($"{name}: mức tồn tối thiểu không được âm.");
            if (minimum > PolicySaveForm.MaxMinimum) return OperationResult.Fail($"{name}: mức tồn tối thiểu quá lớn.");
            if (decimal.Round(minimum, 3) != minimum) return OperationResult.Fail($"{name}: mức tồn tối thiểu tối đa 3 chữ số sau dấu thập phân.");

            var days = canEditWarning ? row.WarningDays : policy?.ExpiryWarningDays;
            if (days is < 0 or > PolicySaveForm.MaxDays)
                return OperationResult.Fail($"{name}: số ngày cận hạn từ 0 đến {PolicySaveForm.MaxDays}, hoặc để trống để dùng ngưỡng chung.");

            if (policy is null)
            {
                if (minimum == 0 && days is null) continue;
                _db.InventoryPolicies.Add(new InventoryPolicy
                {
                    MedicineId = row.MedicineId,
                    WarehouseId = warehouseId.Value,
                    MinimumQuantity = minimum,
                    ExpiryWarningDays = days,
                    UpdatedAt = now
                });
                changes.Add(new { row.MedicineId, OldMinimum = (decimal?)null, NewMinimum = minimum, OldDays = (int?)null, NewDays = days });
            }
            else if (policy.MinimumQuantity != minimum || policy.ExpiryWarningDays != days)
            {
                changes.Add(new { row.MedicineId, OldMinimum = (decimal?)policy.MinimumQuantity, NewMinimum = minimum, OldDays = policy.ExpiryWarningDays, NewDays = days });
                policy.MinimumQuantity = minimum;
                policy.ExpiryWarningDays = days;
                policy.UpdatedAt = now;
            }
        }
        if (changes.Count == 0) return OperationResult.Ok("Không có thay đổi nào để lưu.");

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            return OperationResult.Fail("Thiết lập vừa được sửa ở máy khác. Tải lại trang rồi nhập lại.");
        }
        await _audit.WriteAsync("POLICY_UPDATE", "InventoryPolicies", null, null, new { WarehouseId = warehouseId, Changes = changes }, actorUserId, ct);
        return OperationResult.Ok($"Đã lưu thiết lập cho {Format.Number(changes.Count)} thuốc.");
    }

    // ================================================================== Ngưỡng cận hạn chung

    public async Task<OperationResult> SaveDefaultWarningDaysAsync(int? days, string actorUserId, CancellationToken ct = default)
    {
        if (days is null) return OperationResult.Fail("Nhập số ngày cận hạn chung.", "days");
        if (days is < 1 or > PolicySaveForm.MaxDays) return OperationResult.Fail($"Số ngày cận hạn chung từ 1 đến {PolicySaveForm.MaxDays}.", "days");

        var employeeId = await _db.Employees.AsNoTracking().Where(e => e.UserId == actorUserId && e.IsActive)
            .Select(e => (int?)e.EmployeeId).FirstOrDefaultAsync(ct);
        var value = days.Value.ToString(CultureInfo.InvariantCulture);
        var setting = await _db.PharmacySettings.FirstOrDefaultAsync(s => s.SettingKey == ExpiryWarningKey, ct);
        var old = setting?.SettingValue;
        if (old == value) return OperationResult.Ok("Không có thay đổi nào để lưu.");

        if (setting is null)
        {
            _db.PharmacySettings.Add(new PharmacySetting
            {
                SettingKey = ExpiryWarningKey,
                SettingValue = value,
                Description = "Số ngày trước hạn dùng thì cảnh báo cận hạn (1.5.3)",
                UpdatedAt = DateTime.Now,
                UpdatedByEmployeeId = employeeId
            });
        }
        else
        {
            setting.SettingValue = value;
            setting.UpdatedAt = DateTime.Now;
            setting.UpdatedByEmployeeId = employeeId;
        }
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            return OperationResult.Fail("Ngưỡng chung vừa được sửa ở máy khác. Tải lại trang rồi thử lại.");
        }
        await _audit.WriteAsync("SETTING_UPDATE", "PharmacySettings", ExpiryWarningKey, new { SettingValue = old }, new { SettingValue = value }, actorUserId, ct);
        return OperationResult.Ok($"Đã đặt ngưỡng cận hạn chung {Format.Number(days.Value)} ngày.");
    }

    // ================================================================== Dùng chung

    private async Task<int> DefaultWarningDaysAsync(CancellationToken ct)
    {
        var raw = await _db.PharmacySettings.AsNoTracking().Where(s => s.SettingKey == ExpiryWarningKey)
            .Select(s => s.SettingValue).FirstOrDefaultAsync(ct);
        return int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var days) ? days : DefaultExpiryWarningDays;
    }

    private Task<int?> DefaultWarehouseAsync(CancellationToken ct)
        => _db.Warehouses.AsNoTracking().Where(w => w.IsActive).OrderBy(w => w.WarehouseId)
            .Select(w => (int?)w.WarehouseId).FirstOrDefaultAsync(ct);
}
