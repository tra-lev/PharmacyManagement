using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PharmacyManagement.Data;
using PharmacyManagement.Helpers;
using PharmacyManagement.Models.Entities;
using PharmacyManagement.Models.Enums;
using PharmacyManagement.Models.ViewModels.Inventory;
using PharmacyManagement.Services.Interfaces;

namespace PharmacyManagement.Services.Implementations;

/// <summary>Thanh toán NCC 2.2.5 + thu tiền hoàn 2.3.5 (DEC-44).</summary>
public sealed class SupplierPaymentService : ISupplierPaymentService
{
    private const string PayPrefix = "PC";
    private const string RefundPrefix = "PT";
    private const decimal MaxAmount = 1_000_000_000m;

    private static readonly Regex PrintableAscii = new(@"^[\x20-\x7E]*$", RegexOptions.CultureInvariant);
    private static readonly StringComparer NameOrder = StringComparer.Create(CultureInfo.GetCultureInfo("vi-VN"), ignoreCase: true);

    private const byte Pay = (byte)SupplierPaymentType.PaymentToSupplier;
    private const byte Refund = (byte)SupplierPaymentType.RefundFromSupplier;
    private const byte Valid = (byte)PaymentStatus.Succeeded;

    private readonly PharmacyDbContext _db;
    private readonly IAuditLogService _audit;

    public SupplierPaymentService(PharmacyDbContext db, IAuditLogService audit)
    {
        _db = db;
        _audit = audit;
    }

    // ================================================================== Công nợ theo NCC

    public async Task<SupplierDebtListViewModel> GetDebtsAsync(SupplierDebtFilter filter, CancellationToken ct = default)
    {
        var receipts = await PostedReceiptBalancesAsync(null, ct);
        var refunds = await OpenRefundsAsync(null, ct);
        var suppliers = await _db.Suppliers.AsNoTracking()
            .Select(s => new { s.SupplierId, s.SupplierCode, s.SupplierName })
            .ToListAsync(ct);

        filter.Show = filter.Show == "all" ? "all" : null;
        var q = AccountHelpers.NormalizeFullName(filter.Q);
        filter.Q = q.Length == 0 ? null : q;

        var rows = suppliers
            .Select(s =>
            {
                var mine = receipts.Where(r => r.SupplierId == s.SupplierId).ToList();
                var refundPending = refunds.Where(r => r.SupplierId == s.SupplierId).Sum(r => r.Remaining);
                return new SupplierDebtRow(s.SupplierId, s.SupplierCode, s.SupplierName,
                    mine.Count(r => r.Remaining > 0), mine.Sum(r => r.Total), mine.Sum(r => r.Paid), mine.Sum(r => r.Remaining), refundPending);
            })
            .Where(r => r.Purchased > 0 || r.RefundPending > 0)
            .ToList();
        var grandTotal = rows.Count;
        IEnumerable<SupplierDebtRow> shown = rows;
        if (filter.Show is null) shown = shown.Where(r => r.Remaining > 0 || r.RefundPending > 0);
        if (filter.Q is not null)
        {
            var folded = Fold(filter.Q);
            shown = shown.Where(r => Fold(r.Name).Contains(folded, StringComparison.Ordinal) || r.Code.Contains(filter.Q, StringComparison.OrdinalIgnoreCase));
        }
        var ordered = shown.OrderByDescending(r => r.Remaining).ThenBy(r => r.Name, NameOrder).ToList();

        var total = ordered.Count;
        var pageCount = Math.Max(1, (int)Math.Ceiling(total / (double)SupplierDebtFilter.PageSize));
        filter.Page = Math.Clamp(filter.Page, 1, pageCount);
        return new SupplierDebtListViewModel
        {
            Filter = filter,
            Rows = ordered.Skip((filter.Page - 1) * SupplierDebtFilter.PageSize).Take(SupplierDebtFilter.PageSize).ToList(),
            Total = total,
            GrandTotal = grandTotal,
            PageCount = pageCount,
            TotalRemaining = rows.Sum(r => r.Remaining),
            TotalRefundPending = rows.Sum(r => r.RefundPending)
        };
    }

    public async Task<SupplierDebtViewModel?> GetSupplierAsync(int supplierId, CancellationToken ct = default)
    {
        var s = await _db.Suppliers.AsNoTracking().Where(x => x.SupplierId == supplierId)
            .Select(x => new { x.SupplierId, x.SupplierCode, x.SupplierName, x.Phone, x.ContactPerson })
            .FirstOrDefaultAsync(ct);
        if (s is null) return null;

        var receipts = await PostedReceiptBalancesAsync(supplierId, ct);
        var refunds = await OpenRefundsAsync(supplierId, ct);
        var payments = await PaymentLines(_db.SupplierPayments.AsNoTracking().Where(p => p.SupplierId == supplierId))
            .Take(50).ToListAsync(ct);

        return new SupplierDebtViewModel
        {
            SupplierId = s.SupplierId,
            Code = s.SupplierCode,
            Name = s.SupplierName,
            Phone = s.Phone,
            ContactPerson = s.ContactPerson,
            OpenReceipts = receipts.Where(r => r.Remaining > 0).OrderBy(r => r.ReceiptDate)
                .Select(r => new ReceiptDebtRow(r.GoodsReceiptId, r.Code, r.ReceiptDate, r.InvoiceNumber, r.Total, r.Paid, r.Remaining)).ToList(),
            OpenReturns = refunds.OrderBy(r => r.ReturnDate)
                .Select(r => new ReturnRefundRow(r.SupplierReturnId, r.Code, r.ReturnDate, r.Total, r.Refunded, r.Remaining)).ToList(),
            Payments = payments,
            Purchased = receipts.Sum(r => r.Total),
            Paid = receipts.Sum(r => r.Paid),
            Remaining = receipts.Sum(r => r.Remaining),
            RefundPending = refunds.Sum(r => r.Remaining)
        };
    }

    // ================================================================== Phiếu chi / thu

    public async Task<SupplierPaymentListViewModel> GetPaymentsAsync(SupplierPaymentFilter filter, CancellationToken ct = default)
    {
        var query = _db.SupplierPayments.AsNoTracking().AsQueryable();
        byte? type = filter.Type switch { "pay" => Pay, "refund" => Refund, _ => null };
        if (type is null) filter.Type = null;
        else query = query.Where(p => p.PaymentType == type);
        byte? status = filter.Status switch { "ok" => Valid, "void" => (byte)PaymentStatus.Voided, _ => null };
        if (status is null) filter.Status = null;
        else query = query.Where(p => p.Status == status);

        var q = AccountHelpers.NormalizeFullName(filter.Q);
        filter.Q = q.Length == 0 ? null : q;
        if (filter.Q is not null)
        {
            var text = filter.Q;
            var folded = SearchText.Fold(text);
            query = query.Where(p => p.SupplierPaymentCode.Contains(text)
                                     || (p.TransactionReference != null && p.TransactionReference.Contains(text))
                                     || (p.GoodsReceipt != null && p.GoodsReceipt.GoodsReceiptCode.Contains(text))
                                     || (p.SupplierReturn != null && p.SupplierReturn.SupplierReturnCode.Contains(text))
                                     || EF.Functions.Collate(p.Supplier.SupplierName.Replace("đ", "d").Replace("Đ", "D"), SearchText.Collation).Contains(folded));
        }

        var total = await query.CountAsync(ct);
        var pageCount = Math.Max(1, (int)Math.Ceiling(total / (double)SupplierPaymentFilter.PageSize));
        filter.Page = Math.Clamp(filter.Page, 1, pageCount);
        var rows = await query
            .OrderByDescending(p => p.PaidAt).ThenByDescending(p => p.SupplierPaymentId)
            .Skip((filter.Page - 1) * SupplierPaymentFilter.PageSize).Take(SupplierPaymentFilter.PageSize)
            .Select(p => new SupplierPaymentRow(p.SupplierPaymentId, p.SupplierPaymentCode, p.PaidAt, p.PaymentType, p.Supplier.SupplierName,
                p.GoodsReceipt != null ? p.GoodsReceipt.GoodsReceiptCode : p.SupplierReturn != null ? p.SupplierReturn.SupplierReturnCode : null,
                p.PaymentMethod, p.Amount, p.Status))
            .ToListAsync(ct);

        return new SupplierPaymentListViewModel
        {
            Filter = filter,
            Rows = rows,
            Total = total,
            GrandTotal = await _db.SupplierPayments.CountAsync(ct),
            PageCount = pageCount
        };
    }

    public async Task<SupplierPaymentDetailsViewModel?> GetPaymentAsync(long paymentId, CancellationToken ct = default)
        => await _db.SupplierPayments.AsNoTracking()
            .Where(p => p.SupplierPaymentId == paymentId)
            .Select(p => new SupplierPaymentDetailsViewModel
            {
                SupplierPaymentId = p.SupplierPaymentId,
                Code = p.SupplierPaymentCode,
                Type = p.PaymentType,
                Method = p.PaymentMethod,
                Amount = p.Amount,
                Reference = p.TransactionReference,
                PaidAt = p.PaidAt,
                Status = p.Status,
                Notes = p.Notes,
                EmployeeName = p.Employee.FullName,
                SupplierId = p.SupplierId,
                SupplierName = p.Supplier.SupplierName,
                GoodsReceiptId = p.GoodsReceiptId,
                GoodsReceiptCode = p.GoodsReceipt != null ? p.GoodsReceipt.GoodsReceiptCode : null,
                SupplierReturnId = p.SupplierReturnId,
                SupplierReturnCode = p.SupplierReturn != null ? p.SupplierReturn.SupplierReturnCode : null
            })
            .FirstOrDefaultAsync(ct);

    // ================================================================== 2.2.5 Thanh toán phiếu nhập

    public async Task<PayReceiptViewModel?> PayFormAsync(int receiptId, CancellationToken ct = default)
    {
        var r = await _db.GoodsReceipts.AsNoTracking()
            .Where(x => x.GoodsReceiptId == receiptId && x.Status == (byte)GoodsReceiptStatus.Posted)
            .Select(x => new { x.GoodsReceiptId, x.GoodsReceiptCode, x.ReceiptDate, x.SupplierInvoiceNumber, x.SupplierId, x.Supplier.SupplierName, x.TotalAmount })
            .FirstOrDefaultAsync(ct);
        if (r is null) return null;
        var (paid, remaining) = await ReceiptBalanceAsync(receiptId, ct);
        return new PayReceiptViewModel
        {
            GoodsReceiptId = r.GoodsReceiptId,
            ReceiptCode = r.GoodsReceiptCode,
            ReceiptDate = r.ReceiptDate,
            InvoiceNumber = r.SupplierInvoiceNumber,
            SupplierId = r.SupplierId,
            SupplierName = r.SupplierName,
            Total = r.TotalAmount,
            Paid = paid,
            Remaining = remaining,
            Form = new SupplierMoneyForm { Amount = Math.Ceiling(remaining), Notes = "Thanh toán phiếu nhập " + r.GoodsReceiptCode }
        };
    }

    public async Task<OperationResult<long>> PayAsync(int receiptId, SupplierMoneyForm form, string actorUserId, CancellationToken ct = default)
    {
        var employeeId = await EmployeeIdOfAsync(actorUserId, ct);
        if (employeeId is null) return OperationResult<long>.Fail("Tài khoản chưa gắn hồ sơ nhân viên nên chưa lập phiếu chi được.");
        var clean = Validate(form);
        if (clean.Error is not null) return OperationResult<long>.Fail(clean.Error, clean.Field);

        var receipt = await _db.GoodsReceipts.AsNoTracking().Where(r => r.GoodsReceiptId == receiptId)
            .Select(r => new { r.GoodsReceiptCode, r.SupplierId, r.Status }).FirstOrDefaultAsync(ct);
        if (receipt is null) return OperationResult<long>.Fail("Không tìm thấy phiếu nhập.");
        if (receipt.Status != (byte)GoodsReceiptStatus.Posted) return OperationResult<long>.Fail($"Phiếu {receipt.GoodsReceiptCode} chưa nhập kho nên chưa thanh toán được.");

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        // Khóa dòng phiếu nhập tới khi commit: hai người cùng thanh toán không vượt số còn nợ
        await _db.GoodsReceipts.Where(r => r.GoodsReceiptId == receiptId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.TotalAmount, r => r.TotalAmount), ct);
        var (_, remaining) = await ReceiptBalanceAsync(receiptId, ct);
        if (remaining <= 0) return OperationResult<long>.Fail($"Phiếu {receipt.GoodsReceiptCode} đã thanh toán đủ.");
        if (clean.Amount > Math.Ceiling(remaining))
            return OperationResult<long>.Fail($"Số tiền vượt số còn nợ của phiếu ({Format.Money(remaining)}).", nameof(SupplierMoneyForm.Amount));

        var now = DateTime.Now;
        var payment = new SupplierPayment
        {
            SupplierPaymentCode = await NextCodeAsync(PayPrefix, now, ct),
            SupplierId = receipt.SupplierId,
            GoodsReceiptId = receiptId,
            EmployeeId = employeeId.Value,
            PaymentType = Pay,
            PaymentMethod = clean.Method,
            Amount = clean.Amount,
            TransactionReference = clean.Reference,
            PaidAt = now,
            Status = Valid,
            Notes = clean.Notes ?? "Thanh toán phiếu nhập " + receipt.GoodsReceiptCode
        };
        _db.SupplierPayments.Add(payment);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            return OperationResult<long>.Fail("Không lưu được: mã phiếu chi vừa bị trùng do người khác lập cùng lúc. Bấm lưu lại.");
        }
        await _audit.WriteAsync("SUPPLIER_PAY", "SupplierPayments", payment.SupplierPaymentId.ToString(CultureInfo.InvariantCulture), null,
            new { payment.SupplierPaymentCode, receipt.GoodsReceiptCode, payment.Amount, payment.PaymentMethod, payment.TransactionReference }, actorUserId, ct);
        await tx.CommitAsync(ct);
        return OperationResult<long>.Ok(payment.SupplierPaymentId,
            $"Đã lập phiếu chi {payment.SupplierPaymentCode}: {Format.Money(payment.Amount)} cho phiếu nhập {receipt.GoodsReceiptCode}.");
    }

    // ================================================================== 2.3.5 Thu tiền hoàn

    public async Task<OperationResult<long>> RefundAsync(int supplierReturnId, SupplierMoneyForm form, string actorUserId, CancellationToken ct = default)
    {
        var employeeId = await EmployeeIdOfAsync(actorUserId, ct);
        if (employeeId is null) return OperationResult<long>.Fail("Tài khoản chưa gắn hồ sơ nhân viên nên chưa lập phiếu thu được.");
        var clean = Validate(form);
        if (clean.Error is not null) return OperationResult<long>.Fail(clean.Error, clean.Field);

        var ret = await _db.SupplierReturns.AsNoTracking().Where(r => r.SupplierReturnId == supplierReturnId)
            .Select(r => new { r.SupplierReturnCode, r.SupplierId, r.Status }).FirstOrDefaultAsync(ct);
        if (ret is null) return OperationResult<long>.Fail("Không tìm thấy phiếu trả nhà cung cấp.");
        if (ret.Status != (byte)SupplierReturnStatus.Issued)
            return OperationResult<long>.Fail($"Phiếu {ret.SupplierReturnCode} {SupplierReturnStatusText.Of(ret.Status).ToLowerInvariant()} nên không thu tiền hoàn được.");

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var locked = await _db.SupplierReturns.Where(r => r.SupplierReturnId == supplierReturnId && r.Status == (byte)SupplierReturnStatus.Issued)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, r => r.Status), ct);
        if (locked == 0) return OperationResult<long>.Fail("Phiếu trả vừa thay đổi ở máy khác. Tải lại trang.");
        var total = await _db.SupplierReturns.Where(r => r.SupplierReturnId == supplierReturnId).Select(r => r.TotalAmount).FirstAsync(ct);
        var refunded = await RefundedAsync(supplierReturnId, ct);
        var remaining = Math.Max(0, total - refunded);
        if (clean.Amount > Math.Ceiling(remaining))
            return OperationResult<long>.Fail($"Số tiền vượt số còn phải hoàn của phiếu ({Format.Money(remaining)}).", nameof(SupplierMoneyForm.Amount));

        var now = DateTime.Now;
        var payment = new SupplierPayment
        {
            SupplierPaymentCode = await NextCodeAsync(RefundPrefix, now, ct),
            SupplierId = ret.SupplierId,
            SupplierReturnId = supplierReturnId,
            EmployeeId = employeeId.Value,
            PaymentType = Refund,
            PaymentMethod = clean.Method,
            Amount = clean.Amount,
            TransactionReference = clean.Reference,
            PaidAt = now,
            Status = Valid,
            Notes = clean.Notes ?? "Thu tiền hoàn phiếu trả " + ret.SupplierReturnCode
        };
        _db.SupplierPayments.Add(payment);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            return OperationResult<long>.Fail("Không lưu được: mã phiếu thu vừa bị trùng do người khác lập cùng lúc. Bấm lưu lại.");
        }

        var settled = refunded + clean.Amount >= total;
        if (settled)
        {
            await _db.SupplierReturns.Where(r => r.SupplierReturnId == supplierReturnId && r.Status == (byte)SupplierReturnStatus.Issued)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, (byte)SupplierReturnStatus.Settled), ct);
        }
        await _audit.WriteAsync("SUPPLIER_REFUND", "SupplierPayments", payment.SupplierPaymentId.ToString(CultureInfo.InvariantCulture), null,
            new { payment.SupplierPaymentCode, ret.SupplierReturnCode, payment.Amount, payment.PaymentMethod, payment.TransactionReference, Settled = settled },
            actorUserId, ct);
        await tx.CommitAsync(ct);
        return OperationResult<long>.Ok(payment.SupplierPaymentId,
            $"Đã lập phiếu thu {payment.SupplierPaymentCode}: {Format.Money(payment.Amount)}." + (settled ? $" Phiếu trả {ret.SupplierReturnCode} đã quyết toán." : ""));
    }

    // ================================================================== Hủy phiếu chi / thu

    public async Task<OperationResult> VoidAsync(long paymentId, string? reason, string actorUserId, CancellationToken ct = default)
    {
        var why = AccountHelpers.NormalizeFullName(reason);
        if (why.Length == 0) return OperationResult.Fail("Nhập lý do hủy phiếu.");
        if (why.Length > 300) return OperationResult.Fail("Lý do hủy phiếu tối đa 300 ký tự.");

        var p = await _db.SupplierPayments.AsNoTracking().Where(x => x.SupplierPaymentId == paymentId)
            .Select(x => new { x.SupplierPaymentCode, x.Status, x.Notes, x.PaymentType, x.SupplierReturnId, x.Amount }).FirstOrDefaultAsync(ct);
        if (p is null) return OperationResult.Fail("Không tìm thấy phiếu.");
        if (p.Status != Valid) return OperationResult.Fail($"Phiếu {p.SupplierPaymentCode} đã hủy trước đó.");

        var note = "Hủy phiếu: " + why;
        var notes = string.IsNullOrEmpty(p.Notes) ? note : p.Notes + "\n" + note;
        if (notes.Length > 500) notes = notes[..500];

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var updated = await _db.SupplierPayments.Where(x => x.SupplierPaymentId == paymentId && x.Status == Valid)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, (byte)PaymentStatus.Voided).SetProperty(x => x.Notes, notes), ct);
        if (updated == 0) return OperationResult.Fail("Phiếu vừa được hủy ở máy khác. Tải lại trang.");

        // Hủy phiếu thu làm phiếu trả chưa nhận đủ tiền hoàn → quay lại "Đã xuất trả"
        if (p.PaymentType == Refund && p.SupplierReturnId is not null)
        {
            var returnId = p.SupplierReturnId.Value;
            var total = await _db.SupplierReturns.Where(r => r.SupplierReturnId == returnId).Select(r => r.TotalAmount).FirstAsync(ct);
            if (await RefundedAsync(returnId, ct) < total)
            {
                await _db.SupplierReturns.Where(r => r.SupplierReturnId == returnId && r.Status == (byte)SupplierReturnStatus.Settled)
                    .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, (byte)SupplierReturnStatus.Issued), ct);
            }
        }
        await _audit.WriteAsync("SUPPLIER_PAYMENT_VOID", "SupplierPayments", paymentId.ToString(CultureInfo.InvariantCulture),
            new { Status = Valid, p.Amount }, new { Status = (byte)PaymentStatus.Voided, Reason = why }, actorUserId, ct);
        await tx.CommitAsync(ct);
        return OperationResult.Ok($"Đã hủy phiếu {p.SupplierPaymentCode}.");
    }

    public async Task<(decimal Paid, decimal Remaining)> ReceiptBalanceAsync(int receiptId, CancellationToken ct = default)
    {
        var total = await _db.GoodsReceipts.AsNoTracking().Where(r => r.GoodsReceiptId == receiptId).Select(r => r.TotalAmount).FirstOrDefaultAsync(ct);
        var paid = await _db.SupplierPayments.AsNoTracking()
            .Where(p => p.GoodsReceiptId == receiptId && p.PaymentType == Pay && p.Status == Valid)
            .SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;
        return (paid, Math.Max(0, total - paid));
    }

    // ================================================================== Dùng chung

    private sealed record ReceiptBalance(int GoodsReceiptId, int SupplierId, string Code, DateTime ReceiptDate, string? InvoiceNumber, decimal Total, decimal Paid)
    {
        public decimal Remaining => Math.Max(0, Total - Paid);
    }

    private sealed record RefundBalance(int SupplierReturnId, int SupplierId, string Code, DateTime ReturnDate, decimal Total, decimal Refunded)
    {
        public decimal Remaining => Math.Max(0, Total - Refunded);
    }

    private sealed record CleanMoney(decimal Amount, byte Method, string? Reference, string? Notes, string? Error = null, string? Field = null)
    {
        public static CleanMoney Fail(string error, string field) => new(0, 0, null, null, error, field);
    }

    private static CleanMoney Validate(SupplierMoneyForm form)
    {
        if (form.Amount is null || form.Amount < 1) return CleanMoney.Fail("Nhập số tiền lớn hơn 0.", nameof(SupplierMoneyForm.Amount));
        var amount = form.Amount.Value;
        if (amount != decimal.Truncate(amount)) return CleanMoney.Fail("Số tiền tính theo đồng, không có số lẻ.", nameof(SupplierMoneyForm.Amount));
        if (amount > MaxAmount) return CleanMoney.Fail("Số tiền quá lớn.", nameof(SupplierMoneyForm.Amount));
        if (form.Method is not ((byte)SupplierPaymentMethod.Cash or (byte)SupplierPaymentMethod.BankTransfer))
            return CleanMoney.Fail("Chọn hình thức tiền mặt hoặc chuyển khoản.", nameof(SupplierMoneyForm.Method));
        var reference = string.IsNullOrWhiteSpace(form.Reference) ? null : form.Reference.Trim();
        if (form.Method == (byte)SupplierPaymentMethod.BankTransfer && reference is null)
            return CleanMoney.Fail("Chuyển khoản cần mã giao dịch (số ủy nhiệm chi / mã giao dịch ngân hàng).", nameof(SupplierMoneyForm.Reference));
        if (reference is not null && (reference.Length > 100 || !PrintableAscii.IsMatch(reference)))
            return CleanMoney.Fail("Mã giao dịch tối đa 100 ký tự, chỉ gồm chữ không dấu, chữ số và ký hiệu.", nameof(SupplierMoneyForm.Reference));
        var notes = string.IsNullOrWhiteSpace(form.Notes) ? null : form.Notes.Trim();
        if (notes is not null && notes.Length > 500) return CleanMoney.Fail("Ghi chú tối đa 500 ký tự.", nameof(SupplierMoneyForm.Notes));
        return new CleanMoney(amount, form.Method, reference, notes);
    }

    private async Task<List<ReceiptBalance>> PostedReceiptBalancesAsync(int? supplierId, CancellationToken ct)
    {
        var query = _db.GoodsReceipts.AsNoTracking().Where(r => r.Status == (byte)GoodsReceiptStatus.Posted);
        if (supplierId is not null) query = query.Where(r => r.SupplierId == supplierId);
        return await query
            .Select(r => new ReceiptBalance(r.GoodsReceiptId, r.SupplierId, r.GoodsReceiptCode, r.ReceiptDate, r.SupplierInvoiceNumber, r.TotalAmount,
                r.SupplierPayments.Where(p => p.PaymentType == Pay && p.Status == Valid).Sum(p => p.Amount)))
            .ToListAsync(ct);
    }

    private async Task<List<RefundBalance>> OpenRefundsAsync(int? supplierId, CancellationToken ct)
    {
        var query = _db.SupplierReturns.AsNoTracking().Where(r => r.Status == (byte)SupplierReturnStatus.Issued);
        if (supplierId is not null) query = query.Where(r => r.SupplierId == supplierId);
        return await query
            .Select(r => new RefundBalance(r.SupplierReturnId, r.SupplierId, r.SupplierReturnCode, r.ReturnDate, r.TotalAmount,
                r.SupplierPayments.Where(p => p.PaymentType == Refund && p.Status == Valid).Sum(p => p.Amount)))
            .ToListAsync(ct);
    }

    private async Task<decimal> RefundedAsync(int supplierReturnId, CancellationToken ct)
        => await _db.SupplierPayments.AsNoTracking()
            .Where(p => p.SupplierReturnId == supplierReturnId && p.PaymentType == Refund && p.Status == Valid)
            .SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;

    /// <summary>Phiếu chi / thu theo thứ tự mới nhất (dùng chung cho trang NCC và phiếu trả).</summary>
    internal static IQueryable<SupplierPaymentLine> PaymentLines(IQueryable<SupplierPayment> query)
        => query
            .OrderByDescending(p => p.PaidAt).ThenByDescending(p => p.SupplierPaymentId)
            .Select(p => new SupplierPaymentLine(p.SupplierPaymentId, p.SupplierPaymentCode, p.PaidAt, p.PaymentType, p.PaymentMethod, p.Amount,
                p.TransactionReference, p.Status, p.Employee.FullName,
                p.GoodsReceipt != null ? p.GoodsReceipt.GoodsReceiptCode : p.SupplierReturn != null ? p.SupplierReturn.SupplierReturnCode : null,
                p.Notes));

    /// <summary>PC / PT + yyMMdd + 3 số nối tiếp trong ngày (giống dữ liệu mẫu PC260513001, PT260725001).</summary>
    private async Task<string> NextCodeAsync(string prefixType, DateTime at, CancellationToken ct)
    {
        var prefix = prefixType + at.ToString("yyMMdd", CultureInfo.InvariantCulture);
        var codes = await _db.SupplierPayments.AsNoTracking().Where(p => p.SupplierPaymentCode.StartsWith(prefix))
            .Select(p => p.SupplierPaymentCode).ToListAsync(ct);
        var max = 0;
        foreach (var code in codes)
            if (int.TryParse(code.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n > max) max = n;
        return prefix + (max + 1).ToString("D3", CultureInfo.InvariantCulture);
    }

    private Task<int?> EmployeeIdOfAsync(string userId, CancellationToken ct)
        => _db.Employees.AsNoTracking().Where(e => e.UserId == userId && e.IsActive).Select(e => (int?)e.EmployeeId).FirstOrDefaultAsync(ct);

    /// <summary>Bỏ dấu để so khớp tìm kiếm trong bộ nhớ (danh sách NCC đã nạp sẵn).</summary>
    private static string Fold(string text)
    {
        var normalized = SearchText.Fold(text).Normalize(System.Text.NormalizationForm.FormD);
        var chars = normalized.Where(ch => CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark).ToArray();
        return new string(chars).ToLowerInvariant();
    }
}
