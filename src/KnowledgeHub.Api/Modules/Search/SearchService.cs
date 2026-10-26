using KnowledgeHub.Api.Common;
using KnowledgeHub.Api.Infrastructure.Persistence;
using KnowledgeHub.Api.Modules.Items;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeHub.Api.Modules.Search;

public sealed class SearchService(AppDbContext db) : ISearchService
{
    // Маркеры — управляющие символы, которых нет в обычном тексте: так экранирование HTML не затронет <mark>.
    private const char Start = '\u0001';
    private const char Stop = '\u0002';
    private const char Delimiter = '\u0003';

    private const string HeadlineOptions =
        "MaxFragments=2, MaxWords=30, MinWords=10, StartSel=\u0001, StopSel=\u0002, FragmentDelimiter=\u0003";

    public async Task<PagedResult<SearchHit>> SearchAsync(SearchQuery query, CancellationToken ct)
    {
        var text = query.Text.Trim();
        if (string.IsNullOrEmpty(text))
        {
            throw Errors.Validation("q", "Query is required.");
        }

        var matches = db.Items.AsNoTracking()
            .Filter(query.Type, query.Unread, query.Tags, query.CollectionId)
            .Where(i => i.SearchVector!.Matches(EF.Functions.WebSearchToTsQuery("russian", text)));
        if (query.From is { } from)
        {
            matches = matches.Where(i => i.CreatedAt >= from);
        }

        if (query.To is { } to)
        {
            matches = matches.Where(i => i.CreatedAt <= to);
        }

        var total = await matches.CountAsync(ct);
        var skip = (long)(query.Page - 1) * query.PageSize;
        if (skip >= total)
        {
            return new PagedResult<SearchHit>([], query.Page, query.PageSize, total);
        }

        // Шаг 1: страница идентификаторов с рангом. Шаг 2: карточки и подсветка только для неё (ts_headline дорогой).
        var ranked = await matches
            .OrderByDescending(i => i.SearchVector!.RankCoverDensity(EF.Functions.WebSearchToTsQuery("russian", text)))
            .ThenByDescending(i => i.CreatedAt)
            .ThenByDescending(i => i.Id)
            .Skip((int)skip)
            .Take(query.PageSize)
            .Select(i => new { i.Id, Rank = i.SearchVector!.RankCoverDensity(EF.Functions.WebSearchToTsQuery("russian", text)) })
            .ToListAsync(ct);

        var ids = ranked.Select(r => r.Id).ToArray();
        var summaries = (await db.Items.AsNoTracking().Where(i => ids.Contains(i.Id)).Select(ItemSummary.Projection).ToListAsync(ct))
            .ToDictionary(s => s.Id, s => s.WithSortedTags());
        var highlights = await HighlightsAsync(text, ids, ct);

        var hits = ranked
            .Select(r => new SearchHit(summaries[r.Id], r.Rank, highlights[r.Id]))
            .ToList();
        return new PagedResult<SearchHit>(hits, query.Page, query.PageSize, total);
    }

    private async Task<Dictionary<Guid, IReadOnlyList<string>>> HighlightsAsync(string text, Guid[] ids, CancellationToken ct)
    {
        // Фильтр EF здесь не действует, но id получены из отфильтрованного запроса, а RLS защищает и сырой SQL.
        var rows = await db.Database.SqlQuery<HeadlineRow>($"""
            SELECT i.id AS "Id",
                   ts_headline('russian',
                       concat_ws(E'\n', i.title, i.user_note, left(i.body, {SearchSql.MaxBodyLength})),
                       websearch_to_tsquery('russian', {text}),
                       {HeadlineOptions}) AS "Headline"
            FROM items i
            WHERE i.id = ANY({ids})
            """).ToListAsync(ct);
        return rows.ToDictionary(r => r.Id, r => ToFragments(r.Headline));
    }

    /// <summary>Экранирует HTML текста и возвращает маркеры подсветки как <c>&lt;mark&gt;</c>.</summary>
    internal static IReadOnlyList<string> ToFragments(string headline) =>
        headline.Split(Delimiter, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(f => Escape(f).Replace(Start.ToString(), "<mark>").Replace(Stop.ToString(), "</mark>"))
            .ToList();

    private static string Escape(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&#39;");

    private sealed record HeadlineRow(Guid Id, string Headline);
}
