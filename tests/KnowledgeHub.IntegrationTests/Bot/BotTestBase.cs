using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using KnowledgeHub.Api.Modules.Bot;
using KnowledgeHub.Api.Modules.Items;
using KnowledgeHub.Api.Modules.Jobs;
using KnowledgeHub.IntegrationTests.Infrastructure;
using KnowledgeHub.IntegrationTests.Jobs;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeHub.IntegrationTests.Bot;

/// <summary>Общие помощники тестов бота: запросы к webhook в формате Telegram, привязка чата, выполнение очереди.</summary>
public abstract class BotTestBase : JobsTestBase
{
    protected const long ChatId = 4242;

    private int _nextUpdateId = 1000;

    protected BotTestBase(ApiFactory factory) : base(factory) => factory.Telegram.Reset();

    protected FakeTelegramClient Telegram => Factory.Telegram;

    /// <summary>Update с текстовым сообщением; <paramref name="entities"/> — как в Bot API: <c>type</c>, <c>offset</c>, <c>length</c>.</summary>
    protected string TextUpdate(string text, long chatId = ChatId, IEnumerable<object>? entities = null, int? updateId = null) =>
        Update(updateId, new Dictionary<string, object?> { ["text"] = text, ["entities"] = entities?.ToArray() }, chatId);

    protected string DocumentUpdate(string fileId, string fileName, long fileSize, string? caption = null, IEnumerable<object>? captionEntities = null) =>
        Update(null, new Dictionary<string, object?>
        {
            ["document"] = new { file_id = fileId, file_unique_id = fileId, file_name = fileName, file_size = fileSize },
            ["caption"] = caption,
            ["caption_entities"] = captionEntities?.ToArray(),
        }, ChatId);

    private string Update(int? updateId, Dictionary<string, object?> content, long chatId)
    {
        var message = new Dictionary<string, object?>
        {
            ["message_id"] = 1,
            ["date"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ["chat"] = new { id = chatId, type = "private" },
            ["from"] = new { id = chatId, is_bot = false, first_name = "Test" },
        };
        foreach (var (key, value) in content.Where(c => c.Value is not null))
        {
            message[key] = value;
        }

        return JsonSerializer.Serialize(new { update_id = updateId ?? _nextUpdateId++, message });
    }

    protected static object Entity(string type, string text, string span, string? url = null) =>
        new { type, offset = text.IndexOf(span, StringComparison.Ordinal), length = span.Length, url };

    protected Task<HttpResponseMessage> PostWebhookAsync(string json, string? secret = ApiFactory.BotWebhookSecret)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, WebhookEndpoint.Path)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        if (secret is not null)
        {
            request.Headers.Add(WebhookEndpoint.SecretHeader, secret);
        }

        return Client.SendAsync(request, Ct);
    }

    /// <summary>Присылает update от Telegram и выполняет все задачи очереди.</summary>
    protected async Task SendAsync(string json)
    {
        (await PostWebhookAsync(json)).EnsureSuccessStatusCode();
        await ProcessAllAsync();
    }

    /// <summary>
    /// Выполняет задачи бота. Задачи <c>ingest</c> откладываются далеко вперёд: им нужна сеть, а тесты бота проверяют только
    /// то, что запись создана и обработка поставлена в очередь.
    /// </summary>
    protected async Task ProcessAllAsync()
    {
        do
        {
            var postponedUntil = DateTimeOffset.UtcNow.AddDays(30);
            await WithDbAsync((db, _) => db.Jobs
                .Where(j => j.Type == "ingest" && j.Status == JobStatus.Pending)
                .ExecuteUpdateAsync(s => s.SetProperty(j => j.RunAfter, postponedUntil), Ct));
        }
        while (await ProcessAsync());
    }

    protected async Task<Guid> LinkedUserAsync(long chatId = ChatId)
    {
        var userId = Guid.CreateVersion7();
        using var client = await Factory.CreateClientForAsync(userId);
        await WithDbAsync(async (db, _) =>
        {
            db.TelegramLinks.Add(new TelegramLink { UserId = userId, ChatId = chatId, LinkedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync(Ct);
            return 0;
        });
        return userId;
    }

    protected Task<List<Item>> ItemsOfAsync(Guid userId) =>
        WithDbAsync((db, _) => db.Items.IgnoreQueryFilters().Include(i => i.Tags).AsNoTracking().Where(i => i.UserId == userId).ToListAsync(Ct));

    protected Task<int> LinkCountAsync() =>
        WithDbAsync((db, _) => db.TelegramLinks.CountAsync(Ct));

    /// <summary>Просит ссылку привязки как пользователь и возвращает код из неё.</summary>
    protected static async Task<string> RequestLinkCodeAsync(HttpClient user)
    {
        var response = await user.PostAsync("/api/me/telegram/link", null, Ct);
        response.EnsureSuccessStatusCode();
        var url = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("url").GetString()!;
        url.ShouldStartWith("https://t.me/kh_test_bot?start=");
        return url[(url.IndexOf('=') + 1)..];
    }
}
