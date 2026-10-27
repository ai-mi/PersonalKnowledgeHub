using System.Text;
using KnowledgeHub.Api.Modules.Items;

namespace KnowledgeHub.Api.Modules.Bot;

/// <summary>Тексты сообщений бота в HTML-разметке Telegram. Весь пользовательский текст экранируется здесь.</summary>
public static class BotMessageFormatter
{
    private const string ReadPrefix = "read:";

    /// <summary>Telegram понимает в HTML только <c>&amp;lt;</c>, <c>&amp;gt;</c>, <c>&amp;amp;</c> и <c>&amp;quot;</c>.</summary>
    public static string Escape(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    public static string Ready(string title) => $"Готово: {Escape(title)}";

    public static string Failed(string error) => $"Не удалось обработать: {Escape(error)}";

    /// <summary>Заголовок для показа: у записи без заголовка — адрес, иначе заглушка.</summary>
    public static string DisplayTitle(ItemSummary item) => DisplayTitle(item.Title, item.Url);

    public static string DisplayTitle(string? title, string? url) =>
        !string.IsNullOrWhiteSpace(title) ? title : !string.IsNullOrWhiteSpace(url) ? url : "Без названия";

    /// <summary>Нумерованный список записей; заголовок — ссылка на запись в вебе, если задан <paramref name="webBaseUrl"/>.</summary>
    public static string ItemList(string heading, IEnumerable<ItemSummary> items, string webBaseUrl)
    {
        var text = new StringBuilder(Escape(heading));
        var number = 0;
        foreach (var item in items)
        {
            var title = Escape(DisplayTitle(item));
            var line = string.IsNullOrWhiteSpace(webBaseUrl) ? title : $"<a href=\"{Escape($"{webBaseUrl.TrimEnd('/')}/items/{item.Id}")}\">{title}</a>";
            text.Append('\n').Append(++number).Append(". ").Append(line);
        }

        return text.ToString();
    }

    public static InlineButton ReadButton(Guid itemId) => new("Прочитано", ReadPrefix + itemId);

    /// <summary><c>callback_data</c> кнопки «Прочитано» → идентификатор записи.</summary>
    public static bool TryParseReadCallback(string? data, out Guid itemId)
    {
        itemId = Guid.Empty;
        return data is not null && data.StartsWith(ReadPrefix, StringComparison.Ordinal) && Guid.TryParse(data.AsSpan(ReadPrefix.Length), out itemId);
    }
}
