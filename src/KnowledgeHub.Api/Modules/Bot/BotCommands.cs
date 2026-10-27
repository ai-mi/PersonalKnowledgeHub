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
        _ => Task.FromResult("Неизвестная команда. Просто пришлите ссылку, текст или файл — я сохраню."),
    };

    /// <returns>Короткий текст для <c>answerCallbackQuery</c>.</returns>
    public async Task<string> MarkReadAsync(string? callbackData, CancellationToken ct) =>
        BotMessageFormatter.TryParseReadCallback(callbackData, out var itemId) && await items.MarkReadAsync(itemId, true, ct) is not null
            ? "Отмечено прочитанным"
            : "Запись не найдена";

    private async Task<string> SearchAsync(string query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return BotMessageFormatter.Escape("Использование: /search <запрос>");
        }

        var result = await search.SearchAsync(new SearchQuery(query, PageSize: ResultCount), ct);
        return result.Items.Count == 0
            ? "Ничего не найдено"
            : BotMessageFormatter.ItemList("Найдено:", result.Items.Select(h => h.Item), options.Value.WebBaseUrl);
    }

    private async Task<string> RecentAsync(CancellationToken ct)
    {
        var result = await items.ListAsync(new ListItemsQuery(null, false, 1, ResultCount), ct);
        return result.Items.Count == 0
            ? "Записей пока нет"
            : BotMessageFormatter.ItemList("Последние записи:", result.Items, options.Value.WebBaseUrl);
    }
}
