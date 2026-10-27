namespace KnowledgeHub.IntegrationTests.Infrastructure;

public class SecurityHeadersTests(ApiFactory factory) : IntegrationTest(factory)
{
    [Theory]
    [InlineData("/health")]
    [InlineData("/api/nope")]
    [InlineData("/api/me")]
    public async Task Responses_HaveSecurityHeaders(string path)
    {
        var response = await Client.GetAsync(path, TestContext.Current.CancellationToken);

        response.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
        response.Headers.GetValues("X-Frame-Options").ShouldBe(["DENY"]);
        response.Headers.GetValues("Referrer-Policy").ShouldHaveSingleItem();
        response.Headers.GetValues("Content-Security-Policy").Single().ShouldStartWith("default-src 'self'");
    }
}
