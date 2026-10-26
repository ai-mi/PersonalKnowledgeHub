namespace KnowledgeHub.Api.Modules.Ingestion;

/// <param name="FinalUrl">Адрес страницы после всех редиректов (нужен для относительных ссылок в тексте).</param>
public sealed record FetchResult(Uri FinalUrl, string Html);
