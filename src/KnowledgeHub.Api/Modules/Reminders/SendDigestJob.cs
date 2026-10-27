using KnowledgeHub.Api.Infrastructure.Persistence;
using KnowledgeHub.Api.Modules.Bot;
using KnowledgeHub.Api.Modules.Identity;
using KnowledgeHub.Api.Modules.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KnowledgeHub.Api.Modules.Reminders;

/// <param name="LocalDate">Локальная дата пользователя, за которую собирается дайджест.</param>
public sealed record SendDigestPayload(DateOnly LocalDate);

/// <summary>Собирает и отправляет дайджест одному пользователю (задача ставится с <c>userId</c>).</summary>
public sealed class SendDigestJob(
    AppDbContext db, DigestBuilder builder, INotificationDispatcher dispatcher, IOptions<BotOptions> botOptions)
    : IJobHandler<SendDigestPayload>
{
    public const string JobType = "digest.send";
    public static string Type => JobType;

    public async Task HandleAsync(SendDigestPayload payload, CancellationToken ct)
    {
        // Фильтр EF оставляет только настройки пользователя задачи.
        var settings = await db.UserSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        if (settings is null || settings.DigestFrequency == DigestFrequency.Off || ToChannel(settings.DigestChannels) is not { } channel)
        {
            return;
        }

        if (await builder.BuildAsync(TimeZoneInfo.FindSystemTimeZoneById(settings.Timezone), ct) is not { } digest)
        {
            return;
        }

        await dispatcher.SendAsync(settings.UserId, channel, DigestFormatter.Format(digest, botOptions.Value.WebBaseUrl), ct);
    }

    private static NotificationChannel? ToChannel(string[] channels) =>
        (channels.Contains("telegram"), channels.Contains("email")) switch
        {
            (true, true) => NotificationChannel.Both,
            (true, false) => NotificationChannel.Telegram,
            (false, true) => NotificationChannel.Email,
            _ => null,
        };
}
