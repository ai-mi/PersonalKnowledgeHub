using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using KnowledgeHub.Api.Modules.Jobs;
using KnowledgeHub.IntegrationTests.Infrastructure;
using KnowledgeHub.IntegrationTests.Items;
using KnowledgeHub.IntegrationTests.Jobs;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace KnowledgeHub.IntegrationTests.Ingestion;

public sealed class LinkIngestionTests(ApiFactory factory) : JobsTestBase(factory)
{
    private const string Article =
        """
        <!doctype html><html><head><meta charset="utf-8"><title>Service Bus queues explained</title></head><body>
        <header><nav><a href="/">Home</a></nav></header><main><article><h1>Service Bus queues explained</h1>
        <p>Azure Service Bus is a fully managed enterprise message broker with message queues and publish-subscribe topics.
        Queues decouple producers from consumers, so a slow consumer never blocks the sender, and messages survive restarts.</p>
        <p>Dead-letter queues collect messages that cannot be delivered, while sessions guarantee ordering for related messages
        and duplicate detection drops repeated sends within a configurable time window.</p>
        <p>Choosing between queues and topics depends on whether one consumer or many subscribers need every message,
        and on how filtering rules should route copies of a message to each subscription.</p>
        </article></main><footer>Copyright Example</footer></body></html>
        """;

    private static string Url(WireMockServer server, string path = "/page") => $"http://127.0.0.1:{server.Port}{path}";

    private static void Serve(WireMockServer server, string body, string path = "/page", string contentType = "text/html; charset=utf-8") =>
        server.Given(Request.Create().WithPath(path).UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", contentType).WithBody(body));

    private async Task<HttpClient> UserAsync() => await Factory.CreateClientForAsync(Guid.CreateVersion7());

    private static Task<JsonElement> GetItemAsync(HttpClient user, Guid id) => user.GetFromJsonAsync<JsonElement>($"/api/items/{id}", Ct);

    private static async Task<Guid> SaveLinkAsync(HttpClient user, string url, string? title = null, string? userNote = null) =>
        (await ItemEndpointsTests.CreateAsync(user, new { type = "link", url, title, userNote })).GetProperty("id").GetGuid();

    private async Task RetryAfterAsync(TimeSpan delay)
    {
        Time.Advance(delay);
        (await Worker.ProcessNextAsync(Ct)).ShouldBeTrue();
    }

    [Fact]
    public async Task NewLink_IsPendingAndQueuedOnce()
    {
        using var user = await UserAsync();

        var id = await SaveLinkAsync(user, "https://example.com/a");
        await SaveLinkAsync(user, "https://example.com/a#again");

        (await GetItemAsync(user, id)).GetProperty("status").GetString().ShouldBe("pending");
        (await AllJobsAsync()).Count(j => j.Type == "ingest").ShouldBe(1);
    }

    [Fact]
    public async Task Article_BecomesReadyAndIsSearchableByText()
    {
        using var server = WireMockServer.Start();
        Serve(server, Article);
        using var user = await UserAsync();
        var id = await SaveLinkAsync(user, Url(server));

        (await ProcessAsync()).ShouldBeTrue();

        var item = await GetItemAsync(user, id);
        item.GetProperty("status").GetString().ShouldBe("ready");
        item.GetProperty("title").GetString().ShouldBe("Service Bus queues explained");
        item.GetProperty("body").GetString().ShouldNotBeNull().ShouldContain("duplicate detection");
        item.GetProperty("error").ValueKind.ShouldBe(JsonValueKind.Null);
        (await Search.SearchTests.SearchAsync(user, "\"duplicate detection\"")).GetProperty("total").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task UserTitle_IsNotOverwritten()
    {
        using var server = WireMockServer.Start();
        Serve(server, Article);
        using var user = await UserAsync();
        var id = await SaveLinkAsync(user, Url(server), title: "My title");

        await ProcessAsync();

        var item = await GetItemAsync(user, id);
        item.GetProperty("status").GetString().ShouldBe("ready");
        item.GetProperty("title").GetString().ShouldBe("My title");
    }

    [Fact]
    public async Task Windows1251Page_IsDecoded()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var paragraphs = string.Concat(Enumerable.Repeat(
            "<p>Очереди сообщений позволяют разделить отправителя и получателя: медленный потребитель не блокирует отправителя.</p>", 6));
        var page = $"<html><head><title>Очереди</title></head><body><article><h1>Очереди</h1>{paragraphs}</article></body></html>";
        using var server = WireMockServer.Start();
        server.Given(Request.Create().WithPath("/page").UsingGet()).RespondWith(Response.Create()
            .WithHeader("Content-Type", "text/html; charset=windows-1251").WithBody(Encoding.GetEncoding("windows-1251").GetBytes(page)));
        using var user = await UserAsync();
        var id = await SaveLinkAsync(user, Url(server));

        await ProcessAsync();

        var item = await GetItemAsync(user, id);
        item.GetProperty("title").GetString().ShouldBe("Очереди");
        item.GetProperty("body").GetString().ShouldNotBeNull().ShouldContain("медленный потребитель");
    }

    [Fact]
    public async Task RedirectToInternalAddress_FailsWithoutRetriesAndKeepsItemFindable()
    {
        using var server = WireMockServer.Start();
        server.Given(Request.Create().WithPath("/page").UsingGet()).RespondWith(Response.Create()
            .WithStatusCode(302).WithHeader("Location", "http://169.254.169.254/latest/meta-data/"));
        using var user = await UserAsync();
        var id = await SaveLinkAsync(user, Url(server), userNote: "metadatalookalike");

        await ProcessAsync();

        var item = await GetItemAsync(user, id);
        item.GetProperty("status").GetString().ShouldBe("failed");
        item.GetProperty("error").GetString().ShouldNotBeNull().ShouldNotContain("   at ");
        (await Worker.ProcessNextAsync(Ct)).ShouldBeFalse();
        (await Search.SearchTests.SearchAsync(user, "metadatalookalike")).GetProperty("total").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task ResponseOver5Mb_Fails()
    {
        using var server = WireMockServer.Start();
        Serve(server, "<html><body>" + new string('a', 6 * 1024 * 1024) + "</body></html>");
        using var user = await UserAsync();
        var id = await SaveLinkAsync(user, Url(server));

        await ProcessAsync();

        var item = await GetItemAsync(user, id);
        item.GetProperty("status").GetString().ShouldBe("failed");
        item.GetProperty("error").GetString().ShouldNotBeNull().ShouldContain("слишком большая");
    }

    [Fact]
    public async Task NonHtmlContent_Fails()
    {
        using var server = WireMockServer.Start();
        Serve(server, "%PDF-1.7 ...", contentType: "application/pdf");
        using var user = await UserAsync();
        var id = await SaveLinkAsync(user, Url(server));

        await ProcessAsync();

        (await GetItemAsync(user, id)).GetProperty("status").GetString().ShouldBe("failed");
    }

    [Fact]
    public async Task TemporaryServerError_IsRetriedThenSucceeds()
    {
        using var server = WireMockServer.Start();
        server.Given(Request.Create().WithPath("/page").UsingGet()).RespondWith(Response.Create().WithStatusCode(503));
        using var user = await UserAsync();
        var id = await SaveLinkAsync(user, Url(server));

        await ProcessAsync();

        var pending = (await AllJobsAsync()).Single();
        pending.Status.ShouldBe(JobStatus.Pending);
        pending.Attempts.ShouldBe(1);
        (await GetItemAsync(user, id)).GetProperty("status").GetString().ShouldBe("processing");

        // Сайт «починился» до повтора.
        server.Reset();
        Serve(server, Article);
        await RetryAfterAsync(TimeSpan.FromMinutes(1));

        (await GetItemAsync(user, id)).GetProperty("status").GetString().ShouldBe("ready");
        (await AllJobsAsync()).Single().Status.ShouldBe(JobStatus.Completed);
    }

    [Fact]
    public async Task PersistentServerError_FailsItemAfterFourAttempts()
    {
        using var server = WireMockServer.Start();
        server.Given(Request.Create().WithPath("/page").UsingGet()).RespondWith(Response.Create().WithStatusCode(503));
        using var user = await UserAsync();
        var id = await SaveLinkAsync(user, Url(server));

        await ProcessAsync();
        foreach (var delay in new[] { 1, 5, 30 })
        {
            await RetryAfterAsync(TimeSpan.FromMinutes(delay));
        }

        (await AllJobsAsync()).Single().Status.ShouldBe(JobStatus.Failed);
        var item = await GetItemAsync(user, id);
        item.GetProperty("status").GetString().ShouldBe("failed");
        item.GetProperty("error").GetString().ShouldBe("Страница ответила кодом 503.");
    }

    [Fact]
    public async Task Reprocess_RequeuesFailedLink()
    {
        using var server = WireMockServer.Start();
        server.Given(Request.Create().WithPath("/page").UsingGet()).RespondWith(Response.Create().WithStatusCode(404));
        using var user = await UserAsync();
        var id = await SaveLinkAsync(user, Url(server));
        await ProcessAsync();
        (await GetItemAsync(user, id)).GetProperty("status").GetString().ShouldBe("failed");

        server.Reset();
        Serve(server, Article);
        var response = await user.PostAsync($"/api/items/{id}/reprocess", null, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var reprocessed = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        reprocessed.GetProperty("status").GetString().ShouldBe("pending");
        reprocessed.GetProperty("error").ValueKind.ShouldBe(JsonValueKind.Null);

        // Повторный запрос, пока запись ждёт обработки, вторую задачу не создаёт.
        (await user.PostAsync($"/api/items/{id}/reprocess", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await AllJobsAsync()).Count(j => j.Status == JobStatus.Pending).ShouldBe(1);

        await ProcessAsync();

        (await GetItemAsync(user, id)).GetProperty("status").GetString().ShouldBe("ready");
    }

    [Fact]
    public async Task Reprocess_NoteIsRejectedAndUnknownIsNotFound()
    {
        using var user = await UserAsync();
        var noteId = await ItemEndpointsTests.CreateNoteIdAsync(user);

        (await user.PostAsync($"/api/items/{noteId}/reprocess", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await user.PostAsync($"/api/items/{Guid.CreateVersion7()}/reprocess", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeletedItem_JobCompletesQuietly()
    {
        using var user = await UserAsync();
        var id = await SaveLinkAsync(user, "https://example.com/gone");
        (await user.DeleteAsync($"/api/items/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await ProcessAsync();

        (await AllJobsAsync()).Single().Status.ShouldBe(JobStatus.Completed);
    }
}
