using System.Text;
using System.Text.RegularExpressions;
using KnowledgeHub.Api.Modules.Ingestion;
using KnowledgeHub.Api.Modules.Items;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace KnowledgeHub.Api.Modules.Bot;

/// <summary>Присланный файл: скачивается и принимается через <see cref="IDocumentIntake"/>.</summary>
public sealed record ParsedDocument(string FileId, string FileName, long? FileSize, DocumentMetadata Metadata);

/// <summary>Что сохранить из сообщения: ровно одно из полей непустое.</summary>
public sealed record ParsedMessage(CreateItemCommand? Item, ParsedDocument? Document);

public sealed record BotCommand(string Name, string Argument);

/// <summary>Разбор входящего сообщения Telegram в команду сохранения записи.</summary>
public static partial class MessageParser
{
    /// <summary>
    /// Файл → документ; первая ссылка → <c>link</c>; иначе текст → <c>note</c>. Остальной текст (или подпись к файлу) — заметка
    /// пользователя, хэштеги (<c>#azure</c>) — теги, и из текста они убираются. Адрес первой ссылки из текста тоже убирается,
    /// а текст «гиперссылки» (<c>text_link</c>) остаётся: он обычно описывает страницу. <c>null</c> — сохранять нечего.
    /// </summary>
    public static ParsedMessage? Parse(Message message)
    {
        var text = message.Text ?? message.Caption ?? "";
        var entities = (message.Text is not null ? message.Entities : message.CaptionEntities) ?? [];

        var tags = new List<string>();
        var removed = new List<(int Start, int End)>();
        string? url = null;
        foreach (var entity in entities.OrderBy(e => e.Offset))
        {
            if (entity.Offset < 0 || entity.Length <= 0 || entity.Offset + entity.Length > text.Length)
            {
                continue;
            }

            var span = text.Substring(entity.Offset, entity.Length);
            switch (entity.Type)
            {
                case MessageEntityType.Hashtag:
                    tags.Add(span);
                    removed.Add((entity.Offset, entity.Offset + entity.Length));
                    break;
                case MessageEntityType.Url when url is null:
                    url = span.Contains("://") ? span : "https://" + span;
                    removed.Add((entity.Offset, entity.Offset + entity.Length));
                    break;
                case MessageEntityType.TextLink when url is null && !string.IsNullOrWhiteSpace(entity.Url):
                    url = entity.Url;
                    break;
            }
        }

        var note = CleanNote(text, removed);
        var tagList = tags.Select(TagName.Normalize).Where(t => t.Length > 0).ToList();

        if (message.Document is { } document)
        {
            return new ParsedMessage(null, new ParsedDocument(
                document.FileId, document.FileName ?? "file", document.FileSize, new DocumentMetadata(UserNote: note, Tags: tagList)));
        }

        if (url is not null)
        {
            return new ParsedMessage(new CreateItemCommand(ItemType.Link, Url: url, UserNote: note, Tags: tagList), null);
        }

        return note is null ? null : new ParsedMessage(new CreateItemCommand(ItemType.Note, Body: note, Tags: tagList), null);
    }

    /// <summary>Команда вида <c>/start code</c> или <c>/search@bot запрос</c>; <c>null</c> — сообщение не команда.</summary>
    public static BotCommand? ParseCommand(Message message)
    {
        var text = message.Text?.TrimStart();
        if (text is not ['/', ..])
        {
            return null;
        }

        var parts = text.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var name = parts[0][1..];
        if (name.IndexOf('@') is >= 0 and var at)
        {
            name = name[..at];
        }

        return new BotCommand(name.ToLowerInvariant(), parts.Length > 1 ? parts[1] : "");
    }

    private static string? CleanNote(string text, List<(int Start, int End)> removed)
    {
        var kept = new StringBuilder();
        var position = 0;
        foreach (var (start, end) in removed.OrderBy(r => r.Start))
        {
            if (start < position)
            {
                continue;
            }

            kept.Append(text, position, start - position);
            position = end;
        }

        kept.Append(text, position, text.Length - position);

        var lines = SpaceRuns().Replace(kept.ToString(), " ").Split('\n').Select(l => l.Trim());
        var note = BlankLineRuns().Replace(string.Join('\n', lines), "\n\n").Trim();
        return note.Length == 0 ? null : note;
    }

    [GeneratedRegex(@"[ \t\r]+")]
    private static partial Regex SpaceRuns();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex BlankLineRuns();
}
