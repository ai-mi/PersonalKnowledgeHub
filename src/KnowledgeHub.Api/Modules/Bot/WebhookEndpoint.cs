using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KnowledgeHub.Api.Modules.Jobs;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace KnowledgeHub.Api.Modules.Bot;

public static class WebhookEndpoint
{
    public const string Path = "/telegram/webhook";
    public const string SecretHeader = "X-Telegram-Bot-Api-Secret-Token";

    private const long MaxBodyBytes = 1024 * 1024;

    /// <summary>
    /// Вне группы <c>/api</c>: без cookie-аутентификации и antiforgery — подлинность запроса подтверждает секретный заголовок.
    /// </summary>
    public static void MapWebhookEndpoint(this IEndpointRouteBuilder routes) =>
        routes.MapPost(Path, Receive).AllowAnonymous().WithMetadata(new RequestSizeLimitAttribute(MaxBodyBytes));

    private static async Task<Results<Ok, UnauthorizedHttpResult, BadRequest>> Receive(
        HttpRequest request, IOptions<BotOptions> options, IJobQueue jobs, CancellationToken ct)
    {
        if (!IsSecretValid(request.Headers[SecretHeader].ToString(), options.Value.WebhookSecret))
        {
            return TypedResults.Unauthorized();
        }

        // Исходный JSON кладём в задачу как есть: повторная сериализация потеряла бы поля, которых нет в наших типах.
        using var reader = new StreamReader(request.Body, Encoding.UTF8);
        var json = await reader.ReadToEndAsync(ct);

        Update? update;
        try
        {
            update = JsonSerializer.Deserialize<Update>(json, JsonBotAPI.Options);
        }
        catch (JsonException)
        {
            return TypedResults.BadRequest();
        }

        if (update is null)
        {
            return TypedResults.BadRequest();
        }

        // Повторная доставка того же update_id игнорируется ключом дедупликации.
        await jobs.EnqueueAsync(ProcessUpdateJob.JobType, new TelegramUpdatePayload(json), dedupKey: $"tg:{update.Id}", ct: ct);
        return TypedResults.Ok();
    }

    // Пустой секрет в настройках — webhook не настроен, принимать нечего.
    private static bool IsSecretValid(string provided, string expected) =>
        expected.Length > 0 && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(provided), Encoding.UTF8.GetBytes(expected));
}
