using KnowledgeHub.Api.Modules.Items;

namespace KnowledgeHub.UnitTests.Items;

public class UrlNormalizerTests
{
    [Theory]
    [InlineData("https://example.com/", "https://example.com")]
    [InlineData("https://example.com", "https://example.com")]
    [InlineData("HTTPS://Example.COM/Path", "https://example.com/Path")]
    [InlineData("https://example.com/a/b/", "https://example.com/a/b")]
    [InlineData("https://example.com/a#section", "https://example.com/a")]
    [InlineData("https://example.com/a?utm_source=x&utm_medium=y", "https://example.com/a")]
    [InlineData("https://example.com/a?id=1&UTM_Campaign=z&page=2", "https://example.com/a?id=1&page=2")]
    [InlineData("https://example.com/a?utm_source=x&id=1#top", "https://example.com/a?id=1")]
    [InlineData("https://example.com:443/a", "https://example.com/a")]
    [InlineData("http://example.com:8080/a", "http://example.com:8080/a")]
    [InlineData("https://user:secret@example.com/a", "https://example.com/a")]
    [InlineData("  https://example.com/a  ", "https://example.com/a")]
    public void Normalize_ProducesCanonicalForm(string input, string expected)
    {
        UrlNormalizer.TryNormalize(input, out var normalized).ShouldBeTrue();
        normalized.ShouldBe(expected);
    }

    [Theory]
    [InlineData("https://example.com/a?utm_source=x#top", "https://example.com/a")]
    [InlineData("https://EXAMPLE.com/a/", "https://example.com/a")]
    public void Normalize_MakesEquivalentUrlsEqual(string variant, string canonical)
    {
        UrlNormalizer.TryNormalize(variant, out var a).ShouldBeTrue();
        UrlNormalizer.TryNormalize(canonical, out var b).ShouldBeTrue();
        a.ShouldBe(b);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("example.com")]
    [InlineData("/relative/path")]
    [InlineData("ftp://example.com/file")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///etc/passwd")]
    [InlineData("mailto:a@example.com")]
    public void Normalize_RejectsNonHttpUrls(string? input)
    {
        UrlNormalizer.TryNormalize(input, out var normalized).ShouldBeFalse();
        normalized.ShouldBeNull();
    }
}
