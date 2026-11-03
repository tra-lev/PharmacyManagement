using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PharmacyManagement.Authorization;
using PharmacyManagement.Data;
using PharmacyManagement.Helpers;
using PharmacyManagement.Models.Entities;
using PharmacyManagement.Services.Interfaces;

namespace PharmacyManagement.Services.Implementations;

/// <summary>
/// Lệnh khôi phục quyền chủ nhà thuốc — chạy TRÊN MÁY CHỦ (cần quyền vào máy và CSDL), không có trên giao diện web:
///   dotnet run --project PharmacyManagement -- owner-recovery --email email-moi@gmail.com
///   dotnet run --project PharmacyManagement -- owner-recovery --email email@gmail.com --employee-code NV01   (khi CSDL không còn Owner)
///
/// - Đã có Owner: chỉ SỬA tài khoản Owner đó (đổi email nếu khác, mở khóa, đăng xuất mọi phiên) rồi gửi mã đặt lại
///   mật khẩu tới email; Owner đặt mật khẩu mới ở /Account/ResetPassword.
/// - Không còn Owner (do sửa tay CSDL hoặc máy mới chưa khởi tạo): gắn vai trò Owner cho hồ sơ nhân viên chỉ định —
///   nhân viên đó phải CHƯA có tài khoản hoặc tài khoản chưa có vai trò; KHÔNG nâng cấp Manager/Pharmacist thành Owner.
/// Không bao giờ tạo Owner thứ hai (CSDL còn chặn bằng UX_AspNetUserRoles_SingleOwner). Mỗi lần chạy ghi AuditLogs.
/// </summary>
public sealed class OwnerRecoveryService
{
    private readonly PharmacyDbContext _db;
    private readonly UserManager<IdentityUser> _users;
    private readonly IVerificationCodeService _codes;
    private readonly IEmailService _email;
    private readonly IAuditLogService _audit;

    public OwnerRecoveryService(PharmacyDbContext db, UserManager<IdentityUser> users, IVerificationCodeService codes,
        IEmailService email, IAuditLogService audit)
    {
        _db = db;
        _users = users;
        _codes = codes;
        _email = email;
        _audit = audit;
    }

    public async Task<int> RunAsync(string[] args, string baseUrl)
    {
        var email = Option(args, "--email")?.Trim();
        var employeeCode = Option(args, "--employee-code")?.Trim();
        if (string.IsNullOrEmpty(email) || !email.Contains('@'))
        {
            Console.WriteLine("Cách dùng: owner-recovery --email <email> [--employee-code <mã nhân viên>]");
            return 2;
        }
        if (!_email.IsConfigured)
        {
            Console.WriteLine("LỖI: chưa cấu hình gửi email (Email:SenderAddress, Email:AppPassword). Không thể gửi mã khôi phục.");
            return 3;
        }

        await using var tx = await _db.Database.BeginTransactionAsync();
        var owners = await _users.GetUsersInRoleAsync(RoleNames.Owner);
        IdentityUser owner;
        Employee? employee;
        string action;

        if (owners.Count > 1)
        {
            Console.WriteLine("LỖI: CSDL đang có nhiều hơn một Owner — dữ liệu sai, cần kiểm tra tay.");
            return 4;
        }

        if (owners.Count == 1)
        {
            owner = owners[0];
            var other = await _users.FindByEmailAsync(email);
            if (other is not null && other.Id != owner.Id)
            {
                Console.WriteLine($"LỖI: email {email} đang thuộc một tài khoản khác.");
                return 5;
            }
            var nameConflict = await AccountHelpers.UserNameConflictAsync(_users, email, owner.Id);
            if (nameConflict is not null)
            {
                Console.WriteLine("LỖI: " + nameConflict);
                return 5;
            }
            var oldEmail = owner.Email;
            if (!string.Equals(oldEmail, email, StringComparison.OrdinalIgnoreCase))
            {
                owner.Email = email;
                owner.NormalizedEmail = _users.NormalizeEmail(email);
                owner.UserName = AccountHelpers.UserNameFromEmail(email);
                owner.NormalizedUserName = _users.NormalizeName(owner.UserName);
                owner.EmailConfirmed = false;
                var updated = await _users.UpdateAsync(owner);
                if (!updated.Succeeded)
                {
                    Console.WriteLine("LỖI: " + AccountHelpers.JoinErrors(updated));
                    return 6;
                }
            }
            employee = await _db.Employees.FirstOrDefaultAsync(e => e.UserId == owner.Id);
            if (employee is not null) employee.Email = email;
            action = $"Sửa tài khoản Owner hiện có (email cũ: {oldEmail})";
        }
        else
        {
            if (string.IsNullOrEmpty(employeeCode))
            {
                Console.WriteLine("CSDL chưa có Owner. Chạy lại với --employee-code <mã hồ sơ nhân viên của chủ nhà thuốc>.");
                return 7;
            }
            employee = await _db.Employees.FirstOrDefaultAsync(e => e.EmployeeCode == employeeCode);
            if (employee is null)
            {
                Console.WriteLine($"LỖI: không có nhân viên mã {employeeCode}.");
                return 8;
            }

            if (employee.UserId is not null)
            {
                var existing = await _users.FindByIdAsync(employee.UserId);
                if (existing is null)
                {
                    Console.WriteLine("LỖI: hồ sơ nhân viên trỏ tới tài khoản không tồn tại.");
                    return 9;
                }
                if ((await _users.GetRolesAsync(existing)).Count > 0)
                {
                    Console.WriteLine("LỖI: tài khoản của nhân viên này đang có vai trò khác. Không nâng cấp tài khoản khác thành Owner.");
                    return 10;
                }
                owner = existing;
            }
            else
            {
                var other = await _users.FindByEmailAsync(email);
                if (other is not null)
                {
                    Console.WriteLine($"LỖI: email {email} đang thuộc một tài khoản khác.");
                    return 5;
                }
                var nameConflict = await AccountHelpers.UserNameConflictAsync(_users, email);
                if (nameConflict is not null)
                {
                    Console.WriteLine("LỖI: " + nameConflict);
                    return 5;
                }
                owner = new IdentityUser { UserName = AccountHelpers.UserNameFromEmail(email), Email = email, EmailConfirmed = false, LockoutEnabled = true };
                var created = await _users.CreateAsync(owner);
                if (!created.Succeeded)
                {
                    Console.WriteLine("LỖI: " + AccountHelpers.JoinErrors(created));
                    return 6;
                }
                employee.UserId = owner.Id;
            }

            var role = await _users.AddToRoleAsync(owner, RoleNames.Owner);
            if (!role.Succeeded)
            {
                Console.WriteLine("LỖI: " + AccountHelpers.JoinErrors(role));
                return 6;
            }
            employee.Email = owner.Email;
            if (!await _db.PharmacySettings.AnyAsync(s => s.SettingKey == OwnerBootstrapService.SetupCompletedKey))
            {
                _db.PharmacySettings.Add(new PharmacySetting
                {
                    SettingKey = OwnerBootstrapService.SetupCompletedKey,
                    SettingValue = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
                    Description = "Thời điểm khởi tạo tài khoản Owner — tạo bằng lệnh owner-recovery (DEC-18)",
                    UpdatedAt = DateTime.Now
                });
            }
            action = $"Gắn vai trò Owner cho nhân viên {employeeCode} (CSDL không còn Owner)";
        }

        await _db.SaveChangesAsync();
        await _users.SetLockoutEndDateAsync(owner, null);
        await _users.ResetAccessFailedCountAsync(owner);
        await _users.UpdateSecurityStampAsync(owner); // đăng xuất mọi phiên đang mở

        var code = await _codes.IssueAsync(owner, CodePurposes.ResetPassword);
        var resetUrl = AccountHelpers.BuildUrl(baseUrl, "/Account/ResetPassword?email=" + Uri.EscapeDataString(email));
        var name = employee?.FullName ?? AccountHelpers.DisplayUserName(owner.UserName);
        var sent = await _email.SendAsync(EmailTemplates.ResetPassword(email, name, code, resetUrl), CodePurposes.ResetPassword, employee?.EmployeeId ?? 0);
        if (!sent.Succeeded)
        {
            Console.WriteLine("LỖI: không gửi được email — mọi thay đổi đã hủy. " + sent.ErrorMessage);
            return 11; // tx bị hủy khi thoát khỏi using
        }

        await _audit.WriteAsync("OWNER_RECOVERY", "AspNetUsers", owner.Id, null, new { email, action }, owner.Id);
        await tx.CommitAsync();

        Console.WriteLine(action + ".");
        Console.WriteLine($"Đã gửi mã đặt lại mật khẩu tới {email} (hiệu lực {(int)_codes.Lifetime.TotalMinutes} phút).");
        Console.WriteLine($"Mở {resetUrl} để đặt mật khẩu mới; email được xác minh khi nhập đúng mã.");
        return 0;
    }

    private static string? Option(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        }
        return null;
    }
}
