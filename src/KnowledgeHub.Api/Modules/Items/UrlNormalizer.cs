using System.Diagnostics.CodeAnalysis;

namespace KnowledgeHub.Api.Modules.Items;

/// <summary>Приводит адрес к каноническому виду, чтобы одну и ту же ссылку нельзя было сохранить дважды.</summary>
public static class UrlNormalizer
{
    /// <summary>
    /// Только <c>http/https</c>; хост в нижнем регистре, без учётных данных, фрагмента (<c>#...</c>), параметров <c>utm_*</c>
    /// и завершающего <c>/</c> у пути.
    /// </summary>
    public static bool TryNormalize(string? input, [NotNullWhen(true)] out string? normalized)
    {
        normalized = null;
        if (!Uri.TryCreate(input?.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || uri.Host.Length == 0)
        {
            return false;
        }

        var port = uri.IsDefaultPort ? "" : $":{uri.Port}";
        var path = uri.AbsolutePath.TrimEnd('/');
        var query = string.Join('&', uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => !p.StartsWith("utm_", StringComparison.OrdinalIgnoreCase)));

        normalized = $"{uri.Scheme}://{uri.Host}{port}{path}{(query.Length > 0 ? "?" + query : "")}";
        return true;
    }
}
