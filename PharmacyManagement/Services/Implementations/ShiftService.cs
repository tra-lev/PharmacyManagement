using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PharmacyManagement.Data;
using PharmacyManagement.Helpers;
using PharmacyManagement.Models.Entities;
using PharmacyManagement.Models.Enums;
using PharmacyManagement.Models.ViewModels.Sales;
using PharmacyManagement.Services.Interfaces;

namespace PharmacyManagement.Services.Implementations;

public sealed class ShiftService : IShiftService
{
    private const string CodePrefix = "CA";
    private const decimal MaxCash = 1_000_000_000m;

    /// <summary>Mở ca trước giờ này là ca sáng (S), từ giờ này là ca chiều (C) — theo mã ca dữ liệu mẫu CA261031S / CA261031C.</summary>
    private const int AfternoonFromHour = 12;

    private readonly PharmacyDbContext _db;
    private readonly IAuditLogService _audit;

    public ShiftService(PharmacyDbContext db, IAuditLogService audit)
    {
        _db = db;
        _audit = audit;
    }

    // ================================================================== Trang "Ca của tôi"

    public async Task<MyShiftViewModel> GetMyShiftAsync(string userId, CancellationToken ct = default)
    {
        var employee = await EmployeeOfAsync(userId, ct);
        if (employee is null) return new MyShiftViewModel { HasEmployeeProfile = false };

        var shift = await _db.Shifts.AsNoTracking()
            .Where(s => s.EmployeeId == employee.EmployeeId && s.ClosedAt == null)
            .FirstOrDefaultAsync(ct);

        CurrentShiftInfo? current = null;
        if (shift is not null)
        {
            current = new CurrentShiftInfo
            {
                ShiftId = shift.ShiftId,
                Code = shift.ShiftCode,
                OpenedAt = shift.OpenedAt,
                Status = shift.Status,
                HandoverNote = shift.HandoverNote,
                HandedOverAt = shift.HandedOverAt,
                Totals = await ComputeTotalsAsync(shift.ShiftId, shift.OpeningCash, ct)
            };
        }

        var history = await (from s in _db.Shifts.AsNoTracking()
                             join c in _db.ShiftClosings.AsNoTracking() on s.ShiftId equals c.ShiftId
                             where s.EmployeeId == employee.EmployeeId
                             orderby s.OpenedAt descending, s.ShiftId descending
                             select new ShiftHistoryRow(s.ShiftId, s.ShiftCode, s.OpenedAt, s.ClosedAt,
                                 c.ExpectedCash, c.ActualCash, c.ActualCash - c.ExpectedCash))
            .Take(MyShiftViewModel.HistoryLimit)
            .ToListAsync(ct);

        var model = new MyShiftViewModel
        {
            HasEmployeeProfile = employee.IsActive,
            EmployeeName = employee.FullName,
            Current = current,
            LastActualCash = history.Count > 0 ? history[0].ActualCash : null,
            LastClosedAt = history.Count > 0 ? history[0].ClosedAt : null,
            History = history
        };
        if (current is { Status: (byte)ShiftStatus.Open })
            model.Handover.HandoverNote = SuggestHandoverNote(current.Totals);
        return model;
    }

    public async Task<ShiftDetailsViewModel?> GetDetailsAsync(int shiftId, string userId, bool canViewAll, CancellationToken ct = default)
    {
        var employee = await EmployeeOfAsync(userId, ct);
        if (employee is null && !canViewAll) return null;
        var ownId = employee?.EmployeeId;

        var s = await _db.Shifts.AsNoTracking()
            .Where(x => x.ShiftId == shiftId && (canViewAll || x.EmployeeId == ownId))
            .Select(x => new
            {
                x.ShiftId, x.ShiftCode, x.Status, x.OpenedAt, x.HandedOverAt, x.HandoverNote, x.ClosedAt, x.OpeningCash,
                x.EmployeeId, EmployeeName = x.Employee.FullName
            })
            .FirstOrDefaultAsync(ct);
        if (s is null) return null;

        var closing = await _db.ShiftClosings.AsNoTracking()
            .Where(c => c.ShiftId == shiftId)
            .Select(c => new
            {
                c.OpeningCash, c.TotalCashSales, c.TotalNonCash, c.TotalCashRefunds, c.ActualCash, c.ExpectedCash, c.Notes,
                ClosedByName = c.ClosedByEmployee.FullName
            })
            .FirstOrDefaultAsync(ct);

        var live = await ComputeTotalsAsync(shiftId, s.OpeningCash, ct);
        // Ca đã chốt: tiền lấy từ biên bản (số đã chốt, không tính lại); số hóa đơn đếm từ CSDL
        var totals = closing is null
            ? live
            : new ShiftTotals(closing.OpeningCash, live.PaidInvoiceCount, live.PendingInvoiceCount,
                closing.TotalCashSales, closing.TotalNonCash, closing.TotalCashRefunds);

        return new ShiftDetailsViewModel
        {
            ShiftId = s.ShiftId,
            Code = s.ShiftCode,
            EmployeeName = s.EmployeeName,
            Status = s.Status,
            OpenedAt = s.OpenedAt,
            HandedOverAt = s.HandedOverAt,
            HandoverNote = s.HandoverNote,
            ClosedAt = s.ClosedAt,
            ClosedByName = closing?.ClosedByName,
            Totals = totals,
            ActualCash = closing?.ActualCash,
            Difference = closing is null ? null : closing.ActualCash - closing.ExpectedCash,
            ClosingNotes = closing?.Notes,
            IsOwnShift = ownId == s.EmployeeId
        };
    }

    // ================================================================== Ca toàn nhà thuốc (F4.1.1)

    public async Task<ShiftListViewModel> GetAllAsync(ShiftFilter filter, CancellationToken ct = default)
    {
        var query = _db.Shifts.AsNoTracking().AsQueryable();

        byte? status = filter.Status switch
        {
            "open" => (byte)ShiftStatus.Open,
            "handedover" => (byte)ShiftStatus.HandedOver,
            "closed" => (byte)ShiftStatus.Closed,
            _ => null
        };
        if (status is null) filter.Status = null;
        else query = query.Where(s => s.Status == status);
        if (filter.EmployeeId is not null) query = query.Where(s => s.EmployeeId == filter.EmployeeId);

        var q = AccountHelpers.NormalizeFullName(filter.Q);
        filter.Q = q.Length == 0 ? null : q;
        if (filter.Q is not null)
        {
            var text = filter.Q;
            var folded = SearchText.Fold(text);
            query = query.Where(s => s.ShiftCode.Contains(text)
                                     || EF.Functions.Collate(s.Employee.FullName.Replace("đ", "d").Replace("Đ", "D"), SearchText.Collation).Contains(folded));
        }

        var total = await query.CountAsync(ct);
        var pageCount = Math.Max(1, (int)Math.Ceiling(total / (double)ShiftFilter.PageSize));
        filter.Page = Math.Clamp(filter.Page, 1, pageCount);

        var rows = await query
            .OrderByDescending(s => s.OpenedAt).ThenByDescending(s => s.ShiftId)
            .Skip((filter.Page - 1) * ShiftFilter.PageSize).Take(ShiftFilter.PageSize)
            .Select(s => new ShiftListRow(s.ShiftId, s.ShiftCode, s.Employee.FullName, s.OpenedAt, s.ClosedAt, s.Status,
                s.ShiftClosing != null ? (decimal?)s.ShiftClosing.ExpectedCash : null,
                s.ShiftClosing != null ? (decimal?)s.ShiftClosing.ActualCash : null))
            .ToListAsync(ct);

        return new ShiftListViewModel
        {
            Filter = filter,
            Rows = rows,
            Total = total,
            GrandTotal = await _db.Shifts.CountAsync(ct),
            PageCount = pageCount,
            Employees = await _db.Employees.AsNoTracking()
                .Where(e => _db.Shifts.Any(s => s.EmployeeId == e.EmployeeId))
                .OrderBy(e => e.FullName)
                .Select(e => new EmployeeOption(e.EmployeeId, e.FullName + " (" + e.EmployeeCode + ")"))
                .ToListAsync(ct)
        };
    }

    public async Task<int?> GetOpenShiftIdAsync(string userId, CancellationToken ct = default)
    {
        var employee = await EmployeeOfAsync(userId, ct);
        if (employee is null || !employee.IsActive) return null;
        return await _db.Shifts.AsNoTracking()
            .Where(s => s.EmployeeId == employee.EmployeeId && s.ClosedAt == null && s.Status == (byte)ShiftStatus.Open)
            .Select(s => (int?)s.ShiftId)
            .FirstOrDefaultAsync(ct);
    }

    // ================================================================== 1.4.1 Mở ca

    public async Task<OperationResult<int>> OpenAsync(OpenShiftForm form, string userId, CancellationToken ct = default)
    {
        var employee = await EmployeeOfAsync(userId, ct);
        if (employee is null || !employee.IsActive)
            return OperationResult<int>.Fail("Tài khoản chưa gắn hồ sơ nhân viên đang làm việc nên chưa mở ca được. Liên hệ chủ nhà thuốc.");

        var cashError = CheckCash(form.OpeningCash, "Tiền mặt đầu ca");
        if (cashError is not null) return OperationResult<int>.Fail(cashError, nameof(form.OpeningCash));

        var existing = await _db.Shifts.AsNoTracking()
            .Where(s => s.EmployeeId == employee.EmployeeId && s.ClosedAt == null)
            .Select(s => s.ShiftCode).FirstOrDefaultAsync(ct);
        if (existing is not null)
            return OperationResult<int>.Fail($"Bạn đang có ca {existing} chưa chốt. Bàn giao và chốt tiền ca đó trước khi mở ca mới.");

        var now = DateTime.Now;
        var shift = new Shift
        {
            ShiftCode = await NextCodeAsync(now, ct),
            EmployeeId = employee.EmployeeId,
            OpenedAt = now,
            OpeningCash = form.OpeningCash!.Value,
            Status = (byte)ShiftStatus.Open
        };

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            _db.Shifts.Add(shift);
            await _db.SaveChangesAsync(ct);
            await _audit.WriteAsync("SHIFT_OPEN", "Shifts", shift.ShiftId.ToString(CultureInfo.InvariantCulture), null,
                new { shift.ShiftCode, shift.EmployeeId, shift.OpenedAt, shift.OpeningCash }, userId, ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException)
        {
            // UX_Shifts_OpenPerEmployee (mở ca ở hai nơi cùng lúc) hoặc UQ_Shifts_Code (hai quầy cùng lúc)
            return OperationResult<int>.Fail("Chưa mở được ca: có thể bạn vừa mở ca ở máy khác, hoặc mã ca vừa bị trùng. Tải lại trang để kiểm tra rồi thử lại.");
        }
        return OperationResult<int>.Ok(shift.ShiftId, $"Đã mở ca {shift.ShiftCode} với {Format.Money(shift.OpeningCash)} tiền mặt đầu ca.");
    }

    // ================================================================== 1.4.2 Bàn giao ca

    public async Task<OperationResult> HandOverAsync(HandoverForm form, string userId, CancellationToken ct = default)
    {
        var (shift, error) = await LoadUnclosedShiftAsync(userId, ct);
        if (shift is null) return OperationResult.Fail(error!);
        if (shift.Status != (byte)ShiftStatus.Open) return OperationResult.Fail($"Ca {shift.ShiftCode} đã bàn giao từ trước. Bước tiếp theo là chốt tiền cuối ca.");

        var note = CleanText(form.HandoverNote);
        if (note is null) return OperationResult.Fail("Nhập ghi chú bàn giao.", nameof(form.HandoverNote));
        if (note.Length > 1000) return OperationResult.Fail("Ghi chú bàn giao tối đa 1000 ký tự.", nameof(form.HandoverNote));

        var pending = await PendingInvoiceCountAsync(shift.ShiftId, ct);
        if (pending > 0)
            return OperationResult.Fail($"Ca còn {pending} hóa đơn chờ thanh toán. Thu tiền hoặc hủy các hóa đơn đó trước khi bàn giao, để tiền được tính đúng vào ca.");

        shift.Status = (byte)ShiftStatus.HandedOver;
        shift.HandedOverAt = DateTime.Now;
        shift.HandoverNote = note;

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("SHIFT_HANDOVER", "Shifts", shift.ShiftId.ToString(CultureInfo.InvariantCulture),
            new { Status = (byte)ShiftStatus.Open }, new { shift.Status, shift.HandedOverAt, shift.HandoverNote }, userId, ct);
        await tx.CommitAsync(ct);
        return OperationResult.Ok($"Đã bàn giao ca {shift.ShiftCode}. Đếm tiền trong két rồi chốt tiền cuối ca.");
    }

    // ================================================================== 1.4.3 Chốt tiền cuối ca

    public async Task<OperationResult> CloseAsync(CloseShiftForm form, string userId, CancellationToken ct = default)
    {
        var (shift, error) = await LoadUnclosedShiftAsync(userId, ct);
        if (shift is null) return OperationResult.Fail(error!);
        if (shift.Status != (byte)ShiftStatus.HandedOver)
            return OperationResult.Fail($"Bàn giao ca {shift.ShiftCode} trước rồi mới chốt tiền cuối ca.");

        var cashError = CheckCash(form.ActualCash, "Tiền mặt thực đếm");
        if (cashError is not null) return OperationResult.Fail(cashError, nameof(form.ActualCash));
        var notes = CleanText(form.Notes);
        if (notes?.Length > 1000) return OperationResult.Fail("Giải trình tối đa 1000 ký tự.", nameof(form.Notes));

        if (await PendingInvoiceCountAsync(shift.ShiftId, ct) > 0)
            return OperationResult.Fail("Ca còn hóa đơn chờ thanh toán — xử lý xong rồi mới chốt tiền.");

        var totals = await ComputeTotalsAsync(shift.ShiftId, shift.OpeningCash, ct);
        var expected = totals.ExpectedCash;
        if (expected < 0)
            return OperationResult.Fail($"Tiền mặt theo sổ của ca đang âm ({Format.Money(expected)}): tiền hoàn lớn hơn tiền đầu ca cộng tiền bán. Kiểm tra lại các phiếu hoàn tiền mặt của ca trước khi chốt.");

        var actual = form.ActualCash!.Value;
        var difference = actual - expected;
        if (difference != 0 && notes is null)
            return OperationResult.Fail(
                $"Tiền thực đếm {(difference > 0 ? "thừa" : "thiếu")} {Format.Money(Math.Abs(difference))} so với sổ ({Format.Money(expected)}). Ghi giải trình chênh lệch rồi chốt lại.",
                nameof(form.Notes));

        var now = DateTime.Now;
        var closing = new ShiftClosing
        {
            ShiftId = shift.ShiftId,
            ClosedByEmployeeId = shift.EmployeeId,
            OpeningCash = totals.OpeningCash,
            TotalCashSales = totals.CashSales,
            TotalCashRefunds = totals.CashRefunds,
            ExpectedCash = expected,
            ActualCash = actual,
            TotalNonCash = totals.NonCashSales,
            ClosedAt = now,
            Notes = notes
        };
        shift.Status = (byte)ShiftStatus.Closed;
        shift.ClosedAt = now;

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            _db.ShiftClosings.Add(closing);
            await _db.SaveChangesAsync(ct);
            await _audit.WriteAsync("SHIFT_CLOSE", "Shifts", shift.ShiftId.ToString(CultureInfo.InvariantCulture),
                new { Status = (byte)ShiftStatus.HandedOver },
                new
                {
                    shift.Status, shift.ClosedAt, closing.OpeningCash, closing.TotalCashSales, closing.TotalCashRefunds,
                    closing.ExpectedCash, closing.ActualCash, Difference = difference, closing.TotalNonCash, closing.Notes
                }, userId, ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException)
        {
            // UQ_ShiftClosings_Shift: bấm chốt hai lần / chốt ở hai máy cùng lúc
            return OperationResult.Fail("Ca này vừa được chốt ở nơi khác. Tải lại trang để xem biên bản.");
        }

        var result = difference == 0
            ? "khớp với sổ"
            : $"{(difference > 0 ? "thừa" : "thiếu")} {Format.Money(Math.Abs(difference))} so với sổ";
        return OperationResult.Ok($"Đã chốt ca {shift.ShiftCode}: thực đếm {Format.Money(actual)}, {result}.");
    }

    // ================================================================== Dùng chung

    /// <summary>
    /// Số liệu tiền của ca. Tiền bán = thanh toán THÀNH CÔNG của hóa đơn thuộc ca (không tính hóa đơn đã hủy);
    /// tiền hoàn = phiếu đổi trả hoàn TIỀN MẶT gắn ca (không tính phiếu đã hủy) — DEC-11, CHANGELOG_DB mục 8.
    /// </summary>
    private async Task<ShiftTotals> ComputeTotalsAsync(int shiftId, decimal openingCash, CancellationToken ct)
    {
        var invoices = _db.SalesInvoices.AsNoTracking().Where(i => i.ShiftId == shiftId);
        var paidCount = await invoices.CountAsync(i => i.Status == (byte)InvoiceStatus.Paid, ct);
        var pendingCount = await invoices.CountAsync(i => i.Status == (byte)InvoiceStatus.PendingPayment, ct);

        var payments = await _db.Payments.AsNoTracking()
            .Where(p => p.Invoice.ShiftId == shiftId && p.Status == (byte)PaymentStatus.Succeeded
                        && p.Invoice.Status != (byte)InvoiceStatus.Cancelled)
            .GroupBy(p => p.PaymentMethod)
            .Select(g => new { Method = g.Key, Total = g.Sum(p => p.Amount) })
            .ToListAsync(ct);
        var cashSales = payments.Where(p => p.Method == (byte)PaymentMethod.Cash).Sum(p => p.Total);
        var nonCashSales = payments.Where(p => p.Method != (byte)PaymentMethod.Cash).Sum(p => p.Total);

        var refunds = await _db.Returns.AsNoTracking()
            .Where(r => r.ShiftId == shiftId && r.RefundMethod == (byte)PaymentMethod.Cash && r.Status != (byte)ReturnStatus.Cancelled)
            .SumAsync(r => (decimal?)r.RefundAmount, ct) ?? 0;

        return new ShiftTotals(
            openingCash,
            paidCount,
            pendingCount,
            cashSales,
            nonCashSales,
            refunds);
    }

    private Task<int> PendingInvoiceCountAsync(int shiftId, CancellationToken ct)
        => _db.SalesInvoices.CountAsync(i => i.ShiftId == shiftId && i.Status == (byte)InvoiceStatus.PendingPayment, ct);

    /// <summary>Ca chưa chốt của chính người dùng (theo dõi để sửa). Trả về lỗi tiếng Việt khi không có.</summary>
    private async Task<(Shift? Shift, string? Error)> LoadUnclosedShiftAsync(string userId, CancellationToken ct)
    {
        var employee = await EmployeeOfAsync(userId, ct);
        if (employee is null) return (null, "Tài khoản chưa gắn hồ sơ nhân viên.");
        var shift = await _db.Shifts.FirstOrDefaultAsync(s => s.EmployeeId == employee.EmployeeId && s.ClosedAt == null, ct);
        return shift is null ? (null, "Bạn không có ca nào đang mở. Tải lại trang để xem tình trạng ca.") : (shift, null);
    }

    private sealed record EmployeeInfo(int EmployeeId, string FullName, bool IsActive);

    private Task<EmployeeInfo?> EmployeeOfAsync(string userId, CancellationToken ct)
        => _db.Employees.AsNoTracking().Where(e => e.UserId == userId)
            .Select(e => new EmployeeInfo(e.EmployeeId, e.FullName, e.IsActive))
            .FirstOrDefaultAsync(ct);

    /// <summary>CA + yyMMdd + S/C (giống dữ liệu mẫu); nhiều quầy mở cùng buổi thì thêm -2, -3…</summary>
    private async Task<string> NextCodeAsync(DateTime openedAt, CancellationToken ct)
    {
        var baseCode = CodePrefix + openedAt.ToString("yyMMdd", CultureInfo.InvariantCulture)
                       + (openedAt.Hour < AfternoonFromHour ? "S" : "C");
        var used = await _db.Shifts.AsNoTracking().Where(s => s.ShiftCode.StartsWith(baseCode))
            .Select(s => s.ShiftCode).ToListAsync(ct);
        if (!used.Contains(baseCode)) return baseCode;
        var n = 2;
        while (used.Contains(baseCode + "-" + n.ToString(CultureInfo.InvariantCulture))) n++;
        return baseCode + "-" + n.ToString(CultureInfo.InvariantCulture);
    }

    private static string? CheckCash(decimal? value, string label)
    {
        if (value is null) return $"Nhập {label.ToLowerInvariant()}.";
        if (value < 0 || value > MaxCash) return $"{label} từ 0 đến {Format.Money(MaxCash)}.";
        if (value != decimal.Truncate(value.Value)) return $"{label} tính theo đồng, không có số lẻ.";
        return null;
    }

    /// <summary>Gợi ý ghi chú bàn giao theo mẫu sổ ca: số hóa đơn + tiền mặt theo sổ (người dùng sửa được).</summary>
    private static string SuggestHandoverNote(ShiftTotals t)
        => $"Bàn giao ca: {t.PaidInvoiceCount} hóa đơn; tiền mặt theo sổ {Format.Money(t.ExpectedCash)}";

    private static string? CleanText(string? value)
    {
        var text = value?.Trim();
        return string.IsNullOrEmpty(text) ? null : text.Replace("\r\n", "\n");
    }
}
