namespace KnowledgeHub.Api.Modules.Reminders;

/// <summary>Куда доставить уведомление; хранится строкой в нижнем регистре.</summary>
public enum NotificationChannel
{
    Telegram,
    Email,
    Both,
}

public static class NotificationChannelExtensions
{
    public static bool IncludesTelegram(this NotificationChannel channel) => channel is NotificationChannel.Telegram or NotificationChannel.Both;

    public static bool IncludesEmail(this NotificationChannel channel) => channel is NotificationChannel.Email or NotificationChannel.Both;
}
