using System.Net.Mail;
using KnowledgeHub.Api.Common;
using KnowledgeHub.Api.Infrastructure.Persistence;
using KnowledgeHub.Api.Infrastructure.Web;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeHub.Api.Modules.Identity;

public static class MeEndpoints
{
    public static void MapMeEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/me").RequireAuthorization();
        group.MapGet("", GetMe);
        group.MapPut("/settings", UpdateSettings);

        // Смена адреса писем — только из сессии в браузере: утёкший API-токен не должен перенаправлять дайджесты.
        var email = routes.MapGroup("/me/email").RequireAuthorization(AuthPolicies.Session);
        email.MapPost("", RequestEmailChange).RequireRateLimiting(RateLimitPolicies.EmailCode);
        email.MapPost("/confirm", ConfirmEmailChange);
    }

    public sealed record EmailChangeRequest(string? Email);

    public sealed record EmailConfirmRequest(string? Code);

    private static async Task<Ok<SettingsDto>> RequestEmailChange(
        EmailChangeRequest request, EmailConfirmationService confirmation, CancellationToken ct) =>
        TypedResults.Ok(SettingsDto.From(await confirmation.RequestAsync(request.Email, ct)));

    private static async Task<Ok<SettingsDto>> ConfirmEmailChange(
        EmailConfirmRequest request, EmailConfirmationService confirmation, CancellationToken ct) =>
        TypedResults.Ok(SettingsDto.From(await confirmation.ConfirmAsync(request.Code, ct)));

    private static async Task<Results<Ok<MeResponse>, UnauthorizedHttpResult>> GetMe(ICurrentUser currentUser, AppDbContext db, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        var settings = await db.UserSettings.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == userId, ct);
        return user is null || settings is null
            ? TypedResults.Unauthorized()
            : TypedResults.Ok(new MeResponse(user.Id, user.Email, user.DisplayName, SettingsDto.From(settings)));
    }

    private static async Task<Ok<SettingsDto>> UpdateSettings(
        SettingsDto request, ICurrentUser currentUser, AppDbContext db, CancellationToken ct)
    {
        var errors = Validate(request);
        if (errors.Count > 0)
        {
            throw Errors.Validation(errors);
        }

        var userId = currentUser.RequireUserId();
        var settings = await db.UserSettings.FirstOrDefaultAsync(s => s.UserId == userId, ct)
            ?? throw Errors.NotFound();
        var profileEmail = await db.Users.Where(u => u.Id == userId).Select(u => u.Email).FirstOrDefaultAsync(ct);
        if (!EmailConfirmationService.NeedsNoConfirmation(request.Email, profileEmail, settings.Email))
        {
            throw Errors.Validation("email", "A new e-mail must be confirmed with a code: POST /api/me/email.");
        }

        settings.Timezone = request.Timezone;
        settings.DigestFrequency = request.DigestFrequency;
        settings.DigestDay = request.DigestDay;
        settings.DigestChannels = request.DigestChannels.Distinct().ToArray();
        settings.Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(SettingsDto.From(settings));
    }

    private static Dictionary<string, string[]> Validate(SettingsDto request)
    {
        var errors = new Dictionary<string, string[]>();

        if (!IsKnownTimeZone(request.Timezone))
        {
            errors["timezone"] = ["Unknown IANA time zone."];
        }

        if (request.DigestChannels is null || request.DigestChannels.Any(c => c is null || !UserSettings.AllowedDigestChannels.Contains(c)))
        {
            errors["digestChannels"] = [$"Allowed channels: {string.Join(", ", UserSettings.AllowedDigestChannels.Order())}."];
        }

        if (!string.IsNullOrWhiteSpace(request.Email)
            && (request.Email.Length > User.MaxEmailLength || !MailAddress.TryCreate(request.Email.Trim(), out _)))
        {
            errors["email"] = ["Invalid e-mail."];
        }

        return errors;
    }

    private static bool IsKnownTimeZone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return false;
        }
    }

    public sealed record MeResponse(Guid Id, string? Email, string DisplayName, SettingsDto Settings);

    public sealed record SettingsDto
    {
        public required string Timezone { get; init; }
        public required DigestFrequency DigestFrequency { get; init; }
        public required DayOfWeek DigestDay { get; init; }
        public required string[] DigestChannels { get; init; }
        public string? Email { get; init; }

        /// <summary>Адрес, ждущий подтверждения кодом; только в ответах (в <c>PUT /me/settings</c> игнорируется).</summary>
        public string? PendingEmail { get; init; }

        public static SettingsDto From(UserSettings s) => new()
        {
            Timezone = s.Timezone,
            DigestFrequency = s.DigestFrequency,
            DigestDay = s.DigestDay,
            DigestChannels = s.DigestChannels,
            Email = s.Email,
            PendingEmail = s.PendingEmail,
        };
    }
}
