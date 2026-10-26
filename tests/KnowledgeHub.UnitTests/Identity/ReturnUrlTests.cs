using KnowledgeHub.Api.Modules.Identity;

namespace KnowledgeHub.UnitTests.Identity;

public class ReturnUrlTests
{
    [Theory]
    [InlineData("/", "/")]
    [InlineData("/items", "/items")]
    [InlineData("/items/42?tab=notes#top", "/items/42?tab=notes#top")]
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("items", "/")]
    [InlineData("https://evil.com", "/")]
    [InlineData("//evil.com", "/")]
    [InlineData("/\\evil.com", "/")]
    [InlineData("/items\\..\\evil", "/")]
    [InlineData("/items\r\nSet-Cookie: x=1", "/")]
    [InlineData("javascript:alert(1)", "/")]
    public void Sanitize_AllowsOnlyLocalPaths(string? input, string expected)
    {
        ReturnUrl.Sanitize(input).ShouldBe(expected);
    }
}
