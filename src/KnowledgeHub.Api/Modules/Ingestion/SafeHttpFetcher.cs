using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace KnowledgeHub.Api.Modules.Ingestion;

/// <summary>
/// Скачивает HTML-страницы по адресам пользователей. Защита от SSRF — в <c>ConnectCallback</c>: адрес проверяется
/// в момент подключения, поэтому подмена DNS между проверкой и запросом (DNS rebinding) невозможна.
/// </summary>
public sealed partial class SafeHttpFetcher : IDisposable
{
    private static readonly string[] AllowedContentTypes = ["text/html", "application/xhtml+xml"];

    private readonly IngestionOptions _options;
    private readonly HttpClient _client;

    static SafeHttpFetcher() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    private readonly Func<string, CancellationToken, Task<IPAddress[]>> _resolve;

    public SafeHttpFetcher(IOptions<IngestionOptions> options) : this(options, Dns.GetHostAddressesAsync)
    {
    }

    /// <param name="resolve">Резолвер имён; подменяется в тестах.</param>
    public SafeHttpFetcher(IOptions<IngestionOptions> options, Func<string, CancellationToken, Task<IPAddress[]>> resolve)
    {
        _options = options.Value;
        _resolve = resolve;
        _client = new HttpClient(new SocketsHttpHandler
        {
            ConnectCallback = (context, ct) => ConnectAsync(context.DnsEndPoint, ct),
            AllowAutoRedirect = false,
            UseProxy = false,
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        })
        {
            // Общий таймаут держит CancellationTokenSource в FetchAsync (редиректы и чтение тела).
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }

    public void Dispose() => _client.Dispose();

    public async Task<FetchResult> FetchAsync(Uri url, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_options.Timeout);
        try
        {
            return await FetchWithRedirectsAsync(url, timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new IngestionException("Страница не ответила вовремя.", retryable: true);
        }
        catch (HttpRequestException e)
        {
            // Отказ из ConnectCallback приходит обёрнутым в HttpRequestException.
            throw FindIngestionException(e) ?? new IngestionException("Не удалось подключиться к странице.", retryable: true, e);
        }
    }

    private async Task<FetchResult> FetchWithRedirectsAsync(Uri url, CancellationToken ct)
    {
        for (var redirects = 0; ; redirects++)
        {
            ValidateUrl(url);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("KnowledgeHub/1.0");
            request.Headers.Accept.ParseAdd("text/html,application/xhtml+xml");
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

            if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther
                or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            {
                if (redirects >= _options.MaxRedirects)
                {
                    throw new IngestionException("Слишком много перенаправлений.", retryable: false);
                }

                url = response.Headers.Location is { } location
                    ? new Uri(url, location)
                    : throw new IngestionException("Страница вернула перенаправление без адреса.", retryable: false);
                continue;
            }

            EnsureSuccess(response);
            EnsureHtml(response);
            return new FetchResult(url, await ReadHtmlAsync(response, ct));
        }
    }

    private void ValidateUrl(Uri url)
    {
        if (url.Scheme is not ("http" or "https"))
        {
            throw new IngestionException("Поддерживаются только ссылки http и https.", retryable: false);
        }

        if (!_options.AllowLoopbackForTests && url.Port is not (80 or 443))
        {
            throw new IngestionException("Поддерживаются только стандартные порты 80 и 443.", retryable: false);
        }
    }

    private static void EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var status = (int)response.StatusCode;

        // 5xx, 429 и 408 — временные: повтор может помочь; остальные 4xx — нет.
        var retryable = status >= 500 || status is 429 or 408;
        throw new IngestionException($"Страница ответила кодом {status}.", retryable);
    }

    private static void EnsureHtml(HttpResponseMessage response)
    {
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (!AllowedContentTypes.Contains(mediaType, StringComparer.OrdinalIgnoreCase))
        {
            throw new IngestionException("Страница не является HTML-документом.", retryable: false);
        }
    }

    private async Task<string> ReadHtmlAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.Content.Headers.ContentLength > _options.MaxResponseBytes)
        {
            throw TooLarge();
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > _options.MaxResponseBytes)
            {
                throw TooLarge();
            }

            buffer.Write(chunk, 0, read);
        }

        var bytes = buffer.ToArray();
        return ResolveEncoding(response.Content.Headers.ContentType!.CharSet, bytes).GetString(bytes);
    }

    private static IngestionException TooLarge() => new("Страница слишком большая.", retryable: false);

    /// <summary>Кодировка: заголовок Content-Type, затем meta charset в начале документа, иначе UTF-8.</summary>
    private static Encoding ResolveEncoding(string? charSet, byte[] bytes)
    {
        var name = charSet?.Trim('"', ' ');
        if (string.IsNullOrEmpty(name))
        {
            // Латиница в первых килобайтах одинакова во всех ASCII-совместимых кодировках.
            var head = Encoding.Latin1.GetString(bytes, 0, Math.Min(bytes.Length, 4096));
            name = MetaCharsetRegex().Match(head) is { Success: true } match ? match.Groups[1].Value : null;
        }

        if (name is not null)
        {
            try
            {
                return Encoding.GetEncoding(name);
            }
            catch (ArgumentException)
            {
                // Неизвестная кодировка — читаем как UTF-8.
            }
        }

        return Encoding.UTF8;
    }

    [GeneratedRegex(@"<meta[^>]+charset\s*=\s*[""']?\s*([\w\-]+)", RegexOptions.IgnoreCase)]
    private static partial Regex MetaCharsetRegex();

    private static IngestionException? FindIngestionException(Exception e)
    {
        for (var inner = e.InnerException; inner is not null; inner = inner.InnerException)
        {
            if (inner is IngestionException found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>Резолвит имя, проверяет <b>все</b> адреса и подключается к проверенному IP — без повторного DNS-запроса.</summary>
    public async ValueTask<Stream> ConnectAsync(DnsEndPoint endPoint, CancellationToken ct)
    {
        var addresses = await _resolve(endPoint.Host, ct);
        if (addresses.Any(a => !IsAllowed(a)))
        {
            throw Unavailable();
        }

        SocketException? last = null;
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, endPoint.Port), ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException e)
            {
                socket.Dispose();
                last = e;
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        // Пустой список адресов: подключаться некуда.
        throw last is null ? Unavailable() : last;
    }

    private static IngestionException Unavailable() => new("Адрес страницы недоступен для сохранения.", retryable: false);

    private bool IsAllowed(IPAddress address) =>
        SsrfGuard.IsAllowed(address) || _options.AllowLoopbackForTests && IPAddress.IsLoopback(address);
}
