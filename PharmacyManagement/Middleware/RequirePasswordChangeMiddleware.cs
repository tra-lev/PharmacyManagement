using PharmacyManagement.Authorization;

namespace PharmacyManagement.Middleware;

/// <summary>
/// Tài khoản có claim bắt đổi mật khẩu (Owner đăng nhập lần đầu bằng mật khẩu tạm) chỉ được vào trang đổi mật khẩu
/// và đăng xuất; mọi trang khác chuyển về /Account/FirstLogin.
/// </summary>
public sealed class RequirePasswordChangeMiddleware
{
    private readonly RequestDelegate _next;

    public RequirePasswordChangeMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true
            && context.User.HasClaim(AppClaimTypes.MustChangePassword, "true")
            && !IsAllowed(context.Request.Path))
        {
            context.Response.Redirect("/Account/FirstLogin");
            return;
        }

        await _next(context);
    }

    private static bool IsAllowed(PathString path)
        => path.StartsWithSegments("/Account/FirstLogin", StringComparison.OrdinalIgnoreCase)
           || path.StartsWithSegments("/Account/Logout", StringComparison.OrdinalIgnoreCase)
           || Path.HasExtension(path.Value);
}
