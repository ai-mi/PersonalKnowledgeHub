namespace KnowledgeHub.Api.Modules.Bot;

public sealed class BotOptions
{
    public const string SectionName = "Bot";

    /// <summary>Токен от BotFather (в Azure — из Key Vault).</summary>
    public string Token { get; set; } = "";

    /// <summary>Имя бота без <c>@</c>: нужно для ссылки привязки <c>https://t.me/&lt;имя&gt;?start=&lt;код&gt;</c>.</summary>
    public string Username { get; set; } = "";

    /// <summary>Публичный адрес веб-интерфейса без завершающего <c>/</c>: из него строятся ссылки на записи в сообщениях бота. Пустой — ссылок нет.</summary>
    public string WebBaseUrl { get; set; } = "";

    /// <summary>Публичный адрес <c>/telegram/webhook</c>; если задан, при старте приложение регистрирует его в Telegram.</summary>
    public string WebhookUrl { get; set; } = "";

    /// <summary>Секрет заголовка <c>X-Telegram-Bot-Api-Secret-Token</c> (1–256 символов <c>A-Z a-z 0-9 _ -</c>). Пустой — webhook отклоняет все запросы.</summary>
    public string WebhookSecret { get; set; } = "";
}
