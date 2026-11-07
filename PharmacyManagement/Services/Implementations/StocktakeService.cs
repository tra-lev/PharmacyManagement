using System.Data;
using System.Globalization;
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

/// <summary>Kiểm kê 2.4.x (DEC-43) cho kho đang hoạt động đầu tiên (KHO01).</summary>
public sealed class StocktakeService : IStocktakeService
{
    private const string CodePrefix = "KK";
    private const decimal MaxQuantity = 1_000_000m;

    private static readonly CultureInfo Vietnamese = CultureInfo.GetCultureInfo("vi-VN");
    private static readonly StringComparer NameOrder = StringComparer.Create(Vietnamese, ignoreCase: true);

    private static readonly byte[] OpenStatuses =
    {
        (byte)StocktakeStatus.Counting, (byte)StocktakeStatus.PendingApproval, (byte)StocktakeStatus.Approved
    };

    private readonly PharmacyDbContext _db;
    private readonly IAuditLogService _audit;
    private readonly string _pharmacyName;

    public StocktakeService(PharmacyDbContext db, IAuditLogService audit, IOptions<PharmacyOptions> pharmacy)
    {
        _db = db;
        _audit = audit;
        _pharmacyName = pharmacy.Value.Name;
    }

    // ================================================================== Danh sách

    public async Task<StocktakeListViewModel> GetListAsync(StocktakeFilter filter, CancellationToken ct = default)
    {
        var query = _db.Stocktakes.AsNoTracking().AsQueryable();

        byte? status = filter.Status switch
        {
            "counting" => (byte)StocktakeStatus.Counting,
            "pending" => (byte)StocktakeStatus.PendingApproval,
            "approved" => (byte)StocktakeStatus.Approved,
            "adjusted" => (byte)StocktakeStatus.Adjusted,
            "rejected" => (byte)StocktakeStatus.Rejected,
            _ => null
        };
        if (status is null) filter.Status = null;
        else query = query.Where(s => s.Status == status);

        var q = AccountHelpers.NormalizeFullName(filter.Q);
        filter.Q = q.Length == 0 ? null : q;
        if (filter.Q is not null)
        {
            var text = filter.Q;
            var folded = SearchText.Fold(text);
            query = query.Where(s => s.StocktakeCode.Contains(text)
                                     || (s.Notes != null && EF.Functions.Collate(s.Notes.Replace("đ", "d").Replace("Đ", "D"), SearchText.Collation).Contains(folded)));
        }

        var total = await query.CountAsync(ct);
        var pageCount = Math.Max(1, (int)Math.Ceiling(total / (double)StocktakeFilter.PageSize));
        filter.Page = Math.Clamp(filter.Page, 1, pageCount);

        var rows = await query
            .OrderByDescending(s => s.StocktakeDate).ThenByDescending(s => s.StocktakeId)
            .Skip((filter.Page - 1) * StocktakeFilter.PageSize).Take(StocktakeFilter.PageSize)
            .Select(s => new
            {
                s.StocktakeId, s.StocktakeCode, s.StocktakeDate, s.Status, CreatedBy = s.CreatedByEmployee.FullName,
                LocationName = s.Location != null ? s.Location.LocationName : null,
                ParentName = s.Location != null && s.Location.ParentLocation != null ? s.Location.ParentLocation.LocationName : null,
                CategoryName = s.Category != null ? s.Category.CategoryName : null,
                Lines = s.StocktakeDetails.Count(),
                Diffs = s.StocktakeDetails.Count(d => d.ActualQuantity != d.SystemQuantity)
            })
            .ToListAsync(ct);

        return new StocktakeListViewModel
        {
            Filter = filter,
            Rows = rows.Select(r => new StocktakeRow(r.StocktakeId, r.StocktakeCode, r.StocktakeDate,
                    ScopeLabel(r.ParentName, r.LocationName, r.CategoryName), r.Lines,
                    r.Status == (byte)StocktakeStatus.Counting ? 0 : r.Diffs, r.CreatedBy, r.Status))
                .ToList(),
            Total = total,
            GrandTotal = await _db.Stocktakes.CountAsync(ct),
            PageCount = pageCount
        };
    }

    // ================================================================== Phạm vi + phiếu đếm (2.4.1.1)

    public async Task<StocktakeScopeViewModel> ScopeFormAsync(StocktakeScope scope, CancellationToken ct = default)
    {
        var warehouseId = await DefaultWarehouseAsync(ct);
        var locations = await _db.StorageLocations.AsNoTracking()
            .Where(l => l.WarehouseId == warehouseId)
            .Select(l => new
            {
                l.LocationId, l.LocationCode, l.LocationName, l.LocationType,
                ParentCode = l.ParentLocation != null ? l.ParentLocation.LocationCode : null,
                ParentName = l.ParentLocation != null ? l.ParentLocation.LocationName : null
            })
            .ToListAsync(ct);
        string? label = null, error = null;
        var lotCount = 0;
        if (scope.Scope is not null)
        {
            var resolved = await ResolveScopeAsync(scope, ct);
            if (resolved.Error is not null) error = resolved.Error;
            else
            {
                label = resolved.Label;
                lotCount = (await LotsInScopeAsync(resolved, ct)).Count;
            }
        }
        return new StocktakeScopeViewModel
        {
            Scope = scope,
            ScopeLabel = label,
            LotCount = lotCount,
            ScopeError = error,
            Locations = locations
                .OrderBy(l => l.ParentCode ?? l.LocationCode, StringComparer.Ordinal).ThenBy(l => l.LocationType).ThenBy(l => l.LocationCode, StringComparer.Ordinal)
                .Select(l => new LookupItem(l.LocationId, LocationLabel(l.ParentName, l.LocationName)!))
                .ToList(),
            Categories = await _db.Categories.AsNoTracking().OrderBy(c => c.CategoryName)
                .Select(c => new LookupItem(c.CategoryId, c.CategoryName)).ToListAsync(ct)
        };
    }

    public async Task<CountSheetViewModel?> CountSheetAsync(int? stocktakeId, StocktakeScope scope, CancellationToken ct = default)
    {
        var warehouseName = await _db.Warehouses.AsNoTracking().Where(w => w.IsActive).OrderBy(w => w.WarehouseId)
            .Select(w => w.WarehouseName).FirstOrDefaultAsync(ct) ?? string.Empty;

        if (stocktakeId is not null)
        {
            var s = await _db.Stocktakes.AsNoTracking().Where(x => x.StocktakeId == stocktakeId)
                .Select(x => new
                {
                    x.StocktakeCode, x.Warehouse.WarehouseName,
                    LocationName = x.Location != null ? x.Location.LocationName : null,
                    ParentName = x.Location != null && x.Location.ParentLocation != null ? x.Location.ParentLocation.LocationName : null,
                    CategoryName = x.Category != null ? x.Category.CategoryName : null
                })
                .FirstOrDefaultAsync(ct);
            if (s is null) return null;
            var lines = await _db.StocktakeDetails.AsNoTracking()
                .Where(d => d.StocktakeId == stocktakeId)
                .Select(d => new LotInfo(d.BatchId, d.Batch.MedicineId, d.Batch.Medicine.MedicineCode, d.Batch.Medicine.MedicineName, d.Batch.BatchNumber,
                    d.Batch.ExpiryDate, d.Batch.Medicine.BaseUnit.UnitName, d.Batch.QuantityOnHand, d.Batch.UnitCost, d.Batch.WarehouseId,
                    d.Batch.Location != null ? d.Batch.Location.LocationCode : null,
                    d.Batch.Location != null ? d.Batch.Location.LocationName : null,
                    d.Batch.Location != null && d.Batch.Location.ParentLocation != null ? d.Batch.Location.ParentLocation.LocationName : null))
                .ToListAsync(ct);
            return new CountSheetViewModel
            {
                StocktakeCode = s.StocktakeCode,
                ScopeLabel = ScopeLabel(s.ParentName, s.LocationName, s.CategoryName),
                WarehouseName = s.WarehouseName,
                PharmacyName = _pharmacyName,
                PrintedAt = DateTime.Now,
                Lines = Order(lines).Select(ToSheetLine).ToList()
            };
        }

        var resolved = await ResolveScopeAsync(scope, ct);
        if (resolved.Error is not null) return null;
        var lots = await LotsInScopeAsync(resolved, ct);
        return new CountSheetViewModel
        {
            ScopeLabel = resolved.Label,
            WarehouseName = warehouseName,
            PharmacyName = _pharmacyName,
            PrintedAt = DateTime.Now,
            Lines = Order(lots).Select(ToSheetLine).ToList()
        };
    }

    // ================================================================== Tạo phiếu (bắt đầu nhập số thực tế 2.4.1.2)

    public async Task<OperationResult<int>> StartAsync(StocktakeScope scope, string actorUserId, CancellationToken ct = default)
    {
        var employeeId = await EmployeeIdOfAsync(actorUserId, ct);
        if (employeeId is null) return OperationResult<int>.Fail("Tài khoản chưa gắn hồ sơ nhân viên nên chưa lập phiếu kiểm kê được.");
        var resolved = await ResolveScopeAsync(scope, ct);
        if (resolved.Error is not null) return OperationResult<int>.Fail(resolved.Error);

        // Serializable: hai người cùng tạo phiếu trùng lô thì chỉ một người thành công
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var lots = await LotsInScopeAsync(resolved, ct);
            if (lots.Count == 0) return OperationResult<int>.Fail("Phạm vi này không có lô nào còn tồn để kiểm kê.");

            var lotIds = lots.Select(l => l.BatchId).ToList();
            var busy = await _db.StocktakeDetails.AsNoTracking()
                .Where(d => lotIds.Contains(d.BatchId) && OpenStatuses.Contains(d.Stocktake.Status))
                .Select(d => new { d.Stocktake.StocktakeCode, d.Batch.BatchNumber, d.Batch.Medicine.MedicineName })
                .FirstOrDefaultAsync(ct);
            if (busy is not null)
                return OperationResult<int>.Fail($"{busy.MedicineName} lô {busy.BatchNumber} đang nằm trong phiếu kiểm kê {busy.StocktakeCode} chưa điều chỉnh xong. Hoàn tất hoặc xóa phiếu đó trước.");

            var now = DateTime.Now;
            var stocktake = new Stocktake
            {
                StocktakeCode = await NextCodeAsync(now, resolved.CodeSuffix, ct),
                WarehouseId = resolved.WarehouseId,
                CreatedByEmployeeId = employeeId.Value,
                StocktakeDate = now,
                Status = (byte)StocktakeStatus.Counting,
                Notes = "Kiểm kê " + resolved.Label,
                LocationId = resolved.LocationId,
                CategoryId = resolved.CategoryId
            };
            foreach (var lot in lots)
            {
                // Số thực tế để tạm bằng tồn hệ thống; chỉ có giá trị sau khi nộp biên bản
                stocktake.StocktakeDetails.Add(new StocktakeDetail
                {
                    BatchId = lot.BatchId,
                    SystemQuantity = lot.Quantity,
                    ActualQuantity = lot.Quantity
                });
            }
            _db.Stocktakes.Add(stocktake);
            await _db.SaveChangesAsync(ct);
            await _audit.WriteAsync("STOCKTAKE_CREATE", "Stocktakes", stocktake.StocktakeId.ToString(CultureInfo.InvariantCulture), null,
                new { stocktake.StocktakeCode, Scope = resolved.Label, Lots = lots.Count }, actorUserId, ct);
            await tx.CommitAsync(ct);
            return OperationResult<int>.Ok(stocktake.StocktakeId,
                $"Đã tạo phiếu {stocktake.StocktakeCode} với {Format.Number(lots.Count)} lô. Nhập số lượng đếm được rồi nộp biên bản.");
        }
        catch (Exception ex) when (ex is DbUpdateException || ex is Microsoft.Data.SqlClient.SqlException { Number: 1205 })
        {
            // Trùng mã phiếu hoặc bị SQL Server chọn hủy do tranh chấp (deadlock) với người tạo phiếu cùng lúc
            _db.ChangeTracker.Clear();
            return OperationResult<int>.Fail("Không tạo được phiếu do có người vừa tạo phiếu kiểm kê cùng lúc. Thử lại.");
        }
    }

    // ================================================================== Chi tiết

    public async Task<StocktakeDetailsViewModel?> GetDetailsAsync(int stocktakeId, CancellationToken ct = default)
    {
        var s = await _db.Stocktakes.AsNoTracking()
            .Where(x => x.StocktakeId == stocktakeId)
            .Select(x => new
            {
                x.StocktakeId, x.StocktakeCode, x.Status, x.StocktakeDate, x.Warehouse.WarehouseName, x.Notes,
                CreatedBy = x.CreatedByEmployee.FullName,
                ApprovedBy = x.ApprovedByEmployee != null ? x.ApprovedByEmployee.FullName : null, x.ApprovedAt, x.ApprovalNote,
                AdjustedBy = x.AdjustedByEmployee != null ? x.AdjustedByEmployee.FullName : null, x.AdjustedAt,
                LocationName = x.Location != null ? x.Location.LocationName : null,
                ParentName = x.Location != null && x.Location.ParentLocation != null ? x.Location.ParentLocation.LocationName : null,
                CategoryName = x.Category != null ? x.Category.CategoryName : null
            })
            .FirstOrDefaultAsync(ct);
        if (s is null) return null;

        var lines = await _db.StocktakeDetails.AsNoTracking()
            .Where(d => d.StocktakeId == stocktakeId)
            .Select(d => new
            {
                d.StocktakeDetailId, d.BatchId, d.SystemQuantity, d.ActualQuantity, d.Reason,
                d.Batch.Medicine.MedicineCode, d.Batch.Medicine.MedicineName, d.Batch.BatchNumber, d.Batch.ExpiryDate,
                BaseUnit = d.Batch.Medicine.BaseUnit.UnitName, d.Batch.UnitCost, d.Batch.QuantityOnHand,
                LocationCode = d.Batch.Location != null ? d.Batch.Location.LocationCode : null,
                LocationName = d.Batch.Location != null ? d.Batch.Location.LocationName : null,
                ParentName = d.Batch.Location != null && d.Batch.Location.ParentLocation != null ? d.Batch.Location.ParentLocation.LocationName : null
            })
            .ToListAsync(ct);

        return new StocktakeDetailsViewModel
        {
            StocktakeId = s.StocktakeId,
            Code = s.StocktakeCode,
            Status = s.Status,
            StocktakeDate = s.StocktakeDate,
            WarehouseName = s.WarehouseName,
            Scope = ScopeLabel(s.ParentName, s.LocationName, s.CategoryName),
            Notes = s.Notes,
            CreatedByName = s.CreatedBy,
            ApprovedByName = s.ApprovedBy,
            ApprovedAt = s.ApprovedAt,
            ApprovalNote = s.ApprovalNote,
            AdjustedByName = s.AdjustedBy,
            AdjustedAt = s.AdjustedAt,
            PharmacyName = _pharmacyName,
            Lines = lines
                .OrderBy(l => l.LocationCode ?? "~", StringComparer.Ordinal).ThenBy(l => l.MedicineName, NameOrder).ThenBy(l => l.ExpiryDate)
                .Select(l => new StocktakeLineView(l.StocktakeDetailId, l.BatchId, LocationLabel(l.ParentName, l.LocationName),
                    l.MedicineCode, l.MedicineName, l.BatchNumber, l.ExpiryDate, l.BaseUnit, l.SystemQuantity,
                    l.ActualQuantity, l.ActualQuantity - l.SystemQuantity, l.UnitCost, l.Reason, l.QuantityOnHand))
                .ToList()
        };
    }

    // ================================================================== Nộp biên bản (2.4.1.2 + 2.4.2)

    public async Task<OperationResult> SubmitAsync(int stocktakeId, StocktakeCountForm form, string actorUserId, CancellationToken ct = default)
    {
        var header = await _db.Stocktakes.AsNoTracking().Where(s => s.StocktakeId == stocktakeId)
            .Select(s => new { s.StocktakeCode, s.Status, s.Notes }).FirstOrDefaultAsync(ct);
        if (header is null) return OperationResult.Fail("Không tìm thấy phiếu kiểm kê.");
        if (header.Status != (byte)StocktakeStatus.Counting)
            return OperationResult.Fail($"Phiếu {header.StocktakeCode} {StocktakeStatusText.Of(header.Status).ToLowerInvariant()} nên không nhập lại số được.");

        var notes = string.IsNullOrWhiteSpace(form.Notes) ? header.Notes : form.Notes.Trim();
        if (notes is not null && notes.Length > 1000) return OperationResult.Fail("Ghi chú tối đa 1000 ký tự.", nameof(StocktakeCountForm.Notes));

        var details = await _db.StocktakeDetails.Where(d => d.StocktakeId == stocktakeId).ToListAsync(ct);
        var names = await _db.StocktakeDetails.AsNoTracking().Where(d => d.StocktakeId == stocktakeId)
            .Select(d => new { d.StocktakeDetailId, d.Batch.Medicine.MedicineName, d.Batch.BatchNumber })
            .ToDictionaryAsync(d => d.StocktakeDetailId, d => d.MedicineName + " lô " + d.BatchNumber, ct);
        var inputs = form.Lines.GroupBy(l => l.StocktakeDetailId).ToDictionary(g => g.Key, g => g.Last());

        var missing = 0;
        foreach (var d in details)
        {
            var label = names.GetValueOrDefault(d.StocktakeDetailId) ?? "Một lô";
            if (!inputs.TryGetValue(d.StocktakeDetailId, out var input) || input.ActualQuantity is null)
            {
                missing++;
                continue;
            }
            var actual = input.ActualQuantity.Value;
            if (actual < 0) return OperationResult.Fail($"{label}: số lượng thực tế không được âm.");
            if (actual > MaxQuantity) return OperationResult.Fail($"{label}: số lượng thực tế quá lớn.");
            if (decimal.Round(actual, 3) != actual) return OperationResult.Fail($"{label}: số lượng tối đa 3 chữ số sau dấu thập phân.");
            var reason = string.IsNullOrWhiteSpace(input.Reason) ? null : input.Reason.Trim();
            if (reason is not null && reason.Length > 500) return OperationResult.Fail($"{label}: lý do chênh lệch tối đa 500 ký tự.");
            if (actual != d.SystemQuantity && reason is null) return OperationResult.Fail($"{label}: có chênh lệch, nhập lý do.");
            d.ActualQuantity = actual;
            d.Reason = reason;
        }
        if (missing > 0)
        {
            _db.ChangeTracker.Clear();
            return OperationResult.Fail($"Còn {Format.Number(missing)} lô chưa nhập số lượng thực tế. Nhập đủ rồi nộp biên bản.");
        }

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var claimed = await _db.Stocktakes
            .Where(s => s.StocktakeId == stocktakeId && s.Status == (byte)StocktakeStatus.Counting)
            .ExecuteUpdateAsync(x => x.SetProperty(s => s.Status, (byte)StocktakeStatus.PendingApproval).SetProperty(s => s.Notes, notes), ct);
        if (claimed == 0)
        {
            _db.ChangeTracker.Clear();
            return OperationResult.Fail("Phiếu vừa được nộp hoặc xóa ở máy khác. Tải lại trang.");
        }
        await _db.SaveChangesAsync(ct);
        var diffs = details.Count(d => d.ActualQuantity != d.SystemQuantity);
        await _audit.WriteAsync("STOCKTAKE_SUBMIT", "Stocktakes", stocktakeId.ToString(CultureInfo.InvariantCulture),
            new { Status = (byte)StocktakeStatus.Counting },
            new
            {
                Status = (byte)StocktakeStatus.PendingApproval, header.StocktakeCode,
                Differences = details.Where(d => d.ActualQuantity != d.SystemQuantity)
                    .Select(d => new { d.BatchId, d.SystemQuantity, d.ActualQuantity, d.Reason }).ToList()
            }, actorUserId, ct);
        await tx.CommitAsync(ct);
        return OperationResult.Ok(diffs == 0
            ? $"Đã nộp biên bản {header.StocktakeCode}: không có chênh lệch. Chờ chủ nhà thuốc duyệt."
            : $"Đã nộp biên bản {header.StocktakeCode}: {Format.Number(diffs)} lô chênh lệch. Chờ chủ nhà thuốc duyệt.");
    }

    public async Task<OperationResult> DeleteAsync(int stocktakeId, string actorUserId, CancellationToken ct = default)
    {
        var code = await _db.Stocktakes.AsNoTracking().Where(s => s.StocktakeId == stocktakeId).Select(s => s.StocktakeCode).FirstOrDefaultAsync(ct);
        if (code is null) return OperationResult.Fail("Không tìm thấy phiếu kiểm kê.");

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var deleted = await _db.Stocktakes
            .Where(s => s.StocktakeId == stocktakeId && s.Status == (byte)StocktakeStatus.Counting)
            .ExecuteDeleteAsync(ct);
        if (deleted == 0) return OperationResult.Fail($"Phiếu {code} đã nộp biên bản nên không xóa được.");
        await _audit.WriteAsync("STOCKTAKE_DELETE", "Stocktakes", stocktakeId.ToString(CultureInfo.InvariantCulture),
            new { StocktakeCode = code, Status = (byte)StocktakeStatus.Counting }, null, actorUserId, ct);
        await tx.CommitAsync(ct);
        return OperationResult.Ok($"Đã xóa phiếu {code}.");
    }

    // ================================================================== Duyệt / từ chối (Owner)

    public async Task<OperationResult> ApproveAsync(int stocktakeId, string? note, string actorUserId, CancellationToken ct = default)
        => await DecideAsync(stocktakeId, note, approve: true, actorUserId, ct);

    public async Task<OperationResult> RejectAsync(int stocktakeId, string? note, string actorUserId, CancellationToken ct = default)
        => await DecideAsync(stocktakeId, note, approve: false, actorUserId, ct);

    private async Task<OperationResult> DecideAsync(int stocktakeId, string? note, bool approve, string actorUserId, CancellationToken ct)
    {
        var employeeId = await EmployeeIdOfAsync(actorUserId, ct);
        if (employeeId is null) return OperationResult.Fail("Tài khoản chưa gắn hồ sơ nhân viên nên chưa duyệt được.");
        var text = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (!approve && text is null) return OperationResult.Fail("Nhập lý do từ chối.");
        if (text is not null && text.Length > 500) return OperationResult.Fail("Ý kiến duyệt tối đa 500 ký tự.");

        var code = await _db.Stocktakes.AsNoTracking().Where(s => s.StocktakeId == stocktakeId).Select(s => s.StocktakeCode).FirstOrDefaultAsync(ct);
        if (code is null) return OperationResult.Fail("Không tìm thấy phiếu kiểm kê.");

        var newStatus = approve ? (byte)StocktakeStatus.Approved : (byte)StocktakeStatus.Rejected;
        var now = DateTime.Now;
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var updated = await _db.Stocktakes
            .Where(s => s.StocktakeId == stocktakeId && s.Status == (byte)StocktakeStatus.PendingApproval)
            .ExecuteUpdateAsync(x => x
                .SetProperty(s => s.Status, newStatus)
                .SetProperty(s => s.ApprovedByEmployeeId, employeeId)
                .SetProperty(s => s.ApprovedAt, now)
                .SetProperty(s => s.ApprovalNote, text), ct);
        if (updated == 0) return OperationResult.Fail($"Phiếu {code} không còn chờ duyệt. Tải lại trang.");
        await _audit.WriteAsync(approve ? "STOCKTAKE_APPROVE" : "STOCKTAKE_REJECT", "Stocktakes", stocktakeId.ToString(CultureInfo.InvariantCulture),
            new { Status = (byte)StocktakeStatus.PendingApproval }, new { Status = newStatus, Note = text }, actorUserId, ct);
        await tx.CommitAsync(ct);
        return OperationResult.Ok(approve
            ? $"Đã duyệt biên bản {code}. Quản lý kho điều chỉnh tồn theo biên bản."
            : $"Đã từ chối biên bản {code}. Lập phiếu kiểm kê mới để kiểm lại.");
    }

    // ================================================================== Điều chỉnh tồn (2.4.3)

    public async Task<OperationResult> AdjustAsync(int stocktakeId, string actorUserId, CancellationToken ct = default)
    {
        var employeeId = await EmployeeIdOfAsync(actorUserId, ct);
        if (employeeId is null) return OperationResult.Fail("Tài khoản chưa gắn hồ sơ nhân viên nên chưa điều chỉnh tồn được.");

        var now = DateTime.Now;
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var claimed = await _db.Stocktakes
            .Where(s => s.StocktakeId == stocktakeId && s.Status == (byte)StocktakeStatus.Approved)
            .ExecuteUpdateAsync(x => x
                .SetProperty(s => s.Status, (byte)StocktakeStatus.Adjusted)
                .SetProperty(s => s.AdjustedByEmployeeId, employeeId)
                .SetProperty(s => s.AdjustedAt, now), ct);
        if (claimed == 0)
        {
            var status = await _db.Stocktakes.AsNoTracking().Where(s => s.StocktakeId == stocktakeId)
                .Select(s => (byte?)s.Status).FirstOrDefaultAsync(ct);
            return OperationResult.Fail(status is null
                ? "Không tìm thấy phiếu kiểm kê."
                : $"Phiếu {StocktakeStatusText.Of(status.Value).ToLowerInvariant()} nên không điều chỉnh được.");
        }

        var code = await _db.Stocktakes.AsNoTracking().Where(s => s.StocktakeId == stocktakeId).Select(s => s.StocktakeCode).FirstAsync(ct);
        var lines = await _db.StocktakeDetails.AsNoTracking()
            .Where(d => d.StocktakeId == stocktakeId && d.ActualQuantity != d.SystemQuantity)
            .Select(d => new
            {
                d.StocktakeDetailId, d.BatchId, Diff = d.ActualQuantity - d.SystemQuantity, d.Batch.MedicineId, d.Batch.WarehouseId,
                d.Batch.BatchNumber, d.Batch.Medicine.MedicineName, BaseUnit = d.Batch.Medicine.BaseUnit.UnitName
            })
            .ToListAsync(ct);

        foreach (var line in lines)
        {
            var batchId = line.BatchId;
            var diff = line.Diff;
            // Cộng chênh lệch vào tồn hiện tại (trong lúc đếm có thể đã bán); thiếu thì không được làm tồn âm
            var updated = diff < 0
                ? await _db.MedicineBatches.Where(b => b.BatchId == batchId && b.QuantityOnHand + diff >= 0)
                    .ExecuteUpdateAsync(x => x.SetProperty(b => b.QuantityOnHand, b => b.QuantityOnHand + diff), ct)
                : await _db.MedicineBatches.Where(b => b.BatchId == batchId)
                    .ExecuteUpdateAsync(x => x.SetProperty(b => b.QuantityOnHand, b => b.QuantityOnHand + diff), ct);
            if (updated == 0)
            {
                var onHand = await _db.MedicineBatches.AsNoTracking().Where(b => b.BatchId == batchId).Select(b => b.QuantityOnHand).FirstAsync(ct);
                _db.ChangeTracker.Clear();
                return OperationResult.Fail($"Chưa điều chỉnh được: {line.MedicineName} lô {line.BatchNumber} hiện chỉ còn {Format.Number(onHand)} {line.BaseUnit}, "
                    + $"không trừ được {Format.Number(-diff)}. Kiểm tra lại lô này (có thể đã bán sau khi đếm).");
            }
            _db.InventoryTransactions.Add(new InventoryTransaction
            {
                MedicineId = line.MedicineId,
                BatchId = batchId,
                WarehouseId = line.WarehouseId,
                TransactionType = (byte)InventoryTransactionType.StocktakeAdjustment,
                QuantityChange = diff,
                ReferenceType = InventoryReferenceTypes.For(InventoryTransactionType.StocktakeAdjustment),
                ReferenceId = line.StocktakeDetailId,
                TransactionDate = now,
                CreatedByEmployeeId = employeeId,
                Notes = "Kiểm kê — " + code
            });
        }

        try
        {
            await _db.SaveChangesAsync(ct);
            await _audit.WriteAsync("STOCKTAKE_ADJUST", "Stocktakes", stocktakeId.ToString(CultureInfo.InvariantCulture),
                new { Status = (byte)StocktakeStatus.Approved },
                new { Status = (byte)StocktakeStatus.Adjusted, StocktakeCode = code, Lines = lines.Select(l => new { l.BatchId, l.Diff }).ToList() },
                actorUserId, ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            return OperationResult.Fail("Chưa điều chỉnh được do dữ liệu vừa thay đổi. Tải lại trang rồi thử lại.");
        }
        return OperationResult.Ok(lines.Count == 0
            ? $"Đã hoàn tất phiếu {code}: không có chênh lệch nên tồn kho giữ nguyên."
            : $"Đã điều chỉnh tồn theo biên bản {code}: {Format.Number(lines.Count)} lô.");
    }

    public async Task<(int Pending, int Approved)> WorkCountsAsync(CancellationToken ct = default)
    {
        var counts = await _db.Stocktakes.AsNoTracking()
            .Where(s => s.Status == (byte)StocktakeStatus.PendingApproval || s.Status == (byte)StocktakeStatus.Approved)
            .GroupBy(s => s.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        return (counts.Where(c => c.Status == (byte)StocktakeStatus.PendingApproval).Sum(c => c.Count),
                counts.Where(c => c.Status == (byte)StocktakeStatus.Approved).Sum(c => c.Count));
    }

    // ================================================================== Dùng chung

    private sealed record LotInfo(int BatchId, int MedicineId, string MedicineCode, string MedicineName, string BatchNumber, DateOnly ExpiryDate,
        string BaseUnit, decimal Quantity, decimal UnitCost, int WarehouseId, string? LocationCode, string? LocationName, string? ParentName);

    private sealed record ResolvedScope(int WarehouseId, string Label, string CodeSuffix, int? LocationId, int? CategoryId, string? Error = null)
    {
        public static ResolvedScope Fail(string error) => new(0, string.Empty, string.Empty, null, null, error);
    }

    private async Task<ResolvedScope> ResolveScopeAsync(StocktakeScope scope, CancellationToken ct)
    {
        var warehouseId = await DefaultWarehouseAsync(ct);
        if (warehouseId is null) return ResolvedScope.Fail("Chưa có kho nào đang hoạt động.");
        switch (scope.Scope)
        {
            case "location":
                if (scope.LocationId is null) return ResolvedScope.Fail("Chọn tủ hoặc kệ cần kiểm kê.");
                var location = await _db.StorageLocations.AsNoTracking()
                    .Where(l => l.LocationId == scope.LocationId && l.WarehouseId == warehouseId)
                    .Select(l => new { l.LocationId, l.LocationCode, l.LocationName, ParentName = l.ParentLocation != null ? l.ParentLocation.LocationName : null })
                    .FirstOrDefaultAsync(ct);
                if (location is null) return ResolvedScope.Fail("Chọn một tủ hoặc kệ trong danh sách.");
                return new ResolvedScope(warehouseId.Value, LocationLabel(location.ParentName, location.LocationName)!,
                    CodePart(location.LocationCode), location.LocationId, null);
            case "category":
                if (scope.CategoryId is null) return ResolvedScope.Fail("Chọn nhóm thuốc cần kiểm kê.");
                var category = await _db.Categories.AsNoTracking().Where(c => c.CategoryId == scope.CategoryId)
                    .Select(c => new { c.CategoryId, c.CategoryCode, c.CategoryName }).FirstOrDefaultAsync(ct);
                if (category is null) return ResolvedScope.Fail("Chọn một nhóm thuốc trong danh sách.");
                return new ResolvedScope(warehouseId.Value, "nhóm " + category.CategoryName, CodePart(category.CategoryCode), null, category.CategoryId);
            case "all":
                return new ResolvedScope(warehouseId.Value, "toàn kho", "TK", null, null);
            default:
                return ResolvedScope.Fail("Chọn phạm vi kiểm kê.");
        }
    }

    /// <summary>Lô còn tồn trong phạm vi (mọi trạng thái lô: hết hạn, khóa, thu hồi vẫn phải đếm).</summary>
    private async Task<List<LotInfo>> LotsInScopeAsync(ResolvedScope scope, CancellationToken ct)
    {
        var query = _db.MedicineBatches.AsNoTracking().Where(b => b.WarehouseId == scope.WarehouseId && b.QuantityOnHand > 0);
        if (scope.LocationId is not null)
        {
            var id = scope.LocationId;
            query = query.Where(b => b.LocationId == id || (b.Location != null && b.Location.ParentLocationId == id));
        }
        if (scope.CategoryId is not null)
        {
            var id = scope.CategoryId;
            query = query.Where(b => b.Medicine.CategoryId == id);
        }
        return await query
            .Select(b => new LotInfo(b.BatchId, b.MedicineId, b.Medicine.MedicineCode, b.Medicine.MedicineName, b.BatchNumber, b.ExpiryDate,
                b.Medicine.BaseUnit.UnitName, b.QuantityOnHand, b.UnitCost, b.WarehouseId,
                b.Location != null ? b.Location.LocationCode : null,
                b.Location != null ? b.Location.LocationName : null,
                b.Location != null && b.Location.ParentLocation != null ? b.Location.ParentLocation.LocationName : null))
            .ToListAsync(ct);
    }

    private static IEnumerable<LotInfo> Order(IEnumerable<LotInfo> lots)
        => lots.OrderBy(l => l.LocationCode ?? "~", StringComparer.Ordinal).ThenBy(l => l.MedicineName, NameOrder).ThenBy(l => l.ExpiryDate).ThenBy(l => l.BatchId);

    private static CountSheetLine ToSheetLine(LotInfo l)
        => new(LocationLabel(l.ParentName, l.LocationName), l.MedicineCode, l.MedicineName, l.BatchNumber, l.ExpiryDate, l.BaseUnit);

    /// <summary>Phần mã phạm vi trong mã phiếu: chữ hoa, chữ số (T01-K1 → T01K1), tối đa 18 ký tự.</summary>
    private static string CodePart(string code)
    {
        var part = new string(code.ToUpperInvariant().Where(ch => ch is >= 'A' and <= 'Z' or >= '0' and <= '9').ToArray());
        if (part.Length == 0) part = "PV";
        return part.Length > 18 ? part[..18] : part;
    }

    /// <summary>KK + yyMMdd + mã phạm vi (giống dữ liệu mẫu KK261030T01); trùng trong ngày thì thêm -2, -3…</summary>
    private async Task<string> NextCodeAsync(DateTime at, string suffix, CancellationToken ct)
    {
        var baseCode = CodePrefix + at.ToString("yyMMdd", CultureInfo.InvariantCulture) + suffix;
        var used = (await _db.Stocktakes.AsNoTracking().Where(s => s.StocktakeCode.StartsWith(baseCode))
            .Select(s => s.StocktakeCode).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
        if (!used.Contains(baseCode)) return baseCode;
        for (var n = 2; ; n++)
        {
            var candidate = baseCode + "-" + n.ToString(CultureInfo.InvariantCulture);
            if (!used.Contains(candidate)) return candidate;
        }
    }

    private static string ScopeLabel(string? parentName, string? locationName, string? categoryName)
        => locationName is not null ? LocationLabel(parentName, locationName)! : categoryName is not null ? "nhóm " + categoryName : "toàn kho";

    private Task<int?> DefaultWarehouseAsync(CancellationToken ct)
        => _db.Warehouses.AsNoTracking().Where(w => w.IsActive).OrderBy(w => w.WarehouseId)
            .Select(w => (int?)w.WarehouseId).FirstOrDefaultAsync(ct);

    private Task<int?> EmployeeIdOfAsync(string userId, CancellationToken ct)
        => _db.Employees.AsNoTracking().Where(e => e.UserId == userId && e.IsActive).Select(e => (int?)e.EmployeeId).FirstOrDefaultAsync(ct);

    private static string? LocationLabel(string? parentName, string? name)
        => name is null ? null : parentName is null ? name : parentName + " › " + name;
}
