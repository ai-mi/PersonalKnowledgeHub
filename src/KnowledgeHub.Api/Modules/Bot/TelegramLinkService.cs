using System.Security.Cryptography;
using System.Text;
using KnowledgeHub.Api.Common;
using KnowledgeHub.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeHub.Api.Modules.Bot;

public sealed class TelegramLinkService(AppDbContext db, ICurrentUser currentUser, SystemContext systemContext, TimeProvider timeProvider)
{
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(10);
    private const int CodeBytes = 16;

    public static string Hash(string code) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(code)));

    /// <summary>Новый код привязки для текущего пользователя; прежние его коды перестают действовать. Открытый код возвращается только здесь.</summary>
    public async Task<(string Code, DateTimeOffset ExpiresAt)> CreateCodeAsync(CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var code = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(CodeBytes));
        var expiresAt = timeProvider.GetUtcNow() + CodeLifetime;

        await db.TelegramLinkCodes.Where(c => c.UserId == userId).ExecuteDeleteAsync(ct);
        db.TelegramLinkCodes.Add(new TelegramLinkCode { Id = Guid.CreateVersion7(), UserId = userId, CodeHash = Hash(code), ExpiresAt = expiresAt });
        await db.SaveChangesAsync(ct);
        return (code, expiresAt);
    }

    /// <summary>
    /// Привязывает чат к владельцу действующего кода; код одноразовый. Владелец кода заранее неизвестен, поэтому поиск идёт
    /// в системном контексте. Чат, привязанный к другому пользователю, переходит к владельцу кода. <c>null</c> — код неверный или просрочен.
    /// </summary>
    public async Task<Guid?> RedeemAsync(string code, long chatId, CancellationToken ct)
    {
        using var _ = systemContext.Enter();
        var now = timeProvider.GetUtcNow();
        var hash = Hash(code);
        var entity = await db.TelegramLinkCodes.AsNoTracking().FirstOrDefaultAsync(c => c.CodeHash == hash && c.ExpiresAt > now, ct);

        // Параллельное использование того же кода: выигрывает тот, чей DELETE удалил строку.
        if (entity is null || await db.TelegramLinkCodes.Where(c => c.Id == entity.Id).ExecuteDeleteAsync(ct) == 0)
        {
            return null;
        }

        await db.TelegramLinks.Where(l => l.ChatId == chatId && l.UserId != entity.UserId).ExecuteDeleteAsync(ct);
        var link = await db.TelegramLinks.FirstOrDefaultAsync(l => l.UserId == entity.UserId, ct);
        if (link is null)
        {
            db.TelegramLinks.Add(new TelegramLink { UserId = entity.UserId, ChatId = chatId, LinkedAt = now });
        }
        else
        {
            link.ChatId = chatId;
            link.LinkedAt = now;
        }

        await db.SaveChangesAsync(ct);
        return entity.UserId;
    }

    /// <summary>Привязка текущего пользователя или <c>null</c>.</summary>
    public Task<TelegramLink?> GetAsync(CancellationToken ct) =>
        db.TelegramLinks.AsNoTracking().FirstOrDefaultAsync(ct);

    public async Task UnlinkAsync(CancellationToken ct) =>
        await db.TelegramLinks.ExecuteDeleteAsync(ct);

    /// <summary>Привязка указанного пользователя (для уведомлений вне его запроса — вызывать в системном контексте).</summary>
    public Task<TelegramLink?> FindByUserAsync(Guid userId, CancellationToken ct) =>
        db.TelegramLinks.AsNoTracking().FirstOrDefaultAsync(l => l.UserId == userId, ct);

    /// <summary>Удаляет привязку указанного пользователя (вызывать в системном контексте).</summary>
    public async Task RemoveForUserAsync(Guid userId, CancellationToken ct) =>
        await db.TelegramLinks.Where(l => l.UserId == userId).ExecuteDeleteAsync(ct);

    /// <summary>Привязка чата (для бота: пользователь заранее неизвестен — вызывать в системном контексте).</summary>
    public Task<TelegramLink?> FindByChatAsync(long chatId, CancellationToken ct) =>
        db.TelegramLinks.AsNoTracking().FirstOrDefaultAsync(l => l.ChatId == chatId, ct);
}
