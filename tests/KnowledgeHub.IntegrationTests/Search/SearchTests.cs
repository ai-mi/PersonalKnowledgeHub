using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KnowledgeHub.IntegrationTests.Infrastructure;
using KnowledgeHub.IntegrationTests.Items;

namespace KnowledgeHub.IntegrationTests.Search;

public sealed class SearchTests(ApiFactory factory) : IntegrationTest(factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<HttpClient> UserAsync() => await Factory.CreateClientForAsync(Guid.CreateVersion7());

    private static Task<JsonElement> NoteAsync(HttpClient client, string? title = null, string? body = null, string? userNote = null, params string[] tags) =>
        ItemEndpointsTests.CreateAsync(client, new { type = "note", title, body, userNote, tags });

    /// <summary>Выполняет поиск и возвращает ответ целиком.</summary>
    public static async Task<JsonElement> SearchAsync(HttpClient client, string q, string extraQuery = "")
    {
        var response = await client.GetAsync($"/api/search?q={Uri.EscapeDataString(q)}{extraQuery}", Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private static async Task<string[]> TitlesAsync(HttpClient client, string q, string extraQuery = "") =>
        Hits(await SearchAsync(client, q, extraQuery)).Select(h => h.GetProperty("item").GetProperty("title").GetString()!).ToArray();

    private static List<JsonElement> Hits(JsonElement response) => response.GetProperty("items").EnumerateArray().ToList();

    [Fact]
    public async Task Russian_FindsOtherWordForms()
    {
        using var user = await UserAsync();
        await NoteAsync(user, title: "Сообщения", body: "Очередь сообщений в Azure");

        (await TitlesAsync(user, "очереди")).ShouldBe(["Сообщения"]);
    }

    [Fact]
    public async Task English_FindsOtherWordForms()
    {
        using var user = await UserAsync();
        await NoteAsync(user, title: "Broker", body: "A message broker");

        (await TitlesAsync(user, "messages")).ShouldBe(["Broker"]);
    }

    [Fact]
    public async Task MixedQuery_MatchesBothLanguages()
    {
        using var user = await UserAsync();
        await NoteAsync(user, title: "Mixed", body: "Очередь сообщений в Azure Service Bus");
        await NoteAsync(user, title: "Only russian", body: "Очередь сообщений");

        (await TitlesAsync(user, "очереди service bus")).ShouldBe(["Mixed"]);
    }

    [Fact]
    public async Task TitleMatch_RanksAboveBodyMatch()
    {
        using var user = await UserAsync();
        await NoteAsync(user, title: "Other", body: "kubernetes");
        await NoteAsync(user, title: "kubernetes", body: "other");

        var hits = Hits(await SearchAsync(user, "kubernetes"));

        hits.Select(h => h.GetProperty("item").GetProperty("title").GetString()).ShouldBe(["kubernetes", "Other"]);
        hits[0].GetProperty("rank").GetSingle().ShouldBeGreaterThan(hits[1].GetProperty("rank").GetSingle());
    }

    [Fact]
    public async Task Search_CoversTagsAndUserNote()
    {
        using var user = await UserAsync();
        await NoteAsync(user, title: "By tag", body: "x", tags: "terraform");
        await NoteAsync(user, title: "By note", body: "x", userNote: "прочитать про gRPC");

        (await TitlesAsync(user, "terraform")).ShouldBe(["By tag"]);
        (await TitlesAsync(user, "grpc")).ShouldBe(["By note"]);
    }

    [Fact]
    public async Task Search_CoversLinkUrl()
    {
        using var user = await UserAsync();
        await ItemEndpointsTests.CreateAsync(user, new { type = "link", title = "Docs", url = "https://learn.example.com/servicebus" });

        (await TitlesAsync(user, "learn.example.com")).ShouldBe(["Docs"]);
    }

    [Fact]
    public async Task QuotedPhrase_MatchesOnlyAdjacentWords()
    {
        using var user = await UserAsync();
        await NoteAsync(user, title: "Phrase", body: "azure service bus");
        await NoteAsync(user, title: "Reversed", body: "bus of the service");

        (await TitlesAsync(user, "\"service bus\"")).ShouldBe(["Phrase"]);
    }

    [Fact]
    public async Task MinusWord_ExcludesItems()
    {
        using var user = await UserAsync();
        await NoteAsync(user, title: "Azure only", body: "azure");
        await NoteAsync(user, title: "Azure and AWS", body: "azure aws");

        (await TitlesAsync(user, "azure -aws")).ShouldBe(["Azure only"]);
    }

    [Fact]
    public async Task RenamingTag_UpdatesSearchResults()
    {
        using var user = await UserAsync();
        await NoteAsync(user, title: "Tagged", body: "x", tags: "oldname");
        var tagId = await TagsTests.TagIdAsync(user, "oldname");

        (await user.PutAsJsonAsync($"/api/tags/{tagId}", new { name = "newname" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await TitlesAsync(user, "oldname")).ShouldBeEmpty();
        (await TitlesAsync(user, "newname")).ShouldBe(["Tagged"]);
    }

    [Fact]
    public async Task DeletingTag_RemovesItFromSearch()
    {
        using var user = await UserAsync();
        await NoteAsync(user, title: "Tagged", body: "x", tags: "gone");
        var tagId = await TagsTests.TagIdAsync(user, "gone");

        (await user.DeleteAsync($"/api/tags/{tagId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await TitlesAsync(user, "gone")).ShouldBeEmpty();
    }

    [Fact]
    public async Task UpdatingItem_UpdatesSearchResults()
    {
        using var user = await UserAsync();
        var id = (await NoteAsync(user, title: "Note", body: "alpha")).GetProperty("id").GetString();

        (await user.PutAsJsonAsync($"/api/items/{id}", new { title = "Note", body = "bravo" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await TitlesAsync(user, "alpha")).ShouldBeEmpty();
        (await TitlesAsync(user, "bravo")).ShouldBe(["Note"]);
    }

    [Fact]
    public async Task Highlights_MarkMatchesAndEscapeHtml()
    {
        using var user = await UserAsync();
        await NoteAsync(user, title: "Xss", body: "azure 1 < 2 & 3 > 2 and then plenty of other plain words follow here <script>alert(1)</script>");

        var hit = Hits(await SearchAsync(user, "azure")).Single();

        var highlights = hit.GetProperty("highlights").EnumerateArray().Select(h => h.GetString()!).ToList();
        highlights.ShouldNotBeEmpty();
        var joined = string.Join(" ", highlights);
        joined.ShouldContain("<mark>azure</mark>");
        joined.ShouldContain("1 &lt; 2");
        joined.ShouldContain("3 &gt; 2");
        joined.ShouldContain("&amp;");
        joined.ShouldNotContain("<script");
    }

    [Fact]
    public async Task Filters_NarrowResults()
    {
        using var user = await UserAsync();
        var collection = await CollectionsTests.CreateCollectionIdAsync(user);
        var kept = await ItemEndpointsTests.CreateAsync(user,
            new { type = "note", title = "Kept", body = "redis", tags = new[] { "cache" }, collectionIds = new[] { collection } });
        await ItemEndpointsTests.CreateAsync(user, new { type = "snippet", title = "Snippet", body = "redis", codeLanguage = "csharp" });
        await ItemEndpointsTests.CreateAsync(user, new { type = "note", title = "Untagged", body = "redis" });

        (await TitlesAsync(user, "redis", "&tags=cache")).ShouldBe(["Kept"]);
        (await TitlesAsync(user, "redis", $"&collection={collection}")).ShouldBe(["Kept"]);
        (await TitlesAsync(user, "redis", "&type=snippet")).ShouldBe(["Snippet"]);
        (await TitlesAsync(user, "redis")).Length.ShouldBe(3);

        await user.PatchAsJsonAsync($"/api/items/{kept.GetProperty("id").GetString()}", new { read = true }, Ct);
        (await TitlesAsync(user, "redis", "&unread=true")).ShouldNotContain("Kept");
    }

    [Fact]
    public async Task DateFilters_UseCreationTime()
    {
        using var user = await UserAsync();
        await NoteAsync(user, title: "Now", body: "postgres");

        (await TitlesAsync(user, "postgres", "&from=2000-01-01T00:00:00Z&to=2001-01-01T00:00:00Z")).ShouldBeEmpty();
        (await TitlesAsync(user, "postgres", "&from=2000-01-01T00:00:00Z")).ShouldBe(["Now"]);
        (await TitlesAsync(user, "postgres", "&to=2999-01-01T00:00:00Z")).ShouldBe(["Now"]);
    }

    [Fact]
    public async Task Pagination_ReturnsTotalAndPages()
    {
        using var user = await UserAsync();
        for (var i = 0; i < 3; i++)
        {
            await NoteAsync(user, title: $"Doc {i}", body: "pagination");
        }

        var page = await SearchAsync(user, "pagination", "&page=2&pageSize=2");

        page.GetProperty("total").GetInt32().ShouldBe(3);
        Hits(page).Count.ShouldBe(1);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EmptyQuery_IsBadRequest(string q)
    {
        using var user = await UserAsync();

        (await user.GetAsync($"/api/search?q={q}", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await user.GetAsync("/api/search", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Search_RequiresAuthentication()
    {
        (await Client.GetAsync("/api/search?q=x", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ItemsOfOtherUsers_AreNotFound()
    {
        using var a = await UserAsync();
        using var b = await UserAsync();
        await NoteAsync(a, title: "Secret", body: "confidential", tags: "private");

        (await TitlesAsync(a, "confidential")).ShouldBe(["Secret"]);
        (await SearchAsync(b, "confidential")).GetProperty("total").GetInt32().ShouldBe(0);
        (await SearchAsync(b, "private")).GetProperty("total").GetInt32().ShouldBe(0);
    }
}
