using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PharmacyManagement.Authorization;
using PharmacyManagement.Configuration;
using PharmacyManagement.Data;
using PharmacyManagement.Helpers;
using PharmacyManagement.Models.Entities;
using PharmacyManagement.Services.Interfaces;

namespace PharmacyManagement.Services.Implementations;

/// <summary>
/// Khởi tạo Owner đầu tiên (DEC-18) — chạy lúc ứng dụng khởi động, CHỈ ở môi trường Development.
/// Điều kiện: chưa có tài khoản Owner, chưa có mốc OwnerSetupCompletedAt, đã đặt Bootstrap:OwnerEmail và
/// Bootstrap:OwnerInitialPassword và họ tên (Bootstrap:OwnerFullName, hoặc Bootstrap:OwnerEmployeeCode trỏ tới hồ sơ có sẵn)
/// trong User Secrets. Tên tài khoản = phần trước @ của email. Không bao giờ tự điền họ tên bằng tên vai trò. Mật khẩu tạm được băm bằng Identity (bỏ qua chính sách độ mạnh
/// cho riêng mật khẩu tạm này), tài khoản bị bắt đổi mật khẩu và chưa xác minh email.
/// Sau khi tạo, ghi mốc OwnerSetupCompletedAt → không bao giờ chạy lại, kể cả khi đổi cấu hình.
/// </summary>
public sealed class OwnerBootstrapService
{
    public const string SetupCompletedKey = "OwnerSetupCompletedAt";

    private readonly PharmacyDbContext _db;
    private readonly UserManager<IdentityUser> _users;
    private readonly IPasswordHasher<IdentityUser> _hasher;
    private readonly IAuditLogService _audit;
    private readonly IHostEnvironment _env;
    private readonly BootstrapOptions _options;
    private readonly ILogger<OwnerBootstrapService> _logger;

    public OwnerBootstrapService(PharmacyDbContext db, UserManager<IdentityUser> users, IPasswordHasher<IdentityUser> hasher,
        IAuditLogService audit, IHostEnvironment env, IOptions<BootstrapOptions> options, ILogger<OwnerBootstrapService> logger)
    {
        _db = db;
        _users = users;
        _hasher = hasher;
        _audit = audit;
        _env = env;
        _options = options.Value;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        var owners = await _users.GetUsersInRoleAsync(RoleNames.Owner);
        if (owners.Count > 0) return; // đã có Owner — cơ chế khởi tạo đóng

        if (await _db.PharmacySettings.AsNoTracking().AnyAsync(s => s.SettingKey == SetupCompletedKey, ct))
        {
            _logger.LogError("Đã khởi tạo Owner trước đây nhưng hiện không còn tài khoản Owner. Không tạo lại; dùng lệnh owner-recovery trên máy chủ.");
            return;
        }

        if (!_env.IsDevelopment())
        {
            _logger.LogWarning("Chưa có tài khoản Owner. Môi trường {Env} không dùng mật khẩu tạm; khởi tạo bằng lệnh owner-recovery trên máy chủ.", _env.EnvironmentName);
            return;
        }

        var email = _options.OwnerEmail?.Trim();
        var password = _options.OwnerInitialPassword;
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
        {
            _logger.LogWarning("Chưa có tài khoản Owner và chưa đặt Bootstrap:OwnerEmail / Bootstrap:OwnerInitialPassword trong User Secrets.");
            return;
        }
        if (await _users.FindByEmailAsync(email) is not null)
        {
            _logger.LogError("Email {Email} đã thuộc một tài khoản khác — không khởi tạo Owner.", email);
            return;
        }
        var userNameConflict = await AccountHelpers.UserNameConflictAsync(_users, email);
        if (userNameConflict is not null)
        {
            _logger.LogError("Không khởi tạo Owner: {Reason}", userNameConflict);
            return;
        }

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var employee = await ResolveEmployeeAsync(email, ct);
        if (employee is null) return;

        var user = new IdentityUser { UserName = AccountHelpers.UserNameFromEmail(email), Email = email, EmailConfirmed = false, LockoutEnabled = true };
        user.PasswordHash = _hasher.HashPassword(user, password);
        var created = await _users.CreateAsync(user); // không kiểm tra độ mạnh: chỉ mật khẩu tạm, bắt đổi ngay lần đầu
        if (!created.Succeeded)
        {
            _logger.LogError("Không tạo được tài khoản Owner: {Errors}", AccountHelpers.JoinErrors(created));
            return;
        }

        var steps = new[]
        {
            await _users.AddToRoleAsync(user, RoleNames.Owner),
            await _users.AddClaimAsync(user, new Claim(AppClaimTypes.MustChangePassword, "true"))
        };
        if (steps.Any(r => !r.Succeeded))
        {
            _logger.LogError("Không gán được vai trò Owner: {Errors}", string.Join(" ", steps.Select(AccountHelpers.JoinErrors)));
            return;
        }

        employee.UserId = user.Id;
        employee.Email = email;
        var now = DateTime.Now;
        _db.PharmacySettings.Add(new PharmacySetting
        {
            SettingKey = SetupCompletedKey,
            SettingValue = now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            Description = "Thời điểm khởi tạo tài khoản Owner đầu tiên — cơ chế khởi tạo đã đóng (DEC-18)",
            UpdatedAt = now,
            UpdatedByEmployeeId = null
        });
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("OWNER_BOOTSTRAP", "AspNetUsers", user.Id, null,
            new { email, userName = user.UserName, employee.EmployeeCode, employee.FullName, environment = _env.EnvironmentName }, user.Id, ct);
        await tx.CommitAsync(ct);

        _logger.LogWarning("ĐÃ KHỞI TẠO tài khoản Owner {Email} (mật khẩu tạm từ User Secrets). Đăng nhập, đổi mật khẩu và xác minh email ngay.", email);
    }

    /// <summary>
    /// Hồ sơ nhân viên của Owner: dùng hồ sơ có mã Bootstrap:OwnerEmployeeCode, hoặc hồ sơ chưa gắn tài khoản có cùng email;
    /// chỉ tạo mới khi không có — để không sinh hồ sơ trùng. Họ tên lấy từ Bootstrap:OwnerFullName nếu có (Owner sửa lại được
    /// ở bước đổi mật khẩu đầu tiên và trang Tài khoản của tôi).
    /// </summary>
    private async Task<Employee?> ResolveEmployeeAsync(string email, CancellationToken ct)
    {
        var userName = AccountHelpers.UserNameFromEmail(email);
        string? fullName = null;
        if (!string.IsNullOrWhiteSpace(_options.OwnerFullName))
        {
            fullName = AccountHelpers.NormalizeFullName(_options.OwnerFullName);
            var nameError = AccountHelpers.ValidateFullName(fullName, userName, email);
            if (nameError is not null)
            {
                _logger.LogError("Bootstrap:OwnerFullName không hợp lệ: {Reason} Không khởi tạo Owner.", nameError);
                return null;
            }
        }

        var code = _options.OwnerEmployeeCode?.Trim();
        Employee? existing = null;
        if (!string.IsNullOrEmpty(code))
            existing = await _db.Employees.FirstOrDefaultAsync(e => e.EmployeeCode == code, ct);
        existing ??= await _db.Employees.FirstOrDefaultAsync(e => e.UserId == null && e.IsActive && e.Email == email, ct);

        if (existing is not null)
        {
            if (existing.UserId is not null)
            {
                _logger.LogError("Nhân viên {Code} đã gắn với một tài khoản khác — không khởi tạo Owner.", existing.EmployeeCode);
                return null;
            }
            if (fullName is not null) existing.FullName = fullName;
            return existing;
        }

        if (fullName is null)
        {
            _logger.LogError("Chưa có họ tên chủ nhà thuốc. Đặt Bootstrap:OwnerFullName (họ và tên đầy đủ) hoặc Bootstrap:OwnerEmployeeCode (mã hồ sơ nhân viên có sẵn) trong User Secrets. Không khởi tạo Owner.");
            return null;
        }

        var employee = new Employee
        {
            EmployeeCode = string.IsNullOrEmpty(code) ? await AccountHelpers.NextEmployeeCodeAsync(_db, ct) : code,
            FullName = fullName,
            Email = email,
            JobTitle = AccountHelpers.JobTitleFor(RoleNames.Owner),
            IsActive = true,
            CreatedAt = DateTime.Now
        };
        _db.Employees.Add(employee);
        await _db.SaveChangesAsync(ct);
        return employee;
    }
}
