using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PharmacyManagement.Authorization;
using PharmacyManagement.Data;

namespace PharmacyManagement.Identity;

/// <summary>
/// Thêm vào cookie đăng nhập: vai trò (Identity tự thêm), claim trong AspNetUserClaims (ví dụ bắt đổi mật khẩu),
/// trạng thái xác minh email, mã và họ tên nhân viên.
/// </summary>
public sealed class AppClaimsPrincipalFactory : UserClaimsPrincipalFactory<IdentityUser, IdentityRole>
{
    private readonly PharmacyDbContext _db;

    public AppClaimsPrincipalFactory(UserManager<IdentityUser> userManager, RoleManager<IdentityRole> roleManager,
        IOptions<IdentityOptions> options, PharmacyDbContext db)
        : base(userManager, roleManager, options)
    {
        _db = db;
    }

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(IdentityUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim(AppClaimTypes.EmailVerified, user.EmailConfirmed ? "true" : "false"));

        var employee = await _db.Employees.AsNoTracking()
            .Where(e => e.UserId == user.Id)
            .Select(e => new { e.EmployeeId, e.FullName })
            .FirstOrDefaultAsync();
        if (employee is not null)
        {
            identity.AddClaim(new Claim(AppClaimTypes.EmployeeId, employee.EmployeeId.ToString(CultureInfo.InvariantCulture)));
            identity.AddClaim(new Claim(AppClaimTypes.FullName, employee.FullName));
        }

        return identity;
    }
}
