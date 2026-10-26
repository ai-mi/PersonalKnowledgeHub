using System.Net;
using System.Net.Sockets;
using System.Text;
using KnowledgeHub.Api.Modules.Ingestion;
using Microsoft.Extensions.Options;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace KnowledgeHub.IntegrationTests.Ingestion;

public sealed class SafeHttpFetcherTests : IDisposable
{
    private readonly WireMockServer _server = WireMockServer.Start();

    private static readonly string Crlf = new([(char)13, (char)10]);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _server.Dispose();

    private Uri Url(string path = "/page") => new($"http://127.0.0.1:{_server.Port}{path}");

    private static SafeHttpFetcher Fetcher(Action<IngestionOptions>? configure = null, Func<string, CancellationToken, Task<IPAddress[]>>? resolve = null)
    {
        var options = new IngestionOptions { AllowLoopbackForTests = true };
        configure?.Invoke(options);
        return resolve is null ? new SafeHttpFetcher(Options.Create(options)) : new SafeHttpFetcher(Options.Create(options), resolve);
    }

    private void Serve(string path, Func<IResponseBuilder, IResponseBuilder> respond) =>
        _server.Given(Request.Create().WithPath(path).UsingGet()).RespondWith(respond(Response.Create()));

    private static Task<IPAddress[]> Resolve(params IPAddress[] addresses) => Task.FromResult(addresses);

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private async Task<IngestionException> FailureAsync(SafeHttpFetcher fetcher, Uri? url = null) =>
        await Should.ThrowAsync<IngestionException>(() => fetcher.FetchAsync(url ?? Url(), Ct));

    [Fact]
    public async Task SlowPage_TimesOutAsRetryable()
    {
        Serve("/page", r => r.WithDelay(TimeSpan.FromSeconds(3)).WithHeader("Content-Type", "text/html").WithBody("<html></html>"));
        using var fetcher = Fetcher(o => o.Timeout = TimeSpan.FromMilliseconds(300));

        var error = await FailureAsync(fetcher);

        error.Retryable.ShouldBeTrue();
        error.Message.ShouldContain("вовремя");
    }

    [Fact]
    public async Task CallerCancellation_IsNotReportedAsTimeout()
    {
        using var fetcher = Fetcher();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => fetcher.FetchAsync(Url(), cancelled.Token));
    }

    [Fact]
    public async Task RedirectLoop_StopsAfterTheLimit()
    {
        Serve("/page", r => r.WithStatusCode(302).WithHeader("Location", "/page"));
        using var fetcher = Fetcher(o => o.MaxRedirects = 2);

        var error = await FailureAsync(fetcher);

        error.Retryable.ShouldBeFalse();
        error.Message.ShouldContain("перенаправлений");
    }

    [Fact]
    public async Task RedirectWithoutLocation_IsRejected()
    {
        Serve("/page", r => r.WithStatusCode(302));
        using var fetcher = Fetcher();

        var error = await FailureAsync(fetcher);

        error.Retryable.ShouldBeFalse();
        error.Message.ShouldContain("без адреса");
    }

    [Fact]
    public async Task RelativeRedirect_IsFollowedAndFinalUrlReported()
    {
        Serve("/old", r => r.WithStatusCode(301).WithHeader("Location", "/new"));
        Serve("/new", r => r.WithHeader("Content-Type", "text/html; charset=utf-8").WithBody("<html>новая</html>"));
        using var fetcher = Fetcher();

        var result = await fetcher.FetchAsync(Url("/old"), Ct);

        result.FinalUrl.ShouldBe(Url("/new"));
        result.Html.ShouldContain("новая");
    }

    [Theory]
    [InlineData(500, true)]
    [InlineData(503, true)]
    [InlineData(429, true)]
    [InlineData(408, true)]
    [InlineData(404, false)]
    [InlineData(403, false)]
    public async Task ErrorStatus_IsRetryableOnlyWhenRepeatingMayHelp(int status, bool retryable)
    {
        Serve("/page", r => r.WithStatusCode(status));
        using var fetcher = Fetcher();

        var error = await FailureAsync(fetcher);

        error.Retryable.ShouldBe(retryable);
        error.Message.ShouldContain(status.ToString());
    }

    [Theory]
    [InlineData("application/json")]
    [InlineData(null)]
    public async Task NonHtmlResponse_IsRejected(string? contentType)
    {
        Serve("/page", r => contentType is null ? r.WithBody([1, 2, 3]) : r.WithHeader("Content-Type", contentType).WithBody("{}"));
        using var fetcher = Fetcher();

        var error = await FailureAsync(fetcher);

        error.Retryable.ShouldBeFalse();
        error.Message.ShouldContain("не является HTML");
    }

    [Fact]
    public async Task DeclaredLengthOverLimit_IsRejectedWithoutReadingTheBody()
    {
        Serve("/page", r => r.WithHeader("Content-Type", "text/html").WithBody(new string('x', 5000)));
        using var fetcher = Fetcher(o => o.MaxResponseBytes = 100);

        var error = await FailureAsync(fetcher);

        error.Retryable.ShouldBeFalse();
        error.Message.ShouldContain("слишком большая");
    }

    [Fact]
    public async Task EncodingFromMetaTag_IsUsedWhenHeaderHasNoCharset()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var html = "<html><head><meta charset=\"windows-1251\"></head><body>Привет</body></html>";
        Serve("/page", r => r.WithHeader("Content-Type", "text/html").WithBody(Encoding.GetEncoding("windows-1251").GetBytes(html)));
        using var fetcher = Fetcher();

        (await fetcher.FetchAsync(Url(), Ct)).Html.ShouldContain("Привет");
    }

    [Theory]
    [InlineData("text/html; charset=no-such-charset", "<html>Привет</html>")]
    [InlineData("text/html", "<html><meta charset=no-such-charset>Привет</html>")]
    [InlineData("text/html", "<html>Привет</html>")]
    public async Task UnknownOrMissingEncoding_FallsBackToUtf8(string contentType, string html)
    {
        Serve("/page", r => r.WithHeader("Content-Type", contentType).WithBody(Encoding.UTF8.GetBytes(html)));
        using var fetcher = Fetcher();

        (await fetcher.FetchAsync(Url(), Ct)).Html.ShouldContain("Привет");
    }

    [Theory]
    [InlineData("ftp://example.com/file")]
    [InlineData("http://example.com:8080/page")]
    public async Task UnsupportedSchemeOrPort_IsRejected(string url)
    {
        using var fetcher = Fetcher(o => o.AllowLoopbackForTests = false);

        var error = await FailureAsync(fetcher, new Uri(url));

        error.Retryable.ShouldBeFalse();
    }

    [Fact]
    public async Task ClosedPort_IsRetryableConnectionFailure()
    {
        using var fetcher = Fetcher();

        var error = await FailureAsync(fetcher, new Uri($"http://127.0.0.1:{FreePort()}/page"));

        error.Retryable.ShouldBeTrue();
        error.Message.ShouldContain("подключиться");
    }

    [Fact]
    public async Task Connect_NoAddresses_IsRejected()
    {
        using var fetcher = Fetcher(resolve: (_, _) => Resolve());

        var error = await Should.ThrowAsync<IngestionException>(async () => await fetcher.ConnectAsync(new DnsEndPoint("nowhere.test", 80), Ct));

        error.Retryable.ShouldBeFalse();
    }

    [Fact]
    public async Task Connect_AnyForbiddenAddress_RejectsTheWholeName()
    {
        // Один разрешённый и один частный адрес: имя целиком отклоняется (защита от DNS rebinding).
        using var fetcher = Fetcher(o => o.AllowLoopbackForTests = false, (_, _) => Resolve(IPAddress.Parse("93.184.216.34"), IPAddress.Parse("10.0.0.5")));

        await Should.ThrowAsync<IngestionException>(async () => await fetcher.ConnectAsync(new DnsEndPoint("rebind.test", 80), Ct));
    }

    [Fact]
    public async Task Connect_LoopbackIsForbiddenUnlessAllowedForTests()
    {
        using var fetcher = Fetcher(o => o.AllowLoopbackForTests = false, (_, _) => Resolve(IPAddress.Loopback));

        await Should.ThrowAsync<IngestionException>(async () => await fetcher.ConnectAsync(new DnsEndPoint("localhost", 80), Ct));
    }

    [Fact]
    public async Task Connect_PublicAddressPassesTheGuard_CancellationStopsTheConnection()
    {
        using var fetcher = Fetcher(o => o.AllowLoopbackForTests = false, (_, _) => Resolve(IPAddress.Parse("93.184.216.34")));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () => await fetcher.ConnectAsync(new DnsEndPoint("example.test", 80), cancelled.Token));
    }

    [Fact]
    public async Task Connect_AllAddressesRefuse_ThrowsTheLastSocketError()
    {
        using var fetcher = Fetcher(resolve: (_, _) => Resolve(IPAddress.Loopback, IPAddress.Loopback));

        await Should.ThrowAsync<SocketException>(async () => await fetcher.ConnectAsync(new DnsEndPoint("localhost", FreePort()), Ct));
    }

    [Fact]
    public async Task Connect_FallsBackToTheNextAddress()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            // IPv6 loopback ничего не слушает (или недоступен) — подключение уходит ко второму адресу.
            using var fetcher = Fetcher(resolve: (_, _) => Resolve(IPAddress.IPv6Loopback, IPAddress.Loopback));

            await using var stream = await fetcher.ConnectAsync(new DnsEndPoint("localhost", port), Ct);

            stream.ShouldNotBeNull();
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task ContentLengthHeaderOverLimit_IsRejectedBeforeReadingTheBody()
    {
        // WireMock сам решает, как передавать тело, поэтому заголовок Content-Length отдаёт минимальный TCP-сервер.
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var serving = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync(Ct);
            var stream = client.GetStream();
            var request = new byte[4096];
            _ = await stream.ReadAsync(request, Ct);
            await stream.WriteAsync(Encoding.ASCII.GetBytes(string.Join(Crlf, "HTTP/1.1 200 OK", "Content-Type: text/html", "Content-Length: 5000", "Connection: close", "", "")), Ct);
        }, Ct);
        try
        {
            using var fetcher = Fetcher(o => o.MaxResponseBytes = 100);

            var error = await FailureAsync(fetcher, new Uri($"http://127.0.0.1:{port}/page"));

            error.Retryable.ShouldBeFalse();
            error.Message.ShouldContain("слишком большая");
        }
        finally
        {
            listener.Stop();
            await serving.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        }
    }
}
