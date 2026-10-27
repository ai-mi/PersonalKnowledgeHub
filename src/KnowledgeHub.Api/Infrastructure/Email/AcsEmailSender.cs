using Azure;
using Azure.Communication.Email;
using Microsoft.Extensions.Options;

namespace KnowledgeHub.Api.Infrastructure.Email;

/// <summary>Отправка через Azure Communication Services Email.</summary>
public sealed class AcsEmailSender(IOptions<EmailOptions> options) : IEmailSender
{
    // Клиент создаётся при первой отправке: без настроек приложение должно стартовать (локально, в тестах).
    private readonly Lazy<EmailClient> _client = new(() => new EmailClient(options.Value.ConnectionString));

    public async Task SendAsync(string to, string subject, string html, string text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(options.Value.ConnectionString) || string.IsNullOrWhiteSpace(options.Value.From))
        {
            throw new InvalidOperationException("Отправка e-mail не настроена: задайте Email:ConnectionString и Email:From.");
        }

        // Started: письмо принято сервисом; ждать окончания доставки незачем.
        await _client.Value.SendAsync(WaitUntil.Started, options.Value.From, to, subject, html, text, ct);
    }
}
