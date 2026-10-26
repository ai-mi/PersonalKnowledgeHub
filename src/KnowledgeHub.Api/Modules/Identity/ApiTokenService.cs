using System.Security.Cryptography;
using System.Text;
using KnowledgeHub.Api.Common;
using KnowledgeHub.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeHub.Api.Modules.Identity;

public sealed class ApiTokenService(AppDbContext db, TimeProvider timeProvider, SystemContext systemContext)
{
    public const string Prefix = "kh_";
    private const int RandomBytes = 32;
    private static readonly TimeSpan LastUsedGranularity = TimeSpan.FromMinutes(1);

    /// <summary>Новый токен: <c>kh_</c> + 32 случайных байта в Base64Url.</summary>
    public static string GenerateToken() =>
        Prefix + WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(RandomBytes));

    public static string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    /// <summary>Создаёт токен текущего пользователя; открытое значение возвращается только здесь.</summary>
    public async Task<(ApiToken Entity, string Token)> CreateAsync(string name, int? expiresInDays, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var token = GenerateToken();
        var entity = new ApiToken
        {
            Id = Guid.CreateVersion7(),
            Name = name,
            TokenHash = Hash(token),
            CreatedAt = now,
            ExpiresAt = expiresInDays is { } days ? now.AddDays(days) : null,
        };
        db.ApiTokens.Add(entity);
        await db.SaveChangesAsync(ct);
        return (entity, token);
    }

    public Task<List<ApiToken>> ListAsync(CancellationToken ct) =>
        db.ApiTokens.AsNoTracking().OrderByDescending(t => t.CreatedAt).ToListAsync(ct);

    /// <summary>Отзывает токен текущего пользователя; повторный отзыв ничего не меняет. <c>false</c> — токена нет.</summary>
    public async Task<bool> RevokeAsync(Guid id, CancellationToken ct)
    {
        var token = await db.ApiTokens.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (token is null)
        {
            return false;
        }

        token.RevokedAt ??= timeProvider.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Находит владельца действующего токена. Пользователь запроса ещё неизвестен, поэтому поиск идёт в системном контексте.
    /// </summary>
    public async Task<Guid?> AuthenticateAsync(string token, CancellationToken ct)
    {
        using var _ = systemContext.Enter();
        var hash = Hash(token);
        var entity = await db.ApiTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        var now = timeProvider.GetUtcNow();
        if (entity is null || entity.RevokedAt is not null || entity.ExpiresAt <= now)
        {
            return null;
        }

        if (entity.LastUsedAt is not { } last || now - last >= LastUsedGranularity)
        {
            entity.LastUsedAt = now;
            await db.SaveChangesAsync(ct);
        }

        return entity.UserId;
    }
}
