using System.Net;
using System.Net.Http.Json;
using KnowledgeHub.Api.Common;
using KnowledgeHub.Api.Modules.Identity;
using KnowledgeHub.Api.Modules.Search;
using KnowledgeHub.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeHub.IntegrationTests.EdgeCases;

/// <summary>Некорректные запросы и служебные пути, которые обычные сценарии не задевают.</summary>
public sealed class ApiEdgeCaseTests(ApiFactory factory) : IntegrationTest(factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly byte[] SomeText = "some text"u8.ToArray();

    [Fact]
    public async Task Upload_WithoutMultipartBody_IsRejected()
    {
        using var user = await Factory.CreateClientForAsync(Guid.CreateVersion7());

        var response = await user.PostAsJsonAsync("/api/items/upload", new { file = "x" }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Upload_WithoutFile_IsRejected()
    {
        using var user = await Factory.CreateClientForAsync(Guid.CreateVersion7());
        using var form = new MultipartFormDataContent { { new StringContent("title only"), "title" } };

        (await user.PostAsync("/api/items/upload", form, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Upload_WithMalformedCollectionId_IsRejected()
    {
        using var user = await Factory.CreateClientForAsync(Guid.CreateVersion7());

        var response = await Ingestion.DocumentUploadTests.UploadAsync(user, SomeText, "a.txt", collectionIds: ["not-a-guid"]);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Upload_IgnoresBlankCollectionIds()
    {
        using var user = await Factory.CreateClientForAsync(Guid.CreateVersion7());

        var response = await Ingestion.DocumentUploadTests.UploadAsync(user, SomeText, "blank.txt", collectionIds: [" "]);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"name\":\"   \"}")]
    public async Task CreateToken_WithoutName_IsRejected(string json)
    {
        using var user = await Factory.CreateClientForAsync(Guid.CreateVersion7());

        var response = await user.PostAsync("/api/me/tokens", new StringContent(json, System.Text.Encoding.UTF8, "application/json"), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SearchService_RejectsBlankQuery()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        using var _ = scope.ServiceProvider.GetRequiredService<SystemContext>().RunAs(Guid.CreateVersion7());

        var error = await Should.ThrowAsync<AppException>(() =>
            scope.ServiceProvider.GetRequiredService<ISearchService>().SearchAsync(new SearchQuery("   ", null, [], null, null, null, false, 1, 10), Ct));

        error.Status.ShouldBe(400);
    }

    [Fact]
    public async Task ApiTokenScheme_WithoutBearerHeader_HasNoResult()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };

        var result = await context.AuthenticateAsync(AuthSchemes.ApiToken);

        result.None.ShouldBeTrue();
    }
}
