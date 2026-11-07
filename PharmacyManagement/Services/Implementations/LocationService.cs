using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PharmacyManagement.Data;
using PharmacyManagement.Helpers;
using PharmacyManagement.Models.Entities;
using PharmacyManagement.Models.Enums;
using PharmacyManagement.Models.ViewModels.Catalog;
using PharmacyManagement.Services.Interfaces;

namespace PharmacyManagement.Services.Implementations;

/// <summary>Tủ, kệ lưu trữ 1.5.1 (DEC-42) của kho đang hoạt động đầu tiên (KHO01).</summary>
public sealed class LocationService : ILocationService
{
    private static readonly Regex CodeRegex = new(LocationFormViewModel.CodePattern, RegexOptions.CultureInvariant);

    private readonly PharmacyDbContext _db;
    private readonly IAuditLogService _audit;

    public LocationService(PharmacyDbContext db, IAuditLogService audit)
    {
        _db = db;
        _audit = audit;
    }

    // ================================================================== Cây tủ – kệ

    public async Task<LocationListViewModel> GetTreeAsync(CancellationToken ct = default)
    {
        var warehouseId = await DefaultWarehouseAsync(ct);
        if (warehouseId is null) return new LocationListViewModel();

        var locations = await _db.StorageLocations.AsNoTracking()
            .Where(l => l.WarehouseId == warehouseId)
            .Select(l => new { l.LocationId, l.LocationCode, l.LocationName, l.Description, l.LocationType, l.ParentLocationId })
            .ToListAsync(ct);
        var inStock = await _db.MedicineBatches.AsNoTracking()
            .Where(b => b.LocationId != null && b.QuantityOnHand > 0)
            .GroupBy(b => b.LocationId!.Value)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, ct);
        var used = await UsedLocationIdsAsync(ct);

        var shelvesByCabinet = locations
            .Where(l => l.LocationType == (byte)LocationType.Shelf && l.ParentLocationId is not null)
            .GroupBy(l => l.ParentLocationId!.Value)
            .ToDictionary(g => g.Key, g => g
                .OrderBy(l => l.LocationCode, StringComparer.Ordinal)
                .Select(l => new ShelfNode(l.LocationId, l.LocationCode, l.LocationName, l.Description,
                    inStock.GetValueOrDefault(l.LocationId), !used.Contains(l.LocationId)))
                .ToList());

        var cabinets = locations
            .Where(l => l.LocationType == (byte)LocationType.Cabinet)
            .OrderBy(l => l.LocationCode, StringComparer.Ordinal)
            .Select(l =>
            {
                var shelves = shelvesByCabinet.GetValueOrDefault(l.LocationId) ?? new List<ShelfNode>();
                return new CabinetNode(l.LocationId, l.LocationCode, l.LocationName, l.Description, shelves.Sum(s => s.BatchCount),
                    shelves.Count == 0 && !used.Contains(l.LocationId), shelves);
            })
            .ToList();

        return new LocationListViewModel { Cabinets = cabinets };
    }

    // ================================================================== Form

    public async Task<LocationFormViewModel> NewFormAsync(byte type, int? parentId, CancellationToken ct = default)
    {
        var form = new LocationFormViewModel
        {
            LocationType = type == (byte)LocationType.Cabinet ? (byte)LocationType.Cabinet : (byte)LocationType.Shelf,
            ParentLocationId = type == (byte)LocationType.Cabinet ? null : parentId
        };
        await PrepareFormAsync(form, ct);
        return form;
    }

    public async Task<LocationFormViewModel?> EditFormAsync(int locationId, CancellationToken ct = default)
    {
        var form = await _db.StorageLocations.AsNoTracking()
            .Where(l => l.LocationId == locationId)
            .Select(l => new LocationFormViewModel
            {
                LocationId = l.LocationId,
                LocationType = l.LocationType,
                Code = l.LocationCode,
                Name = l.LocationName,
                Description = l.Description,
                ParentLocationId = l.ParentLocationId
            })
            .FirstOrDefaultAsync(ct);
        if (form is null) return null;
        form.Deletable = !await _db.StorageLocations.AnyAsync(l => l.ParentLocationId == locationId, ct)
                         && !(await UsedLocationIdsAsync(ct)).Contains(locationId);
        await PrepareFormAsync(form, ct);
        return form;
    }

    public async Task PrepareFormAsync(LocationFormViewModel form, CancellationToken ct = default)
    {
        var warehouseId = await DefaultWarehouseAsync(ct);
        form.CabinetOptions = await _db.StorageLocations.AsNoTracking()
            .Where(l => l.WarehouseId == warehouseId && l.LocationType == (byte)LocationType.Cabinet)
            .OrderBy(l => l.LocationCode)
            .Select(l => new LookupItem(l.LocationId, l.LocationCode + " · " + l.LocationName))
            .ToListAsync(ct);
    }

    // ================================================================== Thêm / sửa / xóa

    public async Task<OperationResult> CreateAsync(LocationFormViewModel form, string actorUserId, CancellationToken ct = default)
    {
        var warehouseId = await DefaultWarehouseAsync(ct);
        if (warehouseId is null) return OperationResult.Fail("Chưa có kho nào đang hoạt động.");
        var type = form.LocationType == (byte)LocationType.Cabinet ? (byte)LocationType.Cabinet : (byte)LocationType.Shelf;

        var clean = await ValidateAsync(form, type, warehouseId.Value, null, ct);
        if (clean.Error is not null) return OperationResult.Fail(clean.Error, clean.Field);

        var location = new StorageLocation
        {
            WarehouseId = warehouseId.Value,
            LocationCode = clean.Code,
            LocationName = clean.Name,
            Description = clean.Description,
            LocationType = type,
            ParentLocationId = clean.ParentId
        };
        _db.StorageLocations.Add(location);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            return OperationResult.Fail($"Mã {clean.Code} vừa được dùng ở nơi khác. Chọn mã khác.", nameof(LocationFormViewModel.Code));
        }
        await _audit.WriteAsync("LOCATION_CREATE", "StorageLocations", location.LocationId.ToString(CultureInfo.InvariantCulture), null,
            new { location.LocationCode, location.LocationName, location.LocationType, location.ParentLocationId }, actorUserId, ct);
        return OperationResult.Ok($"Đã thêm {Kind(type)} {clean.Code} · {clean.Name}.");
    }

    public async Task<OperationResult> UpdateAsync(LocationFormViewModel form, string actorUserId, CancellationToken ct = default)
    {
        var location = await _db.StorageLocations.FirstOrDefaultAsync(l => l.LocationId == form.LocationId, ct);
        if (location is null) return OperationResult.Fail("Không tìm thấy tủ, kệ.");

        // Loại (tủ / kệ) không đổi sau khi tạo
        var clean = await ValidateAsync(form, location.LocationType, location.WarehouseId, location.LocationId, ct);
        if (clean.Error is not null) return OperationResult.Fail(clean.Error, clean.Field);

        var old = new { location.LocationCode, location.LocationName, location.Description, location.ParentLocationId };
        location.LocationCode = clean.Code;
        location.LocationName = clean.Name;
        location.Description = clean.Description;
        location.ParentLocationId = clean.ParentId;
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            return OperationResult.Fail($"Mã {clean.Code} vừa được dùng ở nơi khác. Chọn mã khác.", nameof(LocationFormViewModel.Code));
        }
        await _audit.WriteAsync("LOCATION_UPDATE", "StorageLocations", location.LocationId.ToString(CultureInfo.InvariantCulture), old,
            new { location.LocationCode, location.LocationName, location.Description, location.ParentLocationId }, actorUserId, ct);
        return OperationResult.Ok($"Đã lưu {Kind(location.LocationType)} {clean.Code} · {clean.Name}.");
    }

    public async Task<OperationResult> DeleteAsync(int locationId, string actorUserId, CancellationToken ct = default)
    {
        var location = await _db.StorageLocations.FirstOrDefaultAsync(l => l.LocationId == locationId, ct);
        if (location is null) return OperationResult.Fail("Không tìm thấy tủ, kệ.");
        var kind = Kind(location.LocationType);

        if (await _db.StorageLocations.AnyAsync(l => l.ParentLocationId == locationId, ct))
            return OperationResult.Fail($"Tủ {location.LocationCode} còn kệ bên trong. Xóa hoặc chuyển các kệ sang tủ khác trước.");
        if ((await UsedLocationIdsAsync(ct)).Contains(locationId))
            return OperationResult.Fail($"{Capitalize(kind)} {location.LocationCode} đã có lô thuốc, phiếu nhập, kiểm kê hoặc sổ nhiệt độ gắn với nó nên không xóa được. Có thể đổi tên thay vì xóa.");

        var old = new { location.LocationCode, location.LocationName, location.LocationType, location.ParentLocationId };
        _db.StorageLocations.Remove(location);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            return OperationResult.Fail($"{Capitalize(kind)} {old.LocationCode} vừa được dùng nên không xóa được. Tải lại trang.");
        }
        await _audit.WriteAsync("LOCATION_DELETE", "StorageLocations", locationId.ToString(CultureInfo.InvariantCulture), old, null, actorUserId, ct);
        return OperationResult.Ok($"Đã xóa {kind} {old.LocationCode} · {old.LocationName}.");
    }

    // ================================================================== Dùng chung

    private sealed record Clean(string Code, string Name, string? Description, int? ParentId, string? Error = null, string? Field = null)
    {
        public static Clean Fail(string error, string field) => new(string.Empty, string.Empty, null, null, error, field);
    }

    private async Task<Clean> ValidateAsync(LocationFormViewModel form, byte type, int warehouseId, int? selfId, CancellationToken ct)
    {
        var code = (form.Code ?? string.Empty).Trim().ToUpperInvariant();
        if (code.Length == 0) return Clean.Fail("Nhập mã.", nameof(LocationFormViewModel.Code));
        if (code.Length > 30) return Clean.Fail("Mã tối đa 30 ký tự.", nameof(LocationFormViewModel.Code));
        if (!CodeRegex.IsMatch(code))
            return Clean.Fail("Mã chỉ gồm chữ không dấu, chữ số và dấu gạch ngang, ví dụ T01-K1.", nameof(LocationFormViewModel.Code));

        var name = AccountHelpers.NormalizeFullName(form.Name);
        if (name.Length == 0) return Clean.Fail("Nhập tên.", nameof(LocationFormViewModel.Name));
        if (name.Length > 100) return Clean.Fail("Tên tối đa 100 ký tự.", nameof(LocationFormViewModel.Name));

        var description = string.IsNullOrWhiteSpace(form.Description) ? null : form.Description.Trim();
        if (description is not null && description.Length > 300)
            return Clean.Fail("Mô tả tối đa 300 ký tự.", nameof(LocationFormViewModel.Description));

        if (await _db.StorageLocations.AnyAsync(l => l.WarehouseId == warehouseId && l.LocationCode == code && l.LocationId != selfId, ct))
            return Clean.Fail($"Mã {code} đã có. Chọn mã khác.", nameof(LocationFormViewModel.Code));

        int? parentId = null;
        if (type == (byte)LocationType.Shelf)
        {
            if (form.ParentLocationId is null) return Clean.Fail("Chọn tủ chứa kệ.", nameof(LocationFormViewModel.ParentLocationId));
            var parentOk = await _db.StorageLocations.AnyAsync(l => l.LocationId == form.ParentLocationId
                && l.WarehouseId == warehouseId && l.LocationType == (byte)LocationType.Cabinet, ct);
            if (!parentOk) return Clean.Fail("Chọn một tủ trong danh sách.", nameof(LocationFormViewModel.ParentLocationId));
            parentId = form.ParentLocationId;
        }
        return new Clean(code, name, description, parentId);
    }

    /// <summary>Tủ, kệ đã có dữ liệu gắn với nó (lô, dòng phiếu nhập, phiếu kiểm kê, sổ nhiệt độ – độ ẩm).</summary>
    private async Task<HashSet<int>> UsedLocationIdsAsync(CancellationToken ct)
    {
        var ids = new HashSet<int>();
        ids.UnionWith(await _db.MedicineBatches.AsNoTracking().Where(b => b.LocationId != null).Select(b => b.LocationId!.Value).Distinct().ToListAsync(ct));
        ids.UnionWith(await _db.GoodsReceiptDetails.AsNoTracking().Where(d => d.LocationId != null).Select(d => d.LocationId!.Value).Distinct().ToListAsync(ct));
        ids.UnionWith(await _db.Stocktakes.AsNoTracking().Where(s => s.LocationId != null).Select(s => s.LocationId!.Value).Distinct().ToListAsync(ct));
        ids.UnionWith(await _db.StorageEnvironmentLogs.AsNoTracking().Where(e => e.LocationId != null).Select(e => e.LocationId!.Value).Distinct().ToListAsync(ct));
        return ids;
    }

    private Task<int?> DefaultWarehouseAsync(CancellationToken ct)
        => _db.Warehouses.AsNoTracking().Where(w => w.IsActive).OrderBy(w => w.WarehouseId)
            .Select(w => (int?)w.WarehouseId).FirstOrDefaultAsync(ct);

    private static string Kind(byte type) => type == (byte)LocationType.Cabinet ? "tủ" : "kệ";

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
