using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PharmacyManagement.Data;
using PharmacyManagement.Helpers;
using PharmacyManagement.Services.Interfaces;

namespace PharmacyManagement.Services.Implementations;

/// <summary>
/// DEC-21: tên tài khoản (UserName) = phần trước @ của email. Tài khoản tạo trước quy tắc này có UserName là cả email;
/// lúc ứng dụng khởi động, dịch vụ này đổi UserName của chúng sang dạng mới. Chạy lại không làm gì thêm.
/// Không đổi email, mật khẩu, vai trò; không đăng xuất người dùng (giao diện cập nhật trong vòng 1 phút).
/// Tên bị trùng với tài khoản khác thì giữ nguyên và ghi cảnh báo để xử lý tay.
/// </summary>
public sealed class UserNameSyncService
{
    private readonly PharmacyDbContext _db;
    private readonly UserManager<IdentityUser> _users;
    private readonly IAuditLogService _audit;
    private readonly ILogger<UserNameSyncService> _logger;

    public UserNameSyncService(PharmacyDbContext db, UserManager<IdentityUser> users, IAuditLogService audit, ILogger<UserNameSyncService> logger)
    {
        _db = db;
        _users = users;
        _audit = audit;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        var pending = await _db.Users.Where(u => u.UserName != null && u.UserName.Contains("@")).ToListAsync(ct);
        foreach (var user in pending)
        {
            var target = AccountHelpers.UserNameFromEmail(user.Email ?? user.UserName);
            if (string.IsNullOrEmpty(target) || target == user.UserName) continue;

            var other = await _users.FindByNameAsync(target);
            if (other is not null && other.Id != user.Id)
            {
                _logger.LogWarning("Không đổi tên tài khoản {Old} thành {New}: tên này đã thuộc tài khoản khác.", user.UserName, target);
                continue;
            }

            var old = user.UserName;
            user.UserName = target;
            var result = await _users.UpdateAsync(user); // cập nhật NormalizedUserName, kiểm tra hợp lệ, không đổi SecurityStamp
            if (!result.Succeeded)
            {
                user.UserName = old;
                _logger.LogWarning("Không đổi tên tài khoản {Old}: {Errors}", old, AccountHelpers.JoinErrors(result));
                continue;
            }
            await _audit.WriteAsync("USERNAME_SYNC", "AspNetUsers", user.Id, new { userName = old }, new { userName = target }, user.Id, ct);
            _logger.LogInformation("Đổi tên tài khoản {Old} → {New} (DEC-21).", old, target);
        }
    }
}
