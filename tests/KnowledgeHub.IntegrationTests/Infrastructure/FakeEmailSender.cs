using KnowledgeHub.Api.Infrastructure.Email;

namespace KnowledgeHub.IntegrationTests.Infrastructure;

public sealed record SentEmail(string To, string Subject, string Html, string Text);

/// <summary>E-mail без сети: запоминает отправленные письма.</summary>
public sealed class FakeEmailSender : IEmailSender
{
    private readonly Lock _gate = new();
    private readonly List<SentEmail> _sent = [];

    /// <summary>Имитирует сбой сервиса отправки.</summary>
    public volatile bool Fail;

    public IReadOnlyList<SentEmail> Sent
    {
        get
        {
            lock (_gate)
            {
                return [.. _sent];
            }
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _sent.Clear();
        }

        Fail = false;
    }

    public Task SendAsync(string to, string subject, string html, string text, CancellationToken ct)
    {
        if (Fail)
        {
            throw new HttpRequestException("Сервис e-mail недоступен.");
        }

        lock (_gate)
        {
            _sent.Add(new SentEmail(to, subject, html, text));
        }

        return Task.CompletedTask;
    }
}
