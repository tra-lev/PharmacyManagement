using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PharmacyManagement.Authorization;
using PharmacyManagement.Data;
using PharmacyManagement.Helpers;
using PharmacyManagement.Models.Entities;
using PharmacyManagement.Models.ViewModels.Account;
using PharmacyManagement.Services.Interfaces;

namespace PharmacyManagement.Services.Implementations;

public sealed class AccountFlowService : IAccountFlowService
{
    private static readonly TimeSpan TicketLifetime = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan ResendCooldown = TimeSpan.FromSeconds(60);

    private readonly PharmacyDbContext _db;
    private readonly UserManager<IdentityUser> _users;
    private readonly IVerificationCodeService _codes;
    private readonly IEmailService _email;
    private readonly IAuditLogService _audit;
    private readonly ITimeLimitedDataProtector _tickets;
    private readonly ILogger<AccountFlowService> _logger;

    public AccountFlowService(PharmacyDbContext db, UserManager<IdentityUser> users, IVerificationCodeService codes,
        IEmailService email, IAuditLogService audit, IDataProtectionProvider protection, ILogger<AccountFlowService> logger)
    {
        _db = db;
        _users = users;
        _codes = codes;
        _email = email;
        _audit = audit;
        _tickets = protection.CreateProtector("PharmacyManagement.Activation.v1").ToTimeLimitedDataProtector();
        _logger = logger;
    }

    public async Task<OperationResult<string>> VerifyActivationCodeAsync(string email, string code)
    {
        const string whenGone = "Báo chủ nhà thuốc gửi lại mã.";
        var user = await _users.FindByEmailAsync(email.Trim());
        if (user is null || user.PasswordHash is not null || await _users.IsInRoleAsync(user, RoleNames.Owner))
            return OperationResult<string>.Fail(AccountHelpers.CodeMessage(new CodeCheckResult(CodeCheckStatus.NotFound, 0, null), whenGone), nameof(ActivateCodeViewModel.Code));

        var check = await _codes.VerifyAsync(user, CodePurposes.Invite, code);
        if (!check.Succeeded)
            return OperationResult<string>.Fail(AccountHelpers.CodeMessage(check, whenGone), nameof(ActivateCodeViewModel.Code));

        return OperationResult<string>.Ok(_tickets.Protect(user.Id, TicketLifetime));
    }

    public async Task<ActivatePasswordViewModel?> GetActivationInfoAsync(string ticket)
    {
        var user = await UserFromTicketAsync(ticket);
        if (user is null || user.PasswordHash is not null) return null;
        var role = (await _users.GetRolesAsync(user)).FirstOrDefault();
        return new ActivatePasswordViewModel
        {
            Ticket = ticket,
            Email = user.Email ?? string.Empty,
            FullName = await AccountHelpers.EmployeeNameOfUserAsync(_db, user.Id) ?? string.Empty,
            RoleDisplay = RoleNames.DisplayName(role)
        };
    }

    public async Task<OperationResult> CompleteActivationAsync(string ticket, string password)
    {
        var user = await UserFromTicketAsync(ticket);
        if (user is null)
            return OperationResult.Fail("Phiên kích hoạt đã hết hạn (15 phút). Báo chủ nhà thuốc gửi lại mã rồi làm lại từ đầu.");
        if (user.PasswordHash is not null)
            return OperationResult.Fail("Tài khoản đã được kích hoạt. Hãy đăng nhập.");

        var added = await _users.AddPasswordAsync(user, password);
        if (!added.Succeeded) return OperationResult.Fail(AccountHelpers.JoinErrors(added), nameof(ActivatePasswordViewModel.NewPassword));

        user.EmailConfirmed = true;
        var updated = await _users.UpdateAsync(user);
        if (!updated.Succeeded) return OperationResult.Fail(AccountHelpers.JoinErrors(updated));

        await _audit.WriteAsync("ACCOUNT_ACTIVATE", "AspNetUsers", user.Id, null, new { user.Email }, user.Id);
        return OperationResult.Ok("Tài khoản đã sẵn sàng. Đăng nhập bằng email và mật khẩu vừa đặt.");
    }

    public async Task SendPasswordResetAsync(string email, string baseUrl)
    {
        var user = await _users.FindByEmailAsync(email.Trim());
        if (user is null || !user.EmailConfirmed || user.PasswordHash is null || AccountHelpers.IsLockedByOwner(user))
        {
            _logger.LogInformation("Bỏ qua yêu cầu quên mật khẩu cho email không hợp lệ hoặc tài khoản không hoạt động.");
            return;
        }

        var expiry = await _codes.GetExpiryAsync(user, CodePurposes.ResetPassword);
        if (expiry is not null && expiry.Value - _codes.Lifetime > DateTimeOffset.UtcNow - ResendCooldown) return;

        var employee = await _db.Employees.AsNoTracking().Where(e => e.UserId == user.Id)
            .Select(e => new { e.EmployeeId, e.FullName }).FirstOrDefaultAsync();
        var code = await _codes.IssueAsync(user, CodePurposes.ResetPassword);
        var resetUrl = AccountHelpers.BuildUrl(baseUrl, "/Account/ResetPassword?email=" + Uri.EscapeDataString(user.Email!));
        var sent = await _email.SendAsync(EmailTemplates.ResetPassword(user.Email!, employee?.FullName ?? AccountHelpers.DisplayUserName(user.UserName), code, resetUrl),
            CodePurposes.ResetPassword, employee?.EmployeeId ?? 0);
        if (!sent.Succeeded) await _codes.RevokeAsync(user, CodePurposes.ResetPassword);
    }

    public async Task<OperationResult> ResetPasswordAsync(ResetPasswordViewModel model)
    {
        const string whenGone = "Bấm “Gửi lại mã” ở trang Quên mật khẩu.";
        var user = await _users.FindByEmailAsync(model.Email.Trim());
        // Không chặn tài khoản chưa có mật khẩu: Owner được khôi phục bằng lệnh owner-recovery đặt mật khẩu ở đây.
        // Lời mời chưa kích hoạt không có mã "reset" nên không đi lọt bước kiểm tra mã.
        if (user is null || AccountHelpers.IsLockedByOwner(user))
            return OperationResult.Fail(AccountHelpers.CodeMessage(new CodeCheckResult(CodeCheckStatus.NotFound, 0, null), whenGone), nameof(model.Code));

        var valid = await AccountHelpers.ValidatePasswordAsync(_users, user, model.NewPassword);
        if (!valid.Succeeded) return OperationResult.Fail(AccountHelpers.JoinErrors(valid), nameof(model.NewPassword));

        var check = await _codes.VerifyAsync(user, CodePurposes.ResetPassword, model.Code);
        if (!check.Succeeded) return OperationResult.Fail(AccountHelpers.CodeMessage(check, whenGone), nameof(model.Code));

        var token = await _users.GeneratePasswordResetTokenAsync(user);
        var reset = await _users.ResetPasswordAsync(user, token, model.NewPassword);
        if (!reset.Succeeded) return OperationResult.Fail(AccountHelpers.JoinErrors(reset), nameof(model.NewPassword));

        if (!user.EmailConfirmed)
        {
            // Khôi phục Owner: nhập đúng mã gửi tới email mới chứng minh quyền sở hữu email.
            user.EmailConfirmed = true;
            await _users.UpdateAsync(user);
        }
        await AccountHelpers.RemoveMustChangePasswordAsync(_users, user);
        await _users.ResetAccessFailedCountAsync(user);
        await _users.SetLockoutEndDateAsync(user, null);
        await _audit.WriteAsync("PASSWORD_RESET", "AspNetUsers", user.Id, null, new { user.Email }, user.Id);
        return OperationResult.Ok("Đã đặt mật khẩu mới. Hãy đăng nhập.");
    }

    public async Task<OperationResult> CompleteFirstLoginAsync(IdentityUser user, string newPassword)
    {
        if (await _users.CheckPasswordAsync(user, newPassword))
            return OperationResult.Fail("Mật khẩu mới phải khác mật khẩu tạm.", nameof(FirstLoginViewModel.NewPassword));

        var valid = await AccountHelpers.ValidatePasswordAsync(_users, user, newPassword);
        if (!valid.Succeeded) return OperationResult.Fail(AccountHelpers.JoinErrors(valid), nameof(FirstLoginViewModel.NewPassword));

        var token = await _users.GeneratePasswordResetTokenAsync(user);
        var reset = await _users.ResetPasswordAsync(user, token, newPassword);
        if (!reset.Succeeded) return OperationResult.Fail(AccountHelpers.JoinErrors(reset), nameof(FirstLoginViewModel.NewPassword));

        await AccountHelpers.RemoveMustChangePasswordAsync(_users, user);
        await _audit.WriteAsync("PASSWORD_CHANGE_FIRST_LOGIN", "AspNetUsers", user.Id, null, null, user.Id);
        return OperationResult.Ok("Đã đổi mật khẩu.");
    }

    public async Task<OperationResult<DateTimeOffset>> SendEmailVerificationAsync(IdentityUser user)
    {
        if (user.EmailConfirmed) return OperationResult<DateTimeOffset>.Fail("Email đã được xác minh.");
        if (!_email.IsConfigured)
            return OperationResult<DateTimeOffset>.Fail("Chưa cấu hình tài khoản gửi email (Email:SenderAddress, Email:AppPassword trong User Secrets).");

        var expiry = await _codes.GetExpiryAsync(user, CodePurposes.VerifyEmail);
        if (expiry is not null && expiry.Value - _codes.Lifetime > DateTimeOffset.UtcNow - ResendCooldown)
            return OperationResult<DateTimeOffset>.Fail("Vừa gửi mã chưa đầy 1 phút. Kiểm tra hộp thư (cả mục Spam) rồi thử lại sau.");

        var employee = await _db.Employees.AsNoTracking().Where(e => e.UserId == user.Id)
            .Select(e => new { e.EmployeeId, e.FullName }).FirstOrDefaultAsync();
        var code = await _codes.IssueAsync(user, CodePurposes.VerifyEmail);
        var sent = await _email.SendAsync(EmailTemplates.VerifyEmail(user.Email!, employee?.FullName ?? AccountHelpers.DisplayUserName(user.UserName), code),
            CodePurposes.VerifyEmail, employee?.EmployeeId ?? 0);
        if (!sent.Succeeded)
        {
            await _codes.RevokeAsync(user, CodePurposes.VerifyEmail);
            return OperationResult<DateTimeOffset>.Fail("Chưa gửi được email. " + sent.ErrorMessage + " Chưa có mã nào được tạo.");
        }
        return OperationResult<DateTimeOffset>.Ok(code.ExpiresAt);
    }

    public async Task<OperationResult> ConfirmEmailAsync(IdentityUser user, string code)
    {
        var check = await _codes.VerifyAsync(user, CodePurposes.VerifyEmail, code);
        if (!check.Succeeded) return OperationResult.Fail(AccountHelpers.CodeMessage(check, "Bấm “Gửi lại mã”."), nameof(VerifyEmailViewModel.Code));

        user.EmailConfirmed = true;
        var updated = await _users.UpdateAsync(user);
        if (!updated.Succeeded) return OperationResult.Fail(AccountHelpers.JoinErrors(updated));
        await _audit.WriteAsync("EMAIL_VERIFY", "AspNetUsers", user.Id, null, new { user.Email }, user.Id);
        return OperationResult.Ok("Email đã được xác minh. Các chức năng quản trị đã mở khóa.");
    }

    public async Task<OperationResult> ConfirmEmailChangeAsync(string newEmail, string code)
    {
        const string whenGone = "Báo chủ nhà thuốc gửi lại mã.";
        var email = newEmail.Trim();
        var payloadName = CodePurposes.ChangeEmail + ":payload";
        var userId = await _db.UserTokens.AsNoTracking()
            .Where(t => t.LoginProvider == VerificationCodeService.Provider && t.Name == payloadName && t.Value == email)
            .Select(t => t.UserId)
            .FirstOrDefaultAsync();
        var user = userId is null ? null : await _users.FindByIdAsync(userId);
        if (user is null)
            return OperationResult.Fail(AccountHelpers.CodeMessage(new CodeCheckResult(CodeCheckStatus.NotFound, 0, null), whenGone), nameof(ConfirmEmailChangeViewModel.Code));

        var check = await _codes.VerifyAsync(user, CodePurposes.ChangeEmail, code);
        if (!check.Succeeded) return OperationResult.Fail(AccountHelpers.CodeMessage(check, whenGone), nameof(ConfirmEmailChangeViewModel.Code));

        var other = await _users.FindByEmailAsync(email);
        if (other is not null && other.Id != user.Id)
            return OperationResult.Fail("Email này vừa được dùng cho tài khoản khác. Báo chủ nhà thuốc.");
        var userNameConflict = await AccountHelpers.UserNameConflictAsync(_users, email, user.Id);
        if (userNameConflict is not null) return OperationResult.Fail(userNameConflict + " Báo chủ nhà thuốc.");

        var oldEmail = user.Email;
        var oldUserName = user.UserName;
        user.Email = email;
        user.NormalizedEmail = _users.NormalizeEmail(email);
        user.UserName = AccountHelpers.UserNameFromEmail(email); // tên tài khoản đi theo email (DEC-21)
        user.NormalizedUserName = _users.NormalizeName(user.UserName);
        user.EmailConfirmed = true;
        var updated = await _users.UpdateAsync(user);
        if (!updated.Succeeded) return OperationResult.Fail(AccountHelpers.JoinErrors(updated));
        await _users.UpdateSecurityStampAsync(user); // đăng xuất các phiên dùng email cũ

        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.UserId == user.Id);
        if (employee is not null)
        {
            employee.Email = email;
            await _db.SaveChangesAsync();
        }

        await _audit.WriteAsync("ACCOUNT_EMAIL_CHANGED", "AspNetUsers", user.Id, new { email = oldEmail, userName = oldUserName }, new { email, userName = user.UserName }, user.Id);
        return OperationResult.Ok("Đã đổi email đăng nhập. Hãy đăng nhập bằng email mới.");
    }

    public Task<string?> GetFullNameAsync(string userId) => AccountHelpers.EmployeeNameOfUserAsync(_db, userId);

    public async Task<OperationResult> UpdateOwnerFullNameAsync(IdentityUser user, string? fullName)
    {
        if (!await _users.IsInRoleAsync(user, RoleNames.Owner))
            return OperationResult.Fail("Chỉ chủ nhà thuốc được tự sửa họ tên. Nhân viên cần đổi họ tên hãy báo chủ nhà thuốc.");

        var name = AccountHelpers.NormalizeFullName(fullName);
        var error = AccountHelpers.ValidateFullName(name, user.UserName, user.Email);
        if (error is not null) return OperationResult.Fail(error, nameof(EditProfileViewModel.FullName));

        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.UserId == user.Id);
        string? oldName = employee?.FullName;
        var linked = false;
        if (employee is null)
        {
            linked = true;
            // Owner chưa gắn hồ sơ: dùng hồ sơ chưa có tài khoản cùng email nếu có, để không tạo trùng.
            employee = await _db.Employees.FirstOrDefaultAsync(e => e.UserId == null && e.IsActive && e.Email == user.Email);
            if (employee is null)
            {
                employee = new Employee
                {
                    EmployeeCode = await AccountHelpers.NextEmployeeCodeAsync(_db),
                    FullName = name,
                    Email = user.Email,
                    JobTitle = AccountHelpers.JobTitleFor(RoleNames.Owner),
                    IsActive = true,
                    CreatedAt = DateTime.Now
                };
                _db.Employees.Add(employee);
            }
            oldName = employee.EmployeeId == 0 ? null : employee.FullName;
            employee.UserId = user.Id;
        }

        if (!linked && oldName == name) return OperationResult.Ok("Họ tên không thay đổi.");
        employee.FullName = name;
        await _db.SaveChangesAsync();
        await _audit.WriteAsync("PROFILE_FULLNAME_UPDATE", "Employees", employee.EmployeeId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            new { fullName = oldName }, new { fullName = name, employee.EmployeeCode }, user.Id);
        return OperationResult.Ok("Đã lưu họ và tên.");
    }

    public async Task<OperationResult> ChangePasswordAsync(IdentityUser user, ChangePasswordViewModel model)
    {
        var result = await _users.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);
        if (!result.Succeeded)
        {
            var field = result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.PasswordMismatch))
                ? nameof(model.CurrentPassword)
                : nameof(model.NewPassword);
            return OperationResult.Fail(AccountHelpers.JoinErrors(result), field);
        }
        await _audit.WriteAsync("PASSWORD_CHANGE", "AspNetUsers", user.Id, null, null, user.Id);
        return OperationResult.Ok("Đã đổi mật khẩu.");
    }

    private async Task<IdentityUser?> UserFromTicketAsync(string ticket)
    {
        if (string.IsNullOrEmpty(ticket)) return null;
        try
        {
            var userId = _tickets.Unprotect(ticket, out _);
            return await _users.FindByIdAsync(userId);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}
