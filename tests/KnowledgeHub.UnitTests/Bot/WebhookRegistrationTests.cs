using KnowledgeHub.Api.Modules.Bot;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KnowledgeHub.UnitTests.Bot;

public sealed class WebhookRegistrationTests
{
    private sealed class RecordingTelegram : ITelegramClient
    {
        public List<(string Url, string Secret)> Webhooks { get; } = [];
        public Exception? Failure { get; set; }

        public Task SetWebhookAsync(string url, string secretToken, CancellationToken ct)
        {
            Webhooks.Add((url, secretToken));
            return Failure is null ? Task.CompletedTask : Task.FromException(Failure);
        }

        public Task SendMessageAsync(long chatId, string text, CancellationToken ct, IReadOnlyList<InlineButton>? buttons = null) => throw new NotSupportedException();

        public Task<TelegramFile> GetFileAsync(string fileId, CancellationToken ct) => throw new NotSupportedException();

        public Task DownloadFileAsync(string filePath, Stream destination, CancellationToken ct) => throw new NotSupportedException();

        public Task AnswerCallbackQueryAsync(string callbackQueryId, string? text, CancellationToken ct) => throw new NotSupportedException();
    }

    private static WebhookRegistration Create(RecordingTelegram telegram, string webhookUrl) =>
        new(telegram, Options.Create(new BotOptions { WebhookUrl = webhookUrl, WebhookSecret = "secret" }), NullLogger<WebhookRegistration>.Instance);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task WithoutWebhookUrl_DoesNothing(string url)
    {
        var telegram = new RecordingTelegram();

        await Create(telegram, url).StartAsync(Ct);

        telegram.Webhooks.ShouldBeEmpty();
    }

    [Fact]
    public async Task WithWebhookUrl_RegistersItWithTheSecret()
    {
        var telegram = new RecordingTelegram();

        await Create(telegram, "https://kh.example.com/telegram/webhook").StartAsync(Ct);

        telegram.Webhooks.ShouldBe([("https://kh.example.com/telegram/webhook", "secret")]);
    }

    [Fact]
    public async Task TelegramFailure_DoesNotStopTheApplication()
    {
        var telegram = new RecordingTelegram { Failure = new HttpRequestException("down") };

        await Create(telegram, "https://kh.example.com/telegram/webhook").StartAsync(Ct);

        telegram.Webhooks.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Cancellation_IsNotSwallowed()
    {
        var telegram = new RecordingTelegram { Failure = new OperationCanceledException() };

        await Should.ThrowAsync<OperationCanceledException>(() => Create(telegram, "https://kh.example.com/x").StartAsync(Ct));
    }

    [Fact]
    public async Task Stop_Completes() =>
        await Create(new RecordingTelegram(), "").StopAsync(Ct);
}
