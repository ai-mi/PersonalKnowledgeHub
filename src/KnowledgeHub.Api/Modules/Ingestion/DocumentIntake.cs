using System.Security.Cryptography;
using KnowledgeHub.Api.Common;
using KnowledgeHub.Api.Infrastructure.Storage;
using KnowledgeHub.Api.Modules.Items;
using Microsoft.Extensions.Options;

namespace KnowledgeHub.Api.Modules.Ingestion;

public sealed class DocumentIntake(
    IItemService items, IBlobStore blobs, ICurrentUser currentUser, IOptions<IngestionOptions> options) : IDocumentIntake
{
    private const int MaxFileNameLength = 100;

    public async Task<CreateItemResult> AcceptAsync(Stream content, string fileName, DocumentMetadata metadata, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();

        // Файл копируется на диск: так считается хэш, проверяется содержимое (DOCX открывается, текст декодируется)
        // и блоб загружается из перематываемого потока, не удерживая до 20 МБ в памяти.
        await using var file = new FileStream(
            Path.Combine(Path.GetTempPath(), $"kh-{Guid.NewGuid():N}.tmp"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
            bufferSize: 81920, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        var hash = await CopyAndHashAsync(content, file, options.Value.MaxDocumentBytes, ct);

        var kind = FileSignature.Detect(file, fileName)
            ?? throw Errors.Validation("file", "Unsupported file type. Allowed: PDF, DOCX, TXT, MD.");

        if (await items.FindByContentHashAsync(hash, ct) is { } existing)
        {
            return new CreateItemResult(existing, false);
        }

        var storedName = StoredName(fileName, kind);
        var id = Guid.CreateVersion7();
        var blobPath = $"{userId}/{id}/{storedName}";

        file.Position = 0;
        await blobs.UploadAsync(BlobContainers.Documents, blobPath, file, FileSignature.ContentTypeFor(storedName), ct);
        try
        {
            var title = string.IsNullOrWhiteSpace(metadata.Title) ? Path.GetFileNameWithoutExtension(storedName) : metadata.Title;
            var result = await items.CreateDocumentAsync(
                new CreateDocumentCommand(id, blobPath, hash, title, metadata.UserNote, metadata.Tags, metadata.CollectionIds), ct);
            if (!result.Created)
            {
                // Параллельная загрузка того же файла успела раньше: наш блоб больше не нужен.
                await DeleteQuietlyAsync(blobPath);
            }

            return result;
        }
        catch
        {
            await DeleteQuietlyAsync(blobPath);
            throw;
        }
    }

    // Откат не должен прерываться отменой запроса и не должен скрывать исходную ошибку.
    private async Task DeleteQuietlyAsync(string blobPath)
    {
        try
        {
            await blobs.DeleteAsync(BlobContainers.Documents, blobPath, CancellationToken.None);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Осиротевший блоб безвреден: пользователю недоступен.
        }
    }

    /// <summary>Имя файла для хранилища: без пути и управляющих символов; расширение PDF/DOCX приводится к фактическому типу.</summary>
    public static string StoredName(string fileName, DocumentKind kind)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var name = new string(Path.GetFileName(fileName.Replace('\\', '/')).Where(c => !char.IsControl(c) && !invalid.Contains(c)).ToArray()).Trim();
        if (FileSignature.CanonicalExtension(kind) is { } extension)
        {
            name = Path.ChangeExtension(name, extension);
        }

        var stem = Path.GetFileNameWithoutExtension(name).Trim();
        if (stem.Length == 0)
        {
            stem = "file";
        }

        var ext = Path.GetExtension(name);
        return stem[..Math.Min(stem.Length, MaxFileNameLength)] + ext;
    }

    private static async Task<string> CopyAndHashAsync(Stream source, Stream target, long maxBytes, CancellationToken ct)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            total += read;
            if (total > maxBytes)
            {
                throw Errors.PayloadTooLarge($"The file must be at most {maxBytes / (1024 * 1024)} MB.");
            }

            hash.AppendData(buffer, 0, read);
            await target.WriteAsync(buffer.AsMemory(0, read), ct);
        }

        await target.FlushAsync(ct);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
