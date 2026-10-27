using System.Text.Json;
using System.Text.Json.Serialization;
using KnowledgeHub.Api.Modules.Identity;
using KnowledgeHub.Api.Modules.Items;
using KnowledgeHub.Api.Modules.Reminders;

namespace KnowledgeHub.Api.Modules.Export;

/// <summary>Содержимое <c>export.json</c>. Тексты записей лежат в Markdown-файлах — здесь только метаданные и пути к ним.</summary>
public sealed record ExportDocument(
    DateTimeOffset ExportedAt,
    IReadOnlyList<ExportedItem> Items,
    IReadOnlyList<TagResponse> Tags,
    IReadOnlyList<CollectionResponse> Collections,
    IReadOnlyList<ReminderResponse> Reminders,
    ExportedSettings? Settings)
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
}

/// <param name="Markdown">Путь Markdown-файла записи в архиве.</param>
/// <param name="File">Путь исходного файла документа в архиве; <c>null</c> — у записи нет файла.</param>
public sealed record ExportedItem(
    Guid Id,
    ItemType Type,
    string? Title,
    string? Url,
    string? UserNote,
    string? CodeLanguage,
    ItemStatus Status,
    DateTimeOffset? ReadAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<string> Tags,
    IReadOnlyList<CollectionRef> Collections,
    string Markdown,
    string? File);

public sealed record ExportedSettings(
    string Timezone, DigestFrequency DigestFrequency, DayOfWeek DigestDay, IReadOnlyList<string> DigestChannels, string? Email);
