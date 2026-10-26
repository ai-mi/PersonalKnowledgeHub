namespace KnowledgeHub.Api.Modules.Identity;

public static class ReturnUrl
{
    /// <summary>Пропускает только локальные пути (<c>/items</c>); всё остальное (в т.ч. <c>//evil.com</c>, <c>https://…</c>) заменяет на <c>/</c>.</summary>
    public static string Sanitize(string? returnUrl)
    {
        if (string.IsNullOrEmpty(returnUrl) || returnUrl[0] != '/')
        {
            return "/";
        }

        if (returnUrl.Length > 1 && returnUrl[1] == '/')
        {
            return "/";
        }

        return returnUrl.Any(c => c == '\\' || char.IsControl(c)) ? "/" : returnUrl;
    }
}
