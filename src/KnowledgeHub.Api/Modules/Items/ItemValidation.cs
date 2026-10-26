namespace KnowledgeHub.Api.Modules.Items;

public static class ItemValidation
{
    /// <summary>Проверяет создание записи; <paramref name="url"/> — нормализованный адрес (только для ссылок).</summary>
    public static Dictionary<string, string[]> ValidateCreate(CreateItemCommand command, out ItemType type, out string? url)
    {
        type = command.Type ?? default;
        url = null;
        var errors = command.Type is { } known
            ? ValidateContent(known, command.Title, command.Body, command.UserNote, command.CodeLanguage)
            : [];

        if (command.Type is null)
        {
            errors["type"] = ["Type is required."];
        }
        else if (type == ItemType.Document)
        {
            errors["type"] = ["Documents are created by uploading a file."];
        }
        else if (type == ItemType.Link)
        {
            if (!UrlNormalizer.TryNormalize(command.Url, out url))
            {
                errors["url"] = ["A valid http or https URL is required."];
            }
            else if (url.Length > Item.MaxUrlLength)
            {
                errors["url"] = [$"URL must be at most {Item.MaxUrlLength} characters."];
            }
        }

        ValidateTags(errors, command.Tags);
        return errors;
    }

    /// <summary>Пустые имена тегов игнорируются, слишком длинные — ошибка поля <c>tags</c>.</summary>
    public static void ValidateTags(Dictionary<string, string[]> errors, IReadOnlyList<string>? tags)
    {
        if (tags is not null && tags.Any(t => TagName.Normalize(t).Length > TagName.MaxLength))
        {
            errors["tags"] = [$"Each tag must be at most {TagName.MaxLength} characters."];
        }
    }

    /// <summary>Проверяет длины полей. Тело проверяется только у заметок и фрагментов кода — у остальных оно не редактируется.</summary>
    public static Dictionary<string, string[]> ValidateContent(
        ItemType type, string? title, string? body, string? userNote, string? codeLanguage)
    {
        var errors = new Dictionary<string, string[]>();
        CheckLength(errors, "title", title?.Trim(), Item.MaxTitleLength);
        CheckLength(errors, "userNote", userNote, Item.MaxUserNoteLength);
        CheckLength(errors, "codeLanguage", codeLanguage?.Trim(), Item.MaxCodeLanguageLength);
        if (type is ItemType.Note or ItemType.Snippet)
        {
            CheckLength(errors, "body", body, Item.MaxBodyLength);
        }

        return errors;
    }

    private static void CheckLength(Dictionary<string, string[]> errors, string field, string? value, int max)
    {
        if (value is not null && value.Length > max)
        {
            errors[field] = [$"Must be at most {max} characters."];
        }
    }
}
