using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using PharmacyManagement.Authorization;
using PharmacyManagement.Configuration;
using PharmacyManagement.Helpers;
using PharmacyManagement.Models.ViewModels.Account;
using PharmacyManagement.Services.Interfaces;

namespace PharmacyManagement.Controllers;

/// <summary>Đăng nhập, kích hoạt tài khoản được mời, quên mật khẩu, đăng nhập lần đầu của Owner, xác minh email.</summary>
public class AccountController : Controller
{
    private readonly SignInManager<IdentityUser> _signIn;
    private readonly UserManager<IdentityUser> _users;
    private readonly IAccountFlowService _flow;
    private readonly IEmailService _email;
    private readonly EmailOptions _emailOptions;

    public AccountController(SignInManager<IdentityUser> signIn, UserManager<IdentityUser> users, IAccountFlowService flow,
        IEmailService email, IOptions<EmailOptions> emailOptions)
    {
        _signIn = signIn;
        _users = users;
        _flow = flow;
        _email = email;
        _emailOptions = emailOptions.Value;
    }

    // ---------------- Đăng nhập / đăng xuất ----------------

    [AllowAnonymous]
    [HttpGet]
    public IActionResult Login(string? returnUrl = null, string? email = null)
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToAction("Index", "Home");
        return View(new LoginViewModel { ReturnUrl = returnUrl, UserNameOrEmail = email ?? string.Empty });
    }

    [AllowAnonymous]
    [HttpPost]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var login = model.UserNameOrEmail.Trim();
        var user = login.Contains('@') ? await _users.FindByEmailAsync(login) : await _users.FindByNameAsync(login);
        if (user is not null && AccountHelpers.IsLockedByOwner(user))
        {
            ModelState.AddModelError(string.Empty, "Tài khoản đã bị khóa. Liên hệ chủ nhà thuốc.");
            return View(model);
        }
        if (user is not null && user.PasswordHash is null)
        {
            ModelState.AddModelError(string.Empty, "Tài khoản chưa kích hoạt. Mở email mời và nhập mã ở trang Kích hoạt tài khoản.");
            return View(model);
        }

        var result = await _signIn.PasswordSignInAsync(user?.UserName ?? login, model.Password, isPersistent: false, lockoutOnFailure: true);
        if (result.Succeeded)
        {
            if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl)) return LocalRedirect(model.ReturnUrl);
            return RedirectToAction("Index", "Home");
        }

        if (result.IsLockedOut)
        {
            ModelState.AddModelError(string.Empty, "Sai mật khẩu quá 5 lần. Tài khoản tạm khóa 15 phút, hoặc dùng Quên mật khẩu.");
            return View(model);
        }

        var message = "Email/tên tài khoản hoặc mật khẩu không đúng.";
        if (user is not null)
        {
            var left = 5 - await _users.GetAccessFailedCountAsync(user);
            if (left is > 0 and < 5) message += $" Còn {left} lần thử.";
        }
        ModelState.AddModelError(string.Empty, message);
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> Logout()
    {
        await _signIn.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult AccessDenied()
    {
        ViewData["NeedsVerifiedEmail"] = User.Identity?.IsAuthenticated == true && !User.HasClaim(AppClaimTypes.EmailVerified, "true");
        return View();
    }

    // ---------------- Kích hoạt tài khoản được mời (1.3.1) ----------------

    [AllowAnonymous]
    [HttpGet]
    public IActionResult Activate(string? email = null)
        => View(new ActivateCodeViewModel { Email = email ?? string.Empty });

    [AllowAnonymous]
    [HttpPost]
    public async Task<IActionResult> Activate(ActivateCodeViewModel model)
    {
        if (!ModelState.IsValid) return View(model);
        var result = await _flow.VerifyActivationCodeAsync(model.Email, model.Code);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(result.Field ?? string.Empty, result.Message!);
            model.Code = string.Empty;
            return View(model);
        }

        var info = await _flow.GetActivationInfoAsync(result.Value!);
        if (info is null)
        {
            ModelState.AddModelError(string.Empty, "Không mở được phiên kích hoạt. Thử lại.");
            return View(model);
        }
        ModelState.Clear();
        return View("ActivatePassword", info);
    }

    [AllowAnonymous]
    [HttpPost]
    public async Task<IActionResult> ActivatePassword(ActivatePasswordViewModel model)
    {
        var info = await _flow.GetActivationInfoAsync(model.Ticket);
        if (info is null)
        {
            TempData["Error"] = "Phiên kích hoạt đã hết hạn hoặc tài khoản đã kích hoạt. Nhập lại mã, hoặc đăng nhập nếu đã có mật khẩu.";
            return RedirectToAction(nameof(Activate));
        }
        if (!ModelState.IsValid) return View(info);

        var result = await _flow.CompleteActivationAsync(model.Ticket, model.NewPassword);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(result.Field ?? string.Empty, result.Message!);
            return View(info);
        }
        TempData["Success"] = result.Message;
        return RedirectToAction(nameof(Login), new { email = info.Email });
    }

    // ---------------- Quên mật khẩu ----------------

    [AllowAnonymous]
    [HttpGet]
    public IActionResult ForgotPassword() => View(new ForgotPasswordViewModel());

    [AllowAnonymous]
    [HttpPost]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
    {
        if (!ModelState.IsValid) return View(model);
        await _flow.SendPasswordResetAsync(model.Email, BaseUrl());
        TempData["Info"] = "Nếu email này thuộc một tài khoản đang hoạt động, mã 6 số đã được gửi tới đó (hiệu lực 15 phút). Kiểm tra cả mục Spam.";
        return RedirectToAction(nameof(ResetPassword), new { email = model.Email.Trim() });
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult ResetPassword(string? email = null)
        => View(new ResetPasswordViewModel { Email = email ?? string.Empty });

    [AllowAnonymous]
    [HttpPost]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
    {
        if (!ModelState.IsValid) return View(model);
        var result = await _flow.ResetPasswordAsync(model);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(result.Field ?? string.Empty, result.Message!);
            model.Code = string.Empty;
            return View(model);
        }
        if (User.Identity?.IsAuthenticated == true) await _signIn.SignOutAsync();
        TempData["Success"] = result.Message;
        return RedirectToAction(nameof(Login), new { email = model.Email.Trim() });
    }

    // ---------------- Owner đăng nhập lần đầu: đổi mật khẩu tạm ----------------

    [HttpGet]
    public async Task<IActionResult> FirstLogin()
    {
        if (!User.HasClaim(AppClaimTypes.MustChangePassword, "true")) return RedirectToAction("Index", "Home");
        var user = await _users.GetUserAsync(User);
        if (user is null) return RedirectToAction(nameof(Login));

        // Điền sẵn họ tên đang có trong hồ sơ, trừ khi đó không phải họ tên thật (ví dụ tên vai trò) — khi ấy để trống bắt nhập.
        var current = await _flow.GetFullNameAsync(user.Id);
        var prefill = current is not null && AccountHelpers.ValidateFullName(current, user.UserName, user.Email) is null ? current : string.Empty;
        return View(new FirstLoginViewModel { UserName = AccountHelpers.DisplayUserName(user.UserName), FullName = prefill });
    }

    [HttpPost]
    public async Task<IActionResult> FirstLogin(FirstLoginViewModel model)
    {
        var user = await _users.GetUserAsync(User);
        if (user is null) return RedirectToAction(nameof(Login));
        model.UserName = AccountHelpers.DisplayUserName(user.UserName);

        var nameError = AccountHelpers.ValidateFullName(AccountHelpers.NormalizeFullName(model.FullName), user.UserName, user.Email);
        if (nameError is not null && ModelState.GetFieldValidationState(nameof(model.FullName)) != Microsoft.AspNetCore.Mvc.ModelBinding.ModelValidationState.Invalid)
            ModelState.AddModelError(nameof(model.FullName), nameError);
        if (!ModelState.IsValid) return View(model);

        var result = await _flow.CompleteFirstLoginAsync(user, model.NewPassword);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(result.Field ?? string.Empty, result.Message!);
            return View(model);
        }
        var named = await _flow.UpdateOwnerFullNameAsync(user, model.FullName);
        await _signIn.RefreshSignInAsync(user);
        if (!named.Succeeded)
        {
            TempData["Warning"] = "Đã đổi mật khẩu nhưng chưa lưu được họ tên: " + named.Message + " Sửa lại ở Tài khoản của tôi.";
            return RedirectToAction(nameof(Manage), new { editProfile = true });
        }
        TempData["Success"] = "Đã lưu họ tên và mật khẩu mới. Bước cuối: xác minh email.";
        return user.EmailConfirmed ? RedirectToAction("Index", "Home") : RedirectToAction(nameof(VerifyEmail));
    }

    // ---------------- Xác minh email (Owner sau khởi tạo / khôi phục) ----------------

    [HttpGet]
    public async Task<IActionResult> VerifyEmail()
    {
        var user = await _users.GetUserAsync(User);
        if (user is null) return RedirectToAction(nameof(Login));
        if (user.EmailConfirmed)
        {
            TempData["Info"] = "Email đã được xác minh.";
            return RedirectToAction("Index", "Home");
        }
        return View(new VerifyEmailViewModel
        {
            Email = user.Email ?? string.Empty,
            CodeSent = TempData["CodeSent"] is string,
            ExpiresAtText = TempData["CodeSent"] as string,
            EmailConfigured = _email.IsConfigured
        });
    }

    [HttpPost]
    public async Task<IActionResult> SendVerification()
    {
        var user = await _users.GetUserAsync(User);
        if (user is null) return RedirectToAction(nameof(Login));
        var result = await _flow.SendEmailVerificationAsync(user);
        if (result.Succeeded)
        {
            TempData["CodeSent"] = AccountHelpers.LocalTime(result.Value);
            TempData["Success"] = $"Đã gửi mã tới {user.Email}.";
        }
        else
        {
            TempData["Error"] = result.Message;
        }
        return RedirectToAction(nameof(VerifyEmail));
    }

    [HttpPost]
    public async Task<IActionResult> VerifyEmail(VerifyEmailViewModel model)
    {
        var user = await _users.GetUserAsync(User);
        if (user is null) return RedirectToAction(nameof(Login));
        model.Email = user.Email ?? string.Empty;
        model.CodeSent = true;
        model.EmailConfigured = _email.IsConfigured;
        if (string.IsNullOrWhiteSpace(model.Code)) ModelState.AddModelError(nameof(model.Code), "Nhập mã 6 số trong email.");
        if (!ModelState.IsValid) return View(model);

        var result = await _flow.ConfirmEmailAsync(user, model.Code!);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(result.Field ?? string.Empty, result.Message!);
            model.Code = null;
            return View(model);
        }
        await _signIn.RefreshSignInAsync(user);
        TempData["Success"] = result.Message;
        return RedirectToAction("Index", "Home");
    }

    // ---------------- Xác nhận email mới do Owner đổi ----------------

    [AllowAnonymous]
    [HttpGet]
    public IActionResult ConfirmEmailChange(string? email = null)
        => View(new ConfirmEmailChangeViewModel { NewEmail = email ?? string.Empty });

    [AllowAnonymous]
    [HttpPost]
    public async Task<IActionResult> ConfirmEmailChange(ConfirmEmailChangeViewModel model)
    {
        if (!ModelState.IsValid) return View(model);
        var result = await _flow.ConfirmEmailChangeAsync(model.NewEmail, model.Code);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(result.Field ?? string.Empty, result.Message!);
            model.Code = string.Empty;
            return View(model);
        }
        if (User.Identity?.IsAuthenticated == true) await _signIn.SignOutAsync();
        TempData["Success"] = result.Message;
        return RedirectToAction(nameof(Login), new { email = model.NewEmail.Trim() });
    }

    // ---------------- Tài khoản của tôi ----------------

    [HttpGet]
    public async Task<IActionResult> Manage(bool editProfile = false)
    {
        var user = await _users.GetUserAsync(User);
        if (user is null) return RedirectToAction(nameof(Login));
        ViewData["Nav"] = "me";
        var model = await BuildManageAsync(user, new ChangePasswordViewModel());
        model.EditingProfile = model.IsOwner && (editProfile || model.FullNameNeedsUpdate);
        return View(model);
    }

    /// <summary>Owner sửa họ và tên đầy đủ. Server kiểm tra lại vai trò (không chỉ ẩn nút).</summary>
    [HttpPost]
    public async Task<IActionResult> UpdateProfile([Bind(Prefix = "Profile")] EditProfileViewModel profile)
    {
        var user = await _users.GetUserAsync(User);
        if (user is null) return RedirectToAction(nameof(Login));
        ViewData["Nav"] = "me";

        OperationResult? result = null;
        if (ModelState.IsValid)
        {
            result = await _flow.UpdateOwnerFullNameAsync(user, profile.FullName);
            if (result.Succeeded)
            {
                await _signIn.RefreshSignInAsync(user); // cập nhật họ tên trong phiên đăng nhập ngay
                TempData["Success"] = result.Message;
                return RedirectToAction(nameof(Manage));
            }
            if (result.Field is null)
            {
                TempData["Error"] = result.Message;
                return RedirectToAction(nameof(Manage));
            }
            ModelState.AddModelError("Profile." + result.Field, result.Message!);
        }

        var model = await BuildManageAsync(user, new ChangePasswordViewModel());
        model.EditingProfile = model.IsOwner;
        model.Profile = profile;
        return View(nameof(Manage), model);
    }

    [HttpPost]
    public async Task<IActionResult> ChangePassword([Bind(Prefix = "ChangePassword")] ChangePasswordViewModel model)
    {
        var user = await _users.GetUserAsync(User);
        if (user is null) return RedirectToAction(nameof(Login));
        ViewData["Nav"] = "me";
        if (!ModelState.IsValid) return View(nameof(Manage), await BuildManageAsync(user, model));

        var result = await _flow.ChangePasswordAsync(user, model);
        if (!result.Succeeded)
        {
            ModelState.AddModelError("ChangePassword." + (result.Field ?? string.Empty), result.Message!);
            return View(nameof(Manage), await BuildManageAsync(user, new ChangePasswordViewModel()));
        }
        await _signIn.RefreshSignInAsync(user);
        TempData["Success"] = result.Message;
        return RedirectToAction(nameof(Manage));
    }

    private async Task<ManageViewModel> BuildManageAsync(IdentityUser user, ChangePasswordViewModel changePassword)
    {
        var role = (await _users.GetRolesAsync(user)).FirstOrDefault();
        var fullName = await _flow.GetFullNameAsync(user.Id);
        var needsUpdate = fullName is null || AccountHelpers.ValidateFullName(fullName, user.UserName, user.Email) is not null;
        return new ManageViewModel
        {
            FullNameNeedsUpdate = needsUpdate,
            UserName = AccountHelpers.DisplayUserName(user.UserName),
            Email = user.Email ?? string.Empty,
            FullName = fullName,
            RoleDisplay = RoleNames.DisplayName(role),
            EmailVerified = user.EmailConfirmed,
            IsOwner = role == RoleNames.Owner,
            Profile = new EditProfileViewModel { FullName = needsUpdate ? string.Empty : fullName! },
            ChangePassword = changePassword
        };
    }

    private string BaseUrl()
        => string.IsNullOrWhiteSpace(_emailOptions.PublicBaseUrl) ? $"{Request.Scheme}://{Request.Host}" : _emailOptions.PublicBaseUrl!;
}
