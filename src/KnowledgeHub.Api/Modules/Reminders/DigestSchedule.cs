using KnowledgeHub.Api.Modules.Identity;

namespace KnowledgeHub.Api.Modules.Reminders;

/// <summary>Когда пользователю пора отправить дайджест: 9:00 по его часовому поясу, ежедневно или в выбранный день недели.</summary>
public static class DigestSchedule
{
    public static readonly TimeOnly SendTime = new(9, 0);

    /// <param name="localDate">Локальная дата пользователя — основа ключа дедупликации (один дайджест в день).</param>
    /// <returns>
    /// <c>true</c> — в часовом поясе пользователя уже не раньше 9:00 и день подходит. Пока день не кончился, остаётся <c>true</c>:
    /// дайджест, пропущенный из-за простоя, уходит при следующей проверке, а повтор отсекает ключ.
    /// </returns>
    public static bool IsDue(UserSettings settings, DateTimeOffset utcNow, out DateOnly localDate)
    {
        localDate = default;
        if (settings.DigestFrequency == DigestFrequency.Off || !TryFindTimeZone(settings.Timezone, out var zone))
        {
            return false;
        }

        var local = TimeZoneInfo.ConvertTimeFromUtc(utcNow.UtcDateTime, zone);
        if (TimeOnly.FromDateTime(local) < SendTime
            || (settings.DigestFrequency == DigestFrequency.Weekly && local.DayOfWeek != settings.DigestDay))
        {
            return false;
        }

        localDate = DateOnly.FromDateTime(local);
        return true;
    }

    private static bool TryFindTimeZone(string id, out TimeZoneInfo zone)
    {
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            zone = TimeZoneInfo.Utc;
            return false;
        }
    }
}
