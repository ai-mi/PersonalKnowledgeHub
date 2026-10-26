using KnowledgeHub.Api.Modules.Items;
using SmartReader;

namespace KnowledgeHub.Api.Modules.Ingestion;

public sealed record ExtractedArticle(string? Title, string? Body);

public static class ArticleExtractor
{
    /// <summary>Тело ограничено так же, как поисковый индекс (<c>left(body, 500000)</c>).</summary>
    public const int MaxBodyLength = 500_000;

    /// <summary>
    /// Достаёт заголовок и текст статьи. Если страница не похожа на статью — только заголовок.
    /// SmartReader сам ничего не скачивает: HTML уже получен безопасным <see cref="SafeHttpFetcher"/>.
    /// </summary>
    public static ExtractedArticle Extract(Uri url, string html)
    {
        var article = new Reader(url.ToString(), html).GetArticle();
        var title = Truncate(Normalize(article.Title), Item.MaxTitleLength);
        var body = article.IsReadable ? Truncate(Normalize(article.TextContent), MaxBodyLength) : null;

        return title is null && body is null
            ? throw new IngestionException("Не удалось извлечь содержимое страницы.", retryable: false)
            : new ExtractedArticle(title, body);
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Truncate(string? value, int max) => value is { } v && v.Length > max ? v[..max] : value;
}
