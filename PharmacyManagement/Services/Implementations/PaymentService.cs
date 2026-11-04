using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PharmacyManagement.Data;
using PharmacyManagement.Helpers;
using PharmacyManagement.Models.Entities;
using PharmacyManagement.Models.Enums;
using PharmacyManagement.Models.ViewModels.Sales;
using PharmacyManagement.Services.Interfaces;

namespace PharmacyManagement.Services.Implementations;

public sealed class PaymentService : IPaymentService
{
    private const decimal MaxAmount = 1_000_000_000m;
    private static readonly Regex ReferencePattern = new(@"^[A-Za-z0-9][A-Za-z0-9./_\-]*$", RegexOptions.CultureInvariant);

    private readonly PharmacyDbContext _db;
    private readonly IFefoAllocator _fefo;
    private readonly IAuditLogService _audit;

    public PaymentService(PharmacyDbContext db, IFefoAllocator fefo, IAuditLogService audit)
    {
        _db = db;
        _fefo = fefo;
        _audit = audit;
    }

    public async Task<OperationResult> PayAsync(int invoiceId, PaymentForm form, string userId, PaymentPermissions permissions, CancellationToken ct = default)
    {
        var employeeId = await _db.Employees.AsNoTracking().Where(e => e.UserId == userId && e.IsActive)
            .Select(e => (int?)e.EmployeeId).FirstOrDefaultAsync(ct);
        if (employeeId is null) return OperationResult.Fail("Tài khoản chưa gắn hồ sơ nhân viên đang làm việc nên chưa thu tiền được.");

        // ---- Số tiền từng hình thức (nguyên đồng) ----
        var cash = form.CashAmount ?? 0;
        var transfer = form.TransferAmount ?? 0;
        var card = form.CardAmount ?? 0;
        foreach (var (value, field, label) in new[] { (cash, nameof(PaymentForm.CashAmount), "Tiền mặt"), (transfer, nameof(PaymentForm.TransferAmount), "Chuyển khoản"), (card, nameof(PaymentForm.CardAmount), "Thẻ") })
        {
            if (value < 0 || value > MaxAmount || value != decimal.Truncate(value))
                return OperationResult.Fail($"{label} từ 0 đến {Format.Money(MaxAmount)}, tính theo đồng.", field);
        }
        var methodCount = (cash > 0 ? 1 : 0) + (transfer > 0 ? 1 : 0) + (card > 0 ? 1 : 0);
        if (methodCount == 0) return OperationResult.Fail("Nhập số tiền thu.", nameof(PaymentForm.CashAmount));

        // ---- Quyền (3.3.2.1 / 3.3.2.2 / 3.3.2.3) — kiểm tra ở server ----
        if (cash > 0 && !permissions.Cash) return OperationResult.Fail("Bạn không có quyền thu tiền mặt.", nameof(PaymentForm.CashAmount));
        if ((transfer > 0 || card > 0) && !permissions.NonCash) return OperationResult.Fail("Bạn không có quyền thu chuyển khoản, thẻ.");
        if (methodCount > 1 && !permissions.Combined) return OperationResult.Fail("Bạn không có quyền thanh toán kết hợp.");

        decimal? tendered = null;
        if (form.TenderedAmount is not null)
        {
            if (cash == 0) return OperationResult.Fail("Chỉ ghi tiền khách đưa khi có thu tiền mặt.", nameof(PaymentForm.TenderedAmount));
            if (form.TenderedAmount < cash) return OperationResult.Fail($"Tiền khách đưa phải từ {Format.Money(cash)} trở lên.", nameof(PaymentForm.TenderedAmount));
            if (form.TenderedAmount > MaxAmount) return OperationResult.Fail($"Tiền khách đưa tối đa {Format.Money(MaxAmount)}.", nameof(PaymentForm.TenderedAmount));
            tendered = form.TenderedAmount;
        }

        string? transferRef = null;
        string? cardRef = null;
        if (transfer > 0)
        {
            transferRef = CleanReference(form.TransferReference, out var error);
            if (error is not null) return OperationResult.Fail(error, nameof(PaymentForm.TransferReference));
        }
        if (card > 0)
        {
            cardRef = CleanReference(form.CardReference, out var error);
            if (error is not null) return OperationResult.Fail(error, nameof(PaymentForm.CardReference));
        }

        // ---- Hóa đơn ----
        var invoice = await _db.SalesInvoices.AsNoTracking()
            .Where(i => i.InvoiceId == invoiceId)
            .Select(i => new { i.InvoiceCode, i.Status, i.EmployeeId, i.ShiftId, i.TotalAmount, i.PrescriptionId, ShiftStatus = i.Shift.Status })
            .FirstOrDefaultAsync(ct);
        if (invoice is null) return OperationResult.Fail("Không tìm thấy hóa đơn.");
        if (invoice.Status != (byte)InvoiceStatus.PendingPayment)
            return OperationResult.Fail($"Hóa đơn {invoice.InvoiceCode} {InvoiceStatusText.Of(invoice.Status).ToLowerInvariant()} nên không thu tiền được.");
        if (invoice.EmployeeId != employeeId)
            return OperationResult.Fail("Chỉ người lập hóa đơn thu tiền được, trong ca của chính họ.");
        if (invoice.ShiftStatus != (byte)ShiftStatus.Open)
            return OperationResult.Fail("Ca của hóa đơn không còn đang mở nên không thu tiền được.");

        var sum = cash + transfer + card;
        if (sum != invoice.TotalAmount)
            return OperationResult.Fail($"Số tiền thu ({Format.Money(sum)}) chưa bằng số phải trả ({Format.Money(invoice.TotalAmount)}).");

        // ---- Một transaction: hóa đơn → Đã thanh toán, ghi tiền thu, xuất kho FEFO (SYS-14) ----
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            // Giành quyền thanh toán (khóa dòng tới khi commit): bấm hai lần / hai máy cùng lúc chỉ một lần thành công
            var claimed = await _db.SalesInvoices
                .Where(i => i.InvoiceId == invoiceId && i.Status == (byte)InvoiceStatus.PendingPayment && i.EmployeeId == employeeId)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, (byte)InvoiceStatus.Paid), ct);
            if (claimed == 0) return OperationResult.Fail($"Hóa đơn {invoice.InvoiceCode} vừa được thanh toán hoặc hủy ở nơi khác. Tải lại trang.");

            var shiftId = invoice.ShiftId;
            if (!await _db.Shifts.AnyAsync(s => s.ShiftId == shiftId && s.Status == (byte)ShiftStatus.Open, ct))
                return OperationResult.Fail("Ca của hóa đơn vừa được bàn giao nên không thu tiền được.");

            var now = DateTime.Now;
            if (cash > 0)
                _db.Payments.Add(new Payment { InvoiceId = invoiceId, PaymentMethod = (byte)PaymentMethod.Cash, Amount = cash, TenderedAmount = tendered, PaidAt = now, Status = (byte)PaymentStatus.Succeeded });
            if (transfer > 0)
                _db.Payments.Add(new Payment { InvoiceId = invoiceId, PaymentMethod = (byte)PaymentMethod.BankTransfer, Amount = transfer, TransactionReference = transferRef, PaidAt = now, Status = (byte)PaymentStatus.Succeeded });
            if (card > 0)
                _db.Payments.Add(new Payment { InvoiceId = invoiceId, PaymentMethod = (byte)PaymentMethod.Card, Amount = card, TransactionReference = cardRef, PaidAt = now, Status = (byte)PaymentStatus.Succeeded });
            await _db.SaveChangesAsync(ct);

            // 2.3.4 FEFO: thiếu tồn → không commit, mọi thay đổi ở trên được hoàn tác
            var issued = await _fefo.AllocateInvoiceAsync(invoiceId, invoice.InvoiceCode, employeeId.Value, ct);
            if (!issued.Succeeded)
            {
                _db.ChangeTracker.Clear();
                return OperationResult.Fail(issued.Message!);
            }

            // 3.2.4: hóa đơn theo đơn thanh toán xong → đơn chuyển Đã bán (một đơn chỉ một hóa đơn, kể cả khi khách mua một phần)
            if (invoice.PrescriptionId is not null)
            {
                var prescriptionId = invoice.PrescriptionId.Value;
                var dispensed = await _db.Prescriptions
                    .Where(p => p.PrescriptionId == prescriptionId && p.Status == (byte)PrescriptionStatus.Saved)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, (byte)PrescriptionStatus.Dispensed), ct);
                if (dispensed == 0)
                {
                    _db.ChangeTracker.Clear();
                    return OperationResult.Fail("Đơn thuốc của hóa đơn này đã bán hoặc đã hủy nên không thu tiền được.");
                }
            }

            await _audit.WriteAsync("INVOICE_PAY", "SalesInvoices", invoiceId.ToString(CultureInfo.InvariantCulture),
                new { Status = (byte)InvoiceStatus.PendingPayment },
                new
                {
                    Status = (byte)InvoiceStatus.Paid, invoice.InvoiceCode, invoice.TotalAmount, invoice.PrescriptionId,
                    Cash = cash, Tendered = tendered, Transfer = transfer, TransferReference = transferRef, Card = card, CardReference = cardRef,
                    Batches = issued.Value
                }, userId, ct);
            await tx.CommitAsync(ct);

            var change = tendered is null ? 0 : tendered.Value - cash;
            return OperationResult.Ok($"Đã thu {Format.Money(invoice.TotalAmount)} cho hóa đơn {invoice.InvoiceCode}"
                                      + (change > 0 ? $" — tiền thối khách {Format.Money(change)}" : "")
                                      + ".");
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            return OperationResult.Fail("Chưa thu tiền được. Tải lại trang rồi thử lại.");
        }
    }

    /// <summary>Mã giao dịch ngân hàng / máy POS: bắt buộc, chữ không dấu, chữ số và . / _ -.</summary>
    private static string? CleanReference(string? value, out string? error)
    {
        error = null;
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            error = "Nhập mã giao dịch.";
            return null;
        }
        if (text.Length > 100 || !ReferencePattern.IsMatch(text))
        {
            error = "Mã giao dịch chỉ gồm chữ không dấu và chữ số.";
            return null;
        }
        return text;
    }
}
