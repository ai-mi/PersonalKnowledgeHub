namespace KnowledgeHub.Api.Modules.Identity;

public static class AuthSchemes
{
    /// <summary>Основная cookie-сессия.</summary>
    public const string Cookies = "Cookies";

    /// <summary>Временная cookie между ответом внешнего провайдера и входом в <see cref="Cookies"/>.</summary>
    public const string External = "External";

    /// <summary>Policy scheme по умолчанию: выбирает <see cref="ApiToken"/> или <see cref="Cookies"/> по заголовку.</summary>
    public const string Smart = "Smart";

    /// <summary>Персональные API-токены.</summary>
    public const string ApiToken = "ApiToken";

    public const string Microsoft = "Microsoft";
    public const string Google = "Google";

    /// <summary>Claim с идентификатором пользователя приложения.</summary>
    public const string UserIdClaim = "kh_user_id";
}
