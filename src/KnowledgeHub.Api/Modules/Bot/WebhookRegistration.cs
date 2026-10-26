using Microsoft.Extensions.Options;

namespace KnowledgeHub.Api.Modules.Bot;

/// <summary>Регистрирует webhook в Telegram при старте, если задан <c>Bot:WebhookUrl</c>.</summary>
public sealed partial class WebhookRegistration(
    ITelegramClient telegram, IOptions<BotOptions> options, ILogger<WebhookRegistration> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var bot = options.Value;
        if (string.IsNullOrWhiteSpace(bot.WebhookUrl))
        {
            return;
        }

        try
        {
            await telegram.SetWebhookAsync(bot.WebhookUrl, bot.WebhookSecret, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Недоступный Telegram не должен ронять приложение: остальные функции работают и без бота.
            LogRegistrationFailed(e);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(EventId = 1, Level = LogLevel.Error, Message = "Не удалось зарегистрировать webhook Telegram")]
    private partial void LogRegistrationFailed(Exception exception);
}
