using System.Buffers;
using System.IO.Compression;

namespace KnowledgeHub.Api.Modules.Ingestion;

/// <summary>
/// Защита от zip-бомб для DOCX: файл в несколько мегабайт может распаковываться в гигабайты, а OpenXML читает части
/// пакета целиком в память. Проверка распаковывает записи потоково и считает настоящие байты — размеры из заголовков
/// zip подделываются, — поэтому памяти нужно только на буфер. Вызывать до открытия пакета: бомбой может быть любая
/// часть, включая <c>[Content_Types].xml</c>, который читается уже при открытии.
/// </summary>
public static class ZipLimits
{
    public const long MaxUncompressedBytes = 50 * 1024 * 1024;
    public const int MaxCompressionRatio = 100;

    /// <summary>
    /// <c>true</c>, если весь архив распаковывается не больше чем в <paramref name="maxUncompressedBytes"/> и не больше чем
    /// в <paramref name="maxCompressionRatio"/> раз от размера файла. Битый архив — <c>false</c>. Позиция потока сбрасывается в 0.
    /// </summary>
    public static bool IsWithinLimits(Stream stream, long maxUncompressedBytes = MaxUncompressedBytes, int maxCompressionRatio = MaxCompressionRatio)
    {
        var limit = Math.Min(maxUncompressedBytes, stream.Length * maxCompressionRatio);
        var buffer = ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            stream.Position = 0;
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            long total = 0;
            foreach (var entry in zip.Entries)
            {
                using var content = entry.Open();
                int read;
                while ((read = content.Read(buffer, 0, buffer.Length)) > 0)
                {
                    total += read;
                    if (total > limit)
                    {
                        return false;
                    }
                }
            }

            return true;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Битый или чужой архив: System.IO.Compression бросает разные типы исключений.
            return false;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            stream.Position = 0;
        }
    }
}
