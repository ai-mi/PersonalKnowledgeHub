using System.Diagnostics;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using UglyToad.PdfPig;

namespace KnowledgeHub.Api.Modules.Ingestion;

/// <summary>
/// Достаёт текст документа в отдельном процессе (<c>dotnet KnowledgeHub.Api.dll extract-document &lt;kind&gt;</c>: файл — в stdin,
/// текст — из stdout). PdfPig и OpenXML синхронны, не прерываются <see cref="CancellationToken"/> и не ограничивают память:
/// PDF в несколько мегабайт может распаковываться в гигабайты. Поэтому разбор вынесен из процесса приложения:
/// <list type="bullet">
/// <item>по таймауту или отмене процесс разбора убивается — процессор действительно освобождается;</item>
/// <item>куча процесса разбора ограничена <see cref="ParserHeapLimitBytes"/>: бомба роняет только его, не API;</item>
/// <item>одновременно идёт один разбор, и место освобождается только после выхода процесса — разборы не копятся.</item>
/// </list>
/// </summary>
public static class DocumentTextExtractor
{
    /// <summary>Аргумент командной строки, по которому <c>Program</c> работает как процесс разбора, а не как веб-приложение.</summary>
    public const string ChildCommand = "extract-document";

    public const int MaxPdfPages = 2000;
    public const long ParserHeapLimitBytes = 512L * 1024 * 1024;
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    private const int ExitOk = 0;
    private const int ExitBadArguments = 2;
    private const int ExitRejected = 3;
    private const int ExitParseFailed = 4;
    private const int MaxErrorBytes = 4096;

    /// <summary>Процесс разбора сам обрезает текст до <see cref="ArticleExtractor.MaxBodyLength"/> символов (до 4 байт UTF-8 на символ).</summary>
    private const int MaxOutputBytes = ArticleExtractor.MaxBodyLength * 4;

    private const string ExtractFailedMessage = "Не удалось извлечь текст из документа.";

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>Переменные окружения, которые процесс разбора наследует от приложения; остальные (в том числе секреты) — нет.</summary>
    internal static readonly HashSet<string> ParserEnvironment = new(StringComparer.OrdinalIgnoreCase)
    {
        "PATH", "HOME", "TMPDIR", "TMP", "TEMP", "DOTNET_ROOT", "SystemRoot", "windir", "USERPROFILE", "LOCALAPPDATA",
    };

    /// <summary>Достаёт текст документа; поток должен поддерживать перемотку.</summary>
    public static Task<string> ExtractAsync(Stream stream, DocumentKind kind, CancellationToken ct, TimeSpan? timeout = null) =>
        ExtractAsync(stream, kind, ct, timeout, onParserStarted: null);

    /// <param name="onParserStarted">Для тестов: вызывается с id процесса разбора сразу после его запуска.</param>
    internal static async Task<string> ExtractAsync(
        Stream stream, DocumentKind kind, CancellationToken ct, TimeSpan? timeout, Action<int>? onParserStarted)
    {
        await Gate.WaitAsync(ct);
        string text;
        try
        {
            text = (await RunParserAsync(stream, kind, timeout ?? Timeout, onParserStarted, ct)).Trim();
        }
        finally
        {
            Gate.Release();
        }

        if (text.Length == 0)
        {
            throw new IngestionException("В документе не найден текст (возможно, это скан).", retryable: false);
        }

        return text.Length > ArticleExtractor.MaxBodyLength ? text[..ArticleExtractor.MaxBodyLength] : text;
    }

    private static async Task<string> RunParserAsync(
        Stream stream, DocumentKind kind, TimeSpan timeout, Action<int>? onParserStarted, CancellationToken ct)
    {
        // Не удалось запустить процесс — сбой окружения, а не документа: исключение уходит как есть, задача повторится.
        using var process = Process.Start(ParserStartInfo(kind))
            ?? throw new InvalidOperationException("Процесс разбора документа не запустился.");
        try
        {
            onParserStarted?.Invoke(process.Id);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeout);
            var token = timeoutCts.Token;

            var output = ReadAsync(process.StandardOutput.BaseStream, MaxOutputBytes, token);
            var errors = ReadAsync(process.StandardError.BaseStream, MaxErrorBytes, token);
            await WriteInputAsync(process, stream, token);
            await process.WaitForExitAsync(token);
            var (stdout, stderr) = (await output, await errors);

            return process.ExitCode switch
            {
                ExitOk => Utf8.GetString(stdout),
                ExitRejected => throw new IngestionException(Utf8.GetString(stderr), retryable: false),
                // Сбой разбора, нехватка памяти или аварийное завершение процесса: текст из этого документа не получить.
                _ => throw new IngestionException(ExtractFailedMessage, retryable: false,
                    new InvalidOperationException($"Процесс разбора завершился с кодом {process.ExitCode}: {Utf8.GetString(stderr)}")),
            };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new IngestionException("Документ обрабатывается слишком долго.", retryable: false);
        }
        finally
        {
            await KillAsync(process);
        }
    }

    internal static ProcessStartInfo ParserStartInfo(DocumentKind kind)
    {
        var info = new ProcessStartInfo(DotnetHostPath())
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        info.ArgumentList.Add(typeof(DocumentTextExtractor).Assembly.Location);
        info.ArgumentList.Add(ChildCommand);
        info.ArgumentList.Add(kind.ToString());

        // Разбор недоверенного файла не должен видеть секреты приложения (токен бота, строки подключения) — только то,
        // без чего не запустится хост .NET.
        var inherited = info.Environment.Where(e => ParserEnvironment.Contains(e.Key)).ToList();
        info.Environment.Clear();
        foreach (var (key, value) in inherited)
        {
            info.Environment[key] = value;
        }

        info.Environment["DOTNET_GCHeapHardLimit"] = ParserHeapLimitBytes.ToString("X");
        info.Environment["DOTNET_gcServer"] = "0";
        return info;
    }

    /// <summary>
    /// Хост <c>dotnet</c>: приложение само запущено им (<c>dotnet KnowledgeHub.Api.dll</c> на App Service), в тестах путь
    /// к нему SDK кладёт в <c>DOTNET_HOST_PATH</c>; иначе — <c>dotnet</c> из PATH.
    /// </summary>
    private static string DotnetHostPath()
    {
        if (Environment.ProcessPath is { } current
            && Path.GetFileNameWithoutExtension(current).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            return current;
        }

        return Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } host ? host : "dotnet";
    }

    private static async Task WriteInputAsync(Process process, Stream stream, CancellationToken ct)
    {
        try
        {
            stream.Position = 0;
            await stream.CopyToAsync(process.StandardInput.BaseStream, ct);
            process.StandardInput.Close();
        }
        catch (IOException)
        {
            // Процесс разбора завершился, не дочитав файл (отказ по пределам, падение), — исход скажет код выхода.
        }
    }

    /// <summary>Читает поток до конца; сверх <paramref name="maxBytes"/> дочитывает, но не хранит — чтобы процесс не завис на записи.</summary>
    private static async Task<byte[]> ReadAsync(Stream source, int maxBytes, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(chunk, ct)) > 0)
        {
            var keep = (int)Math.Min(read, maxBytes - buffer.Length);
            if (keep > 0)
            {
                buffer.Write(chunk, 0, keep);
            }
        }

        return buffer.ToArray();
    }

    /// <summary>Убивает процесс разбора, если он ещё жив, и ждёт выхода: место в очереди разбора освобождается только после этого.</summary>
    private static async Task KillAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // Процесс завершился между проверкой и Kill.
        }

        await process.WaitForExitAsync(CancellationToken.None);
    }

    // ---- Процесс разбора ----

    /// <summary>Точка входа процесса разбора: файл из stdin, текст (UTF-8) в stdout, отказ — сообщение в stderr и код выхода.</summary>
    public static async Task<int> RunParserProcessAsync(string kindArgument)
    {
        if (!Enum.TryParse<DocumentKind>(kindArgument, ignoreCase: true, out var kind) || !Enum.IsDefined(kind))
        {
            return ExitBadArguments;
        }

        await using var stdout = Console.OpenStandardOutput();
        await using var stderr = Console.OpenStandardError();
        try
        {
            using var input = new MemoryStream();
            await using (var stdin = Console.OpenStandardInput())
            {
                await stdin.CopyToAsync(input);
            }

            var text = Extract(input, kind).Trim();
            if (text.Length > ArticleExtractor.MaxBodyLength)
            {
                text = text[..ArticleExtractor.MaxBodyLength];
            }

            await stdout.WriteAsync(Utf8.GetBytes(text));
            return ExitOk;
        }
        catch (IngestionException e)
        {
            await stderr.WriteAsync(Utf8.GetBytes(e.Message));
            return ExitRejected;
        }
        catch (Exception e)
        {
            // Только тип: тексты библиотек пользователю не показываются, а для журнала родителю хватит типа.
            await stderr.WriteAsync(Utf8.GetBytes(e.GetType().FullName ?? nameof(Exception)));
            return ExitParseFailed;
        }
    }

    private static string Extract(Stream stream, DocumentKind kind)
    {
        stream.Position = 0;
        return kind switch
        {
            DocumentKind.Pdf => ExtractPdf(stream),
            DocumentKind.Docx => ExtractDocx(stream),
            _ => Utf8.GetString(ReadAll(stream)).TrimStart('﻿'),
        };
    }

    private static string ExtractPdf(Stream stream)
    {
        using var pdf = PdfDocument.Open(stream);
        var text = new StringBuilder();
        for (var number = 1; number <= Math.Min(pdf.NumberOfPages, MaxPdfPages); number++)
        {
            text.AppendLine(pdf.GetPage(number).Text);
        }

        return text.ToString();
    }

    private static string ExtractDocx(Stream stream)
    {
        // Файл мог попасть в хранилище до появления проверки при загрузке — проверяем и здесь, до открытия пакета.
        if (!ZipLimits.IsWithinLimits(stream))
        {
            throw new IngestionException("Документ слишком большой в распакованном виде.", retryable: false);
        }

        // Второй барьер внутри OpenXML: предел символов на часть пакета при разборе XML.
        using var document = WordprocessingDocument.Open(stream, isEditable: false,
            new OpenSettings { MaxCharactersInPart = ZipLimits.MaxUncompressedBytes });
        var paragraphs = document.MainDocumentPart?.Document?.Body?.Descendants<Paragraph>() ?? [];
        return string.Join('\n', paragraphs.Select(p => p.InnerText).Where(t => t.Length > 0));
    }

    private static byte[] ReadAll(Stream stream)
    {
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
