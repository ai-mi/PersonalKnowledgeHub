namespace KnowledgeHub.Api.Infrastructure.Email;

/// <summary>
/// Для локального запуска (<c>Email:LogOnly</c>, включён в appsettings.Development.json): письмо не отправляется, а пишется
/// в журнал — так вручную проверяются код подтверждения адреса, дайджест и напоминания без Azure Communication Services.
/// В Azure не включать: письма не дойдут, а коды подтверждения окажутся в журналах.
/// </summary>
public sealed partial class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(string to, string subject, string html, string text, CancellationToken ct)
    {
        LogEmail(to, subject, text);
        return Task.CompletedTask;
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
        Message = "E-mail НЕ отправлен (Email:LogOnly). Кому: {To}; тема: {Subject}\n{Text}")]
    private partial void LogEmail(string to, string subject, string text);
}
