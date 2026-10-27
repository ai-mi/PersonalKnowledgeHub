using System.Text;
using KnowledgeHub.Api.Modules.Items;
using YamlDotNet.Serialization;

namespace KnowledgeHub.Api.Modules.Export;

/// <summary>Превращает запись в Markdown-файл с YAML-метаданными (front matter).</summary>
public static class MarkdownWriter
{
    private const int MaxSlugLength = 60;
    private const int IdSuffixLength = 8;

    private static readonly ISerializer Yaml = new SerializerBuilder()
        .WithNewLine("\n")
        .Build();

    private static readonly Dictionary<char, string> Translit = new()
    {
        ['а'] = "a", ['б'] = "b", ['в'] = "v", ['г'] = "g", ['д'] = "d", ['е'] = "e", ['ё'] = "e", ['ж'] = "zh", ['з'] = "z",
        ['и'] = "i", ['й'] = "y", ['к'] = "k", ['л'] = "l", ['м'] = "m", ['н'] = "n", ['о'] = "o", ['п'] = "p", ['р'] = "r",
        ['с'] = "s", ['т'] = "t", ['у'] = "u", ['ф'] = "f", ['х'] = "h", ['ц'] = "ts", ['ч'] = "ch", ['ш'] = "sh", ['щ'] = "sch",
        ['ъ'] = "", ['ы'] = "y", ['ь'] = "", ['э'] = "e", ['ю'] = "yu", ['я'] = "ya",
    };

    /// <summary><c>items/{slug}-{первые 8 символов id}.md</c>.</summary>
    public static string FileName(Item item) =>
        $"items/{Slug(item.Title ?? item.Url)}-{item.Id.ToString("N")[..IdSuffixLength]}.md";

    /// <summary>Латиница, цифры и дефисы: кириллица транслитерируется, остальное становится разделителем.</summary>
    public static string Slug(string? text)
    {
        var slug = new StringBuilder();
        foreach (var ch in (text ?? "").ToLowerInvariant())
        {
            if (Translit.TryGetValue(ch, out var latin))
            {
                slug.Append(latin);
            }
            else if (ch is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                slug.Append(ch);
            }
            else if (slug.Length > 0 && slug[^1] != '-')
            {
                slug.Append('-');
            }
        }

        var result = slug.ToString();
        if (result.Length > MaxSlugLength)
        {
            result = result[..MaxSlugLength];
        }

        result = result.Trim('-');
        return result.Length == 0 ? "item" : result;
    }

    /// <param name="file">Путь исходного файла документа в архиве, если он есть.</param>
    public static string Render(Item item, string? file)
    {
        var meta = new Dictionary<string, object?>
        {
            ["id"] = item.Id.ToString(),
            ["type"] = item.Type.ToString().ToLowerInvariant(),
            ["title"] = item.Title,
            ["url"] = item.Url,
            ["tags"] = TagName.Sort(item.Tags.Select(t => t.Name)),
            ["collections"] = item.Collections.Select(c => c.Name).Order(StringComparer.OrdinalIgnoreCase).ToList(),
            ["created"] = item.CreatedAt.ToString("O"),
            ["updated"] = item.UpdatedAt.ToString("O"),
            ["read"] = item.ReadAt?.ToString("O"),
            ["code_language"] = item.CodeLanguage,
            ["file"] = file,
        };

        // Пустые необязательные поля не пишем: «url:» без значения читается как null и только шумит.
        var present = meta.Where(p => p.Value is not null).ToDictionary(p => p.Key, p => p.Value);
        var text = new StringBuilder("---\n").Append(Yaml.Serialize(present)).Append("---\n");
        if (!string.IsNullOrWhiteSpace(item.UserNote))
        {
            text.Append('\n').Append(item.UserNote.TrimEnd()).Append('\n');
        }

        if (!string.IsNullOrWhiteSpace(item.Body))
        {
            text.Append('\n').Append(item.Type == ItemType.Snippet ? CodeBlock(item.Body, item.CodeLanguage) : item.Body.TrimEnd()).Append('\n');
        }

        return text.ToString();
    }

    private static string CodeBlock(string code, string? language)
    {
        // Ограда длиннее любой серии обратных кавычек внутри кода, иначе код «выпадет» из блока.
        var longest = 0;
        var run = 0;
        foreach (var ch in code)
        {
            run = ch == '`' ? run + 1 : 0;
            longest = Math.Max(longest, run);
        }

        var fence = new string('`', Math.Max(3, longest + 1));
        return $"{fence}{language}\n{code.TrimEnd()}\n{fence}";
    }
}
