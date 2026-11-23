using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using KnowledgeHub.Api.Common;
using KnowledgeHub.Api.Infrastructure.Email;
using KnowledgeHub.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeHub.Api.Modules.Identity;

/// <summary>
/// Смена адреса для дайджестов и напоминаний. Новый адрес становится действующим только после ввода кода из письма,
/// отправленного на него: иначе опечатка или украденная сессия тихо перенаправили бы письма с данными пользователя на чужой
/// ящик, а приложение рассылало бы письма на любые адреса. Без кода применяются только адреса, которые и так «свои»:
/// пусто (письма идут на адрес профиля), адрес профиля (его подтвердил провайдер входа) и уже действующий адрес.
/// </summary>
public sealed partial class EmailConfirmationService(
    AppDbContext db, ICurrentUser currentUser, IEmailSender email, TimeProvider timeProvider, ILogger<EmailConfirmationService> logger)
{
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(15);
    public const int MaxAttempts = 5;
    public const string Subject = "Код подтверждения адреса — Personal Knowledge Hub";

    /// <summary>Адрес можно сохранить без подтверждения: пустой, адрес профиля или уже действующий.</summary>
    public static bool NeedsNoConfirmation(string? address, string? profileEmail, string? currentEmail) =>
        string.IsNullOrWhiteSpace(address)
        || SameAddress(address, profileEmail)
        || SameAddress(address, currentEmail);

    public static bool IsValidAddress(string? address) =>
        !string.IsNullOrWhiteSpace(address) && address.Trim().Length <= User.MaxEmailLength && MailAddress.TryCreate(address.Trim(), out _);

    /// <summary>
    /// Адрес, не требующий подтверждения, применяется сразу. Иначе на него уходит код, а адрес ждёт подтверждения
    /// (прежний запрос и его код перестают действовать); действующий адрес до подтверждения не меняется.
    /// </summary>
    public async Task<UserSettings> RequestAsync(string? address, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(address) && !IsValidAddress(address))
        {
            throw Errors.Validation("email", "Invalid e-mail.");
        }

        address = string.IsNullOrWhiteSpace(address) ? null : address.Trim();
        var (settings, profileEmail) = await LoadAsync(ct);
        if (NeedsNoConfirmation(address, profileEmail, settings.Email))
        {
            settings.Email = address;
            settings.ClearPendingEmail();
            await db.SaveChangesAsync(ct);
            return settings;
        }

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        // Сначала письмо: если отправить не удалось, ожидающего адреса с недоставленным кодом не остаётся.
        await SendCodeAsync(address!, code, ct);

        settings.PendingEmail = address;
        settings.PendingEmailCodeHash = Hash(settings.UserId, code);
        settings.PendingEmailExpiresAt = timeProvider.GetUtcNow() + CodeLifetime;
        settings.PendingEmailAttempts = 0;
        await db.SaveChangesAsync(ct);
        return settings;
    }

    /// <summary>Верный код делает ожидающий адрес действующим. После <see cref="MaxAttempts"/> неверных попыток запрос отменяется.</summary>
    public async Task<UserSettings> ConfirmAsync(string? code, CancellationToken ct)
    {
        var (settings, _) = await LoadAsync(ct);
        if (settings.PendingEmail is null || settings.PendingEmailCodeHash is null
            || settings.PendingEmailExpiresAt <= timeProvider.GetUtcNow())
        {
            throw Errors.Validation("code", "There is no address awaiting confirmation, or the code has expired. Request a new code.");
        }

        var expected = Encoding.ASCII.GetBytes(settings.PendingEmailCodeHash);
        var actual = Encoding.ASCII.GetBytes(Hash(settings.UserId, code?.Trim() ?? ""));
        if (!CryptographicOperations.FixedTimeEquals(expected, actual))
        {
            settings.PendingEmailAttempts++;
            var exhausted = settings.PendingEmailAttempts >= MaxAttempts;
            if (exhausted)
            {
                settings.ClearPendingEmail();
            }

            await db.SaveChangesAsync(ct);
            throw Errors.Validation("code", exhausted
                ? "Too many wrong codes. Request a new code."
                : "Invalid code.");
        }

        settings.Email = settings.PendingEmail;
        settings.ClearPendingEmail();
        await db.SaveChangesAsync(ct);
        return settings;
    }

    private async Task<(UserSettings Settings, string? ProfileEmail)> LoadAsync(CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var settings = await db.UserSettings.FirstOrDefaultAsync(s => s.UserId == userId, ct) ?? throw Errors.NotFound();
        var profileEmail = await db.Users.Where(u => u.Id == userId).Select(u => u.Email).FirstOrDefaultAsync(ct);
        return (settings, profileEmail);
    }

    private async Task SendCodeAsync(string address, string code, CancellationToken ct)
    {
        var text =
            $"Код для подтверждения адреса для дайджестов и напоминаний: {code}\n\n" +
            $"Код действует {CodeLifetime.TotalMinutes:0} минут. Если вы не меняли адрес в Personal Knowledge Hub, просто проигнорируйте это письмо.";
        var html =
            $"<p>Код для подтверждения адреса для дайджестов и напоминаний: <strong>{WebUtility.HtmlEncode(code)}</strong></p>" +
            $"<p>Код действует {CodeLifetime.TotalMinutes:0} минут. Если вы не меняли адрес в Personal Knowledge Hub, просто проигнорируйте это письмо.</p>";
        try
        {
            await email.SendAsync(address, Subject, html, text, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            LogSendFailed(currentUser.UserId, e);
            throw new AppException(StatusCodes.Status503ServiceUnavailable, "Service Unavailable",
                "Could not send the confirmation e-mail. Try again later.");
        }
    }

    private static bool SameAddress(string? a, string? b) =>
        a is not null && b is not null && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>Хеш с id пользователя: одинаковые коды разных пользователей дают разные хеши.</summary>
    private static string Hash(Guid userId, string code) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{userId:N}:{code}")));

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Не удалось отправить код подтверждения адреса пользователю {UserId}")]
    private partial void LogSendFailed(Guid? userId, Exception exception);
}
