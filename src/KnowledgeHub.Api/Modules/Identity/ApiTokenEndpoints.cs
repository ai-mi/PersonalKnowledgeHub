using KnowledgeHub.Api.Common;
using Microsoft.AspNetCore.Http.HttpResults;

namespace KnowledgeHub.Api.Modules.Identity;

public static class ApiTokenEndpoints
{
    public static void MapApiTokenEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/me/tokens").RequireAuthorization();
        group.MapPost("", Create);
        group.MapGet("", List);
        group.MapDelete("/{id:guid}", Revoke);
    }

    private static async Task<Ok<CreatedTokenResponse>> Create(
        CreateTokenRequest request, ApiTokenService tokens, CancellationToken ct)
    {
        var name = request.Name?.Trim();
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrEmpty(name) || name.Length > ApiToken.MaxNameLength)
        {
            errors["name"] = [$"Name is required and must be at most {ApiToken.MaxNameLength} characters."];
        }

        if (request.ExpiresInDays is < 1 or > ApiToken.MaxExpiresInDays)
        {
            errors["expiresInDays"] = [$"Must be between 1 and {ApiToken.MaxExpiresInDays}."];
        }

        if (errors.Count > 0)
        {
            throw Errors.Validation(errors);
        }

        var (entity, token) = await tokens.CreateAsync(name!, request.ExpiresInDays, ct);
        return TypedResults.Ok(new CreatedTokenResponse(entity.Id, entity.Name, token));
    }

    private static async Task<Ok<List<TokenResponse>>> List(ApiTokenService tokens, CancellationToken ct) =>
        TypedResults.Ok((await tokens.ListAsync(ct)).Select(TokenResponse.From).ToList());

    private static async Task<Results<NoContent, NotFound>> Revoke(Guid id, ApiTokenService tokens, CancellationToken ct) =>
        await tokens.RevokeAsync(id, ct) ? TypedResults.NoContent() : TypedResults.NotFound();

    public sealed record CreateTokenRequest(string? Name, int? ExpiresInDays);

    public sealed record CreatedTokenResponse(Guid Id, string Name, string Token);

    public sealed record TokenResponse(
        Guid Id, string Name, DateTimeOffset CreatedAt, DateTimeOffset? ExpiresAt, DateTimeOffset? RevokedAt, DateTimeOffset? LastUsedAt)
    {
        public static TokenResponse From(ApiToken t) => new(t.Id, t.Name, t.CreatedAt, t.ExpiresAt, t.RevokedAt, t.LastUsedAt);
    }
}
