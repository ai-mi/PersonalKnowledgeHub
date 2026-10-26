using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KnowledgeHub.IntegrationTests.Infrastructure;

namespace KnowledgeHub.IntegrationTests.Items;

public sealed class TagsTests(ApiFactory factory) : IntegrationTest(factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<HttpClient> UserAsync() => await Factory.CreateClientForAsync(Guid.CreateVersion7());

    private static Task<JsonElement> CreateNoteAsync(HttpClient client, params string[] tags) =>
        ItemEndpointsTests.CreateAsync(client, new { type = "note", body = "n", tags });

    private static async Task<JsonElement> ListTagsAsync(HttpClient client) =>
        await client.GetFromJsonAsync<JsonElement>("/api/tags", Ct);

    public static async Task<string> TagIdAsync(HttpClient client, string name) =>
        (await ListTagsAsync(client)).EnumerateArray().Single(t => t.GetProperty("name").GetString() == name).GetProperty("id").GetString()!;

    private static string[] Strings(JsonElement array) => array.EnumerateArray().Select(e => e.GetString()!).ToArray();

    [Fact]
    public async Task Item_IsCreatedWithNormalizedTags()
    {
        using var user = await UserAsync();

        var item = await CreateNoteAsync(user, "#azure", "service bus", "", "  ");

        Strings(item.GetProperty("tags")).ShouldBe(["azure", "service-bus"]);
        var fetched = await user.GetFromJsonAsync<JsonElement>($"/api/items/{item.GetProperty("id").GetString()}", Ct);
        Strings(fetched.GetProperty("tags")).ShouldBe(["azure", "service-bus"]);
    }

    [Fact]
    public async Task TagsDifferingOnlyInCase_AreOneTag()
    {
        using var user = await UserAsync();

        var first = await CreateNoteAsync(user, "Azure", "azure", "AZURE");
        var second = await CreateNoteAsync(user, "azure");

        Strings(first.GetProperty("tags")).ShouldBe(["Azure"]);
        Strings(second.GetProperty("tags")).ShouldBe(["Azure"]);
        var tags = (await ListTagsAsync(user)).EnumerateArray().ToList();
        tags.Count.ShouldBe(1);
        tags[0].GetProperty("itemCount").GetInt32().ShouldBe(2);
    }

    [Fact]
    public async Task SameTagName_CanBeUsedByDifferentUsers()
    {
        using var a = await UserAsync();
        using var b = await UserAsync();

        await CreateNoteAsync(a, "shared");
        await CreateNoteAsync(b, "shared");

        (await ListTagsAsync(a)).GetArrayLength().ShouldBe(1);
        (await ListTagsAsync(b)).GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task ConcurrentCreates_WithTheSameNewTag_ShareOneTag()
    {
        using var user = await UserAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(i =>
            user.PostAsJsonAsync("/api/items", new { type = "note", body = $"n{i}", tags = new[] { "race" } }, Ct)));

        responses.ShouldAllBe(r => r.StatusCode == HttpStatusCode.Created);
        var tags = (await ListTagsAsync(user)).EnumerateArray().ToList();
        tags.Count.ShouldBe(1);
        tags[0].GetProperty("itemCount").GetInt32().ShouldBe(8);
    }

    [Fact]
    public async Task ConcurrentUpdates_WithTheSameNewTag_ShareOneTag()
    {
        using var user = await UserAsync();
        var ids = new List<string>();
        for (var i = 0; i < 8; i++)
        {
            ids.Add(await ItemEndpointsTests.CreateNoteIdAsync(user, $"n{i}"));
        }

        var responses = await Task.WhenAll(ids.Select(id =>
            user.PutAsJsonAsync($"/api/items/{id}", new { body = "n", tags = new[] { "race" } }, Ct)));

        responses.ShouldAllBe(r => r.StatusCode == HttpStatusCode.OK);
        (await ListTagsAsync(user)).GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task TooLongTag_IsRejected()
    {
        using var user = await UserAsync();

        var response = await user.PostAsJsonAsync("/api/items", new { type = "note", tags = new[] { new string('x', 51) } }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errors").TryGetProperty("tags", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Update_ReplacesTags_AndNullLeavesThemAsTheyAre()
    {
        using var user = await UserAsync();
        var id = (await CreateNoteAsync(user, "a", "b")).GetProperty("id").GetString();

        var replaced = await (await user.PutAsJsonAsync($"/api/items/{id}", new { body = "n", tags = new[] { "b", "C" } }, Ct))
            .Content.ReadFromJsonAsync<JsonElement>(Ct);
        var untouched = await (await user.PutAsJsonAsync($"/api/items/{id}", new { body = "n" }, Ct))
            .Content.ReadFromJsonAsync<JsonElement>(Ct);
        var cleared = await (await user.PutAsJsonAsync($"/api/items/{id}", new { body = "n", tags = Array.Empty<string>() }, Ct))
            .Content.ReadFromJsonAsync<JsonElement>(Ct);

        Strings(replaced.GetProperty("tags")).ShouldBe(["b", "C"]);
        Strings(untouched.GetProperty("tags")).ShouldBe(["b", "C"]);
        Strings(cleared.GetProperty("tags")).ShouldBe([]);
        (await ListTagsAsync(user)).GetArrayLength().ShouldBe(3);
    }

    [Fact]
    public async Task List_ShowsItemCountsSortedByName()
    {
        using var user = await UserAsync();
        await CreateNoteAsync(user, "beta", "alpha");
        await CreateNoteAsync(user, "beta");

        var tags = (await ListTagsAsync(user)).EnumerateArray().ToList();

        tags.Select(t => t.GetProperty("name").GetString()).ShouldBe(["alpha", "beta"]);
        tags.Select(t => t.GetProperty("itemCount").GetInt32()).ShouldBe([1, 2]);
    }

    [Fact]
    public async Task Rename_ChangesTheTagOnAllItems()
    {
        using var user = await UserAsync();
        var item = await CreateNoteAsync(user, "azur");
        var id = await TagIdAsync(user, "azur");

        var response = await user.PutAsJsonAsync($"/api/tags/{id}", new { name = "#Azure Cloud" }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var renamed = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        renamed.GetProperty("name").GetString().ShouldBe("Azure-Cloud");
        renamed.GetProperty("itemCount").GetInt32().ShouldBe(1);
        var fetched = await user.GetFromJsonAsync<JsonElement>($"/api/items/{item.GetProperty("id").GetString()}", Ct);
        Strings(fetched.GetProperty("tags")).ShouldBe(["Azure-Cloud"]);
    }

    [Fact]
    public async Task Rename_CanChangeOnlyTheCase()
    {
        using var user = await UserAsync();
        await CreateNoteAsync(user, "azure");
        var id = await TagIdAsync(user, "azure");

        (await user.PutAsJsonAsync($"/api/tags/{id}", new { name = "Azure" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Rename_ToExistingTagName_Returns409()
    {
        using var user = await UserAsync();
        await CreateNoteAsync(user, "one", "two");
        var id = await TagIdAsync(user, "one");

        (await user.PutAsJsonAsync($"/api/tags/{id}", new { name = "TWO" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Theory]
    [InlineData("")]
    [InlineData("#")]
    [InlineData("   ")]
    public async Task Rename_RejectsEmptyName(string name)
    {
        using var user = await UserAsync();
        await CreateNoteAsync(user, "one");
        var id = await TagIdAsync(user, "one");

        (await user.PutAsJsonAsync($"/api/tags/{id}", new { name }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Delete_RemovesTagFromItemsButKeepsThem()
    {
        using var user = await UserAsync();
        var item = await CreateNoteAsync(user, "gone", "stays");
        var id = await TagIdAsync(user, "gone");

        (await user.DeleteAsync($"/api/tags/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var fetched = await user.GetFromJsonAsync<JsonElement>($"/api/items/{item.GetProperty("id").GetString()}", Ct);
        Strings(fetched.GetProperty("tags")).ShouldBe(["stays"]);
        (await user.DeleteAsync($"/api/tags/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeletingItem_KeepsItsTags()
    {
        using var user = await UserAsync();
        var item = await CreateNoteAsync(user, "kept");

        await user.DeleteAsync($"/api/items/{item.GetProperty("id").GetString()}", Ct);

        var tags = (await ListTagsAsync(user)).EnumerateArray().ToList();
        tags.Count.ShouldBe(1);
        tags[0].GetProperty("itemCount").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task List_FiltersByAllRequestedTags()
    {
        using var user = await UserAsync();
        await ItemEndpointsTests.CreateAsync(user, new { type = "note", title = "both", tags = new[] { "azure", "Cloud" } });
        await ItemEndpointsTests.CreateAsync(user, new { type = "note", title = "azure-only", tags = new[] { "azure" } });
        await ItemEndpointsTests.CreateAsync(user, new { type = "note", title = "none" });

        async Task<string[]> TitlesAsync(string query) =>
            (await user.GetFromJsonAsync<JsonElement>($"/api/items{query}", Ct)).GetProperty("items").EnumerateArray()
                .Select(i => i.GetProperty("title").GetString()!).Order().ToArray();

        (await TitlesAsync("?tags=azure")).ShouldBe(["azure-only", "both"]);
        (await TitlesAsync("?tags=AZURE,cloud")).ShouldBe(["both"]);
        (await TitlesAsync("?tags=%23azure,%20cloud")).ShouldBe(["both"]);
        (await TitlesAsync("?tags=azure,missing")).ShouldBe([]);
        (await TitlesAsync("?tags=")).Length.ShouldBe(3);
    }

    [Fact]
    public async Task List_ReturnsSortedTagsOfEachItem()
    {
        using var user = await UserAsync();
        await CreateNoteAsync(user, "beta", "Alpha");

        var page = await user.GetFromJsonAsync<JsonElement>("/api/items", Ct);

        Strings(page.GetProperty("items")[0].GetProperty("tags")).ShouldBe(["Alpha", "beta"]);
    }

    [Fact]
    public async Task DuplicateLink_ReturnsExistingItemWithItsTags()
    {
        using var user = await UserAsync();
        await ItemEndpointsTests.CreateAsync(user, new { type = "link", url = "https://example.com/t", tags = new[] { "kept" } });

        var again = await user.PostAsJsonAsync("/api/items", new { type = "link", url = "https://example.com/t", tags = new[] { "other" } }, Ct);

        again.StatusCode.ShouldBe(HttpStatusCode.OK);
        Strings((await again.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("tags")).ShouldBe(["kept"]);
    }

    [Fact]
    public async Task UnknownTag_Returns404()
    {
        using var user = await UserAsync();
        var id = Guid.CreateVersion7();

        (await user.PutAsJsonAsync($"/api/tags/{id}", new { name = "x" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await user.DeleteAsync($"/api/tags/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
