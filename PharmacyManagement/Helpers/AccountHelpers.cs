using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PharmacyManagement.Authorization;
using PharmacyManagement.Data;
using PharmacyManagement.Services.Interfaces;

namespace PharmacyManagement.Helpers;

/// <summary>Hàm dùng chung cho nghiệp vụ tài khoản.</summary>
public static class AccountHelpers
{
    /// <summary>Owner khóa tài khoản đặt LockoutEnd = MaxValue; khóa tạm do sai mật khẩu chỉ vài phút.</summary>
    public static bool IsLockedByOwner(IdentityUser user)
        => user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTimeOffset.UtcNow.AddYears(50);

    public static bool IsTemporarilyLocked(IdentityUser user)
        => user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTimeOffset.UtcNow && !IsLockedByOwner(user);

    // ---------------- Tên tài khoản (UserName), email, họ tên — ba thông tin độc lập (DEC-21) ----------------
    // UserName  = phần trước @ của email đăng nhập, dùng để đăng nhập và nhận diện trên giao diện.
    // Email     = địa chỉ đầy đủ, dùng để đăng nhập, nhận mã, khôi phục.
    // Họ tên    = Employees.FullName, dùng trên hóa đơn, báo cáo, chứng từ, sổ GPP, nhật ký. Không thay bằng UserName/Email/tên vai trò.

    /// <summary>Tên tài khoản sinh từ email: phần trước @ (quynhpham.eculis@gmail.com → quynhpham.eculis).</summary>
    public static string UserNameFromEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return string.Empty;
        var trimmed = email.Trim();
        var at = trimmed.IndexOf('@');
        return at > 0 ? trimmed[..at] : trimmed;
    }

    /// <summary>Tên tài khoản để hiển thị. Tài khoản tạo trước DEC-21 có UserName là cả email: vẫn chỉ hiện phần trước @.</summary>
    public static string DisplayUserName(string? userName) => UserNameFromEmail(userName);

    public static string DisplayUserName(ClaimsPrincipal user) => DisplayUserName(user.Identity?.Name);

    public static string? EmailOf(ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.Email);

    /// <summary>
    /// Ảnh đại diện mặc định: tách tên tài khoản theo . _ - rồi lấy chữ đầu của hai phần đầu (quynhpham.eculis → QE);
    /// chỉ có một phần thì lấy hai ký tự đầu (admin → AD). Tối đa 2 ký tự để vừa ô tròn.
    /// </summary>
    public static string Initials(string? userName)
    {
        var name = DisplayUserName(userName);
        var parts = name.Split(new[] { '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "?";
        var text = parts.Length > 1 ? string.Concat(parts[0][0], parts[1][0]) : parts[0][..Math.Min(2, parts[0].Length)];
        return text.ToUpperInvariant();
    }

    /// <summary>
    /// Kiểm tra họ tên đầy đủ (đã bỏ khoảng trắng thừa). Trả về câu lỗi, hoặc null nếu hợp lệ.
    /// Không chấp nhận họ tên trùng tên tài khoản, email hay tên vai trò.
    /// </summary>
    public static string? ValidateFullName(string fullName, string? userName, string? email)
    {
        if (fullName.Length == 0) return "Nhập họ và tên đầy đủ.";
        if (fullName.Length > 150) return "Họ tên tối đa 150 ký tự.";
        if (!fullName.Any(char.IsLetter)) return "Họ tên phải có chữ cái.";
        var same = (string? other) => other is not null && string.Equals(fullName, other.Trim(), StringComparison.OrdinalIgnoreCase);
        if (same(email) || same(userName) || same(DisplayUserName(userName)) || fullName.Contains('@'))
            return "Nhập họ tên thật, không dùng email hay tên tài khoản.";
        if (same(RoleNames.DisplayName(RoleNames.Owner)) || same(RoleNames.DisplayName(RoleNames.Manager)) || same(RoleNames.DisplayName(RoleNames.Pharmacist)))
            return "Nhập họ tên thật, không dùng tên vai trò.";
        return null;
    }

    /// <summary>Câu lỗi nếu tên tài khoản sinh từ email đã thuộc tài khoản khác (ví dụ a@gmail.com và a@yahoo.com); null nếu dùng được.</summary>
    public static async Task<string?> UserNameConflictAsync(UserManager<IdentityUser> users, string email, string? exceptUserId = null)
    {
        var userName = UserNameFromEmail(email);
        var other = await users.FindByNameAsync(userName);
        return other is not null && other.Id != exceptUserId
            ? $"Tên tài khoản \"{userName}\" (phần trước @ của email) đã thuộc một tài khoản khác. Dùng email khác."
            : null;
    }

    /// <summary>Gộp khoảng trắng thừa: "  Phạm   Thị  Quỳnh " → "Phạm Thị Quỳnh".</summary>
    public static string NormalizeFullName(string? fullName)
        => string.Join(' ', (fullName ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    public static string JoinErrors(IdentityResult result)
        => string.Join(" ", result.Errors.Select(e => e.Description).Distinct());

    public static string CodeMessage(CodeCheckResult check, string whenGone)
        => check.Status switch
        {
            CodeCheckStatus.Invalid => $"Mã không đúng. Còn {check.AttemptsLeft} lần thử.",
            CodeCheckStatus.Expired => "Mã đã hết hạn. " + whenGone,
            CodeCheckStatus.TooManyAttempts => "Nhập sai quá 5 lần nên mã đã bị hủy. " + whenGone,
            _ => "Không có mã nào đang chờ cho email này, hoặc mã đã được dùng. " + whenGone
        };

    public static string LocalTime(DateTimeOffset value)
        => value.ToLocalTime().ToString("HH:mm dd/MM/yyyy", CultureInfo.InvariantCulture);

    public static string BuildUrl(string baseUrl, string pathAndQuery)
        => baseUrl.TrimEnd('/') + pathAndQuery;

    public static string JobTitleFor(string roleName) => roleName switch
    {
        RoleNames.Owner => "Chủ nhà thuốc (Owner)",
        RoleNames.Manager => "Quản lý kiêm thủ kho (Manager)",
        _ => "Dược sĩ bán hàng (Pharmacist)"
    };

    /// <summary>Mã nhân viên kế tiếp dạng NV01, NV02… (theo số lớn nhất đang có).</summary>
    public static async Task<string> NextEmployeeCodeAsync(PharmacyDbContext db, CancellationToken ct = default)
    {
        var codes = await db.Employees.AsNoTracking()
            .Where(e => e.EmployeeCode.StartsWith("NV"))
            .Select(e => e.EmployeeCode)
            .ToListAsync(ct);
        var max = 0;
        foreach (var code in codes)
        {
            if (int.TryParse(code.AsSpan(2), NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n > max) max = n;
        }
        return "NV" + (max + 1).ToString("D2", CultureInfo.InvariantCulture);
    }

    /// <summary>Họ tên nhân viên gắn với tài khoản; null nếu chưa gắn.</summary>
    public static Task<string?> EmployeeNameOfUserAsync(PharmacyDbContext db, string userId, CancellationToken ct = default)
        => db.Employees.AsNoTracking().Where(e => e.UserId == userId).Select(e => (string?)e.FullName).FirstOrDefaultAsync(ct);

    /// <summary>Họ tên của Owner duy nhất (dùng trong email mời); chưa có hồ sơ nhân viên thì dùng tên tài khoản.</summary>
    public static async Task<string> OwnerNameAsync(PharmacyDbContext db, CancellationToken ct = default)
    {
        var owner = await (from ur in db.UserRoles
                           join u in db.Users on ur.UserId equals u.Id
                           where ur.RoleId == RoleNames.OwnerId
                           select new { u.Id, u.UserName }).FirstOrDefaultAsync(ct);
        if (owner is null) return string.Empty;
        var name = await db.Employees.AsNoTracking().Where(e => e.UserId == owner.Id).Select(e => e.FullName).FirstOrDefaultAsync(ct);
        return name ?? DisplayUserName(owner.UserName);
    }

    /// <summary>Kiểm tra mật khẩu theo chính sách Identity mà không đổi gì.</summary>
    public static async Task<IdentityResult> ValidatePasswordAsync(UserManager<IdentityUser> users, IdentityUser user, string password)
    {
        var errors = new List<IdentityError>();
        foreach (var validator in users.PasswordValidators)
        {
            var result = await validator.ValidateAsync(users, user, password);
            if (!result.Succeeded) errors.AddRange(result.Errors);
        }
        return errors.Count == 0 ? IdentityResult.Success : IdentityResult.Failed(errors.ToArray());
    }

    /// <summary>Gỡ claim bắt đổi mật khẩu (nếu có).</summary>
    public static async Task RemoveMustChangePasswordAsync(UserManager<IdentityUser> users, IdentityUser user)
    {
        var claims = (await users.GetClaimsAsync(user)).Where(c => c.Type == AppClaimTypes.MustChangePassword).ToList();
        if (claims.Count > 0) await users.RemoveClaimsAsync(user, claims);
    }
}
