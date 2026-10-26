using System.Net;
using KnowledgeHub.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace KnowledgeHub.IntegrationTests;

public sealed class SpaFallbackTests : IntegrationTest, IDisposable
{
    private readonly string _webRoot = Directory.CreateTempSubdirectory("kh-wwwroot-").FullName;
    private readonly WebApplicationFactory<Program> _spaFactory;
    private readonly HttpClient _spa;

    public SpaFallbackTests(ApiFactory factory) : base(factory)
    {
        File.WriteAllText(Path.Combine(_webRoot, "index.html"), "<!doctype html><title>spa</title>");
        Directory.CreateDirectory(Path.Combine(_webRoot, "assets"));
        File.WriteAllText(Path.Combine(_webRoot, "assets", "app.js"), "console.log(1)");
        _spaFactory = factory.WithWebHostBuilder(b => b.UseWebRoot(_webRoot));
        _spa = _spaFactory.CreateClient();
    }

    [Fact]
    public async Task UnknownClientRoute_ReturnsIndexHtml()
    {
        var response = await _spa.GetAsync("/items/123", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("text/html");
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("<title>spa</title>");
    }

    [Fact]
    public async Task StaticAsset_IsServedAsFile_NotAsIndexHtml()
    {
        var response = await _spa.GetAsync("/assets/app.js", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("text/javascript");
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldBe("console.log(1)");
    }

    [Fact]
    public async Task UnknownApiRoute_ReturnsProblemDetails404()
    {
        var response = await _spa.GetAsync("/api/unknown", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/apikeys")]
    [InlineData("/settings/")]
    [InlineData("/items/a/b/c")]
    [InlineData("/search?q=%D0%BE%D1%87%D0%B5%D1%80%D0%B5%D0%B4%D1%8C")]
    public async Task ClientRoutes_ReturnIndexHtml(string path)
    {
        var response = await _spa.GetAsync(path, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("<title>spa</title>");
    }

    [Theory]
    [InlineData("/api")]
    [InlineData("/api/")]
    [InlineData("/api/items/123")]
    [InlineData("/API/items")]
    public async Task ApiPrefixedRoutes_NeverFallBackToIndexHtml(string path)
    {
        var response = await _spa.GetAsync(path, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task NonGetRequestToClientRoute_IsNotServedAsIndexHtml()
    {
        var response = await _spa.PostAsync("/settings", new StringContent(""), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldNotBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task HeadRequestToClientRoute_ReturnsOkWithoutBody()
    {
        using var request = new HttpRequestMessage(HttpMethod.Head, "/settings");
        var response = await _spa.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldBeEmpty();
    }

    public void Dispose()
    {
        _spa.Dispose();
        _spaFactory.Dispose();
        Directory.Delete(_webRoot, recursive: true);
    }
}