using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PharmacyManagement.Configuration;
using PharmacyManagement.Data;
using PharmacyManagement.Helpers;
using PharmacyManagement.Models.Entities;
using PharmacyManagement.Models.Enums;
using PharmacyManagement.Models.ViewModels.Inventory;
using PharmacyManagement.Services.Interfaces;

namespace PharmacyManagement.Services.Implementations;

/// <summary>Hủy thuốc 2.3.2 (DEC-41). Biên bản thuộc kho đang hoạt động đầu tiên (KHO01) như phiếu nhập.</summary>
public sealed class StockDisposalService : IStockDisposalService
{
    private const string CodePrefix = "HUY";
    private const string ExpiredReason = "Hủy thuốc hết hạn sử dụng";

    private static readonly CultureInfo Vietnamese = CultureInfo.GetCultureInfo("vi-VN");
    private static readonly StringComparer NameOrder = StringComparer.Create(Vietnamese, ignoreCase: true);

    private readonly PharmacyDbContext _db;
    private readonly IAuditLogService _audit;
    private readonly string _pharmacyName;

    public StockDisposalService(PharmacyDbContext db, IAuditLogService audit, IOptions<PharmacyOptions> pharmacy)
    {
        _db = db;
        _audit = audit;
        _pharmacyName = pharmacy.Value.Name;
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    // ================================================================== Danh sách

    public async Task<DisposalListViewModel> GetListAsync(DisposalFilter filter, CancellationToken ct = default)
    {
        var query = _db.StockDisposals.AsNoTracking().AsQueryable();

        byte? status = filter.Status switch
        {
            "draft" => (byte)DisposalStatus.Draft,
            "posted" => (byte)DisposalStatus.Posted,
            "cancelled" => (byte)DisposalStatus.Cancelled,
            _ => null
        };
        if (status is null) filter.Status = null;
        else query = query.Where(d => d.Status == status);

        var q = AccountHelpers.NormalizeFullName(filter.Q);
        filter.Q = q.Length == 0 ? null : q;
        if (filter.Q is not null)
        {
            var text = filter.Q;
            var folded = SearchText.Fold(text);
            query = query.Where(d => d.DisposalCode.Contains(text)
                                     || EF.Functions.Collate(d.Reason.Replace("đ", "d").Replace("Đ", "D"), SearchText.Collation).Contains(folded)
                                     || d.StockDisposalDetails.Any(x => x.Batch.BatchNumber.Contains(text)
                                         || EF.Functions.Collate(x.Batch.Medicine.MedicineName.Replace("đ", "d").Replace("Đ", "D"), SearchText.Collation).Contains(folded)));
        }

        var total = await query.CountAsync(ct);
        var pageCount = Math.Max(1, (int)Math.Ceiling(total / (double)DisposalFilter.PageSize));
        filter.Page = Math.Clamp(filter.Page, 1, pageCount);

        var rows = await query
            .OrderByDescending(d => d.DisposalDate).ThenByDescending(d => d.DisposalId)
            .Skip((filter.Page - 1) * DisposalFilter.PageSize).Take(DisposalFilter.PageSize)
            .Select(d => new DisposalRow(d.DisposalId, d.DisposalCode, d.DisposalDate, d.Reason,
                d.StockDisposalDetails.Count(), d.StockDisposalDetails.Sum(x => x.Quantity * x.UnitCost),
                d.CreatedByEmployee.FullName, d.Status))
            .ToListAsync(ct);

        return new DisposalListViewModel
        {
            Filter = filter,
            Rows = rows.Select(r => r with { TotalValue = RoundMoney(r.TotalValue) }).ToList(),
            Total = total,
            GrandTotal = await _db.StockDisposals.CountAsync(ct),
            PageCount = pageCount
        };
    }

    // ================================================================== Chi tiết

    public async Task<DisposalDetailsViewModel?> GetDetailsAsync(int disposalId, CancellationToken ct = default)
    {
        var d = await _db.StockDisposals.AsNoTracking()
            .Where(x => x.DisposalId == disposalId)
            .Select(x => new
            {
                x.DisposalId, x.DisposalCode, x.Status, x.DisposalDate, x.Warehouse.WarehouseName, x.Reason, x.Notes,
                CreatedBy = x.CreatedByEmployee.FullName
            })
            .FirstOrDefaultAsync(ct);
        if (d is null) return null;

        var lines = await _db.StockDisposalDetails.AsNoTracking()
            .Where(x => x.DisposalId == disposalId)
            .OrderBy(x => x.DisposalDetailId)
            .Select(x => new
            {
                x.DisposalDetailId, x.BatchId, x.Batch.Medicine.MedicineCode, x.Batch.Medicine.MedicineName, x.Batch.BatchNumber, x.Batch.ExpiryDate,
                LocationName = x.Batch.Location != null ? x.Batch.Location.LocationName : null,
                ParentName = x.Batch.Location != null && x.Batch.Location.ParentLocation != null ? x.Batch.Location.ParentLocation.LocationName : null,
                x.Quantity, BaseUnit = x.Batch.Medicine.BaseUnit.UnitName, x.UnitCost, x.ReasonType, x.ReasonNote,
                OnHand = x.Batch.QuantityOnHand, LotStatus = x.Batch.Status
            })
            .ToListAsync(ct);

        DateTime? postedAt = null;
        if (d.Status == (byte)DisposalStatus.Posted && lines.Count > 0)
        {
            var refIds = lines.Select(l => (long)l.DisposalDetailId).ToList();
            postedAt = await _db.InventoryTransactions.AsNoTracking()
                .Where(t => t.ReferenceType == InventoryReferenceTypes.StockDisposalDetail && refIds.Contains(t.ReferenceId))
                .MinAsync(t => (DateTime?)t.TransactionDate, ct);
        }

        var views = lines.Select(l => new DisposalLineView(
                l.BatchId, l.MedicineCode, l.MedicineName, l.BatchNumber, l.ExpiryDate, LocationLabel(l.ParentName, l.LocationName),
                l.Quantity, l.BaseUnit, l.UnitCost, RoundMoney(l.Quantity * l.UnitCost), l.ReasonType, l.ReasonNote, l.OnHand, l.LotStatus))
            .ToList();

        return new DisposalDetailsViewModel
        {
            DisposalId = d.DisposalId,
            Code = d.DisposalCode,
            Status = d.Status,
            DisposalDate = d.DisposalDate,
            WarehouseName = d.WarehouseName,
            Reason = d.Reason,
            Notes = d.Notes,
            CreatedByName = d.CreatedBy,
            PostedAt = postedAt,
            Lines = views,
            TotalValue = views.Sum(v => v.Value),
            PharmacyName = _pharmacyName
        };
    }

    // ================================================================== Form

    public async Task<DisposalFormViewModel> NewFormAsync(int? batchId, bool expiredLots, CancellationToken ct = default)
    {
        var form = new DisposalFormViewModel();
        var warehouseId = await DefaultWarehouseAsync(ct);
        var today = Today;
        var recalled = (byte)BatchStatus.Recalled;

        if (warehouseId is not null && batchId is not null)
        {
            var lot = await _db.MedicineBatches.AsNoTracking()
                .Where(b => b.BatchId == batchId && b.WarehouseId == warehouseId && b.QuantityOnHand > 0 && b.Status != recalled)
                .Select(b => new { b.BatchId, b.QuantityOnHand, b.ExpiryDate })
                .FirstOrDefaultAsync(ct);
            if (lot is not null)
            {
                var expired = lot.ExpiryDate <= today;
                form.Lines.Add(new DisposalLineForm
                {
                    BatchId = lot.BatchId,
                    Quantity = lot.QuantityOnHand,
                    ReasonType = expired ? (byte)DisposalReason.Expired : null
                });
                if (expired) form.Reason = ExpiredReason;
            }
        }
        else if (warehouseId is not null && expiredLots)
        {
            var lots = await _db.MedicineBatches.AsNoTracking()
                .Where(b => b.WarehouseId == warehouseId && b.QuantityOnHand > 0 && b.ExpiryDate <= today && b.Status != recalled)
                .OrderBy(b => b.ExpiryDate).ThenBy(b => b.BatchId)
                .Select(b => new { b.BatchId, b.QuantityOnHand })
                .ToListAsync(ct);
            form.Reason = ExpiredReason;
            foreach (var lot in lots.Take(DisposalFormViewModel.MaxLines))
            {
                form.Lines.Add(new DisposalLineForm
                {
                    BatchId = lot.BatchId,
                    Quantity = lot.QuantityOnHand,
                    ReasonType = (byte)DisposalReason.Expired
                });
            }
            if (lots.Count == 0)
                form.PrefillNote = "Hiện không còn lô hết hạn nào trong kho.";
            else if (lots.Count > DisposalFormViewModel.MaxLines)
                form.PrefillNote = $"Có {Format.Number(lots.Count)} lô hết hạn; biên bản này lấy {DisposalFormViewModel.MaxLines} lô hạn sớm nhất. Lập thêm biên bản cho các lô còn lại.";
        }

        if (form.Lines.Count == 0) form.Lines.Add(new DisposalLineForm());
        await PrepareFormAsync(form, ct);
        return form;
    }

    public async Task<DisposalFormViewModel?> EditFormAsync(int disposalId, CancellationToken ct = default)
    {
        var d = await _db.StockDisposals.AsNoTracking()
            .Where(x => x.DisposalId == disposalId)
            .Select(x => new { x.DisposalId, x.DisposalCode, x.Reason, x.Notes, x.Status })
            .FirstOrDefaultAsync(ct);
        if (d is null) return null;

        var form = new DisposalFormViewModel
        {
            DisposalId = d.DisposalId,
            Code = d.DisposalCode,
            Reason = d.Reason,
            Notes = d.Notes,
            IsDraft = d.Status == (byte)DisposalStatus.Draft,
            Lines = await _db.StockDisposalDetails.AsNoTracking()
                .Where(x => x.DisposalId == disposalId)
                .OrderBy(x => x.DisposalDetailId)
                .Select(x => new DisposalLineForm { BatchId = x.BatchId, Quantity = x.Quantity, ReasonType = x.ReasonType, ReasonNote = x.ReasonNote })
                .ToListAsync(ct)
        };
        await PrepareFormAsync(form, ct);
        return form;
    }

    public async Task PrepareFormAsync(DisposalFormViewModel form, CancellationToken ct = default)
    {
        int? warehouseId;
        if (form.IsEdit)
        {
            var d = await _db.StockDisposals.AsNoTracking().Where(x => x.DisposalId == form.DisposalId)
                .Select(x => new { x.DisposalCode, x.WarehouseId }).FirstOrDefaultAsync(ct);
            form.Code = d?.DisposalCode;
            warehouseId = d?.WarehouseId;
        }
        else
        {
            form.Code = await NextCodeAsync(DateTime.Now, ct);
            warehouseId = await DefaultWarehouseAsync(ct);
        }
        if (warehouseId is null)
        {
            form.BatchOptions = Array.Empty<DisposalBatchOption>();
            return;
        }

        // Lô chọn được: còn hàng, không đang thu hồi; giữ cả lô đã có trên form (để ô chọn không mất giá trị, server sẽ báo lỗi nếu không hợp lệ)
        var today = Today;
        var recalled = (byte)BatchStatus.Recalled;
        var keep = form.Lines.Where(l => l.BatchId is not null).Select(l => l.BatchId!.Value).Distinct().ToList();
        var rows = await _db.MedicineBatches.AsNoTracking()
            .Where(b => b.WarehouseId == warehouseId
                        && ((b.QuantityOnHand > 0 && b.Status != recalled) || keep.Contains(b.BatchId)))
            .Select(b => new
            {
                b.BatchId, b.BatchNumber, b.ExpiryDate, b.QuantityOnHand, b.UnitCost, b.Status,
                b.Medicine.MedicineCode, b.Medicine.MedicineName, BaseUnit = b.Medicine.BaseUnit.UnitName
            })
            .ToListAsync(ct);

        form.BatchOptions = rows
            .OrderBy(r => r.MedicineName, NameOrder).ThenBy(r => r.ExpiryDate).ThenBy(r => r.BatchId)
            .Select(r =>
            {
                var expired = r.ExpiryDate <= today;
                var label = r.MedicineName + " · Lô " + r.BatchNumber + " · HSD " + r.ExpiryDate.ToString("dd/MM/yyyy", Vietnamese)
                            + " · còn " + Format.Number(r.QuantityOnHand) + " " + r.BaseUnit
                            + (expired ? " · đã hết hạn" : "")
                            + (r.Status == recalled ? " · đang thu hồi" : "");
                return new DisposalBatchOption(r.BatchId, label, r.MedicineCode + " " + r.BatchNumber, r.QuantityOnHand, r.UnitCost, r.BaseUnit, expired);
            })
            .ToList();
    }

    // ================================================================== Lập / sửa biên bản (Nháp)

    public async Task<OperationResult<int>> CreateAsync(DisposalFormViewModel form, string actorUserId, CancellationToken ct = default)
    {
        var employeeId = await EmployeeIdOfAsync(actorUserId, ct);
        if (employeeId is null) return OperationResult<int>.Fail("Tài khoản chưa gắn hồ sơ nhân viên nên chưa lập biên bản hủy được.");
        var warehouseId = await DefaultWarehouseAsync(ct);
        if (warehouseId is null) return OperationResult<int>.Fail("Chưa có kho nào đang hoạt động.");

        var clean = await ValidateAsync(form, warehouseId.Value, ct);
        if (clean.Error is not null) return OperationResult<int>.Fail(clean.Error, clean.Field);

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var now = DateTime.Now;
            var disposal = new StockDisposal
            {
                DisposalCode = await NextCodeAsync(now, ct),
                WarehouseId = warehouseId.Value,
                CreatedByEmployeeId = employeeId.Value,
                DisposalDate = now,
                Reason = clean.Reason,
                Status = (byte)DisposalStatus.Draft,
                Notes = clean.Notes
            };
            foreach (var line in clean.Lines) disposal.StockDisposalDetails.Add(ToDetail(line));
            _db.StockDisposals.Add(disposal);
            await _db.SaveChangesAsync(ct);
            await _audit.WriteAsync("DISPOSAL_CREATE", "StockDisposals", disposal.DisposalId.ToString(CultureInfo.InvariantCulture), null,
                new { disposal.DisposalCode, clean.Reason, Lines = clean.Lines.Select(l => new { l.BatchId, l.Quantity, l.ReasonType }).ToList() },
                actorUserId, ct);
            await tx.CommitAsync(ct);
            return OperationResult<int>.Ok(disposal.DisposalId,
                $"Đã lưu biên bản {disposal.DisposalCode} (nháp). Kiểm tra lại rồi bấm Ghi sổ để trừ tồn kho.");
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            return OperationResult<int>.Fail("Không lưu được: mã biên bản vừa bị trùng do người khác lập cùng lúc. Bấm Lưu lại.");
        }
    }

    public async Task<OperationResult> UpdateAsync(DisposalFormViewModel form, string actorUserId, CancellationToken ct = default)
    {
        var current = await _db.StockDisposals.AsNoTracking().Where(d => d.DisposalId == form.DisposalId)
            .Select(d => new { d.DisposalId, d.DisposalCode, d.WarehouseId, d.Status }).FirstOrDefaultAsync(ct);
        if (current is null) return OperationResult.Fail("Không tìm thấy biên bản hủy.");
        if (current.Status != (byte)DisposalStatus.Draft)
            return OperationResult.Fail($"Biên bản {current.DisposalCode} {DisposalStatusText.Of(current.Status).ToLowerInvariant()} nên không sửa được.");

        var clean = await ValidateAsync(form, current.WarehouseId, ct);
        if (clean.Error is not null) return OperationResult.Fail(clean.Error, clean.Field);

        var id = current.DisposalId;
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        // Chỉ sửa khi vẫn là Nháp (khóa dòng tới khi commit — không chen được với Ghi sổ ở máy khác)
        var claimed = await _db.StockDisposals
            .Where(d => d.DisposalId == id && d.Status == (byte)DisposalStatus.Draft)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.Reason, clean.Reason).SetProperty(d => d.Notes, clean.Notes), ct);
        if (claimed == 0) return OperationResult.Fail("Biên bản vừa được ghi sổ hoặc hủy ở nơi khác. Tải lại trang.");

        // Biên bản nháp chưa trừ tồn, chưa ghi sổ kho: thay toàn bộ dòng
        await _db.StockDisposalDetails.Where(x => x.DisposalId == id).ExecuteDeleteAsync(ct);
        foreach (var line in clean.Lines)
        {
            var detail = ToDetail(line);
            detail.DisposalId = id;
            _db.StockDisposalDetails.Add(detail);
        }
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("DISPOSAL_UPDATE", "StockDisposals", id.ToString(CultureInfo.InvariantCulture), null,
            new { current.DisposalCode, clean.Reason, Lines = clean.Lines.Select(l => new { l.BatchId, l.Quantity, l.ReasonType }).ToList() },
            actorUserId, ct);
        await tx.CommitAsync(ct);
        return OperationResult.Ok($"Đã lưu biên bản {current.DisposalCode}.");
    }

    // ================================================================== Ghi sổ (trừ tồn)

    public async Task<OperationResult> PostAsync(int disposalId, string actorUserId, CancellationToken ct = default)
    {
        var employeeId = await EmployeeIdOfAsync(actorUserId, ct);
        if (employeeId is null) return OperationResult.Fail("Tài khoản chưa gắn hồ sơ nhân viên nên chưa ghi sổ được.");

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // Giành quyền ghi sổ (khóa dòng tới khi commit): bấm hai lần / hai máy cùng lúc chỉ một lần thành công
        var claimed = await _db.StockDisposals
            .Where(d => d.DisposalId == disposalId && d.Status == (byte)DisposalStatus.Draft)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.Status, (byte)DisposalStatus.Posted), ct);
        if (claimed == 0)
        {
            var status = await _db.StockDisposals.AsNoTracking().Where(d => d.DisposalId == disposalId)
                .Select(d => (byte?)d.Status).FirstOrDefaultAsync(ct);
            return OperationResult.Fail(status is null
                ? "Không tìm thấy biên bản hủy."
                : $"Biên bản {DisposalStatusText.Of(status.Value).ToLowerInvariant()} nên không ghi sổ lại được.");
        }

        var code = await _db.StockDisposals.AsNoTracking().Where(d => d.DisposalId == disposalId)
            .Select(d => d.DisposalCode).FirstAsync(ct);
        var lines = await _db.StockDisposalDetails.AsNoTracking()
            .Where(x => x.DisposalId == disposalId)
            .OrderBy(x => x.DisposalDetailId)
            .Select(x => new
            {
                x.DisposalDetailId, x.BatchId, x.Quantity, x.Batch.MedicineId, x.Batch.WarehouseId, x.Batch.BatchNumber,
                x.Batch.Medicine.MedicineName, BaseUnit = x.Batch.Medicine.BaseUnit.UnitName
            })
            .ToListAsync(ct);
        if (lines.Count == 0) return OperationResult.Fail("Biên bản chưa có lô nào. Sửa biên bản rồi ghi sổ.");

        var recalled = (byte)BatchStatus.Recalled;
        var now = DateTime.Now;
        foreach (var line in lines)
        {
            var batchId = line.BatchId;
            var quantity = line.Quantity;
            // Trừ tồn có điều kiện: đủ tồn và lô không đang thu hồi (SYS-14 — lỗi thì hoàn tác toàn bộ)
            var updated = await _db.MedicineBatches
                .Where(b => b.BatchId == batchId && b.Status != recalled && b.QuantityOnHand >= quantity)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.QuantityOnHand, b => b.QuantityOnHand - quantity), ct);
            if (updated == 0)
            {
                var lot = await _db.MedicineBatches.AsNoTracking().Where(b => b.BatchId == batchId)
                    .Select(b => new { b.QuantityOnHand, b.Status }).FirstAsync(ct);
                _db.ChangeTracker.Clear();
                var why = lot.Status == recalled
                    ? "đang thu hồi"
                    : $"chỉ còn {Format.Number(lot.QuantityOnHand)} {line.BaseUnit}, biên bản ghi hủy {Format.Number(quantity)}";
                return OperationResult.Fail($"Chưa ghi sổ được: {line.MedicineName} lô {line.BatchNumber} {why}. Sửa biên bản rồi ghi sổ lại.");
            }

            _db.InventoryTransactions.Add(new InventoryTransaction
            {
                MedicineId = line.MedicineId,
                BatchId = batchId,
                WarehouseId = line.WarehouseId,
                TransactionType = (byte)InventoryTransactionType.Disposal,
                QuantityChange = -quantity,
                ReferenceType = InventoryReferenceTypes.For(InventoryTransactionType.Disposal),
                ReferenceId = line.DisposalDetailId,
                TransactionDate = now,
                CreatedByEmployeeId = employeeId,
                Notes = "Hủy thuốc — " + code
            });
        }

        try
        {
            await _db.SaveChangesAsync(ct);
            await _audit.WriteAsync("DISPOSAL_POST", "StockDisposals", disposalId.ToString(CultureInfo.InvariantCulture),
                new { Status = (byte)DisposalStatus.Draft },
                new
                {
                    Status = (byte)DisposalStatus.Posted, DisposalCode = code,
                    Lines = lines.Select(l => new { l.BatchId, l.BatchNumber, l.Quantity }).ToList()
                }, actorUserId, ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            return OperationResult.Fail("Chưa ghi sổ được do dữ liệu vừa thay đổi. Tải lại trang rồi thử lại.");
        }

        return OperationResult.Ok($"Đã ghi sổ biên bản {code}: trừ tồn {lines.Count} lô.");
    }

    // ================================================================== Hủy biên bản nháp

    public async Task<OperationResult> CancelAsync(int disposalId, string? reason, string actorUserId, CancellationToken ct = default)
    {
        var why = AccountHelpers.NormalizeFullName(reason);
        if (why.Length == 0) return OperationResult.Fail("Nhập lý do hủy biên bản.");
        if (why.Length > 300) return OperationResult.Fail("Lý do hủy biên bản tối đa 300 ký tự.");

        var current = await _db.StockDisposals.AsNoTracking().Where(d => d.DisposalId == disposalId)
            .Select(d => new { d.DisposalCode, d.Status, d.Notes }).FirstOrDefaultAsync(ct);
        if (current is null) return OperationResult.Fail("Không tìm thấy biên bản hủy.");
        if (current.Status != (byte)DisposalStatus.Draft)
            return OperationResult.Fail($"Biên bản {current.DisposalCode} {DisposalStatusText.Of(current.Status).ToLowerInvariant()} nên không hủy được.");

        var note = "Hủy biên bản: " + why;
        var notes = string.IsNullOrEmpty(current.Notes) ? note : current.Notes + "\n" + note;
        if (notes.Length > 1000) notes = notes[..1000];

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var updated = await _db.StockDisposals
            .Where(d => d.DisposalId == disposalId && d.Status == (byte)DisposalStatus.Draft)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.Status, (byte)DisposalStatus.Cancelled).SetProperty(d => d.Notes, notes), ct);
        if (updated == 0) return OperationResult.Fail("Biên bản vừa được ghi sổ hoặc hủy ở nơi khác. Tải lại trang.");
        await _audit.WriteAsync("DISPOSAL_CANCEL", "StockDisposals", disposalId.ToString(CultureInfo.InvariantCulture),
            new { Status = (byte)DisposalStatus.Draft }, new { Status = (byte)DisposalStatus.Cancelled, Reason = why }, actorUserId, ct);
        await tx.CommitAsync(ct);
        return OperationResult.Ok($"Đã hủy biên bản {current.DisposalCode}.");
    }

    // ================================================================== Kiểm tra form

    private sealed record CleanLine(int BatchId, decimal Quantity, decimal UnitCost, byte ReasonType, string? ReasonNote);

    private sealed record Clean(string Reason, string? Notes, List<CleanLine> Lines, string? Error = null, string? Field = null)
    {
        public static Clean Fail(string error, string? field = null) => new(string.Empty, null, new List<CleanLine>(), error, field);
    }

    private async Task<Clean> ValidateAsync(DisposalFormViewModel form, int warehouseId, CancellationToken ct)
    {
        var reason = AccountHelpers.NormalizeFullName(form.Reason);
        if (reason.Length == 0) return Clean.Fail("Nhập lý do hủy.", nameof(DisposalFormViewModel.Reason));
        if (reason.Length > 500) return Clean.Fail("Lý do hủy tối đa 500 ký tự.", nameof(DisposalFormViewModel.Reason));
        var notes = string.IsNullOrWhiteSpace(form.Notes) ? null : form.Notes.Trim();
        if (notes is not null && notes.Length > 1000) return Clean.Fail("Ghi chú tối đa 1000 ký tự.", nameof(DisposalFormViewModel.Notes));

        // Bỏ qua dòng để trống hoàn toàn
        var raw = form.Lines
            .Where(l => l.BatchId is not null || l.Quantity is not null || l.ReasonType is not null || !string.IsNullOrWhiteSpace(l.ReasonNote))
            .ToList();
        if (raw.Count == 0) return Clean.Fail("Thêm ít nhất một lô cần hủy.", nameof(DisposalFormViewModel.Lines));
        if (raw.Count > DisposalFormViewModel.MaxLines)
            return Clean.Fail($"Một biên bản tối đa {DisposalFormViewModel.MaxLines} lô.", nameof(DisposalFormViewModel.Lines));

        var ids = raw.Where(l => l.BatchId is not null).Select(l => l.BatchId!.Value).Distinct().ToList();
        var lots = await _db.MedicineBatches.AsNoTracking()
            .Where(b => ids.Contains(b.BatchId))
            .Select(b => new
            {
                b.BatchId, b.BatchNumber, b.WarehouseId, b.QuantityOnHand, b.UnitCost, b.Status,
                b.Medicine.MedicineName, BaseUnit = b.Medicine.BaseUnit.UnitName
            })
            .ToDictionaryAsync(b => b.BatchId, ct);

        var seen = new HashSet<int>();
        var lines = new List<CleanLine>();
        for (var i = 0; i < raw.Count; i++)
        {
            var l = raw[i];
            var n = i + 1;
            if (l.BatchId is null) return Clean.Fail($"Dòng {n}: chọn lô cần hủy.", nameof(DisposalFormViewModel.Lines));
            if (!lots.TryGetValue(l.BatchId.Value, out var lot)) return Clean.Fail($"Dòng {n}: không tìm thấy lô đã chọn.", nameof(DisposalFormViewModel.Lines));

            var label = $"Dòng {n} ({lot.MedicineName}, lô {lot.BatchNumber})";
            if (!seen.Add(lot.BatchId)) return Clean.Fail($"{label}: lô này đã có ở dòng khác.", nameof(DisposalFormViewModel.Lines));
            if (lot.WarehouseId != warehouseId) return Clean.Fail($"{label}: lô thuộc kho khác.", nameof(DisposalFormViewModel.Lines));
            if (lot.Status == (byte)BatchStatus.Recalled)
                return Clean.Fail($"{label}: lô đang thu hồi, xử lý ở phần Thu hồi.", nameof(DisposalFormViewModel.Lines));

            if (l.Quantity is null || l.Quantity <= 0) return Clean.Fail($"{label}: nhập số lượng hủy lớn hơn 0.", nameof(DisposalFormViewModel.Lines));
            var quantity = l.Quantity.Value;
            if (decimal.Round(quantity, 3) != quantity)
                return Clean.Fail($"{label}: số lượng tối đa 3 chữ số sau dấu thập phân.", nameof(DisposalFormViewModel.Lines));
            if (quantity > lot.QuantityOnHand)
                return Clean.Fail($"{label}: lô chỉ còn {Format.Number(lot.QuantityOnHand)} {lot.BaseUnit}.", nameof(DisposalFormViewModel.Lines));

            if (l.ReasonType is null || !DisposalReasonText.All.Contains(l.ReasonType.Value))
                return Clean.Fail($"{label}: chọn lý do hủy.", nameof(DisposalFormViewModel.Lines));
            var note = string.IsNullOrWhiteSpace(l.ReasonNote) ? null : l.ReasonNote.Trim();
            if (l.ReasonType == (byte)DisposalReason.Other && note is null)
                return Clean.Fail($"{label}: lý do Khác cần ghi chú.", nameof(DisposalFormViewModel.Lines));
            if (note is not null && note.Length > 500)
                return Clean.Fail($"{label}: ghi chú tối đa 500 ký tự.", nameof(DisposalFormViewModel.Lines));

            lines.Add(new CleanLine(lot.BatchId, quantity, lot.UnitCost, l.ReasonType.Value, note));
        }
        return new Clean(reason, notes, lines);
    }

    // ================================================================== Dùng chung

    private static StockDisposalDetail ToDetail(CleanLine line) => new()
    {
        BatchId = line.BatchId,
        Quantity = line.Quantity,
        UnitCost = line.UnitCost,
        ReasonType = line.ReasonType,
        ReasonNote = line.ReasonNote
    };

    private Task<int?> DefaultWarehouseAsync(CancellationToken ct)
        => _db.Warehouses.AsNoTracking().Where(w => w.IsActive).OrderBy(w => w.WarehouseId)
            .Select(w => (int?)w.WarehouseId).FirstOrDefaultAsync(ct);

    private Task<int?> EmployeeIdOfAsync(string userId, CancellationToken ct)
        => _db.Employees.AsNoTracking().Where(e => e.UserId == userId && e.IsActive).Select(e => (int?)e.EmployeeId).FirstOrDefaultAsync(ct);

    /// <summary>HUY + yyMMdd + 3 số nối tiếp trong ngày (giống dữ liệu mẫu HUY261031001).</summary>
    private async Task<string> NextCodeAsync(DateTime at, CancellationToken ct)
    {
        var prefix = CodePrefix + at.ToString("yyMMdd", CultureInfo.InvariantCulture);
        var codes = await _db.StockDisposals.AsNoTracking().Where(d => d.DisposalCode.StartsWith(prefix))
            .Select(d => d.DisposalCode).ToListAsync(ct);
        var max = 0;
        foreach (var code in codes)
            if (int.TryParse(code.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n > max) max = n;
        return prefix + (max + 1).ToString("D3", CultureInfo.InvariantCulture);
    }

    private static decimal RoundMoney(decimal value) => Math.Round(value, 0, MidpointRounding.AwayFromZero);

    private static string? LocationLabel(string? parentName, string? name)
        => name is null ? null : parentName is null ? name : parentName + " › " + name;
}
