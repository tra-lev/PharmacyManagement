using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using PharmacyManagement.Authorization;
using PharmacyManagement.Data;
using PharmacyManagement.Services.Interfaces;

namespace PharmacyManagement.Services.Implementations;

public sealed class PermissionService : IPermissionService
{
    private static readonly IReadOnlySet<string> Empty = new HashSet<string>();
    private readonly PharmacyDbContext _db;
    private readonly IMemoryCache _cache;

    public PermissionService(PharmacyDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<IReadOnlySet<string>> GetCodesAsync(string roleName, CancellationToken ct = default)
    {
        var roleId = RoleNames.IdOf(roleName);
        if (roleId is null) return Empty;

        var cached = await _cache.GetOrCreateAsync(CacheKey(roleName), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
            var codes = await _db.RolePermissions.AsNoTracking()
                .Where(rp => rp.RoleId == roleId && rp.CanView)
                .Join(_db.Permissions.AsNoTracking(), rp => rp.PermissionId, p => p.PermissionId, (rp, p) => p.PermissionCode)
                .ToListAsync(ct);

            var allowed = codes
                .Where(c => !(roleName == RoleNames.Pharmacist && PermissionCodes.IsLockedForPharmacist(c)))
                .Where(c => !(roleName != RoleNames.Owner && PermissionCodes.IsOwnerOnly(c)))
                .ToHashSet(StringComparer.Ordinal);
            return (IReadOnlySet<string>)allowed;
        });

        return cached ?? Empty;
    }

    public async Task<bool> RoleHasAsync(string roleName, string code, CancellationToken ct = default)
        => (await GetCodesAsync(roleName, ct)).Contains(code);

    public void Invalidate(string roleName) => _cache.Remove(CacheKey(roleName));

    private static string CacheKey(string roleName) => "perm:" + roleName;
}
