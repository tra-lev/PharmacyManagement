using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PharmacyManagement.Data;
using PharmacyManagement.Helpers;
using PharmacyManagement.Models.Entities;
using PharmacyManagement.Models.Enums;
using PharmacyManagement.Models.ViewModels.Catalog;
using PharmacyManagement.Services.Interfaces;

namespace PharmacyManagement.Services.Implementations;

public sealed class CustomerService : ICustomerService
{
    private const string CodePrefix = "KH";
    private const int MaxAllergies = 30;
    private const int MaxAgeYears = 120;

    private readonly PharmacyDbContext _db;
    private readonly IAuditLogService _audit;

    public CustomerService(PharmacyDbContext db, IAuditLogService audit)
    {
        _db = db;
        _audit = audit;
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    // ================================================================== Danh sách

    public async Task<CustomerListViewModel> GetListAsync(CustomerFilter filter, CancellationToken ct = default)
    {
        var query = _db.Customers.AsNoTracking().AsQueryable();

        filter.Status = filter.Status is "inactive" or "all" ? filter.Status : "active";
        query = filter.Status switch
        {
            "inactive" => query.Where(c => !c.IsActive),
            "all" => query,
            _ => query.Where(c => c.IsActive)
        };

        if (filter.Health == "allergy") query = query.Where(c => c.CustomerAllergies.Any());
        else if (filter.Health == "chronic") query = query.Where(c => c.ChronicConditions != null && c.ChronicConditions != "");
        else filter.Health = null;

        var q = AccountHelpers.NormalizeFullName(filter.Q);
        filter.Q = q.Length == 0 ? null : q;
        var folded = string.Empty;
        if (filter.Q is not null)
        {
            // Họ tên: không phân biệt dấu (SearchText); mã khách, số điện thoại: so bình thường
            var text = filter.Q;
            folded = SearchText.Fold(text);
            var digits = ContactRules.NormalizePhone(text) ?? string.Empty;
            var byPhone = digits.Length >= 3;
            query = query.Where(c =>
                EF.Functions.Collate(c.FullName.Replace("đ", "d").Replace("Đ", "D"), SearchText.Collation).Contains(folded)
                || c.CustomerCode.Contains(text)
                || (byPhone && c.Phone != null && c.Phone.Contains(digits)));
        }

        var total = await query.CountAsync(ct);
        var pageCount = Math.Max(1, (int)Math.Ceiling(total / (double)CustomerFilter.PageSize));
        filter.Page = Math.Clamp(filter.Page, 1, pageCount);

        // Đang tìm: họ tên BẮT ĐẦU bằng từ khóa lên trước
        var ordered = folded.Length == 0
            ? query.OrderBy(c => c.FullName)
            : query.OrderBy(c => EF.Functions.Collate(c.FullName.Replace("đ", "d").Replace("Đ", "D"), SearchText.Collation).StartsWith(folded) ? 0 : 1)
                .ThenBy(c => c.FullName);
        var page = await ordered.ThenBy(c => c.CustomerCode)
            .Skip((filter.Page - 1) * CustomerFilter.PageSize).Take(CustomerFilter.PageSize)
            .Select(c => new
            {
                c.CustomerId, c.CustomerCode, c.FullName, c.Phone, c.DateOfBirth, c.Gender, c.IsActive,
                AllergyCount = c.CustomerAllergies.Count(),
                HasChronic = c.ChronicConditions != null && c.ChronicConditions != ""
            })
            .ToListAsync(ct);

        var today = Today;
        return new CustomerListViewModel
        {
            Filter = filter,
            Total = total,
            PageCount = pageCount,
            Rows = page.Select(p => new CustomerRow(p.CustomerId, p.CustomerCode, p.FullName, p.Phone, AgeOf(p.DateOfBirth, today),
                p.Gender, p.AllergyCount, p.HasChronic, p.IsActive)).ToList()
        };
    }

    // ================================================================== Chi tiết

    public async Task<CustomerDetailsViewModel?> GetDetailsAsync(int customerId, CancellationToken ct = default)
    {
        var c = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(x => x.CustomerId == customerId, ct);
        if (c is null) return null;

        var allergies = await _db.CustomerAllergies.AsNoTracking()
            .Where(a => a.CustomerId == customerId)
            .OrderBy(a => a.Ingredient.IngredientName)
            .Select(a => new AllergyInfo(a.Ingredient.IngredientName, a.ReactionDescription, a.RecordedAt))
            .ToListAsync(ct);

        // Lịch sử mua (3.5.1): chỉ hóa đơn đã thanh toán
        var paid = _db.SalesInvoices.AsNoTracking()
            .Where(i => i.CustomerId == customerId && i.Status == (byte)InvoiceStatus.Paid);
        var recent = await paid
            .OrderByDescending(i => i.InvoiceDate).ThenByDescending(i => i.InvoiceId)
            .Take(CustomerDetailsViewModel.PurchaseLimit)
            .Select(i => new PurchaseRow(i.InvoiceCode, i.InvoiceDate, i.TotalAmount, i.Employee.FullName, i.PrescriptionId != null))
            .ToListAsync(ct);

        return new CustomerDetailsViewModel
        {
            CustomerId = c.CustomerId,
            Code = c.CustomerCode,
            FullName = c.FullName,
            Phone = c.Phone,
            Email = c.Email,
            DateOfBirth = c.DateOfBirth,
            Age = AgeOf(c.DateOfBirth, Today),
            Gender = c.Gender,
            Address = c.Address,
            ChronicConditions = SplitConditions(c.ChronicConditions),
            Notes = c.Notes,
            IsActive = c.IsActive,
            CreatedAt = c.CreatedAt,
            Allergies = allergies,
            PaidInvoiceCount = await paid.CountAsync(ct),
            TotalSpent = await paid.SumAsync(i => (decimal?)i.TotalAmount, ct) ?? 0,
            LastPurchaseAt = await paid.MaxAsync(i => (DateTime?)i.InvoiceDate, ct),
            PrescriptionCount = await _db.Prescriptions.AsNoTracking().CountAsync(p => p.CustomerId == customerId, ct),
            RecentPurchases = recent
        };
    }

    // ================================================================== Form

    public async Task<CustomerFormViewModel> NewFormAsync(CancellationToken ct = default)
    {
        var form = new CustomerFormViewModel();
        await PrepareFormAsync(form, ct);
        return form;
    }

    public async Task<CustomerFormViewModel?> EditFormAsync(int customerId, CancellationToken ct = default)
    {
        var c = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(x => x.CustomerId == customerId, ct);
        if (c is null) return null;
        var form = new CustomerFormViewModel
        {
            CustomerId = c.CustomerId,
            CustomerCode = c.CustomerCode,
            FullName = c.FullName,
            Phone = c.Phone,
            Email = c.Email,
            DateOfBirth = c.DateOfBirth?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Gender = c.Gender,
            Address = c.Address,
            ChronicConditions = c.ChronicConditions,
            Notes = c.Notes,
            Allergies = await _db.CustomerAllergies.AsNoTracking()
                .Where(a => a.CustomerId == customerId)
                .OrderBy(a => a.Ingredient.IngredientName)
                .Select(a => new AllergyLine { IngredientName = a.Ingredient.IngredientName, Reaction = a.ReactionDescription })
                .ToListAsync(ct)
        };
        await PrepareFormAsync(form, ct);
        return form;
    }

    public async Task PrepareFormAsync(CustomerFormViewModel form, CancellationToken ct = default)
    {
        if (form.CustomerId is null)
        {
            form.CustomerCode = await NextCodeAsync(ct);
        }
        else
        {
            form.CustomerCode = await _db.Customers.AsNoTracking().Where(c => c.CustomerId == form.CustomerId)
                .Select(c => c.CustomerCode).FirstOrDefaultAsync(ct);
        }
        form.IngredientNames = await _db.ActiveIngredients.AsNoTracking().OrderBy(a => a.IngredientName)
            .Select(a => a.IngredientName).ToListAsync(ct);
        var today = Today;
        form.MaxBirthDate = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        form.MinBirthDate = today.AddYears(-MaxAgeYears).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    // ================================================================== Thêm

    public async Task<OperationResult<int>> CreateAsync(CustomerFormViewModel form, string actorUserId, CancellationToken ct = default)
    {
        var clean = await ValidateAsync(form, customerId: null, checkPhone: true, ct);
        if (clean.Error is not null) return OperationResult<int>.Fail(clean.Error, clean.Field);

        var now = DateTime.Now;
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var customer = new Customer
            {
                CustomerCode = await NextCodeAsync(ct),
                IsActive = true,
                CreatedAt = now
            };
            Apply(customer, clean);
            _db.Customers.Add(customer);
            await _db.SaveChangesAsync(ct);

            foreach (var a in clean.Allergies)
            {
                _db.CustomerAllergies.Add(new CustomerAllergy
                {
                    CustomerId = customer.CustomerId, IngredientId = a.IngredientId, ReactionDescription = a.Reaction, RecordedAt = now
                });
            }
            await _db.SaveChangesAsync(ct);

            await _audit.WriteAsync("CUSTOMER_CREATE", "Customers", customer.CustomerId.ToString(CultureInfo.InvariantCulture),
                null, Snapshot(customer, clean.Allergies.Select(a => (a.Name, a.Reaction))), actorUserId, ct);
            await tx.CommitAsync(ct);
            return OperationResult<int>.Ok(customer.CustomerId, $"Đã thêm khách hàng {customer.CustomerCode} · {customer.FullName}.");
        }
        catch (DbUpdateException)
        {
            return OperationResult<int>.Fail("Không lưu được: mã khách hàng vừa được người khác dùng cùng lúc. Bấm Lưu lại.");
        }
    }

    // ================================================================== Sửa

    public async Task<OperationResult> UpdateAsync(CustomerFormViewModel form, string actorUserId, CancellationToken ct = default)
    {
        var customer = await _db.Customers
            .Include(c => c.CustomerAllergies).ThenInclude(a => a.Ingredient)
            .FirstOrDefaultAsync(c => c.CustomerId == form.CustomerId, ct);
        if (customer is null) return OperationResult.Fail("Không tìm thấy khách hàng.");

        // Khách đã ngừng: chưa xét trùng số điện thoại; xét khi mở lại.
        var clean = await ValidateAsync(form, customer.CustomerId, checkPhone: customer.IsActive, ct);
        if (clean.Error is not null) return OperationResult.Fail(clean.Error, clean.Field);

        var before = Snapshot(customer, customer.CustomerAllergies.Select(a => (a.Ingredient.IngredientName, a.ReactionDescription)));
        Apply(customer, clean);

        // Dị ứng: bỏ dòng không còn, sửa mô tả dòng còn giữ (giữ RecordedAt), thêm dòng mới
        var now = DateTime.Now;
        var wanted = clean.Allergies.ToDictionary(a => a.IngredientId);
        foreach (var existing in customer.CustomerAllergies.ToList())
        {
            if (!wanted.TryGetValue(existing.IngredientId, out var line)) _db.CustomerAllergies.Remove(existing);
            else if (existing.ReactionDescription != line.Reaction) existing.ReactionDescription = line.Reaction;
        }
        var kept = customer.CustomerAllergies.Select(a => a.IngredientId).ToHashSet();
        foreach (var line in clean.Allergies.Where(a => !kept.Contains(a.IngredientId)))
        {
            _db.CustomerAllergies.Add(new CustomerAllergy
            {
                CustomerId = customer.CustomerId, IngredientId = line.IngredientId, ReactionDescription = line.Reaction, RecordedAt = now
            });
        }

        if (!_db.ChangeTracker.HasChanges()) return OperationResult.Ok("Không có thay đổi nào.");

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            await _db.SaveChangesAsync(ct);
            await _audit.WriteAsync("CUSTOMER_UPDATE", "Customers", customer.CustomerId.ToString(CultureInfo.InvariantCulture),
                before, Snapshot(customer, clean.Allergies.Select(a => (a.Name, a.Reaction))), actorUserId, ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException)
        {
            return OperationResult.Fail("Không lưu được: hồ sơ vừa được người khác sửa cùng lúc. Mở lại hồ sơ rồi sửa tiếp.");
        }
        return OperationResult.Ok($"Đã lưu hồ sơ {customer.FullName}.");
    }

    // ================================================================== Ngừng / mở lại

    public async Task<OperationResult> SetActiveAsync(int customerId, bool active, string actorUserId, CancellationToken ct = default)
    {
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.CustomerId == customerId, ct);
        if (customer is null) return OperationResult.Fail("Không tìm thấy khách hàng.");
        if (customer.IsActive == active)
            return OperationResult.Ok(active ? "Khách hàng đang hoạt động." : "Khách hàng đã ngừng từ trước.");

        if (active && customer.Phone is not null)
        {
            var conflict = await PhoneOwnerAsync(customer.Phone, customerId, ct);
            if (conflict is not null)
                return OperationResult.Fail($"Chưa mở lại được: số điện thoại {ContactRules.DisplayPhone(customer.Phone)} đang dùng cho khách {conflict}. Sửa số điện thoại của một trong hai hồ sơ trước.");
        }

        customer.IsActive = active;
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(active ? "CUSTOMER_ACTIVATE" : "CUSTOMER_DEACTIVATE", "Customers",
            customerId.ToString(CultureInfo.InvariantCulture), new { isActive = !active }, new { isActive = active }, actorUserId, ct);
        await tx.CommitAsync(ct);
        return OperationResult.Ok(active ? $"Đã mở lại hồ sơ {customer.FullName}." : $"Đã ngừng hồ sơ {customer.FullName}.");
    }

    // ================================================================== Kiểm tra dữ liệu

    private sealed record AllergyData(int IngredientId, string Name, string? Reaction);

    private sealed class CleanCustomer
    {
        public string? Error { get; private set; }
        public string? Field { get; private set; }
        public string Name { get; set; } = string.Empty;
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public DateOnly? DateOfBirth { get; set; }
        public byte? Gender { get; set; }
        public string? Address { get; set; }
        public string? ChronicConditions { get; set; }
        public string? Notes { get; set; }
        public List<AllergyData> Allergies { get; } = new();

        public CleanCustomer Fail(string message, string? field = null)
        {
            Error = message;
            Field = field;
            return this;
        }
    }

    private async Task<CleanCustomer> ValidateAsync(CustomerFormViewModel form, int? customerId, bool checkPhone, CancellationToken ct)
    {
        var data = new CleanCustomer();

        data.Name = AccountHelpers.NormalizeFullName(form.FullName);
        if (data.Name.Length == 0) return data.Fail("Nhập họ tên khách hàng.", nameof(form.FullName));
        if (data.Name.Length > 150) return data.Fail("Họ tên tối đa 150 ký tự.", nameof(form.FullName));

        data.Phone = ContactRules.NormalizePhone(form.Phone);
        if (!ContactRules.IsValidPhone(data.Phone)) return data.Fail(ContactRules.PhoneMessage, nameof(form.Phone));
        if (checkPhone && data.Phone is not null)
        {
            var conflict = await PhoneOwnerAsync(data.Phone, customerId, ct);
            if (conflict is not null)
                return data.Fail($"Số điện thoại này đã dùng cho khách {conflict}. Mở hồ sơ đó thay vì tạo mới, hoặc dùng số khác.", nameof(form.Phone));
        }

        data.Email = ContactRules.NormalizeEmail(form.Email);
        if (!ContactRules.IsValidEmail(data.Email)) return data.Fail(ContactRules.EmailMessage + " Email chỉ dùng chữ không dấu.", nameof(form.Email));

        var dobText = form.DateOfBirth?.Trim();
        if (!string.IsNullOrEmpty(dobText))
        {
            if (!DateOnly.TryParseExact(dobText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dob))
                return data.Fail("Ngày sinh chưa đúng dạng.", nameof(form.DateOfBirth));
            var today = Today;
            if (dob > today) return data.Fail("Ngày sinh không được sau hôm nay.", nameof(form.DateOfBirth));
            if (dob < today.AddYears(-MaxAgeYears)) return data.Fail($"Ngày sinh không quá {MaxAgeYears} năm trước.", nameof(form.DateOfBirth));
            data.DateOfBirth = dob;
        }

        if (form.Gender is not null && GenderText.Of(form.Gender) is null)
            return data.Fail("Chọn giới tính trong danh sách.", nameof(form.Gender));
        data.Gender = form.Gender;

        data.Address = Clean(form.Address);
        if (data.Address?.Length > 300) return data.Fail("Địa chỉ tối đa 300 ký tự.", nameof(form.Address));

        var conditions = SplitConditions(form.ChronicConditions);
        data.ChronicConditions = conditions.Count == 0 ? null : string.Join("; ", conditions);
        if (data.ChronicConditions?.Length > 500) return data.Fail("Bệnh nền tối đa 500 ký tự.", nameof(form.ChronicConditions));

        data.Notes = CleanText(form.Notes);
        if (data.Notes?.Length > 1000) return data.Fail("Ghi chú tối đa 1000 ký tự.", nameof(form.Notes));

        // ---- Dị ứng: hoạt chất phải có sẵn trong danh mục (thêm hoạt chất mới thuộc 1.1.x) ----
        var lines = new List<(string Name, string? Reaction)>();
        foreach (var line in form.Allergies)
        {
            var name = AccountHelpers.NormalizeFullName(line.IngredientName).Normalize(NormalizationForm.FormC);
            var reaction = Clean(line.Reaction);
            if (name.Length == 0 && reaction is null) continue;
            if (name.Length == 0) return data.Fail("Có dòng dị ứng ghi phản ứng nhưng chưa chọn hoạt chất.", nameof(form.Allergies));
            if (reaction?.Length > 500) return data.Fail($"Mô tả phản ứng với {name} tối đa 500 ký tự.", nameof(form.Allergies));
            lines.Add((name, reaction));
        }
        if (lines.Count > MaxAllergies) return data.Fail($"Tối đa {MaxAllergies} dòng dị ứng.", nameof(form.Allergies));

        if (lines.Count > 0)
        {
            var names = lines.Select(l => l.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var found = await _db.ActiveIngredients.AsNoTracking().Where(a => names.Contains(a.IngredientName))
                .Select(a => new { a.IngredientId, a.IngredientName }).ToListAsync(ct);
            var byName = new Dictionary<string, (int Id, string Name)>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in found) byName[a.IngredientName.Normalize(NormalizationForm.FormC)] = (a.IngredientId, a.IngredientName);

            var missing = names.Where(n => !byName.ContainsKey(n)).ToList();
            if (missing.Count > 0)
                return data.Fail($"Chưa có hoạt chất {string.Join(", ", missing.Select(m => "“" + m + "”"))} trong danh mục. Chọn từ danh sách gợi ý; hoạt chất mới do người quản lý thêm khi khai báo thuốc.",
                    nameof(form.Allergies));

            var seen = new HashSet<int>();
            foreach (var (name, reaction) in lines)
            {
                var ingredient = byName[name];
                if (!seen.Add(ingredient.Id)) return data.Fail($"Hoạt chất {ingredient.Name} được nhập hai lần ở phần dị ứng.", nameof(form.Allergies));
                data.Allergies.Add(new AllergyData(ingredient.Id, ingredient.Name, reaction));
            }
        }

        return data;
    }

    private static void Apply(Customer c, CleanCustomer d)
    {
        c.FullName = d.Name;
        c.Phone = d.Phone;
        c.Email = d.Email;
        c.DateOfBirth = d.DateOfBirth;
        c.Gender = d.Gender;
        c.Address = d.Address;
        c.ChronicConditions = d.ChronicConditions;
        c.Notes = d.Notes;
    }

    /// <summary>Khách ĐANG HOẠT ĐỘNG khác đã dùng số điện thoại này ("KH0012 · Tên"), null nếu chưa ai dùng.</summary>
    private async Task<string?> PhoneOwnerAsync(string phone, int? exceptCustomerId, CancellationToken ct)
    {
        var owner = await _db.Customers.AsNoTracking()
            .Where(c => c.IsActive && c.Phone == phone && c.CustomerId != exceptCustomerId)
            .Select(c => new { c.CustomerCode, c.FullName }).FirstOrDefaultAsync(ct);
        return owner is null ? null : $"{owner.CustomerCode} · {owner.FullName}";
    }

    private async Task<string> NextCodeAsync(CancellationToken ct)
    {
        var codes = await _db.Customers.AsNoTracking().Where(c => c.CustomerCode.StartsWith(CodePrefix))
            .Select(c => c.CustomerCode).ToListAsync(ct);
        var max = 0;
        foreach (var code in codes)
            if (int.TryParse(code.AsSpan(CodePrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n > max) max = n;
        return CodePrefix + (max + 1).ToString("D4", CultureInfo.InvariantCulture);
    }

    /// <summary>Tách bệnh nền theo dấu ; hoặc xuống dòng, gộp khoảng trắng, bỏ trùng (không phân biệt hoa/thường).</summary>
    private static List<string> SplitConditions(string? value)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(value)) return result;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in value.Split(new[] { ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var text = AccountHelpers.NormalizeFullName(part);
            if (text.Length > 0 && seen.Add(text)) result.Add(text);
        }
        return result;
    }

    private static int? AgeOf(DateOnly? dob, DateOnly today)
    {
        if (dob is null) return null;
        var age = today.Year - dob.Value.Year;
        if (dob.Value > today.AddYears(-age)) age--;
        return age;
    }

    /// <summary>Ảnh chụp dữ liệu để ghi nhật ký (SYS-09).</summary>
    private static object Snapshot(Customer c, IEnumerable<(string Name, string? Reaction)> allergies) => new
    {
        c.CustomerCode, c.FullName, c.Phone, c.Email,
        DateOfBirth = c.DateOfBirth?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        c.Gender, c.Address, c.ChronicConditions, c.Notes, c.IsActive,
        Allergies = allergies.OrderBy(a => a.Name).Select(a => a.Reaction is null ? a.Name : a.Name + " (" + a.Reaction + ")").ToList()
    };

    /// <summary>Văn bản nhiều dòng: chỉ bỏ khoảng trắng đầu/cuối, giữ xuống dòng.</summary>
    private static string? CleanText(string? value)
    {
        var text = value?.Trim();
        return string.IsNullOrEmpty(text) ? null : text.Replace("\r\n", "\n");
    }

    private static string? Clean(string? value)
    {
        var text = AccountHelpers.NormalizeFullName(value);
        return text.Length == 0 ? null : text;
    }
}
