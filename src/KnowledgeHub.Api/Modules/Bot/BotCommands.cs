using KnowledgeHub.Api.Modules.Items;
using KnowledgeHub.Api.Modules.Search;
using Microsoft.Extensions.Options;

namespace KnowledgeHub.Api.Modules.Bot;

/// <summary>Команды привязанного чата (<c>/search</c>, <c>/recent</c>) и кнопка «Прочитано». Выполняются в контексте пользователя чата.</summary>
public sealed class BotCommands(ISearchService search, IItemService items, IOptions<BotOptions> options)
{
    private const int ResultCount = 5;

    /// <returns>Текст ответа в HTML-разметке Telegram.</returns>
    public Task<string> ExecuteAsync(BotCommand command, CancellationToken ct) => command.Name switch
    {
        "search" => SearchAsync(command.Argument, ct),
        "recent" => RecentAsync(ct),
        _ => Task.FromResult("Unknown command. Just send me a link, text, or a file — I'll save it."),
    };

    /// <returns>Короткий текст для <c>answerCallbackQuery</c>.</returns>
    public async Task<string> MarkReadAsync(string? callbackData, CancellationToken ct) =>
        BotMessageFormatter.TryParseReadCallback(callbackData, out var itemId) && await items.MarkReadAsync(itemId, true, ct) is not null
            ? "Marked as read"
            : "Item not found";

    private async Task<string> SearchAsync(string query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return BotMessageFormatter.Escape("Usage: /search <query>");
        }

        var result = await search.SearchAsync(new SearchQuery(query, PageSize: ResultCount), ct);
        return result.Items.Count == 0
            ? "Nothing found"
            : BotMessageFormatter.ItemList("Found:", result.Items.Select(h => h.Item), options.Value.WebBaseUrl);
    }

    private async Task<string> RecentAsync(CancellationToken ct)
    {
        var result = await items.ListAsync(new ListItemsQuery(null, false, 1, ResultCount), ct);
        return result.Items.Count == 0
            ? "No items yet"
            : BotMessageFormatter.ItemList("Recent items:", result.Items, options.Value.WebBaseUrl);
    }
}
