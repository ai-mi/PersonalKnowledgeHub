using System.Net;
using KnowledgeHub.Api.Modules.Ingestion;
using Microsoft.Extensions.Options;

namespace KnowledgeHub.UnitTests.Ingestion;

public sealed class SsrfGuardTests
{
    [Theory]
    [InlineData("127.0.0.1", false)]
    [InlineData("127.255.255.254", false)]
    [InlineData("10.1.2.3", false)]
    [InlineData("172.16.0.1", false)]
    [InlineData("172.31.255.255", false)]
    [InlineData("192.168.1.1", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("100.127.255.255", false)]
    [InlineData("0.0.0.0", false)]
    [InlineData("0.1.2.3", false)]
    [InlineData("224.0.0.1", false)]
    [InlineData("255.255.255.255", false)]
    [InlineData("::", false)]
    [InlineData("::1", false)]
    [InlineData("::ffff:127.0.0.1", false)]
    [InlineData("::ffff:10.0.0.1", false)]
    [InlineData("fd00::1", false)]
    [InlineData("fc00::1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("ff02::1", false)]
    [InlineData("64:ff9b::7f00:1", false)]
    [InlineData("8.8.8.8", true)]
    [InlineData("172.15.0.1", true)]
    [InlineData("172.32.0.1", true)]
    [InlineData("100.63.255.255", true)]
    [InlineData("100.128.0.1", true)]
    [InlineData("::ffff:8.8.8.8", true)]
    [InlineData("2606:4700:4700::1111", true)]
    public void IsAllowed_ClassifiesAddress(string address, bool expected) =>
        SsrfGuard.IsAllowed(IPAddress.Parse(address)).ShouldBe(expected);

    // Адреса в десятичной и шестнадцатеричной записи .NET разбирает как IPv4 — до сети дело не доходит.
    [Theory]
    [InlineData("http://2130706433/")]
    [InlineData("http://0x7f.0.0.1/")]
    [InlineData("http://127.1/")]
    [InlineData("http://[::ffff:127.0.0.1]/")]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    public async Task Fetcher_RejectsInternalAddressesAfterResolve(string url)
    {
        using var fetcher = new SafeHttpFetcher(Options.Create(new IngestionOptions()));

        var error = await Should.ThrowAsync<IngestionException>(() => fetcher.FetchAsync(new Uri(url), TestContext.Current.CancellationToken));

        error.Retryable.ShouldBeFalse();
    }

    [Theory]
    [InlineData("ftp://example.com/file")]
    [InlineData("http://example.com:8080/")]
    [InlineData("https://example.com:22/")]
    public async Task Fetcher_RejectsUnsupportedSchemeAndPort(string url)
    {
        using var fetcher = new SafeHttpFetcher(Options.Create(new IngestionOptions()));

        var error = await Should.ThrowAsync<IngestionException>(() => fetcher.FetchAsync(new Uri(url), TestContext.Current.CancellationToken));

        error.Retryable.ShouldBeFalse();
    }
}
