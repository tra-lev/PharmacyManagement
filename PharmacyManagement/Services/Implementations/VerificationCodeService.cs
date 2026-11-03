using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using PharmacyManagement.Services.Interfaces;

namespace PharmacyManagement.Services.Implementations;

public sealed class VerificationCodeService : IVerificationCodeService
{
    public const string Provider = "PharmacyVerify";

    private readonly UserManager<IdentityUser> _users;
    private readonly IPasswordHasher<IdentityUser> _hasher;

    public VerificationCodeService(UserManager<IdentityUser> users, IPasswordHasher<IdentityUser> hasher)
    {
        _users = users;
        _hasher = hasher;
    }

    public TimeSpan Lifetime => TimeSpan.FromMinutes(15);

    public int MaxAttempts => 5;

    public async Task<IssuedCode> IssueAsync(IdentityUser user, string purpose, string? payload = null)
    {
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
        var expiresAt = DateTimeOffset.UtcNow.Add(Lifetime);

        await Set(user, purpose, "hash", _hasher.HashPassword(user, code));
        await Set(user, purpose, "exp", expiresAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        await Set(user, purpose, "fails", "0");
        if (payload is null) await _users.RemoveAuthenticationTokenAsync(user, Provider, Name(purpose, "payload"));
        else await Set(user, purpose, "payload", payload);

        return new IssuedCode(code, expiresAt);
    }

    public async Task<CodeCheckResult> VerifyAsync(IdentityUser user, string purpose, string code)
    {
        var hash = await Get(user, purpose, "hash");
        if (string.IsNullOrEmpty(hash)) return new CodeCheckResult(CodeCheckStatus.NotFound, 0, null);

        var expiresAt = await GetExpiryAsync(user, purpose);
        if (expiresAt is null || expiresAt <= DateTimeOffset.UtcNow)
        {
            await RevokeAsync(user, purpose);
            return new CodeCheckResult(CodeCheckStatus.Expired, 0, null);
        }

        _ = int.TryParse(await Get(user, purpose, "fails"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var fails);
        if (fails >= MaxAttempts)
        {
            await RevokeAsync(user, purpose);
            return new CodeCheckResult(CodeCheckStatus.TooManyAttempts, 0, null);
        }

        var normalized = new string((code ?? string.Empty).Where(char.IsDigit).ToArray());
        var ok = normalized.Length == 6 && _hasher.VerifyHashedPassword(user, hash, normalized) != PasswordVerificationResult.Failed;
        if (!ok)
        {
            fails++;
            if (fails >= MaxAttempts)
            {
                await RevokeAsync(user, purpose);
                return new CodeCheckResult(CodeCheckStatus.TooManyAttempts, 0, null);
            }
            await Set(user, purpose, "fails", fails.ToString(CultureInfo.InvariantCulture));
            return new CodeCheckResult(CodeCheckStatus.Invalid, MaxAttempts - fails, null);
        }

        var payload = await Get(user, purpose, "payload");
        await RevokeAsync(user, purpose);
        return new CodeCheckResult(CodeCheckStatus.Ok, MaxAttempts - fails, payload);
    }

    public async Task<DateTimeOffset?> GetExpiryAsync(IdentityUser user, string purpose)
    {
        var raw = await Get(user, purpose, "exp");
        return long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;
    }

    public Task<string?> GetPayloadAsync(IdentityUser user, string purpose) => Get(user, purpose, "payload");

    public async Task RevokeAsync(IdentityUser user, string purpose)
    {
        foreach (var part in new[] { "hash", "exp", "fails", "payload" })
        {
            await _users.RemoveAuthenticationTokenAsync(user, Provider, Name(purpose, part));
        }
    }

    private static string Name(string purpose, string part) => purpose + ":" + part;

    private Task<string?> Get(IdentityUser user, string purpose, string part)
        => _users.GetAuthenticationTokenAsync(user, Provider, Name(purpose, part));

    private Task<IdentityResult> Set(IdentityUser user, string purpose, string part, string value)
        => _users.SetAuthenticationTokenAsync(user, Provider, Name(purpose, part), value);
}
