using KnowledgeHub.Api.Common;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace KnowledgeHub.Api.Modules.Bot;

public static class LinkEndpoints
{
    public static void MapLinkEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/me/telegram").RequireAuthorization();
        group.MapGet("", GetStatus);
        group.MapPost("/link", CreateLink);
        group.MapDelete("/link", Unlink);
    }

    private static async Task<Ok<TelegramStatusResponse>> GetStatus(TelegramLinkService links, CancellationToken ct)
    {
        var link = await links.GetAsync(ct);
        return TypedResults.Ok(new TelegramStatusResponse(link is not null, link?.LinkedAt));
    }

    private static async Task<Ok<LinkCodeResponse>> CreateLink(TelegramLinkService links, IOptions<BotOptions> options, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(options.Value.Username))
        {
            throw new AppException(StatusCodes.Status503ServiceUnavailable, "Service Unavailable", "The Telegram bot is not configured.");
        }

        var (code, expiresAt) = await links.CreateCodeAsync(ct);
        return TypedResults.Ok(new LinkCodeResponse($"https://t.me/{options.Value.Username}?start={code}", expiresAt));
    }

    private static async Task<NoContent> Unlink(TelegramLinkService links, CancellationToken ct)
    {
        await links.UnlinkAsync(ct);
        return TypedResults.NoContent();
    }

    public sealed record TelegramStatusResponse(bool Linked, DateTimeOffset? LinkedAt);

    public sealed record LinkCodeResponse(string Url, DateTimeOffset ExpiresAt);
}
