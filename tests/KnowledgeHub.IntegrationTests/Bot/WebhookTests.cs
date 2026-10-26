using System.Net;
using KnowledgeHub.Api.Modules.Bot;
using KnowledgeHub.Api.Modules.Items;
using KnowledgeHub.Api.Modules.Jobs;
using KnowledgeHub.IntegrationTests.Infrastructure;

namespace KnowledgeHub.IntegrationTests.Bot;

public sealed class WebhookTests(ApiFactory factory) : BotTestBase(factory)
{
    private static byte[] Fixture(string name) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Ingestion", "Fixtures", name));

    [Theory]
    [InlineData(null)]
    [InlineData("wrong-secret")]
    [InlineData("")]
    public async Task WrongOrMissingSecret_Returns401AndQueuesNothing(string? secret)
    {
        var response = await PostWebhookAsync(TextUpdate("hello"), secret);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await AllJobsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task MalformedBody_Returns400()
    {
        (await PostWebhookAsync("not json")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await PostWebhookAsync("null")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await AllJobsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task ValidUpdate_Returns200AndQueuesTheRawJson()
    {
        var json = TextUpdate("hello", updateId: 777);

        (await PostWebhookAsync(json)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var job = (await AllJobsAsync()).ShouldHaveSingleItem();
        job.Type.ShouldBe("telegram.update");
        job.DedupKey.ShouldBe("tg:777");
        job.UserId.ShouldBeNull();
    }

    [Fact]
    public async Task RepeatedUpdateId_CreatesOneItem()
    {
        var userId = await LinkedUserAsync();
        var json = TextUpdate("повторная доставка", updateId: 55);

        await SendAsync(json);
        await SendAsync(json);

        (await ItemsOfAsync(userId)).ShouldHaveSingleItem().Body.ShouldBe("повторная доставка");
        (await AllJobsAsync()).Count.ShouldBe(1);
    }

    [Fact]
    public async Task UnlinkedChat_SavesNothingAndExplainsHowToLink()
    {
        var other = await LinkedUserAsync(chatId: 1);

        await SendAsync(TextUpdate("не моё", chatId: 2));

        (await ItemsOfAsync(other)).ShouldBeEmpty();
        (await WithDbAsync((db, _) => Task.FromResult(db.Items.Count()))).ShouldBe(0);
        Telegram.TextsTo(2).ShouldHaveSingleItem().ShouldContain("Привязать Telegram");
    }

    [Fact]
    public async Task LinkWithCaptionAndHashtags_IsSavedWithNoteAndTags()
    {
        var userId = await LinkedUserAsync();
        const string text = "https://example.com/post?utm_source=x Про шину #azure #dotnet";

        await SendAsync(TextUpdate(text, entities:
        [
            Entity("url", text, "https://example.com/post?utm_source=x"),
            Entity("hashtag", text, "#azure"),
            Entity("hashtag", text, "#dotnet"),
        ]));

        var item = (await ItemsOfAsync(userId)).ShouldHaveSingleItem();
        item.Type.ShouldBe(ItemType.Link);
        item.Url.ShouldBe("https://example.com/post");
        item.UserNote.ShouldBe("Про шину");
        item.Tags.Select(t => t.Name).Order().ShouldBe(["azure", "dotnet"]);
        item.Status.ShouldBe(ItemStatus.Pending);
        Telegram.TextsTo(ChatId).ShouldBe(["Сохранено"]);
        (await AllJobsAsync()).Count(j => j.Type == "ingest").ShouldBe(1);
    }

    [Fact]
    public async Task SameLinkTwice_RepliesThatItWasAlreadySaved()
    {
        var userId = await LinkedUserAsync();
        const string text = "https://example.com/dup";
        var entities = new[] { Entity("url", text, text) };

        await SendAsync(TextUpdate(text, entities: entities));
        await SendAsync(TextUpdate(text, entities: entities));

        (await ItemsOfAsync(userId)).ShouldHaveSingleItem();
        Telegram.TextsTo(ChatId).ShouldBe(["Сохранено", "Уже было сохранено"]);
    }

    [Fact]
    public async Task PlainText_IsSavedAsNote()
    {
        var userId = await LinkedUserAsync();

        await SendAsync(TextUpdate("Купить молоко"));

        var item = (await ItemsOfAsync(userId)).ShouldHaveSingleItem();
        item.Type.ShouldBe(ItemType.Note);
        item.Body.ShouldBe("Купить молоко");
        item.Status.ShouldBe(ItemStatus.Ready);
    }

    [Fact]
    public async Task SentPdf_BecomesDocumentWithCaptionAndTags()
    {
        var userId = await LinkedUserAsync();
        var pdf = Fixture("sample.pdf");
        Telegram.AddFile("file-1", pdf);
        const string caption = "Договор #legal";

        await SendAsync(DocumentUpdate("file-1", "contract.pdf", pdf.Length, caption, [Entity("hashtag", caption, "#legal")]));

        var item = (await ItemsOfAsync(userId)).ShouldHaveSingleItem();
        item.Type.ShouldBe(ItemType.Document);
        item.Title.ShouldBe("contract");
        item.UserNote.ShouldBe("Договор");
        item.Tags.Select(t => t.Name).ShouldBe(["legal"]);
        item.BlobPath.ShouldNotBeNull().ShouldEndWith("contract.pdf");
        Telegram.TextsTo(ChatId).ShouldBe(["Сохранено"]);
        (await AllJobsAsync()).Count(j => j.Type == "ingest").ShouldBe(1);
    }

    [Fact]
    public async Task TooLargeFile_IsRejectedWithExplanationBeforeDownload()
    {
        var userId = await LinkedUserAsync();

        // Файла нет в фейке: попытка скачать упала бы, значит размер проверен заранее.
        await SendAsync(DocumentUpdate("huge", "huge.pdf", 25L * 1024 * 1024));

        (await ItemsOfAsync(userId)).ShouldBeEmpty();
        Telegram.TextsTo(ChatId).ShouldHaveSingleItem().ShouldContain("20 МБ");
    }

    [Fact]
    public async Task UnsupportedFileType_RepliesWithTheReason()
    {
        var userId = await LinkedUserAsync();
        Telegram.AddFile("exe", "MZ\0\0binary"u8.ToArray());

        await SendAsync(DocumentUpdate("exe", "tool.exe", 10));

        (await ItemsOfAsync(userId)).ShouldBeEmpty();
        Telegram.TextsTo(ChatId).ShouldHaveSingleItem().ShouldStartWith("Не удалось сохранить: Unsupported file type");
        (await AllJobsAsync()).Single().Status.ShouldBe(JobStatus.Completed);
    }

    [Fact]
    public async Task EmptySecretInSettings_RejectsEverything()
    {
        using var noSecret = Factory.WithWebHostBuilder(b => b.UseSetting("Bot:WebhookSecret", ""));
        using var client = noSecret.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, WebhookEndpoint.Path) { Content = new StringContent(TextUpdate("x")) };
        request.Headers.Add(WebhookEndpoint.SecretHeader, "");

        (await client.SendAsync(request, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UpdateWithoutMessage_IsIgnored()
    {
        var userId = await LinkedUserAsync();
        const string callback = """{"update_id":9,"callback_query":{"id":"1","from":{"id":1,"is_bot":false,"first_name":"x"},"chat_instance":"c","data":"d"}}""";

        await SendAsync(callback);

        (await ItemsOfAsync(userId)).ShouldBeEmpty();
        Telegram.Sent.ShouldBeEmpty();
        (await AllJobsAsync()).Single().Status.ShouldBe(JobStatus.Completed);
    }

    [Fact]
    public async Task JobWithNullUpdate_IsIgnored()
    {
        await WithDbAsync((_, queue) => queue.EnqueueAsync(ProcessUpdateJob.JobType, new TelegramUpdatePayload("null"), ct: Ct));

        await ProcessAllAsync();

        (await AllJobsAsync()).Single().Status.ShouldBe(JobStatus.Completed);
    }

    [Fact]
    public async Task OnlyHashtags_RepliesThatThereIsNothingToSave()
    {
        var userId = await LinkedUserAsync();
        const string text = "#azure";

        await SendAsync(TextUpdate(text, entities: [Entity("hashtag", text, text)]));

        (await ItemsOfAsync(userId)).ShouldBeEmpty();
        Telegram.TextsTo(ChatId).ShouldHaveSingleItem().ShouldContain("Не нашёл");
    }

    [Fact]
    public async Task FileTooLargeAccordingToGetFile_IsRejectedBeforeDownload()
    {
        var userId = await LinkedUserAsync();
        // В сообщении размер не указан честно, но getFile называет настоящий.
        Telegram.AddFile("big", "x"u8.ToArray(), reportedSize: 30L * 1024 * 1024);

        await SendAsync(DocumentUpdate("big", "big.txt", 10));

        (await ItemsOfAsync(userId)).ShouldBeEmpty();
        Telegram.TextsTo(ChatId).ShouldHaveSingleItem().ShouldContain("20 МБ");
    }

    [Fact]
    public async Task FileLargerThanReported_IsRejectedByIntake()
    {
        var userId = await LinkedUserAsync();
        Telegram.AddFile("liar", new byte[21 * 1024 * 1024], reportedSize: 10);

        await SendAsync(DocumentUpdate("liar", "liar.txt", 10));

        (await ItemsOfAsync(userId)).ShouldBeEmpty();
        Telegram.TextsTo(ChatId).ShouldHaveSingleItem().ShouldStartWith("Не удалось сохранить: The file must be at most 20 MB");
    }

    [Fact]
    public async Task UnknownCommand_IsNotSavedAsNote()
    {
        var userId = await LinkedUserAsync();
        const string text = "/unknown что-то";

        await SendAsync(TextUpdate(text, entities: [Entity("bot_command", text, "/unknown")]));

        (await ItemsOfAsync(userId)).ShouldBeEmpty();
        Telegram.TextsTo(ChatId).ShouldHaveSingleItem().ShouldContain("Неизвестная команда");
    }

    [Fact]
    public async Task FailedReply_DoesNotRepeatTheJobAndCreateDuplicates()
    {
        var userId = await LinkedUserAsync();
        // Заметка сохраняется до ответа; если бы сбой ответа ронял задачу, повтор создал бы вторую заметку.
        Telegram.FailSending = true;

        await SendAsync(TextUpdate("важная заметка"));

        (await ItemsOfAsync(userId)).ShouldHaveSingleItem();
        (await AllJobsAsync()).Single().Status.ShouldBe(JobStatus.Completed);
    }
}
