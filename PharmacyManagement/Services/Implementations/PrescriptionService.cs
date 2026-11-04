using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PharmacyManagement.Data;
using PharmacyManagement.Helpers;
using PharmacyManagement.Models.Entities;
using PharmacyManagement.Models.Enums;
using PharmacyManagement.Models.ViewModels.Catalog;
using PharmacyManagement.Models.ViewModels.Sales;
using PharmacyManagement.Services.Interfaces;

namespace PharmacyManagement.Services.Implementations;

public sealed class PrescriptionService : IPrescriptionService
{
    private const string CodePrefix = "DT";
    private const int NotesMax = 1000;
    private const int MaxQuantity = 10_000;
    private const string WarningsNotePrefix = "Đã xem cảnh báo an toàn:";

    private readonly PharmacyDbContext _db;
    private readonly IAuditLogService _audit;
    private readonly ISafetyCheckService _safety;
    private readonly ISalesInvoiceService _sales;

    public PrescriptionService(PharmacyDbContext db, IAuditLogService audit, ISafetyCheckService safety, ISalesInvoiceService sales)
    {
        _db = db;
        _audit = audit;
        _safety = safety;
        _sales = sales;
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    // ================================================================== Form

    public async Task<PrescriptionFormViewModel> NewFormAsync(CancellationToken ct = default)
    {
        var form = new PrescriptionFormViewModel { PrescriptionDate = Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };
        form.Lines.Add(new PrescriptionLineForm());
        await PrepareFormAsync(form, ct);
        return form;
    }

    public async Task PrepareFormAsync(PrescriptionFormViewModel form, CancellationToken ct = default)
    {
        form.MedicineOptions = await LoadMedicinesAsync(ct);
        form.CustomerOptions = await _sales.GetCustomerOptionsAsync(ct);
        await LoadSuggestionsAsync(form, ct);
        form.Today = Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        form.MinDate = Today.AddDays(-PrescriptionRules.ValidDays).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (form.Lines.Count == 0) form.Lines.Add(new PrescriptionLineForm());

        var warnings = await WarningsAsync(form.CustomerId, form.Diagnosis,
            form.Lines.Where(l => l.MedicineId is not null).Select(l => (l.MedicineId!.Value, l.DosePerTime, l.TimesPerDay)).ToList(), ct);
        form.Warnings = warnings;
        form.ShownWarningsKey = warnings.Count == 0 ? null : SafetyCheckService.KeyOf(warnings);
        form.AcknowledgeWarnings = false;
    }

    // ================================================================== 3.2.2.1 Kiểm tra hợp lệ / 3.2.3 Lưu đơn

    public async Task<OperationResult> CheckAsync(PrescriptionFormViewModel form, CancellationToken ct = default)
    {
        var clean = await ValidateAsync(form, ct);
        return clean.Error is not null ? OperationResult.Fail(clean.Error, clean.Field) : OperationResult.Ok();
    }

    public async Task<OperationResult<int>> CreateAsync(PrescriptionFormViewModel form, string userId, CancellationToken ct = default)
    {
        var employeeId = await _db.Employees.AsNoTracking().Where(e => e.UserId == userId && e.IsActive)
            .Select(e => (int?)e.EmployeeId).FirstOrDefaultAsync(ct);
        if (employeeId is null) return OperationResult<int>.Fail("Tài khoản chưa gắn hồ sơ nhân viên đang làm việc nên chưa lưu được đơn thuốc.");

        var clean = await ValidateAsync(form, ct);
        if (clean.Error is not null) return OperationResult<int>.Fail(clean.Error, clean.Field);

        // 3.2.2.2 + 3.2.2.3: chỉ cảnh báo — dược sĩ đã xem mới lưu
        var warnings = await WarningsAsync(clean.CustomerId, clean.Diagnosis,
            clean.Lines.Select(l => (l.MedicineId, l.DosePerTime, l.TimesPerDay is null ? (int?)null : l.TimesPerDay.Value)).ToList(), ct);
        if (warnings.Count > 0 && (!form.AcknowledgeWarnings || form.WarningsKey != SafetyCheckService.KeyOf(warnings)))
            return OperationResult<int>.Fail($"Có {warnings.Count} cảnh báo. Xem cảnh báo, tích “Đã xem cảnh báo, vẫn bán” rồi lưu lại.",
                nameof(PrescriptionFormViewModel.AcknowledgeWarnings));

        var notes = Clean(form.Notes);
        if (warnings.Count > 0)
        {
            var record = WarningsNotePrefix + " " + string.Join(" | ", warnings.Select(w => w.Text));
            notes = notes is null ? record : notes + "\n" + record;
            if (notes.Length > NotesMax) notes = notes[..(NotesMax - 1)] + "…";
        }

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var prescription = new Prescription
            {
                PrescriptionCode = await NextCodeAsync(DateTime.Now, ct),
                CustomerId = clean.CustomerId,
                DoctorName = clean.DoctorName,
                ClinicName = clean.ClinicName,
                Diagnosis = clean.Diagnosis,
                PrescriptionDate = clean.Date,
                CheckedByEmployeeId = employeeId.Value,
                Status = (byte)PrescriptionStatus.Saved,
                Notes = notes
            };
            foreach (var l in clean.Lines) prescription.PrescriptionDetails.Add(l);
            _db.Prescriptions.Add(prescription);
            await _db.SaveChangesAsync(ct);
            await _audit.WriteAsync("PRESCRIPTION_CREATE", "Prescriptions", prescription.PrescriptionId.ToString(CultureInfo.InvariantCulture), null,
                new
                {
                    prescription.PrescriptionCode, prescription.CustomerId, prescription.DoctorName, prescription.ClinicName,
                    prescription.PrescriptionDate, Lines = clean.Lines.Count, Warnings = warnings.Count
                }, userId, ct);
            await tx.CommitAsync(ct);
            return OperationResult<int>.Ok(prescription.PrescriptionId, $"Đã lưu đơn thuốc {prescription.PrescriptionCode}.");
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            return OperationResult<int>.Fail("Chưa lưu được. Bấm Lưu lại.");
        }
    }

    // ================================================================== Danh sách, chi tiết

    public async Task<PrescriptionListViewModel> GetListAsync(PrescriptionFilter filter, CancellationToken ct = default)
    {
        var query = _db.Prescriptions.AsNoTracking().AsQueryable();
        byte? status = filter.Status switch
        {
            "saved" => (byte)PrescriptionStatus.Saved,
            "dispensed" => (byte)PrescriptionStatus.Dispensed,
            "cancelled" => (byte)PrescriptionStatus.Cancelled,
            _ => null
        };
        if (status is null) filter.Status = null;
        else query = query.Where(p => p.Status == status);

        var q = AccountHelpers.NormalizeFullName(filter.Q);
        filter.Q = q.Length == 0 ? null : q;
        if (filter.Q is not null)
        {
            var text = filter.Q;
            var folded = SearchText.Fold(text);
            var digits = new string(text.Where(char.IsAsciiDigit).ToArray());
            var byPhone = digits.Length >= 3 && digits.Length == text.Count(c => !char.IsWhiteSpace(c) && c != '.' && c != '-');
            query = query.Where(p => p.PrescriptionCode.Contains(text)
                                     || EF.Functions.Collate(p.Customer.FullName.Replace("đ", "d").Replace("Đ", "D"), SearchText.Collation).Contains(folded)
                                     || EF.Functions.Collate(p.DoctorName.Replace("đ", "d").Replace("Đ", "D"), SearchText.Collation).Contains(folded)
                                     || (byPhone && p.Customer.Phone != null && p.Customer.Phone.Contains(digits)));
        }

        var total = await query.CountAsync(ct);
        var pageCount = Math.Max(1, (int)Math.Ceiling(total / (double)PrescriptionFilter.PageSize));
        filter.Page = Math.Clamp(filter.Page, 1, pageCount);
        var oldest = Today.AddDays(-PrescriptionRules.ValidDays);

        var rows = await query
            .OrderByDescending(p => p.PrescriptionId)
            .Skip((filter.Page - 1) * PrescriptionFilter.PageSize).Take(PrescriptionFilter.PageSize)
            .Select(p => new PrescriptionRow(p.PrescriptionId, p.PrescriptionCode, p.PrescriptionDate, p.Customer.FullName, p.DoctorName, p.ClinicName,
                p.PrescriptionDetails.Count(), p.Status, p.PrescriptionDate < oldest))
            .ToListAsync(ct);

        return new PrescriptionListViewModel
        {
            Filter = filter,
            Rows = rows,
            Total = total,
            GrandTotal = await _db.Prescriptions.CountAsync(ct),
            PageCount = pageCount
        };
    }

    public async Task<PrescriptionDetailsViewModel?> GetDetailsAsync(int prescriptionId, CancellationToken ct = default)
    {
        var p = await _db.Prescriptions.AsNoTracking()
            .Where(x => x.PrescriptionId == prescriptionId)
            .Select(x => new
            {
                x.PrescriptionId, x.PrescriptionCode, x.Status, x.CustomerId, CustomerName = x.Customer.FullName, x.Customer.CustomerCode,
                x.Customer.Phone, x.DoctorName, x.ClinicName, x.Diagnosis, x.PrescriptionDate, CheckedBy = x.CheckedByEmployee.FullName, x.Notes
            })
            .FirstOrDefaultAsync(ct);
        if (p is null) return null;

        var lines = await _db.PrescriptionDetails.AsNoTracking()
            .Where(d => d.PrescriptionId == prescriptionId)
            .OrderBy(d => d.PrescriptionDetailId)
            .Select(d => new PrescriptionLineInfo(d.Medicine.MedicineCode, d.Medicine.MedicineName, d.Medicine.IsPrescription, d.Unit.UnitName,
                d.PrescribedQuantity, d.Dosage, d.Frequency, d.DurationDays, d.Instructions))
            .ToListAsync(ct);

        var invoices = await _db.SalesInvoices.AsNoTracking()
            .Where(i => i.PrescriptionId == prescriptionId)
            .OrderByDescending(i => i.InvoiceDate)
            .Select(i => new PrescriptionInvoiceInfo(i.InvoiceId, i.InvoiceCode, i.InvoiceDate, i.TotalAmount, i.Status))
            .ToListAsync(ct);

        var validUntil = p.PrescriptionDate.AddDays(PrescriptionRules.ValidDays);
        return new PrescriptionDetailsViewModel
        {
            PrescriptionId = p.PrescriptionId,
            Code = p.PrescriptionCode,
            Status = p.Status,
            CustomerId = p.CustomerId,
            CustomerName = p.CustomerName,
            CustomerCode = p.CustomerCode,
            CustomerPhone = ContactRules.DisplayPhone(p.Phone),
            DoctorName = p.DoctorName,
            ClinicName = p.ClinicName,
            Diagnosis = p.Diagnosis,
            PrescriptionDate = p.PrescriptionDate,
            ValidUntil = validUntil,
            Expired = Today > validUntil,
            CheckedByName = p.CheckedBy,
            Notes = p.Notes,
            Lines = lines,
            Invoices = invoices
        };
    }

    // ================================================================== Hủy đơn chưa bán

    public async Task<OperationResult> CancelAsync(int prescriptionId, string? reason, string userId, CancellationToken ct = default)
    {
        var why = AccountHelpers.NormalizeFullName(reason);
        if (why.Length == 0) return OperationResult.Fail("Nhập lý do hủy đơn.");
        if (why.Length > 300) return OperationResult.Fail("Lý do hủy tối đa 300 ký tự.");

        var current = await _db.Prescriptions.AsNoTracking().Where(p => p.PrescriptionId == prescriptionId)
            .Select(p => new { p.PrescriptionCode, p.Status, p.Notes }).FirstOrDefaultAsync(ct);
        if (current is null) return OperationResult.Fail("Không tìm thấy đơn thuốc.");
        if (current.Status != (byte)PrescriptionStatus.Saved)
            return OperationResult.Fail($"Đơn {current.PrescriptionCode} {PrescriptionStatusText.Of(current.Status).ToLowerInvariant()} nên không hủy được.");
        var pending = await _db.SalesInvoices.AsNoTracking()
            .Where(i => i.PrescriptionId == prescriptionId && i.Status == (byte)InvoiceStatus.PendingPayment)
            .Select(i => i.InvoiceCode).FirstOrDefaultAsync(ct);
        if (pending is not null)
            return OperationResult.Fail($"Đơn đang có hóa đơn {pending} chờ thanh toán. Thu tiền hoặc hủy hóa đơn đó trước.");

        var note = "Hủy đơn: " + why;
        var notes = string.IsNullOrEmpty(current.Notes) ? note : current.Notes + "\n" + note;
        if (notes.Length > NotesMax) notes = notes[..NotesMax];

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var updated = await _db.Prescriptions
            .Where(p => p.PrescriptionId == prescriptionId && p.Status == (byte)PrescriptionStatus.Saved)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, (byte)PrescriptionStatus.Cancelled).SetProperty(p => p.Notes, notes), ct);
        if (updated == 0) return OperationResult.Fail("Đơn vừa được bán hoặc hủy ở nơi khác. Tải lại trang.");
        await _audit.WriteAsync("PRESCRIPTION_CANCEL", "Prescriptions", prescriptionId.ToString(CultureInfo.InvariantCulture),
            new { Status = (byte)PrescriptionStatus.Saved }, new { Status = (byte)PrescriptionStatus.Cancelled, Reason = why }, userId, ct);
        await tx.CommitAsync(ct);
        return OperationResult.Ok($"Đã hủy đơn thuốc {current.PrescriptionCode}.");
    }

    // ================================================================== Kiểm tra form (3.2.2.1)

    private sealed class CleanPrescription
    {
        public string? Error { get; private set; }
        public string? Field { get; private set; }
        public int CustomerId { get; set; }
        public string DoctorName { get; set; } = string.Empty;
        public string ClinicName { get; set; } = string.Empty;
        public string? Diagnosis { get; set; }
        public DateOnly Date { get; set; }
        public List<PrescriptionDetail> Lines { get; } = new();

        public CleanPrescription Fail(string error, string? field = null)
        {
            Error = error;
            Field = field;
            return this;
        }
    }

    private async Task<CleanPrescription> ValidateAsync(PrescriptionFormViewModel form, CancellationToken ct)
    {
        var data = new CleanPrescription();

        // ---- Khách hàng, bác sĩ, cơ sở khám, ngày kê ----
        if (form.CustomerId is null) return data.Fail("Chọn khách hàng của đơn thuốc.", nameof(form.CustomerId));
        var customerId = form.CustomerId.Value;
        var active = await _db.Customers.AsNoTracking().Where(c => c.CustomerId == customerId).Select(c => (bool?)c.IsActive).FirstOrDefaultAsync(ct);
        if (active is null) return data.Fail("Không tìm thấy khách hàng.", nameof(form.CustomerId));
        if (active == false) return data.Fail("Khách hàng đã ngừng theo dõi — chọn khách khác.", nameof(form.CustomerId));
        data.CustomerId = customerId;

        var doctor = AccountHelpers.NormalizeFullName(form.DoctorName);
        if (doctor.Length == 0) return data.Fail("Nhập tên bác sĩ kê đơn.", nameof(form.DoctorName));
        if (doctor.Length > 150) return data.Fail("Tên bác sĩ tối đa 150 ký tự.", nameof(form.DoctorName));
        data.DoctorName = doctor;

        var clinic = AccountHelpers.NormalizeFullName(form.ClinicName);
        if (clinic.Length == 0) return data.Fail("Nhập cơ sở khám bệnh.", nameof(form.ClinicName));
        if (clinic.Length > 200) return data.Fail("Cơ sở khám bệnh tối đa 200 ký tự.", nameof(form.ClinicName));
        data.ClinicName = clinic;

        var diagnosis = Clean(form.Diagnosis);
        if (diagnosis is { Length: > 500 }) return data.Fail("Chẩn đoán tối đa 500 ký tự.", nameof(form.Diagnosis));
        data.Diagnosis = diagnosis;

        if (!DateOnly.TryParseExact(form.PrescriptionDate?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return data.Fail("Nhập ngày kê đơn.", nameof(form.PrescriptionDate));
        var today = Today;
        if (date > today) return data.Fail("Ngày kê đơn không được sau hôm nay.", nameof(form.PrescriptionDate));
        if (date.AddDays(PrescriptionRules.ValidDays) < today)
            return data.Fail($"Đơn kê ngày {date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)} đã quá hạn mua ({PrescriptionRules.ValidDays} ngày kể từ ngày kê).",
                nameof(form.PrescriptionDate));
        data.Date = date;

        // ---- Dòng thuốc ----
        var lines = form.Lines.Where(l => l.MedicineId is not null || l.UnitId is not null || l.Quantity is not null
                                          || l.DosePerTime is not null || l.TimesPerDay is not null || l.DurationDays is not null
                                          || !string.IsNullOrWhiteSpace(l.Instructions)).ToList();
        if (lines.Count == 0) return data.Fail("Thêm ít nhất một thuốc vào đơn.");
        if (lines.Count > PrescriptionRules.MaxLines) return data.Fail($"Một đơn tối đa {PrescriptionRules.MaxLines} dòng thuốc.");

        var options = (await LoadMedicinesAsync(ct)).ToDictionary(m => m.MedicineId);
        var seen = new HashSet<int>();
        for (var i = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            var label = $"Dòng {i + 1}";
            if (l.MedicineId is null) return data.Fail($"{label}: chọn thuốc.");
            if (!options.TryGetValue(l.MedicineId.Value, out var med))
            {
                var id = l.MedicineId.Value;
                var info = await _db.Medicines.AsNoTracking().Where(m => m.MedicineId == id)
                    .Select(m => new { m.MedicineName, m.IsControlledDrug }).FirstOrDefaultAsync(ct);
                if (info is null) return data.Fail($"{label}: không tìm thấy thuốc.");
                return data.Fail(info.IsControlledDrug
                    ? $"{label}: {info.MedicineName} là thuốc kiểm soát đặc biệt, chưa bán được trên hệ thống."
                    : $"{label}: {info.MedicineName} đã ngừng kinh doanh.");
            }
            if (!seen.Add(med.MedicineId)) return data.Fail($"{med.Label} bị nhập ở 2 dòng — gộp lại một dòng.");
            var unit = l.UnitId is null ? null : med.Units.FirstOrDefault(u => u.UnitId == l.UnitId);
            if (unit is null) return data.Fail($"{label} ({med.Label}): chọn đơn vị.");
            if (l.Quantity is null || l.Quantity < 1 || l.Quantity > MaxQuantity)
                return data.Fail($"{label} ({med.Label}): số lượng kê là số nguyên từ 1 đến {MaxQuantity.ToString("N0", CultureInfo.GetCultureInfo("vi-VN"))}.");
            if (l.DosePerTime is not null && (l.DosePerTime <= 0 || l.DosePerTime > 1000 || l.DosePerTime * 1000 != decimal.Truncate(l.DosePerTime.Value * 1000)))
                return data.Fail($"{label} ({med.Label}): liều mỗi lần lớn hơn 0, tối đa 3 chữ số thập phân.");
            if (l.TimesPerDay is not null && (l.TimesPerDay < 1 || l.TimesPerDay > 24))
                return data.Fail($"{label} ({med.Label}): số lần mỗi ngày từ 1 đến 24.");
            if (l.DurationDays is not null && (l.DurationDays < 1 || l.DurationDays > 365))
                return data.Fail($"{label} ({med.Label}): số ngày dùng từ 1 đến 365.");
            var instructions = Clean(l.Instructions);
            if (instructions is { Length: > 500 }) return data.Fail($"{label} ({med.Label}): cách dùng tối đa 500 ký tự.");

            data.Lines.Add(new PrescriptionDetail
            {
                MedicineId = med.MedicineId,
                UnitId = unit.UnitId,
                PrescribedQuantity = l.Quantity.Value,
                DosePerTime = l.DosePerTime,
                TimesPerDay = l.TimesPerDay is null ? null : (byte)l.TimesPerDay.Value,
                DurationDays = l.DurationDays,
                Dosage = l.DosePerTime is null ? null : $"{Format.Number(l.DosePerTime.Value)} {med.BaseUnitName}/lần",
                Frequency = l.TimesPerDay is null ? null : $"{l.TimesPerDay} lần/ngày",
                Instructions = instructions
            });
        }
        return data;
    }

    // ================================================================== 3.2.2.2 Liều dùng + 3.2.2.3 an toàn (chỉ cảnh báo)

    private async Task<List<SafetyWarning>> WarningsAsync(int? customerId, string? diagnosis, IReadOnlyList<(int MedicineId, decimal? Dose, int? Times)> lines,
        CancellationToken ct)
    {
        var medicineIds = lines.Select(l => l.MedicineId).Distinct().ToList();
        if (medicineIds.Count == 0) return new List<SafetyWarning>();
        var warnings = await _safety.CheckAsync(customerId, medicineIds, true, diagnosis, ct);

        var limits = await _db.Medicines.AsNoTracking()
            .Where(m => medicineIds.Contains(m.MedicineId))
            .Select(m => new { m.MedicineId, m.MedicineName, BaseUnitName = m.BaseUnit.UnitName, m.MaxSingleDose, m.MaxDailyDose, m.MinPatientAgeYears })
            .ToDictionaryAsync(m => m.MedicineId, ct);
        DateOnly? birth = null;
        if (customerId is not null)
        {
            var id = customerId.Value;
            birth = await _db.Customers.AsNoTracking().Where(c => c.CustomerId == id).Select(c => c.DateOfBirth).FirstOrDefaultAsync(ct);
        }
        int? age = null;
        if (birth is not null)
        {
            var today = Today;
            var years = today.Year - birth.Value.Year;
            if (today < birth.Value.AddYears(years)) years--;
            age = years;
        }

        foreach (var (medicineId, dose, times) in lines)
        {
            if (!limits.TryGetValue(medicineId, out var m)) continue;
            if (dose is not null && m.MaxSingleDose is not null && dose > m.MaxSingleDose)
                warnings.Add(new SafetyWarning("bad",
                    $"{m.MedicineName}: liều mỗi lần {Format.Number(dose.Value)} {m.BaseUnitName} vượt liều tối đa {Format.Number(m.MaxSingleDose.Value)} {m.BaseUnitName}."));
            if (dose is not null && times is not null && m.MaxDailyDose is not null && dose * times > m.MaxDailyDose)
                warnings.Add(new SafetyWarning("bad",
                    $"{m.MedicineName}: liều cả ngày {Format.Number(dose.Value * times.Value)} {m.BaseUnitName} vượt liều tối đa {Format.Number(m.MaxDailyDose.Value)} {m.BaseUnitName}/ngày."));
            if (m.MinPatientAgeYears is not null)
            {
                if (age is null)
                    warnings.Add(new SafetyWarning("warn", $"{m.MedicineName}: dùng cho người từ {m.MinPatientAgeYears} tuổi — hồ sơ khách chưa có ngày sinh."));
                else if (age < m.MinPatientAgeYears)
                    warnings.Add(new SafetyWarning("bad", $"{m.MedicineName}: dùng cho người từ {m.MinPatientAgeYears} tuổi, khách {age} tuổi."));
            }
        }
        return warnings;
    }

    // ================================================================== Dùng chung

    /// <summary>Thuốc chọn được: đang kinh doanh, không thuộc nhóm kiểm soát đặc biệt; kèm đơn vị và tên hoạt chất để tìm.</summary>
    private async Task<List<RxMedicineOption>> LoadMedicinesAsync(CancellationToken ct)
    {
        var medicines = await _db.Medicines.AsNoTracking()
            .Where(m => m.IsActive && !m.IsControlledDrug)
            .OrderBy(m => m.MedicineName).ThenBy(m => m.MedicineCode)
            .Select(m => new { m.MedicineId, m.MedicineCode, m.MedicineName, m.IsPrescription, m.BaseUnitId, BaseUnitName = m.BaseUnit.UnitName })
            .ToListAsync(ct);
        var conversions = (await _db.MedicineUnitConversions.AsNoTracking()
                .Where(c => c.Medicine.IsActive && !c.Medicine.IsControlledDrug)
                .Select(c => new { c.MedicineId, c.UnitId, c.Unit.UnitName, c.ConversionToBase })
                .ToListAsync(ct))
            .GroupBy(c => c.MedicineId)
            .ToDictionary(g => g.Key, g => g.OrderBy(c => c.ConversionToBase).ToList());
        var keywords = (await _db.MedicineIngredients.AsNoTracking()
                .Select(i => new { i.MedicineId, i.Ingredient.IngredientName })
                .ToListAsync(ct))
            .GroupBy(i => i.MedicineId)
            .ToDictionary(g => g.Key, g => string.Join(", ", g.Select(i => i.IngredientName).OrderBy(n => n)));

        return medicines.Select(m =>
        {
            var units = new List<RxUnitOption> { new(m.BaseUnitId, m.BaseUnitName, 1m) };
            if (conversions.TryGetValue(m.MedicineId, out var list))
                units.AddRange(list.Where(c => c.UnitId != m.BaseUnitId).Select(c => new RxUnitOption(c.UnitId, c.UnitName, c.ConversionToBase)));
            return new RxMedicineOption(m.MedicineId, $"{m.MedicineName} ({m.MedicineCode})" + (m.IsPrescription ? " · kê đơn" : ""),
                m.BaseUnitName, units, keywords.GetValueOrDefault(m.MedicineId, string.Empty));
        }).ToList();
    }

    private const int SuggestionMax = 300;

    /// <summary>Tên bác sĩ, cơ sở khám đã dùng ở các đơn trước — dùng nhiều xếp trước; bác sĩ kèm cơ sở khám của đơn gần nhất.</summary>
    private async Task LoadSuggestionsAsync(PrescriptionFormViewModel form, CancellationToken ct)
    {
        var doctors = await _db.Prescriptions.AsNoTracking()
            .GroupBy(p => p.DoctorName)
            .Select(g => new { Name = g.Key, Count = g.Count(), LastId = g.Max(p => p.PrescriptionId) })
            .OrderByDescending(x => x.Count).ThenByDescending(x => x.LastId)
            .Take(SuggestionMax)
            .ToListAsync(ct);
        var lastIds = doctors.Select(d => d.LastId).ToList();
        var clinicOf = await _db.Prescriptions.AsNoTracking()
            .Where(p => lastIds.Contains(p.PrescriptionId))
            .Select(p => new { p.PrescriptionId, p.ClinicName })
            .ToDictionaryAsync(p => p.PrescriptionId, p => p.ClinicName, ct);
        form.DoctorSuggestions = doctors
            .Select(d => new DoctorSuggestion(d.Name.Trim(), clinicOf.GetValueOrDefault(d.LastId, string.Empty).Trim()))
            .Where(d => d.Name.Length > 0)
            .ToList();
        form.ClinicSuggestions = (await _db.Prescriptions.AsNoTracking()
                .GroupBy(p => p.ClinicName)
                .Select(g => new { Name = g.Key, Count = g.Count(), LastId = g.Max(p => p.PrescriptionId) })
                .OrderByDescending(x => x.Count).ThenByDescending(x => x.LastId)
                .Take(SuggestionMax)
                .ToListAsync(ct))
            .Select(c => c.Name.Trim()).Where(n => n.Length > 0).Distinct().ToList();
    }

    /// <summary>DT + yyMMdd + 3 số nối tiếp trong ngày (giống dữ liệu mẫu DT260501001).</summary>
    private async Task<string> NextCodeAsync(DateTime at, CancellationToken ct)
    {
        var prefix = CodePrefix + at.ToString("yyMMdd", CultureInfo.InvariantCulture);
        var codes = await _db.Prescriptions.AsNoTracking().Where(p => p.PrescriptionCode.StartsWith(prefix))
            .Select(p => p.PrescriptionCode).ToListAsync(ct);
        var max = 0;
        foreach (var code in codes)
            if (int.TryParse(code.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n > max) max = n;
        return prefix + (max + 1).ToString("D3", CultureInfo.InvariantCulture);
    }

    private static string? Clean(string? value)
    {
        var text = value?.Trim();
        return string.IsNullOrEmpty(text) ? null : text.Replace("\r\n", "\n");
    }
}
