using System.IO.Compression;
using System.Text.Json;
using KnowledgeHub.Api.Common;
using KnowledgeHub.Api.Infrastructure.Persistence;
using KnowledgeHub.Api.Infrastructure.Storage;
using KnowledgeHub.Api.Modules.Identity;
using KnowledgeHub.Api.Modules.Items;
using KnowledgeHub.Api.Modules.Jobs;
using KnowledgeHub.Api.Modules.Reminders;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeHub.Api.Modules.Export;

public sealed record BuildExportPayload(Guid ExportId);

/// <summary>Собирает ZIP с данными пользователя прямо в Blob Storage (задача ставится с <c>userId</c>, фильтры EF применяются).</summary>
public sealed class BuildExportJob(
    AppDbContext db, IItemService items, TagService tags, CollectionService collections, ReminderService reminders,
    IUserContactService contacts, ICurrentUser currentUser, IBlobStore blobs, TimeProvider timeProvider) : IJobHandler<BuildExportPayload>
{
    public const string JobType = "export.build";
    public static string Type => JobType;

    public async Task HandleAsync(BuildExportPayload payload, CancellationToken ct)
    {
        if (await db.Exports.FirstOrDefaultAsync(e => e.Id == payload.ExportId && e.Status != ExportStatus.Ready, ct) is not { } export)
        {
            return;
        }

        export.Status = ExportStatus.Processing;
        await db.SaveChangesAsync(ct);

        var path = $"{export.UserId}/{export.Id}.zip";
        await WriteArchiveAsync(path, ct);

        export.Status = ExportStatus.Ready;
        export.BlobPath = path;
        export.ExpiresAt = timeProvider.GetUtcNow() + ExportService.Retention;
        await db.SaveChangesAsync(ct);
    }

    private async Task WriteArchiveAsync(string path, CancellationToken ct)
    {
        var exported = new List<ExportedItem>();
        await using (var blob = await blobs.OpenWriteAsync(BlobContainers.Exports, path, "application/zip", ct))
        {
            await using var zip = new ZipArchive(blob, ZipArchiveMode.Create);

            await foreach (var item in items.StreamAllAsync(ct))
            {
                var file = await AddDocumentFileAsync(zip, item, ct);
                var markdown = MarkdownWriter.FileName(item);
                await WriteEntryAsync(zip, markdown, MarkdownWriter.Render(item, file), ct);
                exported.Add(new ExportedItem(
                    item.Id, item.Type, item.Title, item.Url, item.UserNote, item.CodeLanguage, item.Status, item.ReadAt, item.CreatedAt,
                    item.UpdatedAt, TagName.Sort(item.Tags.Select(t => t.Name)),
                    item.Collections.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).Select(c => new CollectionRef(c.Id, c.Name)).ToList(),
                    markdown, file));
            }

            var settings = await contacts.FindSettingsAsync(currentUser.RequireUserId(), ct);
            var document = new ExportDocument(
                timeProvider.GetUtcNow(), exported, await tags.ListAsync(ct), await collections.ListAsync(ct), await reminders.ListAsync(false, ct),
                settings is null ? null : new ExportedSettings(settings.Timezone, settings.DigestFrequency, settings.DigestDay, settings.DigestChannels, settings.Email));

            var entry = zip.CreateEntry("export.json", CompressionLevel.Optimal);
            await using var entryStream = entry.Open();
            await JsonSerializer.SerializeAsync(entryStream, document, ExportDocument.JsonOptions, ct);
        }
    }

    /// <returns>Путь файла в архиве; <c>null</c> — у записи нет файла (или его нет в хранилище).</returns>
    private async Task<string?> AddDocumentFileAsync(ZipArchive zip, Item item, CancellationToken ct)
    {
        if (item.Type != ItemType.Document || item.BlobPath is not { } blobPath
            || await blobs.OpenReadAsync(BlobContainers.Documents, blobPath, ct) is not { } source)
        {
            return null;
        }

        await using (source)
        {
            var name = $"files/{item.Id}/{Path.GetFileName(blobPath)}";
            var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
            await using var target = entry.Open();
            await source.CopyToAsync(target, ct);
            return name;
        }
    }

    private static async Task WriteEntryAsync(ZipArchive zip, string name, string text, CancellationToken ct)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false));
        await writer.WriteAsync(text.AsMemory(), ct);
    }
}

/// <summary>Экспорт исчерпал попытки: запрос получает статус <c>failed</c> и тоже удаляется через неделю.</summary>
public sealed class ExportFailureHandler(AppDbContext db, TimeProvider timeProvider) : IJobFailureHandler
{
    public string JobType => BuildExportJob.JobType;

    public async Task OnFailedAsync(Job job, string error, CancellationToken ct)
    {
        if (JsonSerializer.Deserialize<BuildExportPayload>(job.Payload) is not { } payload)
        {
            return;
        }

        var expiresAt = timeProvider.GetUtcNow() + ExportService.Retention;
        await db.Exports.Where(e => e.Id == payload.ExportId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.Status, ExportStatus.Failed).SetProperty(e => e.ExpiresAt, expiresAt), ct);
    }
}
