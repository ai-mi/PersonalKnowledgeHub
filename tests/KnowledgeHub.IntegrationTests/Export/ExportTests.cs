using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Azure.Storage.Blobs;
using KnowledgeHub.Api.Infrastructure.Storage;
using KnowledgeHub.Api.Modules.Export;
using KnowledgeHub.Api.Modules.Jobs;
using KnowledgeHub.IntegrationTests.Infrastructure;
using KnowledgeHub.IntegrationTests.Ingestion;
using KnowledgeHub.IntegrationTests.Items;
using KnowledgeHub.IntegrationTests.Jobs;
using KnowledgeHub.IntegrationTests.Reminders;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeHub.IntegrationTests.Export;

public sealed class ExportTests(ApiFactory factory) : JobsTestBase(factory)
{
    private Task<HttpClient> UserAsync(Guid? id = null) => Factory.CreateClientForAsync(id ?? Guid.CreateVersion7());

    public static async Task<string> StartAsync(HttpClient client)
    {
        var response = await client.PostAsync("/api/exports", null, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetString()!;
    }

    private async Task DrainJobsAsync()
    {
        while (await ProcessAsync())
        {
        }
    }

    private async Task<string> ExportAndWaitAsync(HttpClient client)
    {
        var id = await StartAsync(client);
        await DrainJobsAsync();
        return id;
    }

    private static async Task<ZipArchive> DownloadAsync(HttpClient client, string id)
    {
        var response = await client.GetAsync($"/api/exports/{id}/download", Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/zip");
        return new ZipArchive(new MemoryStream(await response.Content.ReadAsByteArrayAsync(Ct)), ZipArchiveMode.Read);
    }

    private static string ReadEntry(ZipArchive zip, string name)
    {
        using var reader = new StreamReader(zip.GetEntry(name)!.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static async Task<JsonElement> ListAsync(HttpClient client) => await client.GetFromJsonAsync<JsonElement>("/api/exports", Ct);

    [Fact]
    public async Task Archive_ContainsMarkdownDocumentFilesAndExportJson_ButNothingOfOtherUsers()
    {
        using var a = await UserAsync();
        using var b = await UserAsync();
        var note = await ItemEndpointsTests.CreateAsync(a, new { type = "note", title = "Заметка: про Azure", body = "text of note", userNote = "my note", tags = new[] { "Azure" } });
        var snippet = await ItemEndpointsTests.CreateAsync(a, new { type = "snippet", title = "Snip", body = "var x = 1;", codeLanguage = "csharp" });
        var documentId = await DocumentUploadTests.UploadIdAsync(a, "document text"u8.ToArray(), "notes.txt");
        await ReminderEndpointsTests.CreateIdAsync(a, note.GetProperty("id").GetString()!);
        await ItemEndpointsTests.CreateAsync(b, new { type = "note", title = "Secret of B", body = "b-only" });

        var id = await ExportAndWaitAsync(a);

        using var zip = await DownloadAsync(a, id);
        var names = zip.Entries.Select(e => e.FullName).ToList();
        names.Count(n => n.StartsWith("items/") && n.EndsWith(".md")).ShouldBe(3);
        names.ShouldContain("export.json");
        names.ShouldContain($"files/{documentId}/notes.txt");
        ReadEntry(zip, $"files/{documentId}/notes.txt").ShouldBe("document text");

        var noteMarkdown = ReadEntry(zip, names.Single(n => n.StartsWith("items/zametka-pro-azure-")));
        noteMarkdown.ShouldContain("title: 'Заметка: про Azure'");
        noteMarkdown.ShouldContain("my note");
        noteMarkdown.ShouldContain("text of note");
        var snippetMarkdown = ReadEntry(zip, names.Single(n => n.StartsWith("items/snip-")));
        snippetMarkdown.ShouldContain("```csharp\nvar x = 1;\n```");
        snippetMarkdown.ShouldContain(snippet.GetProperty("id").GetString()!);

        var json = JsonDocument.Parse(ReadEntry(zip, "export.json")).RootElement;
        json.GetProperty("items").GetArrayLength().ShouldBe(3);
        json.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetString()).ShouldContain(documentId);
        var jsonNote = json.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetString() == note.GetProperty("id").GetString());
        jsonNote.GetProperty("tags").EnumerateArray().Select(t => t.GetString()).ShouldBe(["Azure"]);
        jsonNote.GetProperty("type").GetString().ShouldBe("note");
        json.GetProperty("tags").GetArrayLength().ShouldBe(1);
        json.GetProperty("reminders").GetArrayLength().ShouldBe(1);
        json.GetProperty("settings").GetProperty("timezone").GetString().ShouldBe("UTC");
        json.GetProperty("collections").GetArrayLength().ShouldBe(0);

        ReadAll(zip).ShouldNotContain("Secret of B");
        ReadAll(zip).ShouldNotContain("b-only");
    }

    private static string ReadAll(ZipArchive zip) =>
        string.Concat(zip.Entries.Where(e => !e.FullName.StartsWith("files/")).Select(e => ReadEntry(zip, e.FullName)));

    [Fact]
    public async Task Archive_HandlesMoreItemsThanOneBatch()
    {
        using var user = await UserAsync();
        for (var i = 0; i < 105; i++)
        {
            await ItemEndpointsTests.CreateAsync(user, new { type = "note", title = $"Note {i}", body = "x" });
        }

        var id = await ExportAndWaitAsync(user);

        using var zip = await DownloadAsync(user, id);
        zip.Entries.Count(e => e.FullName.StartsWith("items/")).ShouldBe(105);
        JsonDocument.Parse(ReadEntry(zip, "export.json")).RootElement.GetProperty("items").GetArrayLength().ShouldBe(105);
    }

    [Fact]
    public async Task EmptyAccount_ProducesAValidArchive()
    {
        using var user = await UserAsync();
        var id = await ExportAndWaitAsync(user);

        using var zip = await DownloadAsync(user, id);
        zip.Entries.Select(e => e.FullName).ShouldBe(["export.json"]);
        JsonDocument.Parse(ReadEntry(zip, "export.json")).RootElement.GetProperty("items").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task StatusAndExpiry_AreListed()
    {
        using var user = await UserAsync();
        var id = await StartAsync(user);

        var pending = (await ListAsync(user)).EnumerateArray().Single();
        pending.GetProperty("id").GetString().ShouldBe(id);
        pending.GetProperty("status").GetString().ShouldBe("pending");
        pending.GetProperty("expiresAt").ValueKind.ShouldBe(JsonValueKind.Null);

        await DrainJobsAsync();

        var ready = (await ListAsync(user)).EnumerateArray().Single();
        ready.GetProperty("status").GetString().ShouldBe("ready");
        ready.GetProperty("expiresAt").GetDateTimeOffset().ShouldBe(Time.GetUtcNow() + ExportService.Retention, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task SecondExportWhileTheFirstIsRunning_IsRejectedWithConflict()
    {
        using var user = await UserAsync();
        await StartAsync(user);

        var second = await user.PostAsync("/api/exports", null, Ct);

        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        second.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        (await ListAsync(user)).GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task ParallelStarts_CreateOnlyOneExport()
    {
        using var user = await UserAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => user.PostAsync("/api/exports", null, Ct)));

        responses.Count(r => r.StatusCode == HttpStatusCode.Accepted).ShouldBe(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).ShouldBe(4);
    }

    [Fact]
    public async Task AnotherExport_IsAllowedAfterTheFirstIsReady()
    {
        using var user = await UserAsync();
        await ExportAndWaitAsync(user);

        await StartAsync(user);

        (await ListAsync(user)).GetArrayLength().ShouldBe(2);
    }

    [Fact]
    public async Task OneUsersActiveExport_DoesNotBlockAnotherUser()
    {
        using var a = await UserAsync();
        using var b = await UserAsync();
        await StartAsync(a);

        await StartAsync(b);
    }

    [Fact]
    public async Task Download_BeforeTheArchiveIsReady_IsConflict()
    {
        using var user = await UserAsync();
        var id = await StartAsync(user);

        (await user.GetAsync($"/api/exports/{id}/download", Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Download_SetsAnAttachmentFileNameWithTheDate()
    {
        using var user = await UserAsync();
        var id = await ExportAndWaitAsync(user);

        var response = await user.GetAsync($"/api/exports/{id}/download", Ct);

        response.Content.Headers.ContentDisposition!.DispositionType.ShouldBe("attachment");
        response.Content.Headers.ContentDisposition.FileName.ShouldBe($"knowledgehub-export-{DateTimeOffset.UtcNow:yyyy-MM-dd}.zip");
    }

    [Fact]
    public async Task UnknownExport_IsNotFound()
    {
        using var user = await UserAsync();

        (await user.GetAsync($"/api/exports/{Guid.NewGuid()}/download", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ExpiredExport_IsDeletedWithItsBlob_AndFreshOnesStay()
    {
        using var user = await UserAsync();
        var old = await ExportAndWaitAsync(user);
        Time.Advance(TimeSpan.FromDays(6));
        var fresh = await ExportAndWaitAsync(user);
        Time.Advance(TimeSpan.FromDays(2));

        await Services.GetRequiredService<RecurringScheduler>().ExecuteOnceAsync(typeof(ExpiredExportsCleanupTask), "exports-cleanup", Ct);

        var remaining = (await ListAsync(user)).EnumerateArray().Select(e => e.GetProperty("id").GetString()).ToList();
        remaining.ShouldBe([fresh]);
        (await user.GetAsync($"/api/exports/{old}/download", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await BlobNamesAsync()).Count(n => n.EndsWith($"{old}.zip")).ShouldBe(0);
        (await BlobNamesAsync()).Count(n => n.EndsWith($"{fresh}.zip")).ShouldBe(1);
    }

    [Fact]
    public async Task FailedExport_IsMarkedFailed_AndDoesNotBlockTheNextOne()
    {
        using var user = await UserAsync();
        await StartAsync(user);
        var job = (await AllJobsAsync()).Single(j => j.Type == BuildExportJob.JobType);

        await using (var scope = Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<KnowledgeHub.Api.Common.SystemContext>();
            using var _ = context.RunAs(job.UserId!.Value);
            await scope.ServiceProvider.GetServices<IJobFailureHandler>().OfType<ExportFailureHandler>().Single().OnFailedAsync(job, "boom", Ct);
        }

        (await ListAsync(user)).EnumerateArray().Single().GetProperty("status").GetString().ShouldBe("failed");
        await StartAsync(user);
    }

    private async Task<List<string>> BlobNamesAsync()
    {
        var names = new List<string>();
        var container = new BlobContainerClient(Factory.AzuriteConnectionString, BlobContainers.Exports);
        await foreach (var blob in container.GetBlobsAsync(cancellationToken: Ct))
        {
            names.Add(blob.Name);
        }

        return names;
    }

    [Fact]
    public async Task ExportJob_UsesTheOwnersData_EvenWhenAnotherUsersJobRunsFirst()
    {
        using var a = await UserAsync();
        using var b = await UserAsync();
        await ItemEndpointsTests.CreateAsync(a, new { type = "note", title = "Only A", body = "a" });
        await ItemEndpointsTests.CreateAsync(b, new { type = "note", title = "Only B", body = "b" });
        var idA = await StartAsync(a);
        var idB = await StartAsync(b);
        await DrainJobsAsync();

        using var zipA = await DownloadAsync(a, idA);
        using var zipB = await DownloadAsync(b, idB);

        ReadAll(zipA).ShouldContain("Only A");
        ReadAll(zipA).ShouldNotContain("Only B");
        ReadAll(zipB).ShouldContain("Only B");
        ReadAll(zipB).ShouldNotContain("Only A");
        (await b.GetAsync($"/api/exports/{idA}/download", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
