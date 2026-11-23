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
    // Wire server Azure (DNS, метаданные хоста) — приложение работает в Azure.
    [InlineData("168.63.129.16", false)]
    // IETF protocol assignments 192.0.0.0/24 и сеть для тестов производительности 198.18.0.0/15.
    [InlineData("192.0.0.1", false)]
    [InlineData("198.18.0.1", false)]
    [InlineData("198.19.255.254", false)]
    // IPv6-формы, несущие IPv4: IPv4-compatible ::/96, 6to4 2002::/16, Teredo 2001::/32, локальный NAT64 64:ff9b:1::/48.
    [InlineData("::7f00:1", false)]
    [InlineData("::a00:1", false)]
    [InlineData("2002:7f00:1::1", false)]
    [InlineData("2002:a00:1::1", false)]
    [InlineData("2001:0:4136:e378:8000:63bf:3fff:fdd2", false)]
    [InlineData("64:ff9b:1::a00:1", false)]
    [InlineData("8.8.8.8", true)]
    [InlineData("168.63.129.17", true)]
    [InlineData("192.0.1.1", true)]
    [InlineData("198.20.0.1", true)]
    [InlineData("2001:4860:4860::8888", true)]
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
