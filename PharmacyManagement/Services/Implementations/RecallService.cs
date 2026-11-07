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

/// <summary>Thu hồi thuốc 2.3.3 (DEC-45).</summary>
public sealed class RecallService : IRecallService
{
    private const string CodePrefix = "THUHOI-";

    private static readonly CultureInfo Vietnamese = CultureInfo.GetCultureInfo("vi-VN");
    private static readonly StringComparer NameOrder = StringComparer.Create(Vietnamese, ignoreCase: true);

    private readonly PharmacyDbContext _db;
    private readonly IAuditLogService _audit;
    private readonly string _pharmacyName;

    public RecallService(PharmacyDbContext db, IAuditLogService audit, IOptions<PharmacyOptions> pharmacy)
    {
        _db = db;
        _audit = audit;
        _pharmacyName = pharmacy.Value.Name;
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    // ================================================================== Danh sách

    public async Task<RecallListViewModel> GetListAsync(RecallFilter filter, CancellationToken ct = default)
    {
        var query = _db.ProductRecalls.AsNoTracking().AsQueryable();
        switch (filter.Status)
        {
            case "open": query = query.Where(r => r.Status != (byte)RecallStatus.Completed); break;
            case "received": query = query.Where(r => r.Status == (byte)RecallStatus.Received); break;
            case "progress": query = query.Where(r => r.Status == (byte)RecallStatus.InProgress); break;
            case "done": query = query.Where(r => r.Status == (byte)RecallStatus.Completed); break;
            default: filter.Status = null; break;
        }

        var q = AccountHelpers.NormalizeFullName(filter.Q);
        filter.Q = q.Length == 0 ? null : q;
        if (filter.Q is not null)
        {
            var text = filter.Q;
            var folded = SearchText.Fold(text);
            query = query.Where(r => r.RecallCode.Contains(text)
                                     || (r.NoticeNumber != null && r.NoticeNumber.Contains(text))
                                     || r.Medicine.MedicineCode.Contains(text)
                                     || EF.Functions.Collate(r.Medicine.MedicineName.Replace("đ", "d").Replace("Đ", "D"), SearchText.Collation).Contains(folded)
                                     || EF.Functions.Collate(r.Reason.Replace("đ", "d").Replace("Đ", "D"), SearchText.Collation).Contains(folded)
                                     || r.RecallBatches.Any(b => b.Batch.BatchNumber.Contains(text)));
        }

        var total = await query.CountAsync(ct);
        var pageCount = Math.Max(1, (int)Math.Ceiling(total / (double)RecallFilter.PageSize));
        filter.Page = Math.Clamp(filter.Page, 1, pageCount);
        var raw = await query
            .OrderBy(r => r.Status == (byte)RecallStatus.Completed).ThenByDescending(r => r.CreatedAt).ThenByDescending(r => r.RecallId)
            .Skip((filter.Page - 1) * RecallFilter.PageSize).Take(RecallFilter.PageSize)
            .Select(r => new
            {
                r.RecallId, r.RecallCode, r.CreatedAt, r.Medicine.MedicineName, r.Medicine.MedicineCode, r.NoticeNumber, r.NoticeDate, r.Status,
                Lots = r.RecallBatches.OrderBy(b => b.RecallBatchId).Select(b => b.Batch.BatchNumber).ToList()
            })
            .ToListAsync(ct);

        return new RecallListViewModel
        {
            Filter = filter,
            Rows = raw.Select(r => new RecallRow(r.RecallId, r.RecallCode, r.CreatedAt, r.MedicineName, r.MedicineCode, r.NoticeNumber, r.NoticeDate,
                r.Lots.Count, string.Join(", ", r.Lots), r.Status)).ToList(),
            Total = total,
            GrandTotal = await _db.ProductRecalls.CountAsync(ct),
            PageCount = pageCount
        };
    }

    public Task<int> OpenCountAsync(CancellationToken ct = default)
        => _db.ProductRecalls.CountAsync(r => r.Status != (byte)RecallStatus.Completed, ct);

    // ================================================================== Chi tiết

    public async Task<RecallDetailsViewModel?> GetDetailsAsync(int recallId, bool includeBuyers, CancellationToken ct = default)
    {
        var r = await _db.ProductRecalls.AsNoTracking()
            .Where(x => x.RecallId == recallId)
            .Select(x => new
            {
                x.RecallId, x.RecallCode, x.Status, x.MedicineId, x.Medicine.MedicineCode, x.Medicine.MedicineName, BaseUnit = x.Medicine.BaseUnit.UnitName,
                x.NoticeNumber, x.NoticeDate, x.Reason, CreatedBy = x.CreatedByEmployee.FullName, x.CreatedAt
            })
            .FirstOrDefaultAsync(ct);
        if (r is null) return null;

        var lots = await _db.RecallBatches.AsNoTracking()
            .Where(b => b.RecallId == recallId)
            .OrderBy(b => b.Batch.ExpiryDate).ThenBy(b => b.RecallBatchId)
            .Select(b => new
            {
                b.RecallBatchId, b.BatchId, b.Batch.BatchNumber, b.Batch.ExpiryDate, b.QuantityOnHandAtRecall, b.QuantityRecovered, b.Batch.QuantityOnHand, b.Notes,
                LocationName = b.Batch.Location != null ? b.Batch.Location.LocationName : null,
                ParentName = b.Batch.Location != null && b.Batch.Location.ParentLocation != null ? b.Batch.Location.ParentLocation.LocationName : null
            })
            .ToListAsync(ct);

        var buyers = new List<RecallBuyerRow>();
        var buyerCount = 0;
        if (includeBuyers && lots.Count > 0)
        {
            var batchIds = lots.Select(l => l.BatchId).ToList();
            var sold = _db.SalesIssueAllocations.AsNoTracking()
                .Where(a => batchIds.Contains(a.BatchId) && a.InvoiceDetail.Invoice.Status != (byte)InvoiceStatus.Cancelled);
            buyerCount = await sold.CountAsync(ct);
            buyers = await sold
                .OrderByDescending(a => a.AllocatedAt).ThenByDescending(a => a.AllocationId)
                .Take(RecallDetailsViewModel.BuyerLimit)
                .Select(a => new RecallBuyerRow(a.AllocatedAt, a.InvoiceDetail.InvoiceId, a.InvoiceDetail.Invoice.InvoiceCode, a.Batch.BatchNumber,
                    a.InvoiceDetail.Invoice.Customer != null ? a.InvoiceDetail.Invoice.Customer.FullName : "Khách lẻ",
                    a.InvoiceDetail.Invoice.Customer != null ? a.InvoiceDetail.Invoice.Customer.Phone : null,
                    a.QuantityBase))
                .ToListAsync(ct);
        }

        return new RecallDetailsViewModel
        {
            RecallId = r.RecallId,
            Code = r.RecallCode,
            Status = r.Status,
            MedicineId = r.MedicineId,
            MedicineCode = r.MedicineCode,
            MedicineName = r.MedicineName,
            BaseUnit = r.BaseUnit,
            NoticeNumber = r.NoticeNumber,
            NoticeDate = r.NoticeDate,
            Reason = r.Reason,
            CreatedByName = r.CreatedBy,
            CreatedAt = r.CreatedAt,
            PharmacyName = _pharmacyName,
            Batches = lots.Select(l => new RecallBatchView(l.RecallBatchId, l.BatchId, l.BatchNumber, l.ExpiryDate,
                LocationLabel(l.ParentName, l.LocationName), l.QuantityOnHandAtRecall, l.QuantityRecovered, l.QuantityOnHand, l.Notes)).ToList(),
            Buyers = buyers,
            BuyerCount = buyerCount
        };
    }

    // ================================================================== Tiếp nhận thông báo (2.3.3.1)

    public async Task<RecallFormViewModel> NewFormAsync(int? medicineId, int? batchId, CancellationToken ct = default)
    {
        var form = new RecallFormViewModel
        {
            MedicineId = medicineId,
            NoticeDate = Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        };
        if (batchId is not null)
        {
            var lot = await _db.MedicineBatches.AsNoTracking().Where(b => b.BatchId == batchId)
                .Select(b => new { b.MedicineId, b.Status }).FirstOrDefaultAsync(ct);
            if (lot is not null)
            {
                form.MedicineId = lot.MedicineId;
                if (lot.Status != (byte)BatchStatus.Recalled) form.BatchIds.Add(batchId.Value);
            }
        }
        await PrepareFormAsync(form, ct);
        return form;
    }

    public async Task PrepareFormAsync(RecallFormViewModel form, CancellationToken ct = default)
    {
        var recalled = (byte)BatchStatus.Recalled;
        var medicines = await _db.Medicines.AsNoTracking()
            .Where(m => m.MedicineBatches.Any(b => b.Status != recalled))
            .Select(m => new { m.MedicineId, m.MedicineName, m.MedicineCode })
            .ToListAsync(ct);
        form.Medicines = medicines
            .OrderBy(m => m.MedicineName, NameOrder)
            .Select(m => new LookupItem(m.MedicineId, m.MedicineName + " (" + m.MedicineCode + ")"))
            .ToList();

        if (form.MedicineId is null) return;
        var medicineId = form.MedicineId.Value;
        var medicine = await _db.Medicines.AsNoTracking().Where(m => m.MedicineId == medicineId)
            .Select(m => new { m.MedicineName, BaseUnit = m.BaseUnit.UnitName }).FirstOrDefaultAsync(ct);
        if (medicine is null)
        {
            form.MedicineId = null;
            return;
        }
        form.MedicineName = medicine.MedicineName;
        form.BaseUnit = medicine.BaseUnit;
        form.Batches = await _db.MedicineBatches.AsNoTracking()
            .Where(b => b.MedicineId == medicineId && b.Status != recalled)
            .OrderBy(b => b.ExpiryDate).ThenBy(b => b.BatchNumber)
            .Select(b => new RecallBatchOption(b.BatchId, b.BatchNumber, b.ExpiryDate, b.QuantityOnHand, b.Status,
                b.GoodsReceipt != null ? b.GoodsReceipt.Supplier.SupplierName : null,
                b.SalesIssueAllocations.Where(a => a.InvoiceDetail.Invoice.Status != (byte)InvoiceStatus.Cancelled).Sum(a => (decimal?)a.QuantityBase) ?? 0m))
            .ToListAsync(ct);
    }

    public async Task<OperationResult<int>> CreateAsync(RecallFormViewModel form, string actorUserId, CancellationToken ct = default)
    {
        var employeeId = await EmployeeIdOfAsync(actorUserId, ct);
        if (employeeId is null) return OperationResult<int>.Fail("Tài khoản chưa gắn hồ sơ nhân viên nên chưa lập phiếu thu hồi được.");

        var medicineId = form.MedicineId!.Value;
        var reason = (form.Reason ?? string.Empty).Trim();
        if (reason.Length == 0) return OperationResult<int>.Fail("Nhập lý do thu hồi.", nameof(form.Reason));
        var notice = string.IsNullOrWhiteSpace(form.NoticeNumber) ? null : form.NoticeNumber.Trim();
        DateOnly? noticeDate = null;
        if (!string.IsNullOrWhiteSpace(form.NoticeDate))
        {
            if (!DateOnly.TryParseExact(form.NoticeDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                return OperationResult<int>.Fail("Ngày công văn không hợp lệ.", nameof(form.NoticeDate));
            if (parsed > Today) return OperationResult<int>.Fail("Ngày công văn không được sau hôm nay.", nameof(form.NoticeDate));
            noticeDate = parsed;
        }

        var ids = form.BatchIds.Distinct().ToList();
        if (ids.Count == 0) return OperationResult<int>.Fail("Chọn ít nhất một lô bị thu hồi.", nameof(form.BatchIds));

        var recalled = (byte)BatchStatus.Recalled;
        var before = await _db.MedicineBatches.AsNoTracking()
            .Where(b => ids.Contains(b.BatchId))
            .Select(b => new { b.BatchId, b.MedicineId, b.BatchNumber, b.Status })
            .ToListAsync(ct);
        if (before.Count != ids.Count || before.Any(b => b.MedicineId != medicineId))
            return OperationResult<int>.Fail("Có lô không thuộc thuốc đã chọn. Tải lại trang rồi chọn lại.", nameof(form.BatchIds));
        var already = before.Where(b => b.Status == recalled).Select(b => b.BatchNumber).ToList();
        if (already.Count > 0)
            return OperationResult<int>.Fail($"Lô {string.Join(", ", already)} đã nằm trong một phiếu thu hồi khác.", nameof(form.BatchIds));

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        // Khóa lô: chuyển sang Thu hồi chỉ khi lô chưa bị thu hồi — số dòng phải khớp, nếu không thì có người vừa thu hồi trước
        var locked = await _db.MedicineBatches
            .Where(b => ids.Contains(b.BatchId) && b.MedicineId == medicineId && b.Status != recalled)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.Status, recalled), ct);
        if (locked != ids.Count)
        {
            await tx.RollbackAsync(ct);
            return OperationResult<int>.Fail("Có lô vừa được đưa vào phiếu thu hồi khác. Tải lại trang rồi chọn lại.", nameof(form.BatchIds));
        }

        // Tồn lúc thu hồi đọc sau khi đã khóa lô (lô Thu hồi không bán, không xuất được nữa)
        var onHand = await _db.MedicineBatches.AsNoTracking().Where(b => ids.Contains(b.BatchId))
            .Select(b => new { b.BatchId, b.QuantityOnHand }).ToDictionaryAsync(b => b.BatchId, b => b.QuantityOnHand, ct);

        var now = DateTime.Now;
        var recall = new ProductRecall
        {
            RecallCode = await NextCodeAsync(now, ct),
            MedicineId = medicineId,
            NoticeNumber = notice,
            NoticeDate = noticeDate,
            Reason = reason,
            Status = (byte)RecallStatus.Received,
            CreatedByEmployeeId = employeeId.Value,
            CreatedAt = now
        };
        foreach (var id in ids)
        {
            recall.RecallBatches.Add(new RecallBatch
            {
                BatchId = id,
                QuantityOnHandAtRecall = onHand[id],
                QuantityRecovered = 0
            });
        }
        _db.ProductRecalls.Add(recall);

        try
        {
            await _db.SaveChangesAsync(ct);
            await _audit.WriteAsync("RECALL_CREATE", "ProductRecalls", recall.RecallId.ToString(CultureInfo.InvariantCulture),
                new { Batches = before.Select(b => new { b.BatchId, b.BatchNumber, b.Status }).ToList() },
                new
                {
                    recall.RecallCode, recall.MedicineId, recall.NoticeNumber, recall.NoticeDate, recall.Reason,
                    Batches = before.Select(b => new { b.BatchId, b.BatchNumber, Status = recalled, QuantityOnHandAtRecall = onHand[b.BatchId] }).ToList()
                },
                actorUserId, ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            return OperationResult<int>.Fail("Chưa lưu được phiếu thu hồi do dữ liệu vừa thay đổi. Tải lại trang rồi thử lại.");
        }

        var stock = ids.Sum(id => onHand[id]);
        return OperationResult<int>.Ok(recall.RecallId,
            $"Đã lập phiếu thu hồi {recall.RecallCode}: khóa {ids.Count} lô, ngừng bán ngay. Tồn cần thu hồi {Format.Number(stock)}.");
    }

    // ================================================================== Lập phiếu thu hồi (2.3.3.3)

    public async Task<OperationResult> RecoverAsync(int recallId, RecoverForm form, string actorUserId, CancellationToken ct = default)
    {
        var employeeId = await EmployeeIdOfAsync(actorUserId, ct);
        if (employeeId is null) return OperationResult.Fail("Tài khoản chưa gắn hồ sơ nhân viên nên chưa thu hồi được.");

        var recall = await _db.ProductRecalls.AsNoTracking().Where(r => r.RecallId == recallId)
            .Select(r => new { r.RecallCode, r.Status, BaseUnit = r.Medicine.BaseUnit.UnitName }).FirstOrDefaultAsync(ct);
        if (recall is null) return OperationResult.Fail("Không tìm thấy phiếu thu hồi.");
        if (recall.Status == (byte)RecallStatus.Completed) return OperationResult.Fail($"Phiếu {recall.RecallCode} đã hoàn tất.");

        var wanted = form.Lines.Where(l => l.Quantity is > 0).ToList();
        if (wanted.Count == 0) return OperationResult.Fail("Nhập số lượng thu hồi cho ít nhất một lô.");
        if (wanted.Select(l => l.RecallBatchId).Distinct().Count() != wanted.Count) return OperationResult.Fail("Mỗi lô chỉ nhập một dòng.");
        foreach (var l in wanted)
        {
            if (l.Quantity!.Value != Math.Round(l.Quantity.Value, 3)) return OperationResult.Fail("Số lượng thu hồi tối đa 3 chữ số thập phân.");
            if (l.Notes is { Length: > 500 }) return OperationResult.Fail("Ghi chú tối đa 500 ký tự.");
        }

        var lineIds = wanted.Select(l => l.RecallBatchId).ToList();
        var lots = await _db.RecallBatches.AsNoTracking()
            .Where(b => b.RecallId == recallId && lineIds.Contains(b.RecallBatchId))
            .Select(b => new { b.RecallBatchId, b.BatchId, b.Batch.MedicineId, b.Batch.WarehouseId, b.Batch.BatchNumber, b.Notes })
            .ToDictionaryAsync(b => b.RecallBatchId, ct);
        if (lots.Count != wanted.Count) return OperationResult.Fail("Có lô không thuộc phiếu thu hồi này. Tải lại trang.");

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var now = DateTime.Now;
        var done = new List<object>();
        foreach (var line in wanted)
        {
            var lot = lots[line.RecallBatchId];
            var quantity = line.Quantity!.Value;
            var recallBatchId = line.RecallBatchId;
            var batchId = lot.BatchId;

            // Không vượt tồn lúc thu hồi (CHECK trong CSDL) và không vượt tồn hiện tại của lô
            var counted = await _db.RecallBatches
                .Where(b => b.RecallBatchId == recallBatchId && b.QuantityRecovered + quantity <= b.QuantityOnHandAtRecall)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.QuantityRecovered, b => b.QuantityRecovered + quantity), ct);
            var taken = counted == 0 ? 0 : await _db.MedicineBatches
                .Where(b => b.BatchId == batchId && b.QuantityOnHand >= quantity)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.QuantityOnHand, b => b.QuantityOnHand - quantity), ct);
            if (counted == 0 || taken == 0)
            {
                await tx.RollbackAsync(ct);
                _db.ChangeTracker.Clear();
                var state = await _db.RecallBatches.AsNoTracking().Where(b => b.RecallBatchId == recallBatchId)
                    .Select(b => new { b.QuantityOnHandAtRecall, b.QuantityRecovered, b.Batch.QuantityOnHand }).FirstAsync(ct);
                var left = Math.Max(0, Math.Min(state.QuantityOnHandAtRecall - state.QuantityRecovered, state.QuantityOnHand));
                return OperationResult.Fail($"Lô {lot.BatchNumber} chỉ còn thu hồi được {Format.Number(left)} {recall.BaseUnit}. Chưa ghi lô nào, sửa số lượng rồi lưu lại.");
            }

            var note = string.IsNullOrWhiteSpace(line.Notes) ? null : line.Notes.Trim();
            if (note is not null)
            {
                var text = string.IsNullOrWhiteSpace(lot.Notes) ? note : lot.Notes + "\n" + note;
                if (text.Length > 500) text = text[^500..];
                await _db.RecallBatches.Where(b => b.RecallBatchId == recallBatchId)
                    .ExecuteUpdateAsync(s => s.SetProperty(b => b.Notes, text), ct);
            }

            _db.InventoryTransactions.Add(new InventoryTransaction
            {
                MedicineId = lot.MedicineId,
                BatchId = batchId,
                WarehouseId = lot.WarehouseId,
                TransactionType = (byte)InventoryTransactionType.Recall,
                QuantityChange = -quantity,
                ReferenceType = InventoryReferenceTypes.For(InventoryTransactionType.Recall),
                ReferenceId = recallBatchId,
                TransactionDate = now,
                CreatedByEmployeeId = employeeId,
                Notes = "Thu hồi — " + recall.RecallCode
            });
            done.Add(new { recallBatchId, batchId, lot.BatchNumber, Quantity = quantity, Notes = note });
        }

        try
        {
            await _db.SaveChangesAsync(ct);
            await _db.ProductRecalls.Where(r => r.RecallId == recallId && r.Status == (byte)RecallStatus.Received)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, (byte)RecallStatus.InProgress), ct);
            await _audit.WriteAsync("RECALL_RECOVER", "ProductRecalls", recallId.ToString(CultureInfo.InvariantCulture),
                new { recall.Status }, new { recall.RecallCode, Lines = done }, actorUserId, ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            return OperationResult.Fail("Chưa ghi được do dữ liệu vừa thay đổi. Tải lại trang rồi thử lại.");
        }

        var sum = wanted.Sum(l => l.Quantity!.Value);
        return OperationResult.Ok($"Đã thu hồi {Format.Number(sum)} {recall.BaseUnit} từ {wanted.Count} lô, tồn kho đã giảm tương ứng.");
    }

    public async Task<OperationResult> CompleteAsync(int recallId, string actorUserId, CancellationToken ct = default)
    {
        var recall = await _db.ProductRecalls.AsNoTracking().Where(r => r.RecallId == recallId)
            .Select(r => new { r.RecallCode, r.Status }).FirstOrDefaultAsync(ct);
        if (recall is null) return OperationResult.Fail("Không tìm thấy phiếu thu hồi.");
        if (recall.Status == (byte)RecallStatus.Completed) return OperationResult.Fail($"Phiếu {recall.RecallCode} đã hoàn tất.");

        var pending = await _db.RecallBatches.AsNoTracking()
            .Where(b => b.RecallId == recallId && b.QuantityRecovered < b.QuantityOnHandAtRecall && b.Batch.QuantityOnHand > 0)
            .Select(b => b.Batch.BatchNumber).ToListAsync(ct);
        if (pending.Count > 0)
            return OperationResult.Fail($"Lô {string.Join(", ", pending)} còn tồn chưa thu hồi. Thu hồi hết rồi mới hoàn tất.");

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var updated = await _db.ProductRecalls
            .Where(r => r.RecallId == recallId && r.Status != (byte)RecallStatus.Completed)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, (byte)RecallStatus.Completed), ct);
        if (updated == 0) return OperationResult.Fail("Phiếu vừa được hoàn tất ở nơi khác. Tải lại trang.");
        await _audit.WriteAsync("RECALL_COMPLETE", "ProductRecalls", recallId.ToString(CultureInfo.InvariantCulture),
            new { recall.Status }, new { recall.RecallCode, Status = (byte)RecallStatus.Completed }, actorUserId, ct);
        await tx.CommitAsync(ct);
        return OperationResult.Ok($"Phiếu thu hồi {recall.RecallCode} đã hoàn tất.");
    }

    // ================================================================== Dùng chung

    private static string? LocationLabel(string? parent, string? name)
        => name is null ? null : parent is null ? name : parent + " › " + name;

    private Task<int?> EmployeeIdOfAsync(string userId, CancellationToken ct)
        => _db.Employees.AsNoTracking().Where(e => e.UserId == userId && e.IsActive).Select(e => (int?)e.EmployeeId).FirstOrDefaultAsync(ct);

    /// <summary>THUHOI-yyMM-nn nối tiếp trong tháng (giống dữ liệu mẫu THUHOI-2610-02).</summary>
    private async Task<string> NextCodeAsync(DateTime at, CancellationToken ct)
    {
        var prefix = CodePrefix + at.ToString("yyMM", CultureInfo.InvariantCulture) + "-";
        var codes = await _db.ProductRecalls.AsNoTracking().Where(r => r.RecallCode.StartsWith(prefix))
            .Select(r => r.RecallCode).ToListAsync(ct);
        var max = 0;
        foreach (var code in codes)
            if (int.TryParse(code.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n > max) max = n;
        return prefix + (max + 1).ToString("D2", CultureInfo.InvariantCulture);
    }
}
