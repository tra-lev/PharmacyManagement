using System.Globalization;
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

public sealed class SalesInvoiceService : ISalesInvoiceService
{
    private const string CodePrefix = "HD";
    private const int NotesMax = 1000;

    private readonly PharmacyDbContext _db;
    private readonly IAuditLogService _audit;
    private readonly ISafetyCheckService _safety;

    public SalesInvoiceService(PharmacyDbContext db, IAuditLogService audit, ISafetyCheckService safety)
    {
        _db = db;
        _audit = audit;
        _safety = safety;
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    // ================================================================== Form bán lẻ

    public async Task<SaleFormViewModel> NewFormAsync(string userId, CancellationToken ct = default)
    {
        var form = new SaleFormViewModel();
        form.Lines.Add(new SaleLineForm());
        await PrepareFormAsync(form, userId, ct);
        return form;
    }

    public async Task<OperationResult<SaleFormViewModel>> NewFromPrescriptionAsync(int prescriptionId, string userId, CancellationToken ct = default)
    {
        var rx = await RxAsync(prescriptionId, ct);
        if (rx is null) return OperationResult<SaleFormViewModel>.Fail("Không tìm thấy đơn thuốc.");
        var error = await RxErrorAsync(rx, null, ct);
        if (error is not null) return OperationResult<SaleFormViewModel>.Fail(error);

        var lines = await _db.PrescriptionDetails.AsNoTracking()
            .Where(d => d.PrescriptionId == prescriptionId)
            .OrderBy(d => d.PrescriptionDetailId)
            .Select(d => new { d.MedicineId, d.UnitId, d.PrescribedQuantity })
            .ToListAsync(ct);
        var form = new SaleFormViewModel
        {
            PrescriptionId = prescriptionId,
            CustomerId = rx.CustomerId,
            Lines = lines.Select(l => new SaleLineForm
            {
                MedicineId = l.MedicineId,
                UnitId = l.UnitId,
                Quantity = (int)Math.Min(decimal.Truncate(l.PrescribedQuantity), SaleFormViewModel.MaxQuantity)
            }).ToList()
        };
        await PrepareFormAsync(form, userId, ct);
        return OperationResult<SaleFormViewModel>.Ok(form);
    }

    public async Task PrepareFormAsync(SaleFormViewModel form, string userId, CancellationToken ct = default)
    {
        var seller = await SellerAsync(userId, ct);
        form.HasEmployeeProfile = seller.EmployeeId is not null;
        form.ShiftCode = seller.ShiftCode;
        if (form.InvoiceId is null)
        {
            form.Code = await NextCodeAsync(DateTime.Now, ct);
        }
        else
        {
            var invoiceId = form.InvoiceId.Value;
            form.Code = await _db.SalesInvoices.AsNoTracking().Where(x => x.InvoiceId == invoiceId).Select(x => x.InvoiceCode).FirstOrDefaultAsync(ct);
        }

        var policy = await ActivePolicyAsync(ct);
        form.PricePolicyName = policy?.Name;
        form.MaxDiscountPercent = policy?.MaxPercent ?? 0;

        RxContext? rx = null;
        if (form.PrescriptionId is not null)
        {
            rx = await RxAsync(form.PrescriptionId.Value, ct);
            if (rx is not null)
            {
                form.CustomerId = rx.CustomerId;
                form.Prescription = new SalePrescriptionInfo(rx.PrescriptionId, rx.Code, rx.CustomerName, rx.DoctorName, rx.ClinicName,
                    rx.Date, rx.Date.AddDays(PrescriptionRules.ValidDays));
            }
        }
        form.MedicineOptions = await LoadMedicinesAsync(rx, ct);
        form.CustomerOptions = await LoadCustomersAsync(ct);
        if (form.Lines.Count == 0) form.Lines.Add(new SaleLineForm());

        if (form.PrescriptionId is not null)
        {
            // Bán theo đơn: cảnh báo an toàn đã được dược sĩ xem khi lưu đơn (3.2.2.3)
            form.Warnings = Array.Empty<SafetyWarning>();
            form.ShownWarningsKey = null;
            form.AcknowledgeWarnings = false;
            return;
        }

        var medicineIds = form.Lines.Where(l => l.MedicineId is not null).Select(l => l.MedicineId!.Value).Distinct().ToList();
        var warnings = await _safety.CheckAsync(form.CustomerId, medicineIds, false, null, ct);
        form.Warnings = warnings;
        form.ShownWarningsKey = warnings.Count == 0 ? null : SafetyCheckService.KeyOf(warnings);
        // Luôn bắt xác nhận lại khi hiện lại form: người dùng phải tích sau khi đã thấy đúng bộ cảnh báo này
        form.AcknowledgeWarnings = false;
    }

    // ================================================================== 3.1.4 Lập hóa đơn bán lẻ

    public async Task<OperationResult<int>> CreateAsync(SaleFormViewModel form, string userId, bool canExceedDiscountLimit, CancellationToken ct = default)
    {
        var seller = await SellerAsync(userId, ct);
        if (seller.EmployeeId is null)
            return OperationResult<int>.Fail("Tài khoản chưa gắn hồ sơ nhân viên đang làm việc nên chưa bán hàng được.");
        if (seller.ShiftId is null)
            return OperationResult<int>.Fail("Bạn chưa mở ca. Mở ca ở trang Ca làm việc rồi bán hàng.");

        RxContext? rx = null;
        if (form.PrescriptionId is not null)
        {
            rx = await RxAsync(form.PrescriptionId.Value, ct);
            if (rx is null) return OperationResult<int>.Fail("Không tìm thấy đơn thuốc.");
            var rxError = await RxErrorAsync(rx, null, ct);
            if (rxError is not null) return OperationResult<int>.Fail(rxError);
        }

        var built = await BuildDraftAsync(form, canExceedDiscountLimit, rx, ct);
        if (!built.Succeeded) return OperationResult<int>.Fail(built.Message!, built.Field);
        var draft = built.Value!;

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var invoice = new SalesInvoice
            {
                InvoiceCode = await NextCodeAsync(DateTime.Now, ct),
                CustomerId = rx?.CustomerId ?? form.CustomerId,
                PrescriptionId = rx?.PrescriptionId,
                EmployeeId = seller.EmployeeId.Value,
                ShiftId = seller.ShiftId.Value,
                InvoiceDate = DateTime.Now,
                Status = (byte)InvoiceStatus.PendingPayment,
                EinvoiceStatus = (byte)EInvoiceStatus.NotIssued
            };
            ApplyDraft(invoice, draft);
            _db.SalesInvoices.Add(invoice);
            await _db.SaveChangesAsync(ct);
            await _audit.WriteAsync("INVOICE_CREATE", "SalesInvoices", invoice.InvoiceId.ToString(CultureInfo.InvariantCulture), null,
                new
                {
                    invoice.InvoiceCode, invoice.ShiftId, invoice.CustomerId, invoice.PrescriptionId, invoice.SubTotal, invoice.DiscountAmount, invoice.TotalAmount,
                    ExtraDiscountPercent = draft.ExtraPercent, ExtraDiscount = draft.Extra,
                    Lines = draft.Details.Count, SafetyWarnings = draft.WarningCount
                }, userId, ct);
            await WriteOverLimitAsync(invoice, draft, userId, ct);
            await tx.CommitAsync(ct);
            return OperationResult<int>.Ok(invoice.InvoiceId, $"Đã lập hóa đơn {invoice.InvoiceCode} — {Format.Money(draft.Total)}, chờ thanh toán.");
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            return OperationResult<int>.Fail("Chưa lưu được. Bấm Lưu lại.");
        }
    }

    // ================================================================== Sửa hóa đơn chờ thanh toán (quay lại từ bước thu tiền)

    public async Task<OperationResult<SaleFormViewModel>> EditFormAsync(int invoiceId, string userId, CancellationToken ct = default)
    {
        var seller = await SellerAsync(userId, ct);
        var error = await NotEditableReasonAsync(invoiceId, seller, ct);
        if (error is not null) return OperationResult<SaleFormViewModel>.Fail(error);

        var invoice = await _db.SalesInvoices.AsNoTracking().Where(x => x.InvoiceId == invoiceId)
            .Select(x => new { x.CustomerId, x.PrescriptionId, x.Notes }).FirstAsync(ct);
        var lines = await _db.SalesInvoiceDetails.AsNoTracking().Where(d => d.InvoiceId == invoiceId)
            .OrderBy(d => d.InvoiceDetailId)
            .Select(d => new { d.MedicineId, d.UnitId, d.Quantity })
            .ToListAsync(ct);

        var form = new SaleFormViewModel
        {
            InvoiceId = invoiceId,
            PrescriptionId = invoice.PrescriptionId,
            CustomerId = invoice.CustomerId,
            Lines = lines.Select(l => new SaleLineForm { MedicineId = l.MedicineId, UnitId = l.UnitId, Quantity = (int)decimal.Truncate(l.Quantity) }).ToList()
        };
        // Ghi chú: tách phần người bán tự ghi khỏi các dòng hệ thống ghi thêm (giảm thêm, cảnh báo đã xem)
        var userNotes = new List<string>();
        foreach (var line in (invoice.Notes ?? string.Empty).Split('\n'))
        {
            var match = ExtraNote.Match(line);
            if (match.Success && line.StartsWith("Giảm thêm ", StringComparison.Ordinal))
            {
                if (decimal.TryParse(match.Groups[1].Value.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var pct))
                    form.ExtraDiscountPercent = pct;
                continue;
            }
            if (line.StartsWith(WarningsNotePrefix, StringComparison.Ordinal)) continue;
            userNotes.Add(line);
        }
        form.Notes = CleanText(string.Join("\n", userNotes));

        await PrepareFormAsync(form, userId, ct);
        return OperationResult<SaleFormViewModel>.Ok(form);
    }

    public async Task<OperationResult> UpdateAsync(int invoiceId, SaleFormViewModel form, string userId, bool canExceedDiscountLimit, CancellationToken ct = default)
    {
        var seller = await SellerAsync(userId, ct);
        var error = await NotEditableReasonAsync(invoiceId, seller, ct);
        if (error is not null) return OperationResult.Fail(error);

        // Hóa đơn theo đơn giữ nguyên đơn thuốc đã gắn (không đọc từ form)
        form.PrescriptionId = await _db.SalesInvoices.AsNoTracking().Where(x => x.InvoiceId == invoiceId)
            .Select(x => x.PrescriptionId).FirstOrDefaultAsync(ct);
        RxContext? rx = null;
        if (form.PrescriptionId is not null)
        {
            rx = await RxAsync(form.PrescriptionId.Value, ct);
            if (rx is null) return OperationResult.Fail("Không tìm thấy đơn thuốc.");
            var rxError = await RxErrorAsync(rx, invoiceId, ct);
            if (rxError is not null) return OperationResult.Fail(rxError);
        }

        var built = await BuildDraftAsync(form, canExceedDiscountLimit, rx, ct);
        if (!built.Succeeded) return OperationResult.Fail(built.Message!, built.Field);
        var draft = built.Value!;

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var invoice = await _db.SalesInvoices.Include(x => x.SalesInvoiceDetails).FirstAsync(x => x.InvoiceId == invoiceId, ct);
            if (invoice.Status != (byte)InvoiceStatus.PendingPayment || invoice.EmployeeId != seller.EmployeeId || invoice.ShiftId != seller.ShiftId)
                return OperationResult.Fail("Hóa đơn vừa được thu tiền hoặc thay đổi ở nơi khác. Tải lại trang.");
            var old = new { invoice.CustomerId, invoice.SubTotal, invoice.DiscountAmount, invoice.TotalAmount, Lines = invoice.SalesInvoiceDetails.Count };

            // Hóa đơn chờ thanh toán chưa thu tiền, chưa xuất kho: thay toàn bộ dòng thuốc
            _db.SalesInvoiceDetails.RemoveRange(invoice.SalesInvoiceDetails);
            invoice.SalesInvoiceDetails.Clear();
            invoice.CustomerId = rx?.CustomerId ?? form.CustomerId;
            ApplyDraft(invoice, draft);
            await _db.SaveChangesAsync(ct); // RowVersion: hóa đơn bị đổi ở nơi khác → DbUpdateConcurrencyException

            await _audit.WriteAsync("INVOICE_UPDATE", "SalesInvoices", invoiceId.ToString(CultureInfo.InvariantCulture), old,
                new
                {
                    invoice.InvoiceCode, invoice.CustomerId, invoice.SubTotal, invoice.DiscountAmount, invoice.TotalAmount,
                    ExtraDiscountPercent = draft.ExtraPercent, ExtraDiscount = draft.Extra,
                    Lines = draft.Details.Count, SafetyWarnings = draft.WarningCount
                }, userId, ct);
            await WriteOverLimitAsync(invoice, draft, userId, ct);
            await tx.CommitAsync(ct);
            return OperationResult.Ok($"Đã cập nhật hóa đơn {invoice.InvoiceCode} — {Format.Money(draft.Total)}, chờ thanh toán.");
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            return OperationResult.Fail("Hóa đơn vừa được thu tiền hoặc thay đổi ở nơi khác. Tải lại trang.");
        }
    }

    /// <summary>null = sửa được: hóa đơn Chờ thanh toán, do chính người dùng lập, ca của hóa đơn là ca đang mở của họ.</summary>
    private async Task<string?> NotEditableReasonAsync(int invoiceId, SellerInfo seller, CancellationToken ct)
    {
        if (seller.EmployeeId is null) return "Tài khoản chưa gắn hồ sơ nhân viên đang làm việc nên chưa sửa được hóa đơn.";
        var inv = await _db.SalesInvoices.AsNoTracking().Where(x => x.InvoiceId == invoiceId)
            .Select(x => new { x.InvoiceCode, x.Status, x.EmployeeId, x.ShiftId }).FirstOrDefaultAsync(ct);
        if (inv is null) return "Không tìm thấy hóa đơn.";
        if (inv.Status != (byte)InvoiceStatus.PendingPayment)
            return $"Hóa đơn {inv.InvoiceCode} {InvoiceStatusText.Of(inv.Status).ToLowerInvariant()} nên không sửa được.";
        if (inv.EmployeeId != seller.EmployeeId) return "Chỉ người lập hóa đơn sửa được.";
        if (seller.ShiftId is null || inv.ShiftId != seller.ShiftId) return "Ca của hóa đơn không còn đang mở nên không sửa được.";
        return null;
    }

    // ================================================================== Kiểm tra + tính tiền dùng chung cho lập mới và sửa

    private sealed record SaleDraft(
        List<SalesInvoiceDetail> Details, decimal SubTotal, decimal PromoTotal, decimal ExtraPercent, decimal Extra, decimal Total,
        decimal MaxPercent, bool OverLimit, int WarningCount, string? Notes);

    private static void ApplyDraft(SalesInvoice invoice, SaleDraft draft)
    {
        invoice.SubTotal = draft.SubTotal;
        // Quy ước dữ liệu mẫu: DiscountAmount = Σ giảm của các dòng (khuyến mãi + giảm thêm đã chia); TotalAmount = SubTotal − DiscountAmount
        invoice.DiscountAmount = draft.PromoTotal + draft.Extra;
        invoice.TaxAmount = 0; // giá bán đã gồm VAT
        invoice.TotalAmount = draft.Total;
        invoice.Notes = draft.Notes;
        foreach (var d in draft.Details) invoice.SalesInvoiceDetails.Add(d);
    }

    /// <summary>Giảm vượt mức chính sách giá (chỉ người có SYS09.DiscountOverLimit) → ghi riêng vào nhật ký.</summary>
    private Task WriteOverLimitAsync(SalesInvoice invoice, SaleDraft draft, string userId, CancellationToken ct)
        => draft.OverLimit
            ? _audit.WriteAsync("INVOICE_DISCOUNT_OVER_LIMIT", "SalesInvoices", invoice.InvoiceId.ToString(CultureInfo.InvariantCulture), null,
                new { invoice.InvoiceCode, invoice.SubTotal, Promotion = draft.PromoTotal, ExtraPercent = draft.ExtraPercent, Extra = draft.Extra, MaxDiscountPercent = draft.MaxPercent }, userId, ct)
            : Task.CompletedTask;

    private async Task<OperationResult<SaleDraft>> BuildDraftAsync(SaleFormViewModel form, bool canExceedDiscountLimit, RxContext? rx, CancellationToken ct)
    {
        if (rx is not null) form.CustomerId = rx.CustomerId;
        // ---- Dòng thuốc (3.1.1, 3.1.2) ----
        var lines = form.Lines.Where(l => l.MedicineId is not null || l.UnitId is not null || l.Quantity is not null).ToList();
        if (lines.Count == 0) return OperationResult<SaleDraft>.Fail("Thêm ít nhất một thuốc vào hóa đơn.");
        if (lines.Count > SaleFormViewModel.MaxLines)
            return OperationResult<SaleDraft>.Fail($"Một hóa đơn tối đa {SaleFormViewModel.MaxLines} dòng thuốc.");

        var options = (await LoadMedicinesAsync(rx, ct)).ToDictionary(m => m.MedicineId);
        var details = new List<SalesInvoiceDetail>();
        var requestedBase = new Dictionary<int, decimal>();
        for (var i = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            var label = $"Dòng {i + 1}";
            if (l.MedicineId is null) return OperationResult<SaleDraft>.Fail($"{label}: chọn thuốc.");
            if (!options.TryGetValue(l.MedicineId.Value, out var med))
            {
                var id = l.MedicineId.Value;
                var info = await _db.Medicines.AsNoTracking().Where(m => m.MedicineId == id)
                    .Select(m => new { m.MedicineName, m.IsPrescription, m.IsControlledDrug, m.IsActive }).FirstOrDefaultAsync(ct);
                if (info is null) return OperationResult<SaleDraft>.Fail($"{label}: không tìm thấy thuốc.");
                if (rx is not null)
                {
                    if (!info.IsActive) return OperationResult<SaleDraft>.Fail($"{label}: {info.MedicineName} đã ngừng kinh doanh.");
                    if (info.IsControlledDrug)
                        return OperationResult<SaleDraft>.Fail($"{label}: {info.MedicineName} là thuốc kiểm soát đặc biệt, chưa bán được trên hệ thống.");
                    return OperationResult<SaleDraft>.Fail($"{label}: {info.MedicineName} không có trong đơn thuốc {rx.Code}.");
                }
                if (info.IsPrescription)
                    return OperationResult<SaleDraft>.Fail($"{label}: {info.MedicineName} là thuốc kê đơn, chỉ bán theo đơn thuốc.");
                return OperationResult<SaleDraft>.Fail($"{label}: {info.MedicineName} đã ngừng kinh doanh.");
            }
            var name = med.Label;
            if (l.UnitId is null) return OperationResult<SaleDraft>.Fail($"{label} ({name}): chọn đơn vị bán.");
            var unit = med.Units.FirstOrDefault(u => u.UnitId == l.UnitId);
            if (unit is null) return OperationResult<SaleDraft>.Fail($"{label} ({name}): đơn vị bán không thuộc thuốc này.");
            if (l.Quantity is null || l.Quantity < 1 || l.Quantity > SaleFormViewModel.MaxQuantity)
                return OperationResult<SaleDraft>.Fail($"{label} ({name}): số lượng là số nguyên từ 1 đến {SaleFormViewModel.MaxQuantity.ToString("N0", CultureInfo.GetCultureInfo("vi-VN"))}.");
            if (unit.Price <= 0)
                return OperationResult<SaleDraft>.Fail($"{label} ({name}): {unit.UnitName} chưa có giá bán — báo quản lý cập nhật giá ở Danh mục thuốc.");

            var quantity = (decimal)l.Quantity.Value;
            var gross = quantity * unit.Price;
            var baseQty = Math.Round(quantity * unit.Factor, 3, MidpointRounding.AwayFromZero);
            var (discount, promotion) = BestPromotion(gross, baseQty, med.Promotions);
            requestedBase[med.MedicineId] = requestedBase.GetValueOrDefault(med.MedicineId) + baseQty;
            details.Add(new SalesInvoiceDetail
            {
                MedicineId = med.MedicineId,
                UnitId = unit.UnitId,
                Quantity = quantity,
                ConversionToBase = unit.Factor,
                UnitPrice = unit.Price,
                DiscountAmount = discount,
                LineTotal = gross - discount,
                PromotionId = promotion?.PromotionId
            });
        }

        // Tồn khả dụng (lô Khả dụng, còn hạn); lúc thanh toán FEFO kiểm tra lại
        foreach (var (medicineId, need) in requestedBase)
        {
            var med = options[medicineId];
            if (need > med.AvailableBase)
                return OperationResult<SaleDraft>.Fail(med.AvailableBase <= 0
                    ? $"{med.Label} đã hết hàng."
                    : $"{med.Label}: chỉ còn {Format.Number(med.AvailableBase)} {med.BaseUnitName}, hóa đơn cần {Format.Number(need)} {med.BaseUnitName}.");
            // 3.2.4: không bán quá số lượng bác sĩ kê (được bán ít hơn)
            if (rx is not null && rx.Items.TryGetValue(medicineId, out var item) && need > item.Base)
                return OperationResult<SaleDraft>.Fail($"{med.Label}: đơn kê {item.Text}, không bán quá số lượng kê.");
        }

        // ---- 3.3.1 Tổng tiền: khuyến mãi từng dòng + giảm thêm trong giới hạn chính sách giá ----
        var subTotal = details.Sum(d => d.Quantity * d.UnitPrice);
        var promoTotal = details.Sum(d => d.DiscountAmount);
        // Giảm thêm nhập theo % tiền hàng; tiền giảm làm tròn XUỐNG 100 đ (quy ước dữ liệu mẫu, mục 4.1 PROJECT_CONTEXT)
        var extraPercent = form.ExtraDiscountPercent ?? 0;
        const string percentField = nameof(SaleFormViewModel.ExtraDiscountPercent);
        if (extraPercent < 0 || extraPercent > 100)
            return OperationResult<SaleDraft>.Fail("Giảm thêm từ 0 đến 100%.", percentField);
        if (extraPercent * 10 != decimal.Truncate(extraPercent * 10))
            return OperationResult<SaleDraft>.Fail("Giảm thêm ghi tối đa 1 chữ số thập phân, ví dụ 5 hoặc 2,5.", percentField);
        var extra = ExtraDiscountAmount(subTotal, extraPercent);
        var policy = await ActivePolicyAsync(ct);
        var maxPercent = policy?.MaxPercent ?? 0;
        var allowedExtra = canExceedDiscountLimit ? subTotal - promoTotal : MaxExtraDiscount(subTotal, promoTotal, maxPercent);
        if (extra > allowedExtra)
        {
            var allowedPercent = subTotal <= 0 ? 0 : Math.Floor(allowedExtra * 1000m / subTotal) / 10m;
            if (canExceedDiscountLimit)
                return OperationResult<SaleDraft>.Fail($"Giảm tối đa {Format.Number(allowedPercent)}%.", percentField);
            return OperationResult<SaleDraft>.Fail(maxPercent == 0
                ? "Hiện không áp dụng giảm thêm."
                : allowedExtra == 0
                    ? $"Hóa đơn đã được giảm đủ {Format.Number(maxPercent)}%, không giảm thêm được."
                    : $"Vượt quá mức giảm tối đa {Format.Number(maxPercent)}%.",
                percentField);
        }
        var total = subTotal - promoTotal - extra;
        // Quy ước dữ liệu mẫu (mục 4.1): giảm của hóa đơn = Σ giảm của các dòng → chia tiền giảm thêm vào từng dòng,
        // để LineTotal là số khách thực trả cho dòng đó (đổi trả 3.4 hoàn theo dòng, báo cáo doanh thu theo thuốc)
        AllocateExtraDiscount(details, extra);
        // SYS-09: giảm vượt mức chính sách giá (chỉ người có SYS09.DiscountOverLimit) → ghi riêng vào nhật ký
        var overLimit = extra > 0 && promoTotal + extra > Math.Floor(subTotal * maxPercent / 100m);

        // ---- Khách hàng (không bắt buộc) ----
        if (form.CustomerId is not null)
        {
            var customerId = form.CustomerId.Value;
            var active = await _db.Customers.AsNoTracking().Where(c => c.CustomerId == customerId).Select(c => (bool?)c.IsActive).FirstOrDefaultAsync(ct);
            if (active is null) return OperationResult<SaleDraft>.Fail("Không tìm thấy khách hàng.", nameof(SaleFormViewModel.CustomerId));
            if (active == false) return OperationResult<SaleDraft>.Fail("Khách hàng đã ngừng theo dõi — chọn khách khác hoặc để Khách lẻ.", nameof(SaleFormViewModel.CustomerId));
        }

        // ---- 3.1.3 Cảnh báo an toàn: dược sĩ đã xem mới lưu ----
        // Bán theo đơn: đã kiểm tra và xác nhận khi lưu đơn (3.2.2.3)
        var warnings = rx is not null
            ? new List<SafetyWarning>()
            : await _safety.CheckAsync(form.CustomerId, requestedBase.Keys.ToList(), false, null, ct);
        if (warnings.Count > 0 && (!form.AcknowledgeWarnings || form.WarningsKey != SafetyCheckService.KeyOf(warnings)))
            return OperationResult<SaleDraft>.Fail($"Có {warnings.Count} cảnh báo an toàn. Xem cảnh báo, tích “Đã xem cảnh báo, vẫn bán” rồi lưu lại.",
                nameof(SaleFormViewModel.AcknowledgeWarnings));

        var notes = CleanText(form.Notes);
        if (extra > 0)
        {
            // CSDL không có cột lưu %: ghi kèm ghi chú để tra cứu (số tiền nằm ở DiscountAmount)
            var record = $"Giảm thêm {Format.Number(extraPercent)}% = {Format.Money(extra)}";
            notes = notes is null ? record : notes + "\n" + record;
        }
        if (warnings.Count > 0)
        {
            var record = WarningsNotePrefix + " " + string.Join(" | ", warnings.Select(w => w.Text));
            notes = notes is null ? record : notes + "\n" + record;
            if (notes.Length > NotesMax) notes = notes[..(NotesMax - 1)] + "…";
        }

        return OperationResult<SaleDraft>.Ok(new SaleDraft(details, subTotal, promoTotal, extraPercent, extra, total, maxPercent, overLimit, warnings.Count, notes));
    }

    // ================================================================== Danh sách, chi tiết

    public async Task<InvoiceListViewModel> GetListAsync(InvoiceFilter filter, string userId, bool viewAll, CancellationToken ct = default)
    {
        var scope = _db.SalesInvoices.AsNoTracking().AsQueryable();
        if (!viewAll)
        {
            var employeeId = await EmployeeIdAsync(userId, ct);
            scope = employeeId is null ? scope.Where(_ => false) : scope.Where(i => i.EmployeeId == employeeId);
        }
        var query = scope;

        byte? status = filter.Status switch
        {
            "pending" => (byte)InvoiceStatus.PendingPayment,
            "paid" => (byte)InvoiceStatus.Paid,
            "cancelled" => (byte)InvoiceStatus.Cancelled,
            _ => null
        };
        if (status is null) filter.Status = null;
        else query = query.Where(i => i.Status == status);

        var q = AccountHelpers.NormalizeFullName(filter.Q);
        filter.Q = q.Length == 0 ? null : q;
        if (filter.Q is not null)
        {
            var text = filter.Q;
            var folded = SearchText.Fold(text);
            var digits = new string(text.Where(char.IsAsciiDigit).ToArray());
            var byPhone = digits.Length >= 3 && digits.Length == text.Count(c => !char.IsWhiteSpace(c) && c != '.' && c != '-');
            query = query.Where(i => i.InvoiceCode.Contains(text)
                                     || (i.Customer != null
                                         && (EF.Functions.Collate(i.Customer.FullName.Replace("đ", "d").Replace("Đ", "D"), SearchText.Collation).Contains(folded)
                                             || (byPhone && i.Customer.Phone != null && i.Customer.Phone.Contains(digits)))));
        }

        var total = await query.CountAsync(ct);
        var pageCount = Math.Max(1, (int)Math.Ceiling(total / (double)InvoiceFilter.PageSize));
        filter.Page = Math.Clamp(filter.Page, 1, pageCount);

        var rows = await query
            .OrderByDescending(i => i.InvoiceDate).ThenByDescending(i => i.InvoiceId)
            .Skip((filter.Page - 1) * InvoiceFilter.PageSize).Take(InvoiceFilter.PageSize)
            .Select(i => new InvoiceRow(i.InvoiceId, i.InvoiceCode, i.InvoiceDate,
                i.Customer != null ? i.Customer.FullName : null, i.Employee.FullName,
                i.SalesInvoiceDetails.Count(), i.TotalAmount, i.Status, i.PrescriptionId != null))
            .ToListAsync(ct);

        return new InvoiceListViewModel
        {
            Filter = filter,
            Rows = rows,
            Total = total,
            GrandTotal = await scope.CountAsync(ct),
            PageCount = pageCount,
            ViewAll = viewAll
        };
    }

    public async Task<InvoiceDetailsViewModel?> GetDetailsAsync(int invoiceId, string userId, bool viewAll, CancellationToken ct = default)
    {
        var ownId = await EmployeeIdAsync(userId, ct);
        if (ownId is null && !viewAll) return null;

        var i = await _db.SalesInvoices.AsNoTracking()
            .Where(x => x.InvoiceId == invoiceId && (viewAll || x.EmployeeId == ownId))
            .Select(x => new
            {
                x.InvoiceId, x.InvoiceCode, x.Status, x.InvoiceDate, x.CustomerId, x.EmployeeId,
                CustomerName = x.Customer != null ? x.Customer.FullName : null,
                CustomerCode = x.Customer != null ? x.Customer.CustomerCode : null,
                CustomerPhone = x.Customer != null ? x.Customer.Phone : null,
                EmployeeName = x.Employee.FullName,
                x.Shift.ShiftCode, ShiftStatus = x.Shift.Status,
                PrescriptionCode = x.Prescription != null ? x.Prescription.PrescriptionCode : null,
                x.PrescriptionId,
                x.SubTotal, x.DiscountAmount, x.TaxAmount, x.TotalAmount, x.Notes,
                CancelledByName = x.CancelledByEmployee != null ? x.CancelledByEmployee.FullName : null,
                x.CancelledAt, x.CancelReason
            })
            .FirstOrDefaultAsync(ct);
        if (i is null) return null;

        var lines = await _db.SalesInvoiceDetails.AsNoTracking()
            .Where(d => d.InvoiceId == invoiceId)
            .OrderBy(d => d.InvoiceDetailId)
            .Select(d => new
            {
                d.InvoiceDetailId, d.Medicine.MedicineCode, d.Medicine.MedicineName, UnitName = d.Unit.UnitName, d.ConversionToBase,
                BaseUnitName = d.Medicine.BaseUnit.UnitName, d.Quantity, d.UnitPrice, d.DiscountAmount, d.LineTotal,
                PromotionName = d.Promotion != null ? d.Promotion.PromotionName : null
            })
            .ToListAsync(ct);

        var allocations = (await _db.SalesIssueAllocations.AsNoTracking()
                .Where(a => a.InvoiceDetail.InvoiceId == invoiceId)
                .OrderBy(a => a.Batch.ExpiryDate).ThenBy(a => a.AllocationId)
                .Select(a => new { a.InvoiceDetailId, a.Batch.BatchNumber, a.Batch.ExpiryDate, a.QuantityBase })
                .ToListAsync(ct))
            .GroupBy(a => a.InvoiceDetailId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<AllocationInfo>)g.Select(a => new AllocationInfo(a.BatchNumber, a.ExpiryDate, a.QuantityBase)).ToList());

        var payments = await _db.Payments.AsNoTracking()
            .Where(p => p.InvoiceId == invoiceId)
            .OrderBy(p => p.PaidAt).ThenBy(p => p.PaymentId)
            .Select(p => new PaymentInfo(p.PaymentMethod, p.Amount, p.TenderedAmount, p.TransactionReference, p.PaidAt, p.Status))
            .ToListAsync(ct);

        // Tách giảm giá để hiển thị: giảm thêm (đọc từ ghi chú), khuyến mãi = phần còn lại; hóa đơn lập trước khi chia giảm thêm vào dòng
        // (DiscountAmount > Σ giảm dòng) → phần chênh hiện riêng "Giảm thêm cho hóa đơn"
        var lineDiscount = lines.Sum(l => l.DiscountAmount);
        var unallocated = Math.Max(0, i.DiscountAmount - lineDiscount);
        string? extraPercent = null;
        decimal extraAmount = 0;
        var match = i.Notes is null ? null : ExtraNote.Match(i.Notes);
        if (match is { Success: true }
            && decimal.TryParse(match.Groups[2].Value.Replace(".", ""), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            && parsed <= i.DiscountAmount)
        {
            extraPercent = match.Groups[1].Value;
            extraAmount = parsed;
        }
        else if (unallocated > 0)
        {
            extraAmount = unallocated;
        }

        return new InvoiceDetailsViewModel
        {
            ExtraDiscountPercent = extraPercent,
            ExtraDiscountAmount = extraAmount,
            PromotionDiscount = i.DiscountAmount - extraAmount,
            UnallocatedDiscount = unallocated,
            InvoiceId = i.InvoiceId,
            Code = i.InvoiceCode,
            Status = i.Status,
            InvoiceDate = i.InvoiceDate,
            CustomerId = i.CustomerId,
            CustomerName = i.CustomerName,
            CustomerCode = i.CustomerCode,
            CustomerPhone = ContactRules.DisplayPhone(i.CustomerPhone),
            EmployeeName = i.EmployeeName,
            ShiftCode = i.ShiftCode,
            PrescriptionCode = i.PrescriptionCode,
            PrescriptionId = i.PrescriptionId,
            SubTotal = i.SubTotal,
            DiscountAmount = i.DiscountAmount,
            TaxAmount = i.TaxAmount,
            TotalAmount = i.TotalAmount,
            Notes = i.Notes,
            CancelledByName = i.CancelledByName,
            CancelledAt = i.CancelledAt,
            CancelReason = i.CancelReason,
            IsOwn = ownId == i.EmployeeId,
            ShiftOpen = i.ShiftStatus == (byte)ShiftStatus.Open,
            Lines = lines.Select(l => new InvoiceLineInfo(l.MedicineCode, l.MedicineName, l.UnitName, l.ConversionToBase, l.BaseUnitName,
                l.Quantity, l.UnitPrice, l.DiscountAmount, l.LineTotal, l.PromotionName,
                allocations.TryGetValue(l.InvoiceDetailId, out var batches) ? batches : Array.Empty<AllocationInfo>())).ToList(),
            Payments = payments
        };
    }

    // ================================================================== Hủy hóa đơn chờ thanh toán (G-10, SYS-09)

    public async Task<OperationResult> CancelAsync(int invoiceId, string? reason, string userId, bool hasCancelPermission, CancellationToken ct = default)
    {
        if (!hasCancelPermission)
            return OperationResult.Fail("Bạn không có quyền hủy hóa đơn. Nhờ chủ nhà thuốc hủy.");
        var why = AccountHelpers.NormalizeFullName(reason);
        if (why.Length == 0) return OperationResult.Fail("Nhập lý do hủy hóa đơn.");
        if (why.Length > 500) return OperationResult.Fail("Lý do hủy tối đa 500 ký tự.");

        var employeeId = await _db.Employees.AsNoTracking().Where(e => e.UserId == userId && e.IsActive)
            .Select(e => (int?)e.EmployeeId).FirstOrDefaultAsync(ct);
        if (employeeId is null) return OperationResult.Fail("Tài khoản chưa gắn hồ sơ nhân viên đang làm việc nên chưa hủy được hóa đơn.");

        var current = await _db.SalesInvoices.AsNoTracking().Where(i => i.InvoiceId == invoiceId)
            .Select(i => new { i.InvoiceCode, i.Status, i.EmployeeId }).FirstOrDefaultAsync(ct);
        if (current is null) return OperationResult.Fail("Không tìm thấy hóa đơn.");
        if (current.Status == (byte)InvoiceStatus.Paid)
            return OperationResult.Fail($"Hóa đơn {current.InvoiceCode} đã thanh toán nên không hủy được — xử lý ở mục Đổi trả.");
        if (current.Status != (byte)InvoiceStatus.PendingPayment)
            return OperationResult.Fail($"Hóa đơn {current.InvoiceCode} đã hủy trước đó.");

        var now = DateTime.Now;
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var updated = await _db.SalesInvoices
            .Where(i => i.InvoiceId == invoiceId && i.Status == (byte)InvoiceStatus.PendingPayment)
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.Status, (byte)InvoiceStatus.Cancelled)
                .SetProperty(i => i.CancelledByEmployeeId, employeeId)
                .SetProperty(i => i.CancelledAt, now)
                .SetProperty(i => i.CancelReason, why), ct);
        if (updated == 0) return OperationResult.Fail("Hóa đơn vừa được thanh toán hoặc hủy ở nơi khác. Tải lại trang.");
        await _audit.WriteAsync("INVOICE_CANCEL", "SalesInvoices", invoiceId.ToString(CultureInfo.InvariantCulture),
            new { Status = (byte)InvoiceStatus.PendingPayment },
            new { Status = (byte)InvoiceStatus.Cancelled, current.InvoiceCode, Reason = why, CancelledByEmployeeId = employeeId }, userId, ct);
        await tx.CommitAsync(ct);
        return OperationResult.Ok($"Đã hủy hóa đơn {current.InvoiceCode}.");
    }

    // ================================================================== Thêm nhanh khách tại quầy

    public async Task<SaleCustomerOption?> GetCustomerOptionAsync(int customerId, CancellationToken ct = default)
        => (await LoadCustomersAsync(ct)).FirstOrDefault(c => c.CustomerId == customerId);

    public Task<int?> GetPrescriptionIdAsync(int invoiceId, CancellationToken ct = default)
        => _db.SalesInvoices.AsNoTracking().Where(x => x.InvoiceId == invoiceId).Select(x => x.PrescriptionId).FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<SaleCustomerOption>> GetCustomerOptionsAsync(CancellationToken ct = default)
        => await LoadCustomersAsync(ct);

    // ================================================================== Đơn thuốc đang bán (3.2.4)

    private sealed record RxItem(decimal Base, string Text);

    private sealed record RxContext(
        int PrescriptionId, string Code, byte Status, int CustomerId, string CustomerName, string DoctorName, string ClinicName, DateOnly Date,
        IReadOnlyDictionary<int, RxItem> Items);

    /// <summary>Đơn thuốc + số lượng kê theo đơn vị cơ sở của từng thuốc; null nếu không có.</summary>
    private async Task<RxContext?> RxAsync(int prescriptionId, CancellationToken ct)
    {
        var p = await _db.Prescriptions.AsNoTracking().Where(x => x.PrescriptionId == prescriptionId)
            .Select(x => new { x.PrescriptionId, x.PrescriptionCode, x.Status, x.CustomerId, CustomerName = x.Customer.FullName, x.DoctorName, x.ClinicName, x.PrescriptionDate })
            .FirstOrDefaultAsync(ct);
        if (p is null) return null;
        var lines = await _db.PrescriptionDetails.AsNoTracking()
            .Where(d => d.PrescriptionId == prescriptionId)
            .Select(d => new
            {
                d.MedicineId, d.PrescribedQuantity, d.UnitId, UnitName = d.Unit.UnitName, d.Medicine.BaseUnitId, BaseUnitName = d.Medicine.BaseUnit.UnitName,
                Factor = d.UnitId == d.Medicine.BaseUnitId
                    ? 1m
                    : d.Medicine.MedicineUnitConversions.Where(c => c.UnitId == d.UnitId).Select(c => (decimal?)c.ConversionToBase).FirstOrDefault() ?? 1m
            })
            .ToListAsync(ct);
        var items = lines.GroupBy(l => l.MedicineId).ToDictionary(g => g.Key, g =>
        {
            var baseQty = g.Sum(l => Math.Round(l.PrescribedQuantity * l.Factor, 3, MidpointRounding.AwayFromZero));
            var text = string.Join(" + ", g.Select(l => $"{Format.Number(l.PrescribedQuantity)} {l.UnitName}"));
            if (g.Any(l => l.UnitId != l.BaseUnitId)) text += $" (= {Format.Number(baseQty)} {g.First().BaseUnitName})";
            return new RxItem(baseQty, text);
        });
        return new RxContext(p.PrescriptionId, p.PrescriptionCode, p.Status, p.CustomerId, p.CustomerName, p.DoctorName, p.ClinicName, p.PrescriptionDate, items);
    }

    /// <summary>null = bán được: đơn Chưa bán, còn hạn mua, chưa có hóa đơn chờ thanh toán khác.</summary>
    private async Task<string?> RxErrorAsync(RxContext rx, int? exceptInvoiceId, CancellationToken ct)
    {
        if (rx.Status != (byte)PrescriptionStatus.Saved)
            return $"Đơn thuốc {rx.Code} {PrescriptionStatusText.Of(rx.Status).ToLowerInvariant()} nên không bán được.";
        var validUntil = rx.Date.AddDays(PrescriptionRules.ValidDays);
        if (Today > validUntil)
            return $"Đơn thuốc {rx.Code} đã hết hạn mua (đến {validUntil.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}).";
        var id = rx.PrescriptionId;
        var pending = await _db.SalesInvoices.AsNoTracking()
            .Where(i => i.PrescriptionId == id && i.Status == (byte)InvoiceStatus.PendingPayment && (exceptInvoiceId == null || i.InvoiceId != exceptInvoiceId))
            .Select(i => i.InvoiceCode).FirstOrDefaultAsync(ct);
        return pending is null ? null : $"Đơn thuốc {rx.Code} đã có hóa đơn {pending} chờ thanh toán.";
    }

    // ================================================================== Dữ liệu chọn

    /// <summary>
    /// Thuốc bán được: bán lẻ — đang kinh doanh, KHÔNG kê đơn; bán theo đơn — chỉ thuốc trong đơn (kể cả thuốc kê đơn),
    /// trừ thuốc kiểm soát đặc biệt. Kèm đơn vị + giá hiện hành, tồn khả dụng, khuyến mãi đang hiệu lực.
    /// </summary>
    private async Task<List<SaleMedicineOption>> LoadMedicinesAsync(RxContext? rx, CancellationToken ct)
    {
        var today = Today;
        var now = DateTime.Now;
        var rxIds = rx?.Items.Keys.ToList() ?? new List<int>();
        var isRx = rx is not null;
        var medicineQuery = _db.Medicines.AsNoTracking().Where(m => m.IsActive);
        medicineQuery = isRx
            ? medicineQuery.Where(m => rxIds.Contains(m.MedicineId) && !m.IsControlledDrug)
            : medicineQuery.Where(m => !m.IsPrescription);
        var conversionQuery = _db.MedicineUnitConversions.AsNoTracking().Where(c => c.Medicine.IsActive);
        conversionQuery = isRx
            ? conversionQuery.Where(c => rxIds.Contains(c.MedicineId) && !c.Medicine.IsControlledDrug)
            : conversionQuery.Where(c => !c.Medicine.IsPrescription);
        var medicines = await medicineQuery
            .OrderBy(m => m.MedicineName).ThenBy(m => m.MedicineCode)
            .Select(m => new { m.MedicineId, m.MedicineCode, m.MedicineName, m.BaseUnitId, BaseUnitName = m.BaseUnit.UnitName, m.DefaultSellingPrice, m.CategoryId })
            .ToListAsync(ct);

        var conversions = (await conversionQuery
                .Select(c => new { c.MedicineId, c.UnitId, c.Unit.UnitName, c.ConversionToBase, c.SellingPrice, c.IsDefaultSaleUnit })
                .ToListAsync(ct))
            .GroupBy(c => c.MedicineId)
            .ToDictionary(g => g.Key, g => g.OrderBy(c => c.ConversionToBase).ToList());

        // 3.1.1 tồn khả dụng: lô Khả dụng, còn hạn (SYS-10)
        var available = await _db.MedicineBatches.AsNoTracking()
            .Where(b => b.Status == (byte)BatchStatus.Available && b.ExpiryDate > today && b.QuantityOnHand > 0)
            .GroupBy(b => b.MedicineId)
            .Select(g => new { MedicineId = g.Key, Quantity = g.Sum(b => b.QuantityOnHand) })
            .ToDictionaryAsync(x => x.MedicineId, x => x.Quantity, ct);

        var keywords = (await _db.MedicineIngredients.AsNoTracking()
                .Select(i => new { i.MedicineId, i.Ingredient.IngredientName })
                .ToListAsync(ct))
            .GroupBy(i => i.MedicineId)
            .ToDictionary(g => g.Key, g => string.Join(", ", g.Select(i => i.IngredientName).OrderBy(n => n)));

        // Khuyến mãi đang hiệu lực (1.5.4 → 3.3.1): theo thuốc, theo nhóm, hoặc toàn bộ
        var promotions = await _db.Promotions.AsNoTracking()
            .Where(p => p.IsActive && p.EffectiveFrom <= now && p.EffectiveTo >= now)
            .OrderBy(p => p.PromotionId)
            .Select(p => new { p.PromotionId, p.PromotionName, p.MedicineId, p.CategoryId, p.DiscountPercent, p.DiscountAmount })
            .ToListAsync(ct);

        var vi = CultureInfo.GetCultureInfo("vi-VN");
        return medicines.Select(m =>
        {
            var units = new List<SaleUnitOption>();
            conversions.TryGetValue(m.MedicineId, out var convs);
            var hasDefault = convs?.Any(c => c.IsDefaultSaleUnit && c.UnitId != m.BaseUnitId) == true;
            units.Add(new SaleUnitOption(m.BaseUnitId, m.BaseUnitName, 1m, RoundPrice(m.DefaultSellingPrice), !hasDefault));
            if (convs is not null)
                units.AddRange(convs.Where(c => c.UnitId != m.BaseUnitId).Select(c => new SaleUnitOption(
                    c.UnitId, c.UnitName, c.ConversionToBase,
                    RoundPrice(c.SellingPrice ?? m.DefaultSellingPrice * c.ConversionToBase), // DB-13
                    c.IsDefaultSaleUnit)));
            var stock = available.GetValueOrDefault(m.MedicineId);
            var promos = promotions
                .Where(p => p.MedicineId == m.MedicineId || p.CategoryId == m.CategoryId || (p.MedicineId == null && p.CategoryId == null))
                .Select(p => new SalePromotionOption(p.PromotionId, p.PromotionName, p.DiscountPercent, p.DiscountAmount))
                .ToList();
            var label = $"{m.MedicineName} ({m.MedicineCode}) · " + (stock > 0 ? $"còn {stock.ToString("#,##0.###", vi)} {m.BaseUnitName}" : "hết hàng");
            RxItem? item = null;
            rx?.Items.TryGetValue(m.MedicineId, out item);
            return new SaleMedicineOption(m.MedicineId, label, m.BaseUnitName, stock, units, promos,
                keywords.GetValueOrDefault(m.MedicineId, string.Empty), item?.Base, item?.Text);
        }).ToList();
    }

    private async Task<List<SaleCustomerOption>> LoadCustomersAsync(CancellationToken ct)
    {
        var customers = await _db.Customers.AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.FullName).ThenBy(c => c.CustomerCode)
            .Select(c => new { c.CustomerId, c.CustomerCode, c.FullName, c.Phone })
            .ToListAsync(ct);
        var allergies = (await _db.CustomerAllergies.AsNoTracking()
                .Where(a => a.Customer.IsActive)
                .Select(a => new { a.CustomerId, a.Ingredient.IngredientName })
                .ToListAsync(ct))
            .GroupBy(a => a.CustomerId)
            .ToDictionary(g => g.Key, g => string.Join(", ", g.Select(a => a.IngredientName).Distinct().OrderBy(n => n)));
        return customers.Select(c => new SaleCustomerOption(
            c.CustomerId,
            c.FullName + (c.Phone is null ? "" : " · " + ContactRules.DisplayPhone(c.Phone)) + " (" + c.CustomerCode + ")",
            c.Phone ?? string.Empty,
            allergies.GetValueOrDefault(c.CustomerId))).ToList();
    }

    // ================================================================== Tính tiền (3.3.1) — js/pos.js tính tạm theo đúng quy tắc này

    /// <summary>
    /// Khuyến mãi có lợi nhất cho khách trong các khuyến mãi áp dụng được: % → làm tròn xuống 100 đ (như dữ liệu mẫu);
    /// số tiền → giảm mỗi đơn vị cơ sở × số đơn vị cơ sở. Không giảm quá tiền hàng của dòng.
    /// </summary>
    private static (decimal Discount, SalePromotionOption? Promotion) BestPromotion(decimal gross, decimal baseQuantity, IReadOnlyList<SalePromotionOption> promotions)
    {
        decimal best = 0;
        SalePromotionOption? chosen = null;
        foreach (var p in promotions)
        {
            decimal d;
            if (p.Percent is not null) d = Math.Floor(gross * p.Percent.Value / 100m / 100m) * 100m;
            else if (p.AmountPerBase is not null) d = Math.Round(p.AmountPerBase.Value * baseQuantity, 0, MidpointRounding.AwayFromZero);
            else continue;
            d = Math.Min(Math.Max(d, 0), gross);
            if (d > best)
            {
                best = d;
                chosen = p;
            }
        }
        return (best, chosen);
    }

    /// <summary>
    /// Chia <paramref name="extra"/> vào các dòng theo tỷ lệ tiền còn lại của dòng (sau khuyến mãi), làm tròn xuống đồng;
    /// phần lẻ dồn vào dòng có tiền lớn nhất. Không dòng nào âm (giảm thêm đã được giới hạn ≤ tiền hàng còn lại).
    /// </summary>
    private static void AllocateExtraDiscount(List<SalesInvoiceDetail> details, decimal extra)
    {
        if (extra <= 0 || details.Count == 0) return;
        var nets = details.Select(d => d.Quantity * d.UnitPrice - d.DiscountAmount).ToArray();
        var netTotal = nets.Sum();
        if (netTotal <= 0) return;
        var shares = nets.Select(n => Math.Floor(extra * n / netTotal)).ToArray();
        var remainder = extra - shares.Sum();
        foreach (var i in Enumerable.Range(0, details.Count).OrderByDescending(k => nets[k]))
        {
            if (remainder <= 0) break;
            var add = Math.Min(remainder, nets[i] - shares[i]);
            shares[i] += add;
            remainder -= add;
        }
        for (var i = 0; i < details.Count; i++)
        {
            var d = details[i];
            d.DiscountAmount += shares[i];
            d.LineTotal = d.Quantity * d.UnitPrice - d.DiscountAmount;
        }
    }

    /// <summary>Ghi chú "Giảm thêm 10% = 2.000 đ" do CreateAsync ghi (CSDL không có cột % giảm thêm).</summary>
    private const string WarningsNotePrefix = "Đã xem cảnh báo an toàn:";

    private static readonly Regex ExtraNote = new(@"Giảm thêm ([0-9]+(?:,[0-9])?)% = ([0-9.]+) đ", RegexOptions.CultureInvariant);

    /// <summary>Tiền giảm thêm = % × tiền hàng, làm tròn XUỐNG 100 đ. js/pos.js tính tạm theo đúng công thức này.</summary>
    private static decimal ExtraDiscountAmount(decimal subTotal, decimal percent)
        => percent <= 0 ? 0 : Math.Floor(subTotal * percent / 100m / 100m) * 100m;

    /// <summary>Giảm thêm tối đa = (MaxDiscountPercent × tiền hàng, làm tròn xuống đồng) − khuyến mãi đã giảm; không âm.</summary>
    private static decimal MaxExtraDiscount(decimal subTotal, decimal promotionTotal, decimal maxPercent)
        => Math.Max(0, Math.Floor(subTotal * maxPercent / 100m) - promotionTotal);

    private static decimal RoundPrice(decimal price) => Math.Round(price, 0, MidpointRounding.AwayFromZero);

    // ================================================================== Dùng chung

    private sealed record SellerInfo(int? EmployeeId, int? ShiftId, string? ShiftCode);

    /// <summary>Nhân viên đang làm việc + ca ĐANG MỞ (chưa bàn giao) của người dùng — cùng điều kiện IShiftService.GetOpenShiftIdAsync.</summary>
    private async Task<SellerInfo> SellerAsync(string userId, CancellationToken ct)
    {
        var employeeId = await _db.Employees.AsNoTracking().Where(e => e.UserId == userId && e.IsActive)
            .Select(e => (int?)e.EmployeeId).FirstOrDefaultAsync(ct);
        if (employeeId is null) return new SellerInfo(null, null, null);
        var shift = await _db.Shifts.AsNoTracking()
            .Where(s => s.EmployeeId == employeeId && s.ClosedAt == null && s.Status == (byte)ShiftStatus.Open)
            .Select(s => new { s.ShiftId, s.ShiftCode })
            .FirstOrDefaultAsync(ct);
        return new SellerInfo(employeeId, shift?.ShiftId, shift?.ShiftCode);
    }

    private Task<int?> EmployeeIdAsync(string userId, CancellationToken ct)
        => _db.Employees.AsNoTracking().Where(e => e.UserId == userId).Select(e => (int?)e.EmployeeId).FirstOrDefaultAsync(ct);

    private sealed record PolicyInfo(string Name, decimal MaxPercent);

    /// <summary>Chính sách giá đang hiệu lực (1.5.4) — bản mới nhất nếu có nhiều.</summary>
    private async Task<PolicyInfo?> ActivePolicyAsync(CancellationToken ct)
    {
        var today = Today;
        return await _db.PricePolicies.AsNoTracking()
            .Where(p => p.IsActive && (p.EffectiveFrom == null || p.EffectiveFrom <= today) && (p.EffectiveTo == null || p.EffectiveTo >= today))
            .OrderByDescending(p => p.PricePolicyId)
            .Select(p => new PolicyInfo(p.PolicyName, p.MaxDiscountPercent))
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>HD + yyMMdd + 3 số nối tiếp trong ngày (giống dữ liệu mẫu HD260501001).</summary>
    private async Task<string> NextCodeAsync(DateTime at, CancellationToken ct)
    {
        var prefix = CodePrefix + at.ToString("yyMMdd", CultureInfo.InvariantCulture);
        var codes = await _db.SalesInvoices.AsNoTracking().Where(i => i.InvoiceCode.StartsWith(prefix))
            .Select(i => i.InvoiceCode).ToListAsync(ct);
        var max = 0;
        foreach (var code in codes)
            if (int.TryParse(code.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n > max) max = n;
        return prefix + (max + 1).ToString("D3", CultureInfo.InvariantCulture);
    }

    private static string? CleanText(string? value)
    {
        var text = value?.Trim();
        return string.IsNullOrEmpty(text) ? null : text.Replace("\r\n", "\n");
    }
}
