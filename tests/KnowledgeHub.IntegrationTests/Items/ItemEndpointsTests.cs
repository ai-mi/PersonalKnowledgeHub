using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KnowledgeHub.Api.Common;
using KnowledgeHub.Api.Modules.Items;
using KnowledgeHub.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeHub.IntegrationTests.Items;

public sealed class ItemEndpointsTests(ApiFactory factory) : IntegrationTest(factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<HttpClient> UserAsync() => await Factory.CreateClientForAsync(Guid.CreateVersion7());

    /// <summary>Создаёт запись и возвращает ответ; проверяет только код успеха (201 или 200).</summary>
    public static async Task<JsonElement> CreateAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/api/items", body, Ct);
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync(Ct));
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    public static async Task<string> CreateNoteIdAsync(HttpClient client, string body = "note") =>
        (await CreateAsync(client, new { type = "note", body })).GetProperty("id").GetString()!;

    private static async Task<JsonElement> ErrorsAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errors");
    }

    [Fact]
    public async Task Note_IsCreatedAndRead()
    {
        using var user = await UserAsync();

        var response = await user.PostAsJsonAsync("/api/items", new { type = "note", title = " Idea ", body = "Hello", userNote = "later" }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        response.Headers.Location!.ToString().ShouldBe($"/api/items/{created.GetProperty("id").GetString()}");

        var item = await user.GetFromJsonAsync<JsonElement>(response.Headers.Location, Ct);
        item.GetProperty("type").GetString().ShouldBe("note");
        item.GetProperty("title").GetString().ShouldBe("Idea");
        item.GetProperty("body").GetString().ShouldBe("Hello");
        item.GetProperty("userNote").GetString().ShouldBe("later");
        item.GetProperty("status").GetString().ShouldBe("ready");
        item.GetProperty("readAt").ValueKind.ShouldBe(JsonValueKind.Null);
        item.GetProperty("url").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Snippet_KeepsIndentationAndLanguage()
    {
        using var user = await UserAsync();
        const string code = "def f():\n    return 1";

        var created = await CreateAsync(user, new { type = "snippet", body = code, codeLanguage = " python " });

        created.GetProperty("type").GetString().ShouldBe("snippet");
        created.GetProperty("body").GetString().ShouldBe(code);
        created.GetProperty("codeLanguage").GetString().ShouldBe("python");
        created.GetProperty("status").GetString().ShouldBe("ready");
    }

    [Fact]
    public async Task Link_IsCreatedWithNormalizedUrl()
    {
        using var user = await UserAsync();

        var created = await CreateAsync(user, new { type = "link", url = "https://Example.com/Article/", title = "Article" });

        created.GetProperty("type").GetString().ShouldBe("link");
        created.GetProperty("url").GetString().ShouldBe("https://example.com/Article");
        created.GetProperty("status").GetString().ShouldBe("ready");
    }

    [Fact]
    public async Task DuplicateLink_ReturnsExistingItemWith200()
    {
        using var user = await UserAsync();
        var first = await user.PostAsJsonAsync("/api/items", new { type = "link", url = "https://example.com/a" }, Ct);
        first.StatusCode.ShouldBe(HttpStatusCode.Created);

        var second = await user.PostAsJsonAsync(
            "/api/items", new { type = "link", url = "https://EXAMPLE.com/a/?utm_source=news#intro", userNote = "ignored" }, Ct);

        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        var a = await first.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var b = await second.Content.ReadFromJsonAsync<JsonElement>(Ct);
        b.GetProperty("id").GetString().ShouldBe(a.GetProperty("id").GetString());
        b.GetProperty("userNote").ValueKind.ShouldBe(JsonValueKind.Null);
        (await user.GetFromJsonAsync<JsonElement>("/api/items", Ct)).GetProperty("total").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task SameLink_CanBeSavedByDifferentUsers()
    {
        using var a = await UserAsync();
        using var b = await UserAsync();

        (await a.PostAsJsonAsync("/api/items", new { type = "link", url = "https://example.com/shared" }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        (await b.PostAsJsonAsync("/api/items", new { type = "link", url = "https://example.com/shared" }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task ConcurrentDuplicateLinks_CreateOneItem()
    {
        using var user = await UserAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
            user.PostAsJsonAsync("/api/items", new { type = "link", url = "https://example.com/race" }, Ct)));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBe(4);
        (await user.GetFromJsonAsync<JsonElement>("/api/items", Ct)).GetProperty("total").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task Update_ReplacesEditableFields()
    {
        using var user = await UserAsync();
        var id = (await CreateAsync(user, new { type = "snippet", title = "old", body = "a", codeLanguage = "js", userNote = "n" }))
            .GetProperty("id").GetString();

        var response = await user.PutAsJsonAsync($"/api/items/{id}", new { title = "new", body = "b", codeLanguage = "ts" }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var item = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        item.GetProperty("title").GetString().ShouldBe("new");
        item.GetProperty("body").GetString().ShouldBe("b");
        item.GetProperty("codeLanguage").GetString().ShouldBe("ts");
        item.GetProperty("userNote").ValueKind.ShouldBe(JsonValueKind.Null);
        item.GetProperty("updatedAt").GetDateTimeOffset().ShouldBeGreaterThan(item.GetProperty("createdAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task Update_DoesNotChangeLinkUrlOrBody()
    {
        using var user = await UserAsync();
        var id = (await CreateAsync(user, new { type = "link", url = "https://example.com/l" })).GetProperty("id").GetString();

        var response = await user.PutAsJsonAsync(
            $"/api/items/{id}", new { title = "T", body = "ignored", userNote = "mine" }, Ct);

        var item = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        item.GetProperty("url").GetString().ShouldBe("https://example.com/l");
        item.GetProperty("title").GetString().ShouldBe("T");
        item.GetProperty("userNote").GetString().ShouldBe("mine");
        item.GetProperty("body").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task MarkRead_SetsAndClearsReadAt()
    {
        using var user = await UserAsync();
        var id = await CreateNoteIdAsync(user);

        var read = await user.PatchAsJsonAsync($"/api/items/{id}", new { read = true }, Ct);
        read.StatusCode.ShouldBe(HttpStatusCode.OK);
        var readAt = (await read.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("readAt").GetDateTimeOffset();

        var again = await user.PatchAsJsonAsync($"/api/items/{id}", new { read = true }, Ct);
        (await again.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("readAt").GetDateTimeOffset().ShouldBe(readAt, TimeSpan.FromMilliseconds(1));

        var unread = await user.PatchAsJsonAsync($"/api/items/{id}", new { read = false }, Ct);
        (await unread.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("readAt").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task MarkRead_RequiresReadFlag()
    {
        using var user = await UserAsync();
        var id = await CreateNoteIdAsync(user);

        var errors = await ErrorsAsync(await user.PatchAsJsonAsync($"/api/items/{id}", new { }, Ct));

        errors.TryGetProperty("read", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Delete_RemovesItem()
    {
        using var user = await UserAsync();
        var id = await CreateNoteIdAsync(user);

        (await user.DeleteAsync($"/api/items/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await user.GetAsync($"/api/items/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await user.DeleteAsync($"/api/items/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeletedLink_CanBeSavedAgain()
    {
        using var user = await UserAsync();
        var id = (await CreateAsync(user, new { type = "link", url = "https://example.com/again" })).GetProperty("id").GetString();
        await user.DeleteAsync($"/api/items/{id}", Ct);

        (await user.PostAsJsonAsync("/api/items", new { type = "link", url = "https://example.com/again" }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task UnknownItem_Returns404()
    {
        using var user = await UserAsync();
        var id = Guid.CreateVersion7();

        (await user.GetAsync($"/api/items/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await user.PutAsJsonAsync($"/api/items/{id}", new { title = "x" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await user.PatchAsJsonAsync($"/api/items/{id}", new { read = true }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task List_PagesNewestFirst()
    {
        using var user = await UserAsync();
        foreach (var n in new[] { "first", "second", "third" })
        {
            await CreateAsync(user, new { type = "note", title = n });
        }

        var page1 = await user.GetFromJsonAsync<JsonElement>("/api/items?pageSize=2", Ct);
        var page2 = await user.GetFromJsonAsync<JsonElement>("/api/items?pageSize=2&page=2", Ct);

        page1.GetProperty("total").GetInt32().ShouldBe(3);
        page1.GetProperty("page").GetInt32().ShouldBe(1);
        page1.GetProperty("pageSize").GetInt32().ShouldBe(2);
        page1.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("title").GetString())
            .ShouldBe(["third", "second"]);
        page2.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("title").GetString()).ShouldBe(["first"]);
    }

    [Fact]
    public async Task List_DefaultsToPageSize20_AndOmitsBodies()
    {
        using var user = await UserAsync();
        for (var i = 0; i < 21; i++)
        {
            await CreateAsync(user, new { type = "note", body = "text" });
        }

        var page = await user.GetFromJsonAsync<JsonElement>("/api/items", Ct);

        page.GetProperty("pageSize").GetInt32().ShouldBe(20);
        page.GetProperty("items").GetArrayLength().ShouldBe(20);
        page.GetProperty("total").GetInt32().ShouldBe(21);
        page.GetProperty("items")[0].TryGetProperty("body", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task List_FiltersByTypeAndUnread()
    {
        using var user = await UserAsync();
        var readNote = await CreateNoteIdAsync(user);
        await CreateNoteIdAsync(user);
        await CreateAsync(user, new { type = "snippet", body = "x" });
        await user.PatchAsJsonAsync($"/api/items/{readNote}", new { read = true }, Ct);

        async Task<int> TotalAsync(string query) =>
            (await user.GetFromJsonAsync<JsonElement>($"/api/items{query}", Ct)).GetProperty("total").GetInt32();

        (await TotalAsync("")).ShouldBe(3);
        (await TotalAsync("?type=note")).ShouldBe(2);
        (await TotalAsync("?type=snippet")).ShouldBe(1);
        (await TotalAsync("?type=link")).ShouldBe(0);
        (await TotalAsync("?unread=true")).ShouldBe(2);
        (await TotalAsync("?type=note&unread=true")).ShouldBe(1);
        (await TotalAsync("?unread=false")).ShouldBe(3);
    }

    [Theory]
    [InlineData("page=2")]
    [InlineData("page=2147483647")]
    [InlineData("page=2147483647&pageSize=100")]
    public async Task List_PageBeyondTheEnd_ReturnsEmptyPage(string query)
    {
        using var user = await UserAsync();
        await CreateNoteIdAsync(user);

        var page = await user.GetFromJsonAsync<JsonElement>($"/api/items?{query}", Ct);

        page.GetProperty("items").GetArrayLength().ShouldBe(0);
        page.GetProperty("total").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task List_OnEmptyAccount_ReturnsEmptyFirstPage()
    {
        using var user = await UserAsync();

        var page = await user.GetFromJsonAsync<JsonElement>("/api/items", Ct);

        page.GetProperty("items").GetArrayLength().ShouldBe(0);
        page.GetProperty("total").GetInt32().ShouldBe(0);
    }

    [Theory]
    [InlineData("type=NOTE", HttpStatusCode.OK)]
    [InlineData("type=Note", HttpStatusCode.OK)]
    [InlineData("type=1", HttpStatusCode.BadRequest)]
    [InlineData("type=", HttpStatusCode.BadRequest)]
    [InlineData("unread=maybe", HttpStatusCode.BadRequest)]
    [InlineData("page=abc", HttpStatusCode.BadRequest)]
    public async Task List_ParsesQueryStrictly(string query, HttpStatusCode expected)
    {
        using var user = await UserAsync();

        (await user.GetAsync($"/api/items?{query}", Ct)).StatusCode.ShouldBe(expected);
    }

    [Fact]
    public async Task Create_StoresBlankOptionalFieldsAsNull()
    {
        using var user = await UserAsync();

        var item = await CreateAsync(user, new { type = "snippet", title = "   ", body = "  \n ", userNote = " ", codeLanguage = " " });

        foreach (var field in new[] { "title", "body", "userNote", "codeLanguage" })
        {
            item.GetProperty(field).ValueKind.ShouldBe(JsonValueKind.Null, field);
        }
    }

    [Fact]
    public async Task Create_IgnoresFieldsThatDoNotApplyToTheType()
    {
        using var user = await UserAsync();

        var note = await CreateAsync(user, new { type = "note", body = "b", url = "https://example.com/x", codeLanguage = "js" });
        var link = await CreateAsync(user, new { type = "link", url = "https://example.com/y", body = "b", codeLanguage = "js" });

        note.GetProperty("url").ValueKind.ShouldBe(JsonValueKind.Null);
        note.GetProperty("codeLanguage").ValueKind.ShouldBe(JsonValueKind.Null);
        link.GetProperty("body").ValueKind.ShouldBe(JsonValueKind.Null);
        link.GetProperty("codeLanguage").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Notes_WithoutUrl_AreNeverTreatedAsDuplicates()
    {
        using var user = await UserAsync();

        await CreateNoteIdAsync(user, "same");
        (await user.PostAsJsonAsync("/api/items", new { type = "note", body = "same" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Update_WithEmptyBody_ClearsOptionalFields()
    {
        using var user = await UserAsync();
        var id = (await CreateAsync(user, new { type = "snippet", title = "t", body = "b", userNote = "n", codeLanguage = "js" }))
            .GetProperty("id").GetString();

        var response = await user.PutAsJsonAsync($"/api/items/{id}", new { }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var item = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        foreach (var field in new[] { "title", "body", "userNote", "codeLanguage" })
        {
            item.GetProperty(field).ValueKind.ShouldBe(JsonValueKind.Null, field);
        }
    }

    [Fact]
    public async Task Update_DoesNotTouchOtherFieldsOfOtherItems()
    {
        using var user = await UserAsync();
        var first = await CreateNoteIdAsync(user, "first");
        var second = await CreateNoteIdAsync(user, "second");

        await user.PutAsJsonAsync($"/api/items/{first}", new { body = "changed" }, Ct);

        (await user.GetFromJsonAsync<JsonElement>($"/api/items/{second}", Ct)).GetProperty("body").GetString().ShouldBe("second");
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("")]
    public async Task Create_RejectsMalformedBody(string json)
    {
        using var user = await UserAsync();

        var response = await user.PostAsync("/api/items", new StringContent(json, System.Text.Encoding.UTF8, "application/json"), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("{\"read\":\"yes\"}")]
    [InlineData("{\"read\":null}")]
    public async Task MarkRead_RejectsInvalidFlag(string json)
    {
        using var user = await UserAsync();
        var id = await CreateNoteIdAsync(user);

        var response = await user.PatchAsync($"/api/items/{id}", new StringContent(json, System.Text.Encoding.UTF8, "application/json"), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task NonGuidId_Returns404()
    {
        using var user = await UserAsync();

        (await user.GetAsync("/api/items/not-a-guid", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Title_IsLimitedAfterTrimming()
    {
        using var user = await UserAsync();

        var padded = $"  {new string('x', Item.MaxTitleLength)}  ";

        var created = await CreateAsync(user, new { type = "note", title = padded });

        created.GetProperty("title").GetString()!.Length.ShouldBe(Item.MaxTitleLength);
    }

    [Fact]
    public async Task Url_WithDifferentQueryParameters_IsADifferentLink()
    {
        using var user = await UserAsync();

        (await user.PostAsJsonAsync("/api/items", new { type = "link", url = "https://example.com/a?id=1" }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        (await user.PostAsJsonAsync("/api/items", new { type = "link", url = "https://example.com/a?id=2" }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Theory]
    [InlineData("page=0", "page")]
    [InlineData("pageSize=0", "pageSize")]
    [InlineData("pageSize=101", "pageSize")]
    public async Task List_RejectsInvalidPaging(string query, string field)
    {
        using var user = await UserAsync();

        var errors = await ErrorsAsync(await user.GetAsync($"/api/items?{query}", Ct));

        errors.TryGetProperty(field, out _).ShouldBeTrue();
    }

    [Fact]
    public async Task List_RejectsUnknownType()
    {
        using var user = await UserAsync();

        var errors = await ErrorsAsync(await user.GetAsync("/api/items?type=bogus", Ct));

        errors.TryGetProperty("type", out _).ShouldBeTrue();
    }

    public static IEnumerable<TheoryDataRow<object, string>> InvalidCreateRequests() =>
    [
        new(new { type = "note", title = new string('x', Item.MaxTitleLength + 1) }, "title"),
        new(new { type = "note", userNote = new string('x', Item.MaxUserNoteLength + 1) }, "userNote"),
        new(new { type = "note", body = new string('x', Item.MaxBodyLength + 1) }, "body"),
        new(new { type = "snippet", body = new string('x', Item.MaxBodyLength + 1) }, "body"),
        new(new { type = "snippet", codeLanguage = new string('x', Item.MaxCodeLanguageLength + 1) }, "codeLanguage"),
        new(new { type = "link" }, "url"),
        new(new { type = "link", url = "not a url" }, "url"),
        new(new { type = "link", url = "ftp://example.com/file" }, "url"),
        new(new { type = "link", url = "javascript:alert(1)" }, "url"),
        new(new { title = "no type" }, "type"),
        new(new { type = "document" }, "type"),
    ];

    [Theory]
    [MemberData(nameof(InvalidCreateRequests))]
    public async Task Create_RejectsInvalidInput(object request, string field)
    {
        using var user = await UserAsync();

        var errors = await ErrorsAsync(await user.PostAsJsonAsync("/api/items", request, Ct));

        errors.TryGetProperty(field, out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Create_AcceptsMaximumLengths()
    {
        using var user = await UserAsync();

        var response = await user.PostAsJsonAsync("/api/items", new
        {
            type = "snippet",
            title = new string('t', Item.MaxTitleLength),
            body = new string('b', Item.MaxBodyLength),
            userNote = new string('n', Item.MaxUserNoteLength),
            codeLanguage = new string('c', Item.MaxCodeLanguageLength),
        }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Create_RejectsUnknownTypeValue()
    {
        using var user = await UserAsync();

        (await user.PostAsJsonAsync("/api/items", new { type = "bogus" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Update_RejectsTooLongFields()
    {
        using var user = await UserAsync();
        var id = await CreateNoteIdAsync(user);

        var errors = await ErrorsAsync(await user.PutAsJsonAsync(
            $"/api/items/{id}", new { title = new string('x', Item.MaxTitleLength + 1) }, Ct));

        errors.TryGetProperty("title", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Anonymous_Gets401()
    {
        (await Client.GetAsync("/api/items", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Client.PostAsJsonAsync("/api/items", new { type = "note" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UserB_CannotSeeOrChangeItemsOfUserA()
    {
        using var a = await UserAsync();
        using var b = await UserAsync();
        var id = await CreateNoteIdAsync(a, "secret");

        (await b.GetFromJsonAsync<JsonElement>("/api/items", Ct)).GetProperty("total").GetInt32().ShouldBe(0);
        (await b.DeleteAsync($"/api/items/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await b.PutAsJsonAsync($"/api/items/{id}", new { body = "hacked" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var item = await a.GetFromJsonAsync<JsonElement>($"/api/items/{id}", Ct);
        item.GetProperty("body").GetString().ShouldBe("secret");
    }

    [Fact]
    public async Task SetProcessingResult_UpdatesStatusBodyAndTitle()
    {
        using var user = await UserAsync();
        var id = (await CreateAsync(user, new { type = "link", url = "https://example.com/p", title = "mine" }))
            .GetProperty("id").GetGuid();

        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            // Ingestion работает в системном контексте — без пользователя запроса.
            using var _ = scope.ServiceProvider.GetRequiredService<SystemContext>().Enter();
            var items = scope.ServiceProvider.GetRequiredService<IItemService>();

            (await items.SetProcessingResultAsync(id, ItemStatus.Failed, null, null, "timeout", Ct)).ShouldBeTrue();
            (await items.SetProcessingResultAsync(Guid.CreateVersion7(), ItemStatus.Ready, null, null, null, Ct)).ShouldBeFalse();
        }

        var failed = await user.GetFromJsonAsync<JsonElement>($"/api/items/{id}", Ct);
        failed.GetProperty("status").GetString().ShouldBe("failed");
        failed.GetProperty("error").GetString().ShouldBe("timeout");
        failed.GetProperty("title").GetString().ShouldBe("mine");

        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            using var _ = scope.ServiceProvider.GetRequiredService<SystemContext>().Enter();
            await scope.ServiceProvider.GetRequiredService<IItemService>()
                .SetProcessingResultAsync(id, ItemStatus.Ready, "Page title", "Extracted text", null, Ct);
        }

        var ready = await user.GetFromJsonAsync<JsonElement>($"/api/items/{id}", Ct);
        ready.GetProperty("status").GetString().ShouldBe("ready");
        ready.GetProperty("title").GetString().ShouldBe("Page title");
        ready.GetProperty("body").GetString().ShouldBe("Extracted text");
        ready.GetProperty("error").ValueKind.ShouldBe(JsonValueKind.Null);
    }
}
