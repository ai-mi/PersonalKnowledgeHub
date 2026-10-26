using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KnowledgeHub.IntegrationTests.Infrastructure;

namespace KnowledgeHub.IntegrationTests.Items;

public sealed class CollectionsTests(ApiFactory factory) : IntegrationTest(factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<HttpClient> UserAsync() => await Factory.CreateClientForAsync(Guid.CreateVersion7());

    public static async Task<string> CreateCollectionIdAsync(HttpClient client, string name = "Reading list")
    {
        var response = await client.PostAsJsonAsync("/api/collections", new { name }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetString()!;
    }

    private static async Task<JsonElement> GetItemAsync(HttpClient client, string id) =>
        await client.GetFromJsonAsync<JsonElement>($"/api/items/{id}", Ct);

    private static string[] CollectionNames(JsonElement item) =>
        item.GetProperty("collections").EnumerateArray().Select(c => c.GetProperty("name").GetString()!).ToArray();

    private static async Task<JsonElement> ListAsync(HttpClient client) =>
        await client.GetFromJsonAsync<JsonElement>("/api/collections", Ct);

    [Fact]
    public async Task Collection_IsCreatedUpdatedAndListed()
    {
        using var user = await UserAsync();

        var created = await user.PostAsJsonAsync("/api/collections", new { name = " Azure ", description = " things " }, Ct);

        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var id = body.GetProperty("id").GetString();
        created.Headers.Location!.ToString().ShouldBe($"/api/collections/{id}");
        body.GetProperty("name").GetString().ShouldBe("Azure");
        body.GetProperty("description").GetString().ShouldBe("things");
        body.GetProperty("itemCount").GetInt32().ShouldBe(0);

        var updated = await user.PutAsJsonAsync($"/api/collections/{id}", new { name = "Cloud" }, Ct);

        updated.StatusCode.ShouldBe(HttpStatusCode.OK);
        var list = (await ListAsync(user)).EnumerateArray().ToList();
        list.Count.ShouldBe(1);
        list[0].GetProperty("name").GetString().ShouldBe("Cloud");
        list[0].GetProperty("description").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Collection_RejectsInvalidFields()
    {
        using var user = await UserAsync();

        foreach (var request in new object[]
                 {
                     new { name = "" },
                     new { name = "  " },
                     new { name = new string('x', 201) },
                     new { name = "ok", description = new string('x', 2001) },
                     new { },
                 })
        {
            (await user.PostAsJsonAsync("/api/collections", request, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        (await ListAsync(user)).GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Item_CanBeInTwoCollections()
    {
        using var user = await UserAsync();
        var first = await CreateCollectionIdAsync(user, "B list");
        var second = await CreateCollectionIdAsync(user, "A list");

        var item = await ItemEndpointsTests.CreateAsync(user, new { type = "note", collectionIds = new[] { first, second } });

        CollectionNames(item).ShouldBe(["A list", "B list"]);
        foreach (var collection in (await ListAsync(user)).EnumerateArray())
        {
            collection.GetProperty("itemCount").GetInt32().ShouldBe(1);
        }
    }

    [Fact]
    public async Task Update_ReplacesCollections_AndNullLeavesThem()
    {
        using var user = await UserAsync();
        var a = await CreateCollectionIdAsync(user, "A");
        var b = await CreateCollectionIdAsync(user, "B");
        var id = (await ItemEndpointsTests.CreateAsync(user, new { type = "note", collectionIds = new[] { a } })).GetProperty("id").GetString();

        var untouched = await (await user.PutAsJsonAsync($"/api/items/{id}", new { }, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);
        var replaced = await (await user.PutAsJsonAsync($"/api/items/{id}", new { collectionIds = new[] { b } }, Ct))
            .Content.ReadFromJsonAsync<JsonElement>(Ct);
        var cleared = await (await user.PutAsJsonAsync($"/api/items/{id}", new { collectionIds = Array.Empty<string>() }, Ct))
            .Content.ReadFromJsonAsync<JsonElement>(Ct);

        CollectionNames(untouched).ShouldBe(["A"]);
        CollectionNames(replaced).ShouldBe(["B"]);
        CollectionNames(cleared).ShouldBe([]);
    }

    [Fact]
    public async Task AddAndRemoveItem_ThroughCollectionEndpoints()
    {
        using var user = await UserAsync();
        var collection = await CreateCollectionIdAsync(user);
        var item = await ItemEndpointsTests.CreateNoteIdAsync(user);

        (await user.PostAsync($"/api/collections/{collection}/items/{item}", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await user.PostAsync($"/api/collections/{collection}/items/{item}", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        CollectionNames(await GetItemAsync(user, item)).ShouldBe(["Reading list"]);
        (await ListAsync(user))[0].GetProperty("itemCount").GetInt32().ShouldBe(1);

        (await user.DeleteAsync($"/api/collections/{collection}/items/{item}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await user.DeleteAsync($"/api/collections/{collection}/items/{item}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        CollectionNames(await GetItemAsync(user, item)).ShouldBe([]);
    }

    [Fact]
    public async Task DeletingCollection_KeepsItsItems()
    {
        using var user = await UserAsync();
        var collection = await CreateCollectionIdAsync(user);
        var item = (await ItemEndpointsTests.CreateAsync(user, new { type = "note", collectionIds = new[] { collection } }))
            .GetProperty("id").GetString()!;

        (await user.DeleteAsync($"/api/collections/{collection}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        CollectionNames(await GetItemAsync(user, item)).ShouldBe([]);
        (await user.DeleteAsync($"/api/collections/{collection}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeletingItem_RemovesItFromCollection()
    {
        using var user = await UserAsync();
        var collection = await CreateCollectionIdAsync(user);
        var item = (await ItemEndpointsTests.CreateAsync(user, new { type = "note", collectionIds = new[] { collection } }))
            .GetProperty("id").GetString();

        await user.DeleteAsync($"/api/items/{item}", Ct);

        (await ListAsync(user))[0].GetProperty("itemCount").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task CannotAddSomeoneElsesItem_ToOwnCollection()
    {
        using var a = await UserAsync();
        using var b = await UserAsync();
        var itemOfA = await ItemEndpointsTests.CreateNoteIdAsync(a);
        var collectionOfB = await CreateCollectionIdAsync(b);

        (await b.PostAsync($"/api/collections/{collectionOfB}/items/{itemOfA}", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await ListAsync(b))[0].GetProperty("itemCount").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task CannotAddOwnItem_ToSomeoneElsesCollection()
    {
        using var a = await UserAsync();
        using var b = await UserAsync();
        var collectionOfA = await CreateCollectionIdAsync(a);
        var itemOfB = await ItemEndpointsTests.CreateNoteIdAsync(b);

        (await b.PostAsync($"/api/collections/{collectionOfA}/items/{itemOfB}", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await b.DeleteAsync($"/api/collections/{collectionOfA}/items/{itemOfB}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ItemRequests_WithUnknownOrForeignCollection_AreRejected()
    {
        using var a = await UserAsync();
        using var b = await UserAsync();
        var collectionOfA = await CreateCollectionIdAsync(a);
        var ownItem = await ItemEndpointsTests.CreateNoteIdAsync(b);

        foreach (var id in new[] { collectionOfA, Guid.CreateVersion7().ToString() })
        {
            (await b.PostAsJsonAsync("/api/items", new { type = "note", collectionIds = new[] { id } }, Ct))
                .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await b.PutAsJsonAsync($"/api/items/{ownItem}", new { collectionIds = new[] { id } }, Ct))
                .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        (await ListAsync(a))[0].GetProperty("itemCount").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task List_FiltersByCollection_AndCombinesWithTags()
    {
        using var user = await UserAsync();
        var collection = await CreateCollectionIdAsync(user);
        await ItemEndpointsTests.CreateAsync(user, new { type = "note", title = "in-tagged", tags = new[] { "x" }, collectionIds = new[] { collection } });
        await ItemEndpointsTests.CreateAsync(user, new { type = "note", title = "in-plain", collectionIds = new[] { collection } });
        await ItemEndpointsTests.CreateAsync(user, new { type = "note", title = "outside", tags = new[] { "x" } });

        async Task<string[]> TitlesAsync(string query) =>
            (await user.GetFromJsonAsync<JsonElement>($"/api/items{query}", Ct)).GetProperty("items").EnumerateArray()
                .Select(i => i.GetProperty("title").GetString()!).Order().ToArray();

        (await TitlesAsync($"?collection={collection}")).ShouldBe(["in-plain", "in-tagged"]);
        (await TitlesAsync($"?collection={collection}&tags=x")).ShouldBe(["in-tagged"]);
        (await TitlesAsync($"?collection={Guid.CreateVersion7()}")).ShouldBe([]);
        (await user.GetAsync("/api/items?collection=abc", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UnknownCollection_Returns404()
    {
        using var user = await UserAsync();
        var id = Guid.CreateVersion7();

        (await user.PutAsJsonAsync($"/api/collections/{id}", new { name = "x" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await user.DeleteAsync($"/api/collections/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
