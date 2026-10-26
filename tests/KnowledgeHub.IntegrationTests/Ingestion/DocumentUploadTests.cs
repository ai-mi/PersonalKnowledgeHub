using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Azure.Storage.Blobs;
using KnowledgeHub.Api.Infrastructure.Storage;
using KnowledgeHub.IntegrationTests.Identity;
using KnowledgeHub.IntegrationTests.Infrastructure;
using KnowledgeHub.IntegrationTests.Items;
using KnowledgeHub.IntegrationTests.Jobs;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeHub.IntegrationTests.Ingestion;

public sealed class DocumentUploadTests(ApiFactory factory) : JobsTestBase(factory)
{
    private static byte[] Fixture(string name) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Ingestion", "Fixtures", name));

    private async Task<HttpClient> UserAsync(Guid? id = null) => await Factory.CreateClientForAsync(id ?? Guid.CreateVersion7());

    /// <summary>Загружает файл от имени клиента (multipart); поля формы — необязательные.</summary>
    public static async Task<HttpResponseMessage> UploadAsync(
        HttpClient client, byte[] bytes, string fileName, string? title = null, string? userNote = null,
        IEnumerable<string>? tags = null, IEnumerable<string>? collectionIds = null)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);
        if (title is not null)
        {
            form.Add(new StringContent(title), "title");
        }

        if (userNote is not null)
        {
            form.Add(new StringContent(userNote), "userNote");
        }

        foreach (var tag in tags ?? [])
        {
            form.Add(new StringContent(tag), "tags");
        }

        foreach (var id in collectionIds ?? [])
        {
            form.Add(new StringContent(id), "collectionIds");
        }

        return await client.PostAsync("/api/items/upload", form, Ct);
    }

    /// <summary>Загружает файл и возвращает идентификатор записи; код ответа — 201 или 200.</summary>
    public static async Task<string> UploadIdAsync(HttpClient client, byte[] bytes, string fileName)
    {
        var response = await UploadAsync(client, bytes, fileName);
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetString()!;
    }

    private static Task<JsonElement> GetItemAsync(HttpClient user, string id) => user.GetFromJsonAsync<JsonElement>($"/api/items/{id}", Ct);

    private static async Task<int> BlobCountAsync(Guid userId, string connectionString)
    {
        var container = new BlobContainerClient(connectionString, BlobContainers.Documents);
        var count = 0;
        await foreach (var _ in container.GetBlobsAsync(Azure.Storage.Blobs.Models.BlobTraits.None, Azure.Storage.Blobs.Models.BlobStates.None, $"{userId}/", Ct))
        {
            count++;
        }

        return count;
    }

    private Task<int> BlobCountAsync(Guid userId) => BlobCountAsync(userId, Factory.AzuriteConnectionString);

    [Fact]
    public async Task Pdf_IsPendingThenReadyAndSearchableByText()
    {
        using var user = await UserAsync();

        var response = await UploadAsync(user, Fixture("sample.pdf"), "Quarterly report.pdf");

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        created.GetProperty("type").GetString().ShouldBe("document");
        created.GetProperty("status").GetString().ShouldBe("pending");
        created.GetProperty("title").GetString().ShouldBe("Quarterly report");
        response.Headers.Location!.ToString().ShouldBe($"/api/items/{created.GetProperty("id").GetString()}");
        (await Search.SearchTests.SearchAsync(user, "zeppelin")).GetProperty("total").GetInt32().ShouldBe(0);

        (await ProcessAsync()).ShouldBeTrue();

        var item = await GetItemAsync(user, created.GetProperty("id").GetString()!);
        item.GetProperty("status").GetString().ShouldBe("ready");
        item.GetProperty("title").GetString().ShouldBe("Quarterly report");
        item.GetProperty("body").GetString().ShouldNotBeNull().ShouldContain("Inspect the gas cells");
        (await Search.SearchTests.SearchAsync(user, "zeppelin")).GetProperty("total").GetInt32().ShouldBe(1);
    }

    [Theory]
    [InlineData("sample.docx", "migration.docx", "quokka")]
    [InlineData("sample.docx", "migration.docx", "воркером")]
    [InlineData("sample.txt", "notes.txt", "ocelot")]
    [InlineData("sample.txt", "notes.md", "очереди")]
    public async Task OtherFormats_AreExtractedAndSearchable(string fixture, string fileName, string query)
    {
        using var user = await UserAsync();
        var id = await UploadIdAsync(user, Fixture(fixture), fileName);

        await ProcessAsync();

        (await GetItemAsync(user, id)).GetProperty("status").GetString().ShouldBe("ready");
        (await Search.SearchTests.SearchAsync(user, query)).GetProperty("total").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task Metadata_TitleNoteTagsAndCollections_AreApplied()
    {
        using var user = await UserAsync();
        var collection = await CollectionsTests.CreateCollectionIdAsync(user);

        var response = await UploadAsync(
            user, Fixture("sample.txt"), "notes.txt", title: "My title", userNote: "read me", tags: ["Azure", "#bus"], collectionIds: [collection]);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var item = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        item.GetProperty("title").GetString().ShouldBe("My title");
        item.GetProperty("userNote").GetString().ShouldBe("read me");
        item.GetProperty("tags").EnumerateArray().Select(t => t.GetString()).ShouldBe(["Azure", "bus"]);
        item.GetProperty("collections").EnumerateArray().Single().GetProperty("id").GetString().ShouldBe(collection);

        await ProcessAsync();
        // Заголовок, заданный пользователем, воркер не перезаписывает.
        (await GetItemAsync(user, item.GetProperty("id").GetString()!)).GetProperty("title").GetString().ShouldBe("My title");
    }

    [Fact]
    public async Task SameFileTwice_ReturnsExistingItemWithoutSecondBlobOrJob()
    {
        var userId = Guid.CreateVersion7();
        using var user = await UserAsync(userId);
        var first = await UploadAsync(user, Fixture("sample.pdf"), "a.pdf");
        var second = await UploadAsync(user, Fixture("sample.pdf"), "renamed.pdf");

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await second.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetString()
            .ShouldBe((await first.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetString());
        (await AllJobsAsync()).Count(j => j.Type == "ingest").ShouldBe(1);
        (await BlobCountAsync(userId)).ShouldBe(1);
    }

    [Fact]
    public async Task SameFile_ByAnotherUser_IsNotADuplicate()
    {
        using var a = await UserAsync();
        using var b = await UserAsync();

        var idA = await UploadIdAsync(a, Fixture("sample.pdf"), "a.pdf");
        var idB = await UploadIdAsync(b, Fixture("sample.pdf"), "a.pdf");

        idB.ShouldNotBe(idA);
    }

    [Theory]
    [InlineData("invoice.pdf", new byte[] { 0x4D, 0x5A, 0x90, 0x00, 0x03 })]
    [InlineData("invoice.txt", new byte[] { 0x4D, 0x5A, 0x00, 0x03 })]
    [InlineData("page.html", new byte[] { 0x3C, 0x68, 0x31, 0x3E })]
    public async Task UnsupportedFile_Returns400_AndLeavesNoTrace(string fileName, byte[] bytes)
    {
        var userId = Guid.CreateVersion7();
        using var user = await UserAsync(userId);

        var response = await UploadAsync(user, bytes, fileName);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errors").TryGetProperty("file", out _).ShouldBeTrue();
        (await user.GetFromJsonAsync<JsonElement>("/api/items", Ct)).GetProperty("total").GetInt32().ShouldBe(0);
        (await BlobCountAsync(userId)).ShouldBe(0);
        (await AllJobsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task MissingFile_Returns400()
    {
        using var user = await UserAsync();
        using var form = new MultipartFormDataContent { { new StringContent("x"), "title" } };

        var response = await user.PostAsync("/api/items/upload", form, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task InvalidMetadata_Returns400_AndRemovesUploadedBlob()
    {
        var userId = Guid.CreateVersion7();
        using var user = await UserAsync(userId);

        var response = await UploadAsync(user, Fixture("sample.pdf"), "a.pdf", title: new string('t', 501));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errors").TryGetProperty("title", out _).ShouldBeTrue();
        (await BlobCountAsync(userId)).ShouldBe(0);
        (await user.GetFromJsonAsync<JsonElement>("/api/items", Ct)).GetProperty("total").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task FileOver20Mb_Returns413()
    {
        var userId = Guid.CreateVersion7();
        using var user = await UserAsync(userId);
        var bytes = new byte[21 * 1024 * 1024];
        Array.Fill(bytes, (byte)'a');

        var response = await UploadAsync(user, bytes, "big.txt");

        response.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        (await BlobCountAsync(userId)).ShouldBe(0);
    }

    [Fact]
    public async Task FileJustUnder20Mb_IsAccepted()
    {
        using var user = await UserAsync();
        var bytes = new byte[20 * 1024 * 1024];
        Array.Fill(bytes, (byte)'a');

        (await UploadAsync(user, bytes, "large.txt")).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task ApiToken_UploadsWithoutXsrfToken()
    {
        using var owner = await UserAsync();
        var (_, token) = await ApiTokenTests.CreateTokenAsync(owner);
        using var bearer = Factory.CreateClient();
        bearer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await UploadAsync(bearer, Fixture("sample.txt"), "notes.txt");

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task CookieSession_RequiresXsrfToken()
    {
        using var devFactory = Factory.WithWebHostBuilder(b => b.UseEnvironment("Development").UseSetting("Auth:EnableDevLogin", "true"));
        using var cookies = devFactory.CreateClient();
        (await cookies.PostAsJsonAsync("/api/auth/dev-login", new { email = "uploader@example.com" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var xsrf = await AntiforgeryTests.FetchXsrfTokenAsync(cookies, Ct);

        var withoutToken = await UploadAsync(cookies, Fixture("sample.txt"), "notes.txt");
        withoutToken.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await withoutToken.Content.ReadAsStringAsync(Ct)).ShouldContain("antiforgery");

        cookies.DefaultRequestHeaders.Add("X-XSRF-TOKEN", xsrf);
        (await UploadAsync(cookies, Fixture("sample.txt"), "notes.txt")).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Anonymous_Upload_Returns401()
    {
        var response = await UploadAsync(Client, Fixture("sample.txt"), "notes.txt");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Download_ReturnsOriginalBytesAsAttachment()
    {
        using var user = await UserAsync();
        var bytes = Fixture("sample.pdf");
        var id = await UploadIdAsync(user, bytes, "Quarterly report.pdf");

        var response = await user.GetAsync($"/api/items/{id}/file", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentDisposition!.DispositionType.ShouldBe("attachment");
        response.Content.Headers.ContentDisposition.FileNameStar.ShouldBe("Quarterly report.pdf");
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        response.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
        (await response.Content.ReadAsByteArrayAsync(Ct)).ShouldBe(bytes);
    }

    [Fact]
    public async Task Download_OfTextFile_NeverServesActiveContentTypes()
    {
        using var user = await UserAsync();
        // Документ с HTML внутри, загруженный как .txt, отдаётся как text/plain-вложение, а не как страница.
        var id = await UploadIdAsync(user, Encoding.UTF8.GetBytes("<script>alert(1)</script>"), "x.txt");

        var response = await user.GetAsync($"/api/items/{id}/file", Ct);

        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/plain");
        response.Content.Headers.ContentDisposition!.DispositionType.ShouldBe("attachment");
    }

    [Fact]
    public async Task Download_OfNoteOrUnknownItem_Returns404()
    {
        using var user = await UserAsync();
        var note = await ItemEndpointsTests.CreateNoteIdAsync(user);

        (await user.GetAsync($"/api/items/{note}/file", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await user.GetAsync($"/api/items/{Guid.NewGuid()}/file", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Download_ByAnotherUser_Returns404()
    {
        using var a = await UserAsync();
        using var b = await UserAsync();
        var id = await UploadIdAsync(a, Fixture("sample.pdf"), "a.pdf");

        (await b.GetAsync($"/api/items/{id}/file", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_RemovesTheBlob()
    {
        var userId = Guid.CreateVersion7();
        using var user = await UserAsync(userId);
        var id = await UploadIdAsync(user, Fixture("sample.pdf"), "a.pdf");
        (await BlobCountAsync(userId)).ShouldBe(1);

        (await user.DeleteAsync($"/api/items/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await BlobCountAsync(userId)).ShouldBe(0);
        (await user.GetAsync($"/api/items/{id}/file", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UnreadablePdf_FailsWithoutRetryAndKeepsItem()
    {
        using var user = await UserAsync();
        var id = await UploadIdAsync(user, "%PDF-1.4 this is not really a pdf"u8.ToArray(), "broken.pdf");

        await ProcessAsync();

        var item = await GetItemAsync(user, id);
        item.GetProperty("status").GetString().ShouldBe("failed");
        item.GetProperty("error").GetString().ShouldBe("Не удалось извлечь текст из документа.");
        item.GetProperty("title").GetString().ShouldBe("broken");
        (await AllJobsAsync()).Single().Status.ToString().ShouldBe("Completed");
        // Запись остаётся находимой по заголовку, файл — доступным для скачивания.
        (await Search.SearchTests.SearchAsync(user, "broken")).GetProperty("total").GetInt32().ShouldBe(1);
        (await user.GetAsync($"/api/items/{id}/file", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task MissingBlob_FailsWithClearError()
    {
        var userId = Guid.CreateVersion7();
        using var user = await UserAsync(userId);
        var id = await UploadIdAsync(user, Fixture("sample.txt"), "notes.txt");
        var store = Factory.Services.GetRequiredService<IBlobStore>();
        await store.DeleteAsync(BlobContainers.Documents, $"{userId}/{id}/notes.txt", Ct);

        await ProcessAsync();

        var item = await GetItemAsync(user, id);
        item.GetProperty("status").GetString().ShouldBe("failed");
        item.GetProperty("error").GetString().ShouldBe("Файл документа не найден.");
    }

    [Fact]
    public async Task Reprocess_OfFailedDocument_QueuesItAgain()
    {
        using var user = await UserAsync();
        var id = await UploadIdAsync(user, "%PDF-1.4 this is not really a pdf"u8.ToArray(), "broken.pdf");
        await ProcessAsync();

        var response = await user.PostAsync($"/api/items/{id}/reprocess", null, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("status").GetString().ShouldBe("pending");
        (await AllJobsAsync()).Count(j => j.Type == "ingest").ShouldBe(2);
    }
}
