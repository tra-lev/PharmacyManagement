using Microsoft.AspNetCore.Identity;

namespace PharmacyManagement.Services.Interfaces;

/// <summary>Mục đích của mã 6 số. Mỗi mục đích một bộ mã riêng cho mỗi tài khoản.</summary>
public static class CodePurposes
{
    /// <summary>Nhân viên được mời kích hoạt tài khoản (1.3.1).</summary>
    public const string Invite = "invite";
    /// <summary>Xác minh email của tài khoản đang dùng (Owner sau khởi tạo).</summary>
    public const string VerifyEmail = "verify-email";
    /// <summary>Quên mật khẩu / khôi phục Owner.</summary>
    public const string ResetPassword = "reset";
    /// <summary>Owner đổi email cho nhân viên; payload = email mới (đã chuẩn hóa).</summary>
    public const string ChangeEmail = "change-email";
}

public enum CodeCheckStatus
{
    Ok,
    Invalid,
    Expired,
    TooManyAttempts,
    NotFound
}

public sealed record IssuedCode(string Code, DateTimeOffset ExpiresAt);

public sealed record CodeCheckResult(CodeCheckStatus Status, int AttemptsLeft, string? Payload)
{
    public bool Succeeded => Status == CodeCheckStatus.Ok;
}

/// <summary>
/// Sinh và kiểm tra mã 6 số. Chỉ lưu BẢN BĂM của mã trong AspNetUserTokens (LoginProvider = PharmacyVerify),
/// kèm hạn dùng 15 phút và số lần nhập sai; sai 5 lần thì mã bị hủy.
/// </summary>
public interface IVerificationCodeService
{
    TimeSpan Lifetime { get; }
    int MaxAttempts { get; }

    /// <summary>Sinh mã mới (mã cũ cùng mục đích hết hiệu lực).</summary>
    Task<IssuedCode> IssueAsync(IdentityUser user, string purpose, string? payload = null);

    /// <summary>Kiểm tra mã; đúng thì hủy mã (dùng một lần).</summary>
    Task<CodeCheckResult> VerifyAsync(IdentityUser user, string purpose, string code);

    Task<DateTimeOffset?> GetExpiryAsync(IdentityUser user, string purpose);

    Task<string?> GetPayloadAsync(IdentityUser user, string purpose);

    Task RevokeAsync(IdentityUser user, string purpose);
}
