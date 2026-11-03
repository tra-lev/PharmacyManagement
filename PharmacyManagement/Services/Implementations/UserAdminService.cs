using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PharmacyManagement.Authorization;
using PharmacyManagement.Data;
using PharmacyManagement.Helpers;
using PharmacyManagement.Models.Entities;
using PharmacyManagement.Models.ViewModels.Admin;
using PharmacyManagement.Services.Interfaces;

namespace PharmacyManagement.Services.Implementations;

public sealed class UserAdminService : IUserAdminService
{
    private static readonly TimeSpan ResendCooldown = TimeSpan.FromSeconds(60);

    private readonly PharmacyDbContext _db;
    private readonly UserManager<IdentityUser> _users;
    private readonly IVerificationCodeService _codes;
    private readonly IEmailService _email;
    private readonly IAuditLogService _audit;
    private readonly ILogger<UserAdminService> _logger;

    public UserAdminService(PharmacyDbContext db, UserManager<IdentityUser> users, IVerificationCodeService codes,
        IEmailService email, IAuditLogService audit, ILogger<UserAdminService> logger)
    {
        _db = db;
        _users = users;
        _codes = codes;
        _email = email;
        _audit = audit;
        _logger = logger;
    }

    public async Task<UserListViewModel> GetListAsync(InviteUserViewModel? invite = null, bool openInvite = false, CancellationToken ct = default)
    {
        var employees = await _db.Employees.AsNoTracking()
            .OrderBy(e => e.EmployeeCode)
            .Select(e => new { e.EmployeeId, e.EmployeeCode, e.FullName, e.Email, e.UserId, e.IsActive })
            .ToListAsync(ct);

        var userIds = employees.Where(e => e.UserId != null).Select(e => e.UserId!).ToList();
        var users = await _db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToListAsync(ct);
        var userById = users.ToDictionary(u => u.Id);

        var roleRows = await (from ur in _db.UserRoles
                              join r in _db.Roles on ur.RoleId equals r.Id
                              where userIds.Contains(ur.UserId)
                              select new { ur.UserId, r.Name }).ToListAsync(ct);
        var roleByUser = roleRows.GroupBy(x => x.UserId).ToDictionary(g => g.Key, g => g.First().Name);

        var tokenRows = await _db.UserTokens.AsNoTracking()
            .Where(t => t.LoginProvider == VerificationCodeService.Provider && userIds.Contains(t.UserId))
            .Select(t => new { t.UserId, t.Name, t.Value })
            .ToListAsync(ct);
        var tokensByUser = tokenRows.GroupBy(t => t.UserId)
            .ToDictionary(g => g.Key, g => g.ToDictionary(t => t.Name, t => t.Value));

        var employeeIds = employees.Select(e => (long)e.EmployeeId).ToList();
        var logs = await _db.IntegrationLogs.AsNoTracking()
            .Where(l => l.IntegrationType == "EMAIL" && l.ReferenceType == "Employee" && employeeIds.Contains(l.ReferenceId))
            .OrderByDescending(l => l.IntegrationLogId)
            .Take(500)
            .Select(l => new { l.ReferenceId, l.Status, l.ErrorMessage })
            .ToListAsync(ct);
        var lastLogByEmployee = logs.GroupBy(l => l.ReferenceId).ToDictionary(g => g.Key, g => g.First());

        var now = DateTimeOffset.UtcNow;
        var rows = new List<UserRowViewModel>();
        foreach (var e in employees)
        {
            if (e.UserId is null || !userById.TryGetValue(e.UserId, out var user))
            {
                rows.Add(new UserRowViewModel
                {
                    EmployeeId = e.EmployeeId, EmployeeCode = e.EmployeeCode, FullName = e.FullName, Email = e.Email,
                    State = e.IsActive ? AccountState.NoAccount : AccountState.FormerEmployee,
                    Detail = e.IsActive ? "Chưa có tài khoản" : "Đã nghỉ việc, không có tài khoản"
                });
                continue;
            }

            var role = roleByUser.GetValueOrDefault(user.Id);
            var tokens = tokensByUser.GetValueOrDefault(user.Id) ?? new Dictionary<string, string?>();
            string? pendingNewEmail = tokens.GetValueOrDefault(CodePurposes.ChangeEmail + ":payload");
            AccountState state;
            string detail;

            if (role == RoleNames.Owner)
            {
                state = user.EmailConfirmed ? AccountState.Owner : AccountState.OwnerUnverified;
                detail = user.EmailConfirmed ? "Chủ nhà thuốc duy nhất · email đã xác minh" : "Chủ nhà thuốc duy nhất · chưa xác minh email";
            }
            else if (AccountHelpers.IsLockedByOwner(user))
            {
                state = AccountState.Locked;
                detail = "Đã khóa — không đăng nhập được";
            }
            else if (user.PasswordHash is null)
            {
                var expiry = ParseExpiry(tokens.GetValueOrDefault(CodePurposes.Invite + ":exp"));
                lastLogByEmployee.TryGetValue(e.EmployeeId, out var lastLog);
                if (expiry is not null && expiry > now)
                {
                    state = AccountState.Pending;
                    detail = "Mã mời còn hiệu lực đến " + AccountHelpers.LocalTime(expiry.Value);
                }
                else if (lastLog is not null && lastLog.Status == 2)
                {
                    state = AccountState.SendFailed;
                    detail = "Gửi email thất bại: " + FirstPart(lastLog.ErrorMessage);
                }
                else
                {
                    state = AccountState.PendingExpired;
                    detail = "Mã mời đã hết hạn hoặc đã bị hủy — gửi lại mã";
                }
            }
            else if (AccountHelpers.IsTemporarilyLocked(user))
            {
                state = AccountState.TemporarilyLocked;
                detail = "Tạm khóa do sai mật khẩu đến " + AccountHelpers.LocalTime(user.LockoutEnd!.Value);
            }
            else
            {
                state = AccountState.Active;
                detail = pendingNewEmail is null ? "Đang hoạt động" : "Chờ nhân viên xác nhận email mới: " + pendingNewEmail;
            }

            rows.Add(new UserRowViewModel
            {
                EmployeeId = e.EmployeeId, EmployeeCode = e.EmployeeCode, FullName = e.FullName,
                Email = user.Email, UserName = AccountHelpers.DisplayUserName(user.UserName),
                RoleName = role, State = state, Detail = detail, PendingNewEmail = pendingNewEmail
            });
        }

        var invitable = employees
            .Where(e => e.IsActive && e.UserId is null)
            .Select(e => new EmployeeOptionViewModel(e.EmployeeId, e.EmployeeCode + " · " + e.FullName, e.Email))
            .ToList();

        return new UserListViewModel
        {
            Rows = rows.OrderBy(r => r.State is AccountState.Owner or AccountState.OwnerUnverified ? 0 : 1).ThenBy(r => r.EmployeeCode).ToList(),
            InvitableEmployees = invitable,
            Invite = invite ?? new InviteUserViewModel
            {
                EmployeeId = invitable.FirstOrDefault()?.EmployeeId,
                Email = invitable.FirstOrDefault()?.Email ?? string.Empty,
                NewEmployee = invitable.Count == 0
            },
            EmailConfigured = _email.IsConfigured,
            OpenInvite = openInvite
        };
    }

    public async Task<OperationResult> InviteAsync(InviteUserViewModel model, string actorUserId, string baseUrl, CancellationToken ct = default)
    {
        if (!_email.IsConfigured)
            return OperationResult.Fail("Chưa cấu hình tài khoản gửi email nên chưa mời được. Đặt Email:SenderAddress và Email:AppPassword trong User Secrets.");
        if (!RoleNames.Invitable.Contains(model.Role))
            return OperationResult.Fail("Chỉ mời được Quản lý hoặc Dược sĩ.", nameof(model.Role));

        var email = model.Email.Trim();
        var existing = await _users.FindByEmailAsync(email);
        if (existing is not null)
        {
            var who = await AccountHelpers.EmployeeNameOfUserAsync(_db, existing.Id, ct);
            return OperationResult.Fail($"Email này đã thuộc tài khoản của {who ?? "người khác"}. Mỗi email chỉ dùng cho một tài khoản.", nameof(model.Email));
        }
        var userNameConflict = await AccountHelpers.UserNameConflictAsync(_users, email);
        if (userNameConflict is not null) return OperationResult.Fail(userNameConflict, nameof(model.Email));

        Employee? employee;
        if (model.NewEmployee)
        {
            var fullName = AccountHelpers.NormalizeFullName(model.FullName);
            var nameError = AccountHelpers.ValidateFullName(fullName, AccountHelpers.UserNameFromEmail(email), email);
            if (nameError is not null) return OperationResult.Fail(nameError, nameof(model.FullName));
            employee = new Employee
            {
                EmployeeCode = await AccountHelpers.NextEmployeeCodeAsync(_db, ct),
                FullName = fullName,
                Phone = string.IsNullOrWhiteSpace(model.Phone) ? null : model.Phone.Trim(),
                JobTitle = AccountHelpers.JobTitleFor(model.Role),
                IsActive = true,
                CreatedAt = DateTime.Now
            };
        }
        else
        {
            if (model.EmployeeId is null)
                return OperationResult.Fail("Chọn nhân viên cần mời.", nameof(model.EmployeeId));
            employee = await _db.Employees.FirstOrDefaultAsync(e => e.EmployeeId == model.EmployeeId, ct);
            if (employee is null || !employee.IsActive)
                return OperationResult.Fail("Nhân viên không tồn tại hoặc đã nghỉ việc.", nameof(model.EmployeeId));
            if (employee.UserId is not null)
                return OperationResult.Fail("Nhân viên này đã có tài khoản.", nameof(model.EmployeeId));
        }

        var user = new IdentityUser { UserName = AccountHelpers.UserNameFromEmail(email), Email = email, EmailConfirmed = false, LockoutEnabled = true };
        try
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            if (model.NewEmployee)
            {
                _db.Employees.Add(employee);
                await _db.SaveChangesAsync(ct);
            }

            var created = await _users.CreateAsync(user);
            if (!created.Succeeded) return OperationResult.Fail(AccountHelpers.JoinErrors(created), nameof(model.Email));

            var roleResult = await _users.AddToRoleAsync(user, model.Role);
            if (!roleResult.Succeeded) return OperationResult.Fail(AccountHelpers.JoinErrors(roleResult));

            employee.UserId = user.Id;
            employee.Email = email;
            await _db.SaveChangesAsync(ct);

            await _audit.WriteAsync("ACCOUNT_INVITE", "AspNetUsers", user.Id, null,
                new { email, role = model.Role, employee.EmployeeCode, employee.FullName }, actorUserId, ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Mời tài khoản {Email} thất bại", email);
            _db.ChangeTracker.Clear();
            return OperationResult.Fail("Không lưu được lời mời: email có thể vừa được dùng cho tài khoản khác. Tải lại trang và thử lại.", nameof(model.Email));
        }

        return await SendInviteCodeAsync(user, employee, model.Role, baseUrl, ct);
    }

    public async Task<OperationResult> ResendInviteAsync(int employeeId, string actorUserId, string baseUrl, CancellationToken ct = default)
    {
        var (employee, user, role) = await LoadAsync(employeeId, ct);
        if (employee is null || user is null) return OperationResult.Fail("Không tìm thấy lời mời.");
        if (role == RoleNames.Owner) return OperationResult.Fail("Không áp dụng cho tài khoản chủ nhà thuốc.");
        if (user.PasswordHash is not null) return OperationResult.Fail("Tài khoản đã kích hoạt, không cần gửi lại mã.");
        if (!_email.IsConfigured) return OperationResult.Fail("Chưa cấu hình tài khoản gửi email (User Secrets).");

        var expiry = await _codes.GetExpiryAsync(user, CodePurposes.Invite);
        if (expiry is not null && expiry.Value - _codes.Lifetime > DateTimeOffset.UtcNow - ResendCooldown)
            return OperationResult.Fail("Vừa gửi mã chưa đầy 1 phút. Đợi một chút rồi gửi lại.");

        await _audit.WriteAsync("ACCOUNT_INVITE_RESEND", "AspNetUsers", user.Id, null, new { user.Email }, actorUserId, ct);
        return await SendInviteCodeAsync(user, employee, role ?? RoleNames.Pharmacist, baseUrl, ct);
    }

    public async Task<OperationResult> CancelInviteAsync(int employeeId, string actorUserId, CancellationToken ct = default)
    {
        var (employee, user, role) = await LoadAsync(employeeId, ct);
        if (employee is null || user is null) return OperationResult.Fail("Không tìm thấy lời mời.");
        if (role == RoleNames.Owner || user.PasswordHash is not null)
            return OperationResult.Fail("Chỉ hủy được lời mời chưa kích hoạt.");

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        employee.UserId = null;
        await _db.SaveChangesAsync(ct);
        var deleted = await _users.DeleteAsync(user);
        if (!deleted.Succeeded) return OperationResult.Fail(AccountHelpers.JoinErrors(deleted));
        await _audit.WriteAsync("ACCOUNT_INVITE_CANCEL", "AspNetUsers", user.Id, new { user.Email, employee.EmployeeCode }, null, actorUserId, ct);
        await tx.CommitAsync(ct);
        return OperationResult.Ok($"Đã hủy lời mời của {employee.FullName}.");
    }

    public async Task<OperationResult> ChangeEmailAsync(ChangeEmailViewModel model, string actorUserId, string baseUrl, CancellationToken ct = default)
    {
        var (employee, user, role) = await LoadAsync(model.EmployeeId, ct);
        if (employee is null || user is null) return OperationResult.Fail("Không tìm thấy tài khoản.");
        if (role == RoleNames.Owner) return OperationResult.Fail("Email của chủ nhà thuốc không đổi ở đây (dùng quy trình khôi phục trên máy chủ).");
        if (user.PasswordHash is null) return OperationResult.Fail("Tài khoản chưa kích hoạt — hãy hủy lời mời rồi mời lại bằng email đúng.");
        if (!_email.IsConfigured) return OperationResult.Fail("Chưa cấu hình tài khoản gửi email (User Secrets).");

        var newEmail = model.NewEmail.Trim();
        if (string.Equals(newEmail, user.Email, StringComparison.OrdinalIgnoreCase))
            return OperationResult.Fail("Email mới trùng email hiện tại.", nameof(model.NewEmail));
        var other = await _users.FindByEmailAsync(newEmail);
        if (other is not null)
        {
            var who = await AccountHelpers.EmployeeNameOfUserAsync(_db, other.Id, ct);
            return OperationResult.Fail($"Email này đã thuộc tài khoản của {who ?? "người khác"}.", nameof(model.NewEmail));
        }
        var userNameConflict = await AccountHelpers.UserNameConflictAsync(_users, newEmail, user.Id);
        if (userNameConflict is not null) return OperationResult.Fail(userNameConflict, nameof(model.NewEmail));

        var code = await _codes.IssueAsync(user, CodePurposes.ChangeEmail, newEmail);
        var ownerName = await AccountHelpers.OwnerNameAsync(_db, ct);
        var confirmUrl = AccountHelpers.BuildUrl(baseUrl, "/Account/ConfirmEmailChange?email=" + Uri.EscapeDataString(newEmail));
        var sent = await _email.SendAsync(EmailTemplates.ChangeEmail(newEmail, employee.FullName, ownerName, code, confirmUrl),
            CodePurposes.ChangeEmail, employee.EmployeeId, ct);
        if (!sent.Succeeded)
        {
            await _codes.RevokeAsync(user, CodePurposes.ChangeEmail);
            return OperationResult.Fail("Chưa gửi được mã tới email mới: " + sent.ErrorMessage);
        }

        await _audit.WriteAsync("ACCOUNT_EMAIL_CHANGE_REQUEST", "AspNetUsers", user.Id,
            new { user.Email }, new { email = newEmail }, actorUserId, ct);
        return OperationResult.Ok($"Đã gửi mã tới {newEmail}. Email mới có hiệu lực khi {employee.FullName} nhập đúng mã; trước đó vẫn đăng nhập bằng email cũ.");
    }

    public async Task<OperationResult> LockAsync(LockUserViewModel model, string actorUserId, CancellationToken ct = default)
    {
        var (employee, user, role) = await LoadAsync(model.EmployeeId, ct);
        if (employee is null || user is null) return OperationResult.Fail("Không tìm thấy tài khoản.");
        if (role == RoleNames.Owner || user.Id == actorUserId)
            return OperationResult.Fail("Không được khóa tài khoản chủ nhà thuốc.");
        if (string.IsNullOrWhiteSpace(model.Reason)) return OperationResult.Fail("Nhập lý do khóa.", nameof(model.Reason));

        await _users.SetLockoutEnabledAsync(user, true);
        var locked = await _users.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
        if (!locked.Succeeded) return OperationResult.Fail(AccountHelpers.JoinErrors(locked));
        await _users.UpdateSecurityStampAsync(user); // đăng xuất mọi phiên đang mở
        await _audit.WriteAsync("ACCOUNT_LOCK", "AspNetUsers", user.Id, null, new { reason = model.Reason.Trim(), employee.EmployeeCode }, actorUserId, ct);
        return OperationResult.Ok($"Đã khóa tài khoản của {employee.FullName}.");
    }

    public async Task<OperationResult> UnlockAsync(int employeeId, string actorUserId, CancellationToken ct = default)
    {
        var (employee, user, role) = await LoadAsync(employeeId, ct);
        if (employee is null || user is null) return OperationResult.Fail("Không tìm thấy tài khoản.");
        if (role == RoleNames.Owner) return OperationResult.Fail("Tài khoản chủ nhà thuốc không bị khóa ở đây.");

        var result = await _users.SetLockoutEndDateAsync(user, null);
        if (!result.Succeeded) return OperationResult.Fail(AccountHelpers.JoinErrors(result));
        await _users.ResetAccessFailedCountAsync(user);
        await _audit.WriteAsync("ACCOUNT_UNLOCK", "AspNetUsers", user.Id, null, new { employee.EmployeeCode }, actorUserId, ct);
        return OperationResult.Ok($"Đã mở khóa tài khoản của {employee.FullName}.");
    }

    private async Task<OperationResult> SendInviteCodeAsync(IdentityUser user, Employee employee, string roleName, string baseUrl, CancellationToken ct)
    {
        var code = await _codes.IssueAsync(user, CodePurposes.Invite);
        var ownerName = await AccountHelpers.OwnerNameAsync(_db, ct);
        var activateUrl = AccountHelpers.BuildUrl(baseUrl, "/Account/Activate?email=" + Uri.EscapeDataString(user.Email!));
        var message = EmailTemplates.Invite(user.Email!, employee.FullName, ownerName, RoleNames.DisplayName(roleName), code, activateUrl);
        var sent = await _email.SendAsync(message, CodePurposes.Invite, employee.EmployeeId, ct);
        if (!sent.Succeeded)
        {
            await _codes.RevokeAsync(user, CodePurposes.Invite);
            return OperationResult.Ok(warning: $"Đã tạo lời mời cho {employee.FullName} nhưng chưa gửi được email. {sent.ErrorMessage} Khắc phục xong bấm “Gửi lại mã”.");
        }
        return OperationResult.Ok($"Đã gửi mã kích hoạt tới {user.Email}. Mã hiệu lực {(int)_codes.Lifetime.TotalMinutes} phút.");
    }

    private async Task<(Employee? Employee, IdentityUser? User, string? Role)> LoadAsync(int employeeId, CancellationToken ct)
    {
        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.EmployeeId == employeeId, ct);
        if (employee?.UserId is null) return (employee, null, null);
        var user = await _users.FindByIdAsync(employee.UserId);
        if (user is null) return (employee, null, null);
        var role = (await _users.GetRolesAsync(user)).FirstOrDefault();
        return (employee, user, role);
    }

    private static DateTimeOffset? ParseExpiry(string? raw)
        => long.TryParse(raw, out var seconds) ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null;

    private static string FirstPart(string? error)
    {
        if (string.IsNullOrEmpty(error)) return "không rõ nguyên nhân";
        var i = error.IndexOf(" | ", StringComparison.Ordinal);
        return i < 0 ? error : error[..i];
    }
}
