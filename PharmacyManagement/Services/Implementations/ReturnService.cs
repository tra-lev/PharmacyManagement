using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PharmacyManagement.Data;
using PharmacyManagement.Helpers;
using PharmacyManagement.Models.Entities;
using PharmacyManagement.Models.Enums;
using PharmacyManagement.Models.ViewModels.Catalog;
using PharmacyManagement.Models.ViewModels.Sales;
using PharmacyManagement.Services.Interfaces;

namespace PharmacyManagement.Services.Implementations;

public sealed class ReturnService : IReturnService
{
    private const string CodePrefix = "TH";
    private const int NoteMax = 500;
    private const int LookupMax = 20;
    private static readonly Regex ReferencePattern = new(@"^[A-Za-z0-9][A-Za-z0-9./_\-]*$", RegexOptions.CultureInvariant);

    private readonly PharmacyDbContext _db;
    private readonly IAuditLogService _audit;
    private readonly IShiftService _shifts;

    public ReturnService(PharmacyDbContext db, IAuditLogService audit, IShiftService shifts)
    {
        _db = db;
        _audit = audit;
        _shifts = shifts;
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    // ================================================================== 3.4.1 Tra hóa đơn gốc

    public async Task<ReturnLookupViewModel> LookupAsync(string? q, CancellationToken ct = default)
    {
        var text = AccountHelpers.NormalizeFullName(q);
        if (text.Length == 0) return new ReturnLookupViewModel();

        var query = _db.SalesInvoices.AsNoTracking().Where(i => i.Status == (byte)InvoiceStatus.Paid);
        var digits = new string(text.Where(char.IsAsciiDigit).ToArray());
        var byPhone = digits.Length >= 3 && digits.Length == text.Count(c => !char.IsWhiteSpace(c) && c != '.' && c != '-');
        var folded = SearchText.Fold(text);
        query = byPhone
            ? query.Where(i => i.InvoiceCode.Contains(text) || (i.Customer != null && i.Customer.Phone != null && i.Customer.Phone.Contains(digits)))
            : query.Where(i => i.InvoiceCode.Contains(text)
                               || (i.Customer != null
                                   && EF.Functions.Collate(i.Customer.FullName.Replace("đ", "d").Replace("Đ", "D"), SearchText.Collation).Contains(folded)));

        var invoices = await query
            .OrderByDescending(i => i.InvoiceDate).ThenByDescending(i => i.InvoiceId)
            .Take(LookupMax)
            .Select(i => new
            {
                i.InvoiceId, i.InvoiceCode, i.InvoiceDate, i.TotalAmount,
                CustomerName = i.Customer != null ? i.Customer.FullName : null,
                CustomerPhone = i.Customer != null ? i.Customer.Phone : null,
                PaidAt = i.Payments.Where(p => p.Status == (byte)PaymentStatus.Succeeded).Max(p => (DateTime?)p.PaidAt),
                Sold = i.SalesInvoiceDetails.Sum(d => (decimal?)d.Quantity) ?? 0
            })
            .ToListAsync(ct);
        var ids = invoices.Select(i => i.InvoiceId).ToList();
        var returned = await _db.ReturnDetails.AsNoTracking()
            .Where(r => ids.Contains(r.InvoiceDetail.InvoiceId) && r.Return.Status != (byte)ReturnStatus.Cancelled)
            .GroupBy(r => r.InvoiceDetail.InvoiceId)
            .Select(g => new { InvoiceId = g.Key, Quantity = g.Sum(r => r.Quantity) })
            .ToDictionaryAsync(x => x.InvoiceId, x => x.Quantity, ct);

        var today = Today;
        return new ReturnLookupViewModel
        {
            Q = text,
            // Hóa đơn mua hôm nay (đổi trả được) xếp trước
            Rows = invoices.Select(i =>
            {
                var soldAt = i.PaidAt ?? i.InvoiceDate;
                return new ReturnLookupRow(i.InvoiceId, i.InvoiceCode, soldAt, i.CustomerName, ContactRules.DisplayPhone(i.CustomerPhone), i.TotalAmount,
                    DateOnly.FromDateTime(soldAt) == today, i.Sold > returned.GetValueOrDefault(i.InvoiceId));
            }).OrderByDescending(r => r.SameDay).ThenByDescending(r => r.SoldAt).ToList()
        };
    }

    // ================================================================== 3.4.2 + 3.4.3 Kiểm tra điều kiện, hoàn tiền

    public async Task<ReturnFormViewModel?> NewFormAsync(int invoiceId, string userId, long? requestId, CancellationToken ct = default)
    {
        var form = new ReturnFormViewModel();
        // Quản lý mở từ yêu cầu của dược sĩ: điền sẵn số lượng, tình trạng hàng, lý do dược sĩ đã ghi
        ReturnRequestInfo? request = null;
        if (requestId is not null)
        {
            request = (await RequestsTodayAsync(null, ct)).FirstOrDefault(r => r.RequestId == requestId && r.InvoiceId == invoiceId);
            if (request is { State: ReturnRequestState.Pending })
            {
                form.RequestId = request.RequestId;
                form.Reason = request.Reason;
                form.Lines = request.Lines.Select(l => new ReturnLineForm { InvoiceDetailId = l.InvoiceDetailId, Quantity = l.Quantity, ConditionNote = l.ConditionNote }).ToList();
            }
        }
        if (!await PrepareFormAsync(form, invoiceId, userId, ct)) return null;
        // Mặc định hoàn theo hình thức khách đã trả (nhiều hình thức: lấy hình thức trả nhiều tiền nhất)
        if (form.PaidMethods.Count > 0) form.RefundMethod = form.PaidMethods[0];
        return form;
    }

    public async Task<bool> PrepareFormAsync(ReturnFormViewModel form, int invoiceId, string userId, CancellationToken ct = default)
    {
        var info = await InvoiceAsync(invoiceId, ct);
        if (info is null) return false;
        form.InvoiceId = invoiceId;
        form.InvoiceCode = info.Code;
        form.SoldAt = info.SoldAt;
        form.CustomerName = info.CustomerName;
        form.CustomerPhone = ContactRules.DisplayPhone(info.CustomerPhone);
        form.PrescriptionCode = info.PrescriptionCode;
        form.InvoiceTotal = info.TotalAmount;
        form.SubTotal = info.SubTotal;
        form.DiscountAmount = info.DiscountAmount;
        form.SellerName = info.SellerName;
        form.ShiftCode = info.ShiftCode;
        form.Payments = info.Payments;
        form.PaidMethods = info.PaidMethods;
        form.InvoiceLines = info.Lines;
        form.BlockedReason = BlockedReason(info);
        form.ReturnConditions = await _db.PricePolicies.AsNoTracking()
            .Where(p => p.IsActive && (p.EffectiveFrom == null || p.EffectiveFrom <= Today) && (p.EffectiveTo == null || p.EffectiveTo >= Today))
            .OrderByDescending(p => p.PricePolicyId)
            .Select(p => p.ReturnConditions)
            .FirstOrDefaultAsync(ct);
        form.HasOpenShift = await _shifts.GetOpenShiftIdAsync(userId, ct) is not null;
        var requests = (await RequestsTodayAsync(null, ct)).Where(r => r.InvoiceId == invoiceId).ToList();
        form.PendingRequest = requests.FirstOrDefault(r => r.State == ReturnRequestState.Pending);
        form.Request = form.RequestId is null ? null : requests.FirstOrDefault(r => r.RequestId == form.RequestId);
        if (form.Request is not { State: ReturnRequestState.Pending }) { form.Request = null; form.RequestId = null; }

        // Giữ số người dùng đã nhập, theo đúng thứ tự dòng của hóa đơn
        var posted = form.Lines.Where(l => l is not null).GroupBy(l => l.InvoiceDetailId).ToDictionary(g => g.Key, g => g.First());
        form.Lines = info.Lines.Select(l => posted.TryGetValue(l.InvoiceDetailId, out var p)
            ? new ReturnLineForm { InvoiceDetailId = l.InvoiceDetailId, Quantity = p.Quantity, ConditionNote = p.ConditionNote }
            : new ReturnLineForm { InvoiceDetailId = l.InvoiceDetailId }).ToList();
        return true;
    }

    public async Task<OperationResult<int>> CreateAsync(int invoiceId, ReturnFormViewModel form, string userId, CancellationToken ct = default)
    {
        var employeeId = await _db.Employees.AsNoTracking().Where(e => e.UserId == userId && e.IsActive)
            .Select(e => (int?)e.EmployeeId).FirstOrDefaultAsync(ct);
        if (employeeId is null) return OperationResult<int>.Fail("Tài khoản chưa gắn hồ sơ nhân viên đang làm việc nên chưa hoàn tiền được.");

        var reason = AccountHelpers.NormalizeFullName(form.Reason);
        if (reason.Length == 0) return OperationResult<int>.Fail("Nhập lý do khách trả hàng.", nameof(form.Reason));
        if (reason.Length > NoteMax) return OperationResult<int>.Fail("Lý do tối đa 500 ký tự.", nameof(form.Reason));

        // Lập từ yêu cầu của dược sĩ: yêu cầu phải còn chờ và đúng hóa đơn này (không thì lập như tự tiếp nhận)
        if (form.RequestId is not null)
        {
            var requestId = form.RequestId.Value;
            var request = (await RequestsTodayAsync(null, ct)).FirstOrDefault(r => r.RequestId == requestId);
            if (request is null || request.InvoiceId != invoiceId || request.State != ReturnRequestState.Pending) form.RequestId = null;
        }

        // ---- Hình thức hoàn tiền ----
        int? shiftId = null;
        string? reference = null;
        switch (form.RefundMethod)
        {
            case (byte)PaymentMethod.Cash:
                // Q-16: hoàn tiền mặt trừ vào ca đang mở của chính người hoàn tiền
                shiftId = await _shifts.GetOpenShiftIdAsync(userId, ct);
                if (shiftId is null) return OperationResult<int>.Fail("Bạn chưa mở ca nên chưa hoàn tiền mặt được. Mở ca hoặc chọn hoàn chuyển khoản / thẻ.");
                break;
            case (byte)PaymentMethod.BankTransfer:
            case (byte)PaymentMethod.Card:
                reference = form.RefundReference?.Trim();
                if (string.IsNullOrEmpty(reference)) return OperationResult<int>.Fail("Nhập mã giao dịch hoàn tiền.", nameof(form.RefundReference));
                if (reference.Length > 100 || !ReferencePattern.IsMatch(reference))
                    return OperationResult<int>.Fail("Mã giao dịch chỉ gồm chữ không dấu và chữ số.", nameof(form.RefundReference));
                break;
            default:
                return OperationResult<int>.Fail("Chọn hình thức hoàn tiền.", nameof(form.RefundMethod));
        }

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            // Khóa hóa đơn tới khi xong: hai người cùng trả một hóa đơn thì người sau thấy đúng số đã trả
            var locked = await _db.SalesInvoices
                .Where(i => i.InvoiceId == invoiceId && i.Status == (byte)InvoiceStatus.Paid)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, i => i.Status), ct);
            if (locked == 0) return OperationResult<int>.Fail("Chỉ trả được hóa đơn đã thanh toán.");

            // ---- Đối chiếu với hóa đơn gốc (đọc lại trong transaction) ----
            var info = await InvoiceAsync(invoiceId, ct);
            if (info is null) return OperationResult<int>.Fail("Không tìm thấy hóa đơn.");
            var blocked = BlockedReason(info);
            if (blocked is not null) return OperationResult<int>.Fail(blocked);

            var lines = info.Lines.ToDictionary(l => l.InvoiceDetailId);
            var details = new List<ReturnDetail>();
            decimal refund = 0;
            foreach (var l in form.Lines)
            {
                if (l.Quantity is null || l.Quantity == 0) continue;
                if (!lines.TryGetValue(l.InvoiceDetailId, out var line)) return OperationResult<int>.Fail("Dòng thuốc không thuộc hóa đơn này.");
                if (l.Quantity < 0 || l.Quantity > line.Remaining)
                    return OperationResult<int>.Fail(line.Remaining <= 0
                        ? $"{line.MedicineName} đã trả hết."
                        : $"{line.MedicineName}: trả tối đa {Format.Number(line.Remaining)} {line.UnitName}.");
                var note = l.ConditionNote?.Trim();
                if (note is { Length: > 300 }) return OperationResult<int>.Fail($"{line.MedicineName}: tình trạng hàng tối đa 300 ký tự.");

                var quantity = (decimal)l.Quantity.Value;
                decimal unit;
                decimal amount;
                if (quantity == line.Remaining)
                {
                    // Trả hết phần còn lại của dòng: hoàn đúng số tiền còn lại (không mất tiền lẻ do làm tròn)
                    amount = line.LineTotal - line.RefundedAmount;
                    unit = Math.Round(amount / quantity, 2, MidpointRounding.AwayFromZero);
                }
                else
                {
                    unit = line.UnitRefundPrice;
                    amount = quantity * unit;
                }
                refund += amount;
                details.Add(new ReturnDetail
                {
                    InvoiceDetailId = line.InvoiceDetailId,
                    Quantity = quantity,
                    UnitRefundPrice = unit,
                    RestockStatus = (byte)RestockStatus.Pending,
                    ConditionNote = string.IsNullOrEmpty(note) ? null : note
                });
            }
            if (details.Count == 0) return OperationResult<int>.Fail("Nhập số lượng khách trả cho ít nhất một thuốc.");

            var now = DateTime.Now;
            var ret = new Return
            {
                ReturnCode = await NextCodeAsync(now, ct),
                InvoiceId = invoiceId,
                ProcessedByEmployeeId = employeeId.Value,
                ReturnDate = now,
                Reason = reason,
                RefundAmount = refund,
                Status = (byte)ReturnStatus.RefundedPendingRestock,
                ShiftId = shiftId,
                RefundMethod = form.RefundMethod,
                RefundReference = reference
            };
            foreach (var d in details) ret.ReturnDetails.Add(d);
            _db.Returns.Add(ret);
            await _db.SaveChangesAsync(ct);
            if (form.RequestId is not null)
                await _audit.WriteAsync(RequestDoneAction, RequestResultEntity, form.RequestId.Value.ToString(CultureInfo.InvariantCulture), null,
                    new { ret.ReturnId, ret.ReturnCode }, userId, ct);
            await _audit.WriteAsync("RETURN_CREATE", "Returns", ret.ReturnId.ToString(CultureInfo.InvariantCulture), null,
                new
                {
                    ret.ReturnCode, ret.InvoiceId, InvoiceCode = info.Code, ret.RefundAmount, ret.RefundMethod, ret.RefundReference, ret.ShiftId,
                    Lines = details.Select(d => new { d.InvoiceDetailId, d.Quantity, d.UnitRefundPrice })
                }, userId, ct);
            await tx.CommitAsync(ct);
            return OperationResult<int>.Ok(ret.ReturnId,
                $"Đã lập phiếu {ret.ReturnCode} — hoàn {Format.Money(refund)} cho khách bằng " + form.RefundMethod switch
                {
                    (byte)PaymentMethod.Cash => "tiền mặt.",
                    (byte)PaymentMethod.BankTransfer => "chuyển khoản.",
                    _ => "thẻ."
                });
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            return OperationResult<int>.Fail("Chưa lưu được. Bấm Lưu lại.");
        }
    }

    // ================================================================== Danh sách, chi tiết

    public async Task<ReturnListViewModel> GetListAsync(ReturnFilter filter, CancellationToken ct = default)
    {
        var query = _db.Returns.AsNoTracking().AsQueryable();
        byte? status = filter.Status switch
        {
            "pending" => (byte)ReturnStatus.RefundedPendingRestock,
            "completed" => (byte)ReturnStatus.Completed,
            _ => null
        };
        if (status is null) filter.Status = null;
        else query = query.Where(r => r.Status == status);

        var q = AccountHelpers.NormalizeFullName(filter.Q);
        filter.Q = q.Length == 0 ? null : q;
        if (filter.Q is not null)
        {
            var text = filter.Q;
            var folded = SearchText.Fold(text);
            var digits = new string(text.Where(char.IsAsciiDigit).ToArray());
            var byPhone = digits.Length >= 3 && digits.Length == text.Count(c => !char.IsWhiteSpace(c) && c != '.' && c != '-');
            query = query.Where(r => r.ReturnCode.Contains(text) || r.Invoice.InvoiceCode.Contains(text)
                                     || (r.Invoice.Customer != null
                                         && (EF.Functions.Collate(r.Invoice.Customer.FullName.Replace("đ", "d").Replace("Đ", "D"), SearchText.Collation).Contains(folded)
                                             || (byPhone && r.Invoice.Customer.Phone != null && r.Invoice.Customer.Phone.Contains(digits)))));
        }

        var total = await query.CountAsync(ct);
        var pageCount = Math.Max(1, (int)Math.Ceiling(total / (double)ReturnFilter.PageSize));
        filter.Page = Math.Clamp(filter.Page, 1, pageCount);
        var rows = await query
            .OrderByDescending(r => r.ReturnDate).ThenByDescending(r => r.ReturnId)
            .Skip((filter.Page - 1) * ReturnFilter.PageSize).Take(ReturnFilter.PageSize)
            .Select(r => new ReturnRow(r.ReturnId, r.ReturnCode, r.ReturnDate, r.Invoice.InvoiceCode,
                r.Invoice.Customer != null ? r.Invoice.Customer.FullName : null, r.RefundAmount, r.RefundMethod, r.Status,
                r.ReturnDetails.Count(d => d.RestockStatus == (byte)RestockStatus.Pending)))
            .ToListAsync(ct);

        return new ReturnListViewModel
        {
            Filter = filter,
            Rows = rows,
            Total = total,
            GrandTotal = await _db.Returns.CountAsync(ct),
            PageCount = pageCount
        };
    }

    public async Task<ReturnDetailsViewModel?> GetDetailsAsync(int returnId, CancellationToken ct = default)
    {
        var r = await _db.Returns.AsNoTracking().Where(x => x.ReturnId == returnId)
            .Select(x => new
            {
                x.ReturnId, x.ReturnCode, x.Status, x.ReturnDate, x.InvoiceId, x.Invoice.InvoiceCode,
                CustomerName = x.Invoice.Customer != null ? x.Invoice.Customer.FullName : null,
                ProcessedBy = x.ProcessedByEmployee.FullName, x.Reason, x.RefundAmount, x.RefundMethod, x.RefundReference,
                ShiftCode = x.Shift != null ? x.Shift.ShiftCode : null
            })
            .FirstOrDefaultAsync(ct);
        if (r is null) return null;

        var lines = await _db.ReturnDetails.AsNoTracking()
            .Where(d => d.ReturnId == returnId)
            .OrderBy(d => d.ReturnDetailId)
            .Select(d => new
            {
                d.ReturnDetailId, d.InvoiceDetailId, d.InvoiceDetail.Medicine.MedicineCode, d.InvoiceDetail.Medicine.MedicineName,
                UnitName = d.InvoiceDetail.Unit.UnitName, d.Quantity, d.InvoiceDetail.ConversionToBase,
                BaseUnitName = d.InvoiceDetail.Medicine.BaseUnit.UnitName, d.UnitRefundPrice, d.RestockStatus,
                BatchNumber = d.Batch != null ? d.Batch.BatchNumber : null, d.ConditionNote
            })
            .ToListAsync(ct);

        var pendingDetailIds = lines.Where(l => l.RestockStatus == (byte)RestockStatus.Pending).Select(l => l.InvoiceDetailId).Distinct().ToList();
        var options = await BatchOptionsAsync(pendingDetailIds, ct);

        return new ReturnDetailsViewModel
        {
            ReturnId = r.ReturnId,
            Code = r.ReturnCode,
            Status = r.Status,
            ReturnDate = r.ReturnDate,
            InvoiceId = r.InvoiceId,
            InvoiceCode = r.InvoiceCode,
            CustomerName = r.CustomerName,
            ProcessedByName = r.ProcessedBy,
            Reason = r.Reason,
            RefundAmount = r.RefundAmount,
            RefundMethod = r.RefundMethod,
            RefundReference = r.RefundReference,
            ShiftCode = r.ShiftCode,
            Lines = lines.Select(l =>
            {
                var baseQty = BaseOf(l.Quantity, l.ConversionToBase);
                var batches = l.RestockStatus == (byte)RestockStatus.Pending && options.TryGetValue(l.InvoiceDetailId, out var list)
                    ? list.Select(b => b with
                    {
                        Usable = b.Usable && b.CapacityBase >= baseQty,
                        Problem = b.Problem ?? (b.CapacityBase < baseQty ? $"lô này chỉ nhận lại được {Format.Number(b.CapacityBase)} {l.BaseUnitName}" : null)
                    }).ToList()
                    : new List<RestockBatchOption>();
                return new ReturnDetailLine(l.ReturnDetailId, l.MedicineCode, l.MedicineName, l.UnitName, l.Quantity, baseQty, l.BaseUnitName,
                    l.UnitRefundPrice, Math.Round(l.Quantity * l.UnitRefundPrice, 0, MidpointRounding.AwayFromZero), l.RestockStatus, l.BatchNumber,
                    l.ConditionNote, batches);
            }).ToList()
        };
    }

    // ================================================================== 3.4.4 Nhập lại hoặc không nhập

    public async Task<OperationResult> RestockAsync(int returnId, RestockForm form, string userId, CancellationToken ct = default)
    {
        var employeeId = await _db.Employees.AsNoTracking().Where(e => e.UserId == userId && e.IsActive)
            .Select(e => (int?)e.EmployeeId).FirstOrDefaultAsync(ct);
        if (employeeId is null) return OperationResult.Fail("Tài khoản chưa gắn hồ sơ nhân viên đang làm việc nên chưa nhập kho được.");

        var line = await _db.ReturnDetails.AsNoTracking()
            .Where(d => d.ReturnDetailId == form.ReturnDetailId && d.ReturnId == returnId)
            .Select(d => new
            {
                d.ReturnDetailId, d.InvoiceDetailId, d.Quantity, d.RestockStatus, d.ConditionNote, d.InvoiceDetail.ConversionToBase,
                d.InvoiceDetail.MedicineId, d.InvoiceDetail.Medicine.MedicineName, BaseUnitName = d.InvoiceDetail.Medicine.BaseUnit.UnitName,
                ReturnStatus = d.Return.Status, d.Return.ReturnCode
            })
            .FirstOrDefaultAsync(ct);
        if (line is null) return OperationResult.Fail("Không tìm thấy dòng thuốc của phiếu.");
        if (line.ReturnStatus != (byte)ReturnStatus.RefundedPendingRestock || line.RestockStatus != (byte)RestockStatus.Pending)
            return OperationResult.Fail($"{line.MedicineName} đã được xử lý trước đó.");

        var note = form.Note?.Trim();
        if (note is { Length: > 300 }) return OperationResult.Fail("Ghi chú tối đa 300 ký tự.");
        var baseQty = BaseOf(line.Quantity, line.ConversionToBase);
        var today = Today;

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        string message;
        if (form.Decision == "reject")
        {
            if (string.IsNullOrEmpty(note)) return OperationResult.Fail("Nhập lý do không nhập lại.");
            var combined = Combine(line.ConditionNote, "Không nhập: " + note);
            var updated = await _db.ReturnDetails
                .Where(d => d.ReturnDetailId == line.ReturnDetailId && d.RestockStatus == (byte)RestockStatus.Pending)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.RestockStatus, (byte)RestockStatus.Rejected).SetProperty(d => d.ConditionNote, combined), ct);
            if (updated == 0) return OperationResult.Fail("Dòng này vừa được xử lý ở nơi khác. Tải lại trang.");
            await _audit.WriteAsync("RETURN_REJECT", "ReturnDetails", line.ReturnDetailId.ToString(CultureInfo.InvariantCulture),
                new { RestockStatus = (byte)RestockStatus.Pending }, new { RestockStatus = (byte)RestockStatus.Rejected, line.ReturnCode, Note = note }, userId, ct);
            message = $"{line.MedicineName}: đã ghi không nhập lại.";
        }
        else if (form.Decision == "restock")
        {
            if (form.BatchId is null) return OperationResult.Fail("Chọn lô để nhập lại.");
            var batchId = form.BatchId.Value;
            var options = await BatchOptionsAsync(new List<int> { line.InvoiceDetailId }, ct);
            var option = options.GetValueOrDefault(line.InvoiceDetailId)?.FirstOrDefault(b => b.BatchId == batchId);
            if (option is null) return OperationResult.Fail("Chỉ nhập lại vào lô mà hóa đơn đã xuất.");
            if (!option.Usable) return OperationResult.Fail($"Lô {option.BatchNumber}: {option.Problem}. Chọn lô khác hoặc Không nhập.");
            if (option.CapacityBase < baseQty)
                return OperationResult.Fail($"Lô {option.BatchNumber} chỉ nhận lại được {Format.Number(option.CapacityBase)} {line.BaseUnitName}. Chọn lô khác hoặc Không nhập.");

            var restockNote = Combine(line.ConditionNote, string.IsNullOrEmpty(note) ? null : "Nhập lại: " + note);
            int? restockBatch = batchId;
            var detailDone = await _db.ReturnDetails
                .Where(d => d.ReturnDetailId == line.ReturnDetailId && d.RestockStatus == (byte)RestockStatus.Pending)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.RestockStatus, (byte)RestockStatus.Restocked)
                    .SetProperty(d => d.BatchId, restockBatch)
                    .SetProperty(d => d.ConditionNote, restockNote), ct);
            if (detailDone == 0) return OperationResult.Fail("Dòng này vừa được xử lý ở nơi khác. Tải lại trang.");
            // Cộng tồn chỉ khi lô còn Khả dụng và còn hạn (cùng điều kiện bán FEFO)
            var added = await _db.MedicineBatches
                .Where(b => b.BatchId == batchId && b.Status == (byte)BatchStatus.Available && b.ExpiryDate > today)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.QuantityOnHand, b => b.QuantityOnHand + baseQty), ct);
            if (added == 0) return OperationResult.Fail($"Lô {option.BatchNumber} vừa bị khóa hoặc hết hạn. Chọn lô khác hoặc Không nhập.");

            var warehouseId = await _db.MedicineBatches.AsNoTracking().Where(b => b.BatchId == batchId).Select(b => b.WarehouseId).FirstAsync(ct);
            _db.InventoryTransactions.Add(new InventoryTransaction
            {
                MedicineId = line.MedicineId,
                BatchId = batchId,
                WarehouseId = warehouseId,
                TransactionType = (byte)InventoryTransactionType.CustomerReturnRestock,
                QuantityChange = baseQty,
                ReferenceType = InventoryReferenceTypes.For(InventoryTransactionType.CustomerReturnRestock),
                ReferenceId = line.ReturnDetailId,
                TransactionDate = DateTime.Now,
                CreatedByEmployeeId = employeeId,
                Notes = "Nhập lại hàng khách trả — " + line.ReturnCode
            });
            await _db.SaveChangesAsync(ct);
            await _audit.WriteAsync("RETURN_RESTOCK", "ReturnDetails", line.ReturnDetailId.ToString(CultureInfo.InvariantCulture),
                new { RestockStatus = (byte)RestockStatus.Pending },
                new { RestockStatus = (byte)RestockStatus.Restocked, line.ReturnCode, BatchId = batchId, QuantityBase = baseQty, Note = note }, userId, ct);
            message = $"{line.MedicineName}: đã nhập lại {Format.Number(baseQty)} {line.BaseUnitName} vào lô {option.BatchNumber}.";
        }
        else
        {
            return OperationResult.Fail("Chọn Nhập lại hoặc Không nhập.");
        }

        // Hết dòng chờ nhập → phiếu Hoàn tất
        var stillPending = await _db.ReturnDetails.AnyAsync(d => d.ReturnId == returnId && d.RestockStatus == (byte)RestockStatus.Pending, ct);
        if (!stillPending)
        {
            await _db.Returns
                .Where(x => x.ReturnId == returnId && x.Status == (byte)ReturnStatus.RefundedPendingRestock)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, (byte)ReturnStatus.Completed), ct);
            message += $" Phiếu {line.ReturnCode} đã hoàn tất.";
        }
        await tx.CommitAsync(ct);
        return OperationResult.Ok(message);
    }

    // ================================================================== Yêu cầu đổi trả: dược sĩ gửi → quản lý xử lý (lưu ở AuditLogs, nhóm "ok A" 04/11)

    private const string RequestAction = "RETURN_REQUEST";
    private const string RequestEntity = "ReturnRequests";
    private const string RequestDoneAction = "RETURN_REQUEST_DONE";
    private const string RequestRejectAction = "RETURN_REQUEST_REJECT";
    private const string RequestResultEntity = "ReturnRequestResults";

    public async Task<OperationResult> SendRequestAsync(int invoiceId, ReturnFormViewModel form, string userId, CancellationToken ct = default)
    {
        var info = await InvoiceAsync(invoiceId, ct);
        if (info is null) return OperationResult.Fail("Không tìm thấy hóa đơn.");
        var blocked = BlockedReason(info);
        if (blocked is not null) return OperationResult.Fail(blocked);
        var pending = (await RequestsTodayAsync(null, ct)).FirstOrDefault(r => r.InvoiceId == invoiceId && r.State == ReturnRequestState.Pending);
        if (pending is not null)
            return OperationResult.Fail($"Hóa đơn {info.Code} đã có yêu cầu đổi trả đang chờ quản lý xử lý (gửi lúc {pending.RequestedAt:HH:mm}).");

        var reason = AccountHelpers.NormalizeFullName(form.Reason);
        if (reason.Length == 0) return OperationResult.Fail("Nhập lý do khách trả hàng.", nameof(form.Reason));
        if (reason.Length > NoteMax) return OperationResult.Fail("Lý do tối đa 500 ký tự.", nameof(form.Reason));

        var lines = info.Lines.ToDictionary(l => l.InvoiceDetailId);
        var requested = new List<ReturnRequestLine>();
        foreach (var l in form.Lines)
        {
            if (l.Quantity is null || l.Quantity == 0) continue;
            if (!lines.TryGetValue(l.InvoiceDetailId, out var line)) return OperationResult.Fail("Dòng thuốc không thuộc hóa đơn này.");
            if (l.Quantity < 0 || l.Quantity > line.Remaining)
                return OperationResult.Fail(line.Remaining <= 0
                    ? $"{line.MedicineName} đã trả hết."
                    : $"{line.MedicineName}: trả tối đa {Format.Number(line.Remaining)} {line.UnitName}.");
            var note = l.ConditionNote?.Trim();
            if (note is { Length: > 300 }) return OperationResult.Fail($"{line.MedicineName}: tình trạng hàng tối đa 300 ký tự.");
            requested.Add(new ReturnRequestLine(line.InvoiceDetailId, l.Quantity.Value, string.IsNullOrEmpty(note) ? null : note));
        }
        if (requested.Count == 0) return OperationResult.Fail("Nhập số lượng khách muốn trả cho ít nhất một thuốc.");

        var name = await _db.Employees.AsNoTracking().Where(e => e.UserId == userId).Select(e => e.FullName).FirstOrDefaultAsync(ct)
                   ?? "Nhân viên";
        await _audit.WriteAsync(RequestAction, RequestEntity, invoiceId.ToString(CultureInfo.InvariantCulture), null,
            new ReturnRequestPayload(info.Code, reason, name, requested), userId, ct);
        return OperationResult.Ok($"Đã gửi yêu cầu đổi trả hóa đơn {info.Code}. Mời quản lý kiểm tra hàng và hoàn tiền.");
    }

    public async Task<OperationResult> RejectRequestAsync(long requestId, string? reason, string userId, CancellationToken ct = default)
    {
        var why = AccountHelpers.NormalizeFullName(reason);
        if (why.Length == 0) return OperationResult.Fail("Nhập lý do từ chối.");
        if (why.Length > 300) return OperationResult.Fail("Lý do tối đa 300 ký tự.");
        var request = (await RequestsTodayAsync(null, ct)).FirstOrDefault(r => r.RequestId == requestId);
        if (request is null) return OperationResult.Fail("Không tìm thấy yêu cầu (yêu cầu chỉ có hiệu lực trong ngày).");
        if (request.State != ReturnRequestState.Pending) return OperationResult.Fail("Yêu cầu này đã được xử lý.");
        var name = await _db.Employees.AsNoTracking().Where(e => e.UserId == userId).Select(e => e.FullName).FirstOrDefaultAsync(ct);
        await _audit.WriteAsync(RequestRejectAction, RequestResultEntity, requestId.ToString(CultureInfo.InvariantCulture), null,
            new { Reason = why, RejectedBy = name, request.InvoiceCode }, userId, ct);
        return OperationResult.Ok($"Đã từ chối yêu cầu đổi trả hóa đơn {request.InvoiceCode}.");
    }

    public Task<IReadOnlyList<ReturnRequestInfo>> GetRequestsTodayAsync(string? onlyUserId, CancellationToken ct = default)
        => RequestsTodayAsync(onlyUserId, ct);

    public async Task<int> PendingRequestCountAsync(CancellationToken ct = default)
        => (await RequestsTodayAsync(null, ct)).Count(r => r.State == ReturnRequestState.Pending);

    private sealed record RejectPayload(string? Reason, string? RejectedBy);
    private sealed record DonePayload(int ReturnId, string? ReturnCode);

    /// <summary>
    /// Yêu cầu gửi hôm nay (mới nhất trước) và kết quả: Đã lập phiếu (RETURN_REQUEST_DONE, hoặc có phiếu đổi trả của hóa đơn lập sau lúc gửi),
    /// Từ chối (RETURN_REQUEST_REJECT), còn lại Đang chờ. Hết ngày thì yêu cầu không còn hiện.
    /// </summary>
    private async Task<IReadOnlyList<ReturnRequestInfo>> RequestsTodayAsync(string? onlyUserId, CancellationToken ct)
    {
        var from = DateTime.Today;
        var to = from.AddDays(1);
        var query = _db.AuditLogs.AsNoTracking()
            .Where(a => a.EntityName == RequestEntity && a.ActionType == RequestAction && a.CreatedAt >= from && a.CreatedAt < to);
        if (onlyUserId is not null) query = query.Where(a => a.UserId == onlyUserId);
        var logs = await query.OrderByDescending(a => a.AuditLogId)
            .Select(a => new { a.AuditLogId, a.EntityId, a.UserId, a.NewValues, a.CreatedAt })
            .Take(200)
            .ToListAsync(ct);
        if (logs.Count == 0) return Array.Empty<ReturnRequestInfo>();

        var keys = logs.Select(l => l.AuditLogId.ToString(CultureInfo.InvariantCulture)).ToList();
        var results = (await _db.AuditLogs.AsNoTracking()
                .Where(a => a.EntityName == RequestResultEntity && keys.Contains(a.EntityId!))
                .OrderBy(a => a.AuditLogId)
                .Select(a => new { a.EntityId, a.ActionType, a.NewValues })
                .ToListAsync(ct))
            .GroupBy(a => a.EntityId!)
            .ToDictionary(g => g.Key, g => g.First());

        var invoiceIds = logs.Select(l => int.TryParse(l.EntityId, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : 0)
            .Where(id => id > 0).Distinct().ToList();
        var invoices = await _db.SalesInvoices.AsNoTracking().Where(i => invoiceIds.Contains(i.InvoiceId))
            .Select(i => new { i.InvoiceId, CustomerName = i.Customer != null ? i.Customer.FullName : null })
            .ToDictionaryAsync(i => i.InvoiceId, i => i.CustomerName, ct);
        var returns = await _db.Returns.AsNoTracking()
            .Where(r => invoiceIds.Contains(r.InvoiceId) && r.ReturnDate >= from)
            .Select(r => new { r.InvoiceId, r.ReturnId, r.ReturnCode, r.ReturnDate })
            .ToListAsync(ct);

        var list = new List<ReturnRequestInfo>();
        foreach (var l in logs)
        {
            if (!int.TryParse(l.EntityId, NumberStyles.None, CultureInfo.InvariantCulture, out var invoiceId)) continue;
            ReturnRequestPayload? payload;
            try { payload = l.NewValues is null ? null : JsonSerializer.Deserialize<ReturnRequestPayload>(l.NewValues); }
            catch (JsonException) { payload = null; }
            if (payload is null) continue;

            var state = ReturnRequestState.Pending;
            string? resultText = null;
            int? returnId = null;
            if (results.TryGetValue(l.AuditLogId.ToString(CultureInfo.InvariantCulture), out var result))
            {
                if (result.ActionType == RequestRejectAction)
                {
                    state = ReturnRequestState.Rejected;
                    RejectPayload? rp = null;
                    try { rp = result.NewValues is null ? null : JsonSerializer.Deserialize<RejectPayload>(result.NewValues); } catch (JsonException) { }
                    resultText = "Từ chối" + (string.IsNullOrEmpty(rp?.RejectedBy) ? "" : " (" + rp!.RejectedBy + ")") + (string.IsNullOrEmpty(rp?.Reason) ? "" : ": " + rp!.Reason);
                }
                else
                {
                    state = ReturnRequestState.Done;
                    DonePayload? dp = null;
                    try { dp = result.NewValues is null ? null : JsonSerializer.Deserialize<DonePayload>(result.NewValues); } catch (JsonException) { }
                    returnId = dp?.ReturnId;
                    resultText = "Đã lập phiếu " + dp?.ReturnCode;
                }
            }
            else
            {
                // Quản lý tự lập phiếu cho hóa đơn này sau lúc gửi yêu cầu → coi như đã xử lý
                var made = returns.Where(r => r.InvoiceId == invoiceId && r.ReturnDate >= l.CreatedAt).OrderBy(r => r.ReturnDate).FirstOrDefault();
                if (made is not null)
                {
                    state = ReturnRequestState.Done;
                    returnId = made.ReturnId;
                    resultText = "Đã lập phiếu " + made.ReturnCode;
                }
            }
            list.Add(new ReturnRequestInfo(l.AuditLogId, invoiceId, payload.InvoiceCode, invoices.GetValueOrDefault(invoiceId), payload.RequestedBy,
                l.UserId, l.CreatedAt, payload.Reason, payload.Lines ?? new List<ReturnRequestLine>(), state, resultText, returnId));
        }
        return list;
    }

    // ================================================================== Trang hóa đơn

    public async Task<IReadOnlyList<InvoiceReturnInfo>> GetInvoiceReturnsAsync(int invoiceId, CancellationToken ct = default)
        => await _db.Returns.AsNoTracking()
            .Where(r => r.InvoiceId == invoiceId)
            .OrderBy(r => r.ReturnDate).ThenBy(r => r.ReturnId)
            .Select(r => new InvoiceReturnInfo(r.ReturnId, r.ReturnCode, r.ReturnDate, r.RefundAmount, r.Status))
            .ToListAsync(ct);

    public async Task<bool> CanReturnTodayAsync(int invoiceId, CancellationToken ct = default)
    {
        var info = await InvoiceAsync(invoiceId, ct);
        return info is not null && BlockedReason(info) is null;
    }

    // ================================================================== Dùng chung

    private sealed record InvoiceInfo(
        int InvoiceId, string Code, byte Status, DateTime SoldAt, string? CustomerName, string? CustomerPhone, string? PrescriptionCode,
        decimal SubTotal, decimal DiscountAmount, decimal TotalAmount, string SellerName, string ShiftCode,
        IReadOnlyList<PaymentInfo> Payments, IReadOnlyList<byte> PaidMethods, IReadOnlyList<ReturnInvoiceLine> Lines);

    /// <summary>Hóa đơn gốc + số đã trả của từng dòng (phiếu chưa hủy). Ngày bán = lúc thanh toán (không có thì ngày lập).</summary>
    private async Task<InvoiceInfo?> InvoiceAsync(int invoiceId, CancellationToken ct)
    {
        var i = await _db.SalesInvoices.AsNoTracking().Where(x => x.InvoiceId == invoiceId)
            .Select(x => new
            {
                x.InvoiceId, x.InvoiceCode, x.Status, x.InvoiceDate, x.SubTotal, x.DiscountAmount, x.TotalAmount,
                SellerName = x.Employee.FullName, x.Shift.ShiftCode,
                CustomerName = x.Customer != null ? x.Customer.FullName : null,
                CustomerPhone = x.Customer != null ? x.Customer.Phone : null,
                PrescriptionCode = x.Prescription != null ? x.Prescription.PrescriptionCode : null
            })
            .FirstOrDefaultAsync(ct);
        if (i is null) return null;

        var payments = await _db.Payments.AsNoTracking()
            .Where(p => p.InvoiceId == invoiceId && p.Status == (byte)PaymentStatus.Succeeded)
            .OrderBy(p => p.PaidAt).ThenBy(p => p.PaymentId)
            .Select(p => new PaymentInfo(p.PaymentMethod, p.Amount, p.TenderedAmount, p.TransactionReference, p.PaidAt, p.Status))
            .ToListAsync(ct);
        var soldAt = payments.Count == 0 ? i.InvoiceDate : payments.Max(p => p.PaidAt);
        var methods = payments.GroupBy(p => p.Method).OrderByDescending(g => g.Sum(p => p.Amount)).Select(g => g.Key).ToList();

        var details = await _db.SalesInvoiceDetails.AsNoTracking()
            .Where(d => d.InvoiceId == invoiceId)
            .OrderBy(d => d.InvoiceDetailId)
            .Select(d => new { d.InvoiceDetailId, d.Medicine.MedicineCode, d.Medicine.MedicineName, d.Unit.UnitName, d.Quantity, d.UnitPrice, d.DiscountAmount, d.LineTotal })
            .ToListAsync(ct);
        var returned = (await _db.ReturnDetails.AsNoTracking()
                .Where(r => r.InvoiceDetail.InvoiceId == invoiceId && r.Return.Status != (byte)ReturnStatus.Cancelled)
                .Select(r => new { r.InvoiceDetailId, r.Quantity, r.UnitRefundPrice })
                .ToListAsync(ct))
            .GroupBy(r => r.InvoiceDetailId)
            .ToDictionary(g => g.Key, g => (
                Quantity: g.Sum(r => r.Quantity),
                Amount: g.Sum(r => Math.Round(r.Quantity * r.UnitRefundPrice, 0, MidpointRounding.AwayFromZero))));

        var lines = details.Select(d =>
        {
            var done = returned.GetValueOrDefault(d.InvoiceDetailId);
            // Tiền hoàn mỗi đơn vị = thành tiền dòng (đã trừ khuyến mãi, giảm thêm) ÷ số lượng, làm tròn xuống đồng
            var unit = d.Quantity <= 0 ? 0 : Math.Floor(d.LineTotal / d.Quantity);
            return new ReturnInvoiceLine(d.InvoiceDetailId, d.MedicineCode, d.MedicineName, d.UnitName, d.Quantity, done.Quantity,
                d.UnitPrice, d.DiscountAmount, d.LineTotal, unit, Math.Min(done.Amount, d.LineTotal));
        }).ToList();

        return new InvoiceInfo(i.InvoiceId, i.InvoiceCode, i.Status, soldAt, i.CustomerName, i.CustomerPhone, i.PrescriptionCode,
            i.SubTotal, i.DiscountAmount, i.TotalAmount, i.SellerName, i.ShiftCode, payments, methods, lines);
    }

    /// <summary>null = trả được. Quy định nhóm chốt 04/11: chỉ đổi trả trong ngày mua, đối chiếu số đã bán.</summary>
    private static string? BlockedReason(InvoiceInfo info)
    {
        if (info.Status != (byte)InvoiceStatus.Paid)
            return $"Hóa đơn {info.Code} {InvoiceStatusText.Of(info.Status).ToLowerInvariant()} — chỉ trả được hóa đơn đã thanh toán.";
        if (DateOnly.FromDateTime(info.SoldAt) != Today)
            return $"Chỉ đổi trả trong ngày mua. Hóa đơn {info.Code} mua ngày {info.SoldAt.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}.";
        if (info.Lines.All(l => l.Remaining <= 0))
            return $"Hóa đơn {info.Code} đã trả hết thuốc.";
        return null;
    }

    /// <summary>
    /// Lô dòng hóa đơn đã xuất (SalesIssueAllocations) và số còn nhận lại được = đã xuất − đã nhập lại từ chính dòng đó.
    /// Chỉ nhập lại vào lô Khả dụng, còn hạn.
    /// </summary>
    private async Task<Dictionary<int, List<RestockBatchOption>>> BatchOptionsAsync(IReadOnlyCollection<int> invoiceDetailIds, CancellationToken ct)
    {
        if (invoiceDetailIds.Count == 0) return new Dictionary<int, List<RestockBatchOption>>();
        var ids = invoiceDetailIds.ToList();
        var allocated = (await _db.SalesIssueAllocations.AsNoTracking()
                .Where(a => ids.Contains(a.InvoiceDetailId))
                .Select(a => new { a.InvoiceDetailId, a.BatchId, a.Batch.BatchNumber, a.Batch.ExpiryDate, a.Batch.Status, a.QuantityBase })
                .ToListAsync(ct))
            .GroupBy(a => new { a.InvoiceDetailId, a.BatchId, a.BatchNumber, a.ExpiryDate, a.Status })
            .Select(g => new { g.Key.InvoiceDetailId, g.Key.BatchId, g.Key.BatchNumber, g.Key.ExpiryDate, g.Key.Status, Quantity = g.Sum(a => a.QuantityBase) })
            .ToList();
        var restocked = (await _db.ReturnDetails.AsNoTracking()
                .Where(d => ids.Contains(d.InvoiceDetailId) && d.RestockStatus == (byte)RestockStatus.Restocked && d.BatchId != null)
                .Select(d => new { d.InvoiceDetailId, d.BatchId, d.Quantity, d.InvoiceDetail.ConversionToBase })
                .ToListAsync(ct))
            .Select(d => new { d.InvoiceDetailId, d.BatchId, Quantity = BaseOf(d.Quantity, d.ConversionToBase) })
            .ToList();
        var today = Today;
        return allocated
            .GroupBy(a => a.InvoiceDetailId)
            .ToDictionary(g => g.Key, g => g.OrderBy(a => a.ExpiryDate).ThenBy(a => a.BatchId).Select(a =>
            {
                var back = restocked.Where(r => r.InvoiceDetailId == a.InvoiceDetailId && r.BatchId == a.BatchId).Sum(r => r.Quantity);
                var capacity = Math.Max(0, Math.Round(a.Quantity - back, 3, MidpointRounding.AwayFromZero));
                string? problem = a.Status != (byte)BatchStatus.Available
                    ? "lô đang bị khóa hoặc ngừng sử dụng"
                    : a.ExpiryDate <= today ? "lô đã hết hạn" : null;
                return new RestockBatchOption(a.BatchId, a.BatchNumber, a.ExpiryDate, capacity, problem is null && capacity > 0, problem);
            }).ToList());
    }

    private static decimal BaseOf(decimal quantity, decimal factor) => Math.Round(quantity * factor, 3, MidpointRounding.AwayFromZero);

    private static string? Combine(string? existing, string? add)
    {
        if (string.IsNullOrEmpty(add)) return existing;
        var text = string.IsNullOrEmpty(existing) ? add : existing + "\n" + add;
        return text.Length > NoteMax ? text[..NoteMax] : text;
    }

    /// <summary>TH + yyMMdd + 3 số nối tiếp trong ngày (giống dữ liệu mẫu TH260505001).</summary>
    private async Task<string> NextCodeAsync(DateTime at, CancellationToken ct)
    {
        var prefix = CodePrefix + at.ToString("yyMMdd", CultureInfo.InvariantCulture);
        var codes = await _db.Returns.AsNoTracking().Where(r => r.ReturnCode.StartsWith(prefix)).Select(r => r.ReturnCode).ToListAsync(ct);
        var max = 0;
        foreach (var code in codes)
            if (int.TryParse(code.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n > max) max = n;
        return prefix + (max + 1).ToString("D3", CultureInfo.InvariantCulture);
    }
}
