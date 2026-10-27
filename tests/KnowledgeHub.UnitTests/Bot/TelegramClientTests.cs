using System.Net;
using System.Text;
using KnowledgeHub.Api.Modules.Bot;
using Microsoft.Extensions.Options;

namespace KnowledgeHub.UnitTests.Bot;

public sealed class TelegramClientTests
{
    private const string Token = "123456:TEST-token";

    private sealed class StubHandler(Func<HttpRequestMessage, string, (HttpStatusCode, string)> respond) : HttpMessageHandler
    {
        public List<(string Path, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.RequestUri!.AbsolutePath, body));
            var (status, content) = respond(request, body);
            return new HttpResponseMessage(status) { Content = new StringContent(content, Encoding.UTF8, "application/json") };
        }
    }

    private static (TelegramClient Client, StubHandler Handler) Create(string result, string token = Token, HttpStatusCode status = HttpStatusCode.OK)
    {
        var handler = new StubHandler((_, _) => (status, result));
        var client = new TelegramClient(Options.Create(new BotOptions { Token = token }), new HttpClient(handler));
        return (client, handler);
    }

    private static string Ok(string result) => $$"""{"ok":true,"result":{{result}}}""";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task WithoutToken_FailsOnFirstUseNotOnCreation()
    {
        var client = new TelegramClient(Options.Create(new BotOptions()));

        var error = await Should.ThrowAsync<InvalidOperationException>(() => client.SendMessageAsync(1, "x", Ct));

        error.Message.ShouldContain("Bot:Token");
    }

    [Fact]
    public async Task SendMessage_CallsSendMessageWithChatAndText()
    {
        var (client, handler) = Create(Ok("""{"message_id":1,"date":1,"chat":{"id":7,"type":"private"}}"""));

        await client.SendMessageAsync(7, "Saved", Ct);

        var request = handler.Requests.ShouldHaveSingleItem();
        request.Path.ShouldBe($"/bot{Token}/sendMessage");
        request.Body.ShouldContain("\"chat_id\":7");
        request.Body.ShouldContain("\"text\":\"Saved\"");
    }

    [Fact]
    public async Task SendMessage_UsesHtmlAndAttachesButtonsWithoutLinkPreview()
    {
        var (client, handler) = Create(Ok("""{"message_id":1,"date":1,"chat":{"id":7,"type":"private"}}"""));

        await client.SendMessageAsync(7, "<b>x</b>", Ct, [new InlineButton("Прочитано", "read:1")]);

        var body = handler.Requests.ShouldHaveSingleItem().Body;
        body.ShouldContain("\"parse_mode\":\"HTML\"");
        body.ShouldContain("\"callback_data\":\"read:1\"");
        body.ShouldContain("\"is_disabled\":true");
    }

    [Fact]
    public async Task SendMessage_WhenBotIsBlocked_ThrowsTelegramBlockedException()
    {
        var (client, _) = Create(
            """{"ok":false,"error_code":403,"description":"Forbidden: bot was blocked by the user"}""", status: HttpStatusCode.Forbidden);

        await Should.ThrowAsync<TelegramBlockedException>(() => client.SendMessageAsync(7, "x", Ct));
    }

    [Fact]
    public async Task SendMessage_OtherApiErrors_AreNotTreatedAsBlocked()
    {
        var (client, _) = Create(
            """{"ok":false,"error_code":400,"description":"Bad Request: chat not found"}""", status: HttpStatusCode.BadRequest);

        var error = await Should.ThrowAsync<Exception>(() => client.SendMessageAsync(7, "x", Ct));

        error.ShouldNotBeOfType<TelegramBlockedException>();
    }

    [Fact]
    public async Task GetFile_ReturnsPathAndSize()
    {
        var (client, handler) = Create(Ok("""{"file_id":"f","file_unique_id":"u","file_size":5,"file_path":"docs/a.pdf"}"""));

        var file = await client.GetFileAsync("f", Ct);

        file.ShouldBe(new TelegramFile("docs/a.pdf", 5));
        handler.Requests.ShouldHaveSingleItem().Path.ShouldBe($"/bot{Token}/getFile");
    }

    [Fact]
    public async Task GetFile_WithoutPath_Fails()
    {
        var (client, _) = Create(Ok("""{"file_id":"f","file_unique_id":"u"}"""));

        await Should.ThrowAsync<InvalidOperationException>(() => client.GetFileAsync("f", Ct));
    }

    [Fact]
    public async Task DownloadFile_StreamsTheFileContent()
    {
        var (client, handler) = Create("содержимое");
        using var destination = new MemoryStream();

        await client.DownloadFileAsync("docs/a.pdf", destination, Ct);

        Encoding.UTF8.GetString(destination.ToArray()).ShouldBe("содержимое");
        handler.Requests.ShouldHaveSingleItem().Path.ShouldBe($"/file/bot{Token}/docs/a.pdf");
    }

    [Fact]
    public async Task AnswerCallbackQuery_CallsTheMethod()
    {
        var (client, handler) = Create(Ok("true"));

        await client.AnswerCallbackQueryAsync("cb-1", "Готово", Ct);

        var request = handler.Requests.ShouldHaveSingleItem();
        request.Path.ShouldBe($"/bot{Token}/answerCallbackQuery");
        request.Body.ShouldContain("cb-1");
    }

    [Fact]
    public async Task SetWebhook_SendsUrlSecretAndAllowedUpdates()
    {
        var (client, handler) = Create(Ok("true"));

        await client.SetWebhookAsync("https://kh.example.com/telegram/webhook", "s3cret_-", Ct);

        var request = handler.Requests.ShouldHaveSingleItem();
        request.Path.ShouldBe($"/bot{Token}/setWebhook");
        request.Body.ShouldContain("https://kh.example.com/telegram/webhook");
        request.Body.ShouldContain("\"secret_token\":\"s3cret_-\"");
        request.Body.ShouldContain("message");
        request.Body.ShouldContain("callback_query");
    }
}
