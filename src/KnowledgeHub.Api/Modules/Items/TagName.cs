using System.Text.RegularExpressions;

namespace KnowledgeHub.Api.Modules.Items;

/// <summary>Единые правила имени тега для API и Telegram-бота.</summary>
public static partial class TagName
{
    public const int MaxLength = 50;

    /// <summary>Сравнение имён тегов: <c>Azure</c> и <c>azure</c> — один тег.</summary>
    public static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// Убирает пробелы по краям и ведущие <c>#</c>, внутренние пробелы заменяет на <c>-</c>.
    /// Пустая строка — имя не получилось; длину (<see cref="MaxLength"/>) проверяет вызывающий.
    /// </summary>
    public static string Normalize(string? raw) =>
        raw is null ? "" : Whitespace().Replace(raw.Trim().TrimStart('#').Trim(), "-");

    public static IReadOnlyList<string> Sort(IEnumerable<string> names) => names.Order(Comparer).ToList();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
