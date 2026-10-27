using System.Globalization;
using System.Text;
using KnowledgeHub.Api.Modules.Bot;
using KnowledgeHub.Api.Modules.Items;

namespace KnowledgeHub.Api.Modules.Reminders;

/// <summary>Превращает <see cref="Digest"/> в письмо и сообщение Telegram: HTML (экранирование — <see cref="BotMessageFormatter"/>) и текст.</summary>
public static class DigestFormatter
{
    public const string Subject = "Дайджест Personal Knowledge Hub";

    /// <summary>Кнопки Telegram идут одним рядом, поэтому «Прочитано» есть только у первых непрочитанных записей.</summary>
    public const int MaxReadButtons = 5;

    private sealed record Line(string Title, string? Link, string? Suffix = null);

    public static NotificationMessage Format(Digest digest, string webBaseUrl)
    {
        var sections = new List<(string Heading, List<Line> Lines)>();
        if (digest.Unread.Count > 0)
        {
            sections.Add(("Непрочитанное", digest.Unread.Select(i => ItemLine(i, webBaseUrl)).ToList()));
        }

        if (digest.YearAgo.Count > 0)
        {
            sections.Add(("Сохранено год назад", digest.YearAgo.Select(i => ItemLine(i, webBaseUrl)).ToList()));
        }

        if (digest.Reminders.Count > 0)
        {
            sections.Add(("Ближайшие напоминания", digest.Reminders.Select(r =>
                new Line(r.Title, ItemLink(webBaseUrl, r.ItemId), r.LocalRemindAt.ToString("dd.MM HH:mm", CultureInfo.InvariantCulture))).ToList()));
        }

        var buttons = digest.Unread.Take(MaxReadButtons)
            .Select((item, index) => BotMessageFormatter.ReadButton(item.Id, $"Прочитано {index + 1}"))
            .ToList();
        return new NotificationMessage(Subject, Render(sections, html: true), Render(sections, html: false), buttons);
    }

    private static Line ItemLine(ItemSummary item, string webBaseUrl) =>
        new(BotMessageFormatter.DisplayTitle(item), ItemLink(webBaseUrl, item.Id));

    private static string? ItemLink(string webBaseUrl, Guid itemId) =>
        string.IsNullOrWhiteSpace(webBaseUrl) ? null : $"{webBaseUrl.TrimEnd('/')}/items/{itemId}";

    private static string Render(List<(string Heading, List<Line> Lines)> sections, bool html)
    {
        var result = new StringBuilder(html ? BotMessageFormatter.Escape(Subject) : Subject);
        foreach (var (heading, lines) in sections)
        {
            result.Append("\n\n").Append(html ? BotMessageFormatter.Escape(heading) : heading);
            var number = 0;
            foreach (var line in lines)
            {
                result.Append('\n').Append(++number).Append(". ");
                if (html)
                {
                    var title = BotMessageFormatter.Escape(line.Title);
                    result.Append(line.Link is null ? title : $"<a href=\"{BotMessageFormatter.Escape(line.Link)}\">{title}</a>");
                    result.Append(line.Suffix is null ? "" : $" — {BotMessageFormatter.Escape(line.Suffix)}");
                }
                else
                {
                    result.Append(line.Title).Append(line.Suffix is null ? "" : $" — {line.Suffix}").Append(line.Link is null ? "" : $"\n   {line.Link}");
                }
            }
        }

        return result.ToString();
    }
}
