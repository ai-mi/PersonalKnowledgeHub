using System.Diagnostics;
using System.Text.RegularExpressions;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace KnowledgeHub.Api.Modules.Bot;

/// <summary>
/// Вырезает токен бота из спанов исходящих запросов: Telegram Bot API принимает его в пути
/// (<c>/bot&lt;token&gt;/method</c>, <c>/file/bot&lt;token&gt;/path</c>), а трассировка HttpClient пишет полный URL.
/// Работает в <see cref="OnEnd"/>, поэтому должен стоять раньше экспортёра (в Program.cs — до UseAzureMonitor).
/// </summary>
public sealed partial class TelegramTokenRedactionProcessor : BaseProcessor<Activity>
{
    private const string Redacted = "/bot[REDACTED]/";

    public override void OnEnd(Activity data)
    {
        if (data.DisplayName.Contains("/bot", StringComparison.Ordinal))
        {
            data.DisplayName = Redact(data.DisplayName);
        }

        List<KeyValuePair<string, string>>? replacements = null;
        foreach (var tag in data.TagObjects)
        {
            if (tag.Value is string value && value.Contains("/bot", StringComparison.Ordinal) && TokenInPath().IsMatch(value))
            {
                (replacements ??= []).Add(new(tag.Key, Redact(value)));
            }
        }

        // Менять теги во время перечисления нельзя.
        foreach (var (key, value) in replacements ?? [])
        {
            data.SetTag(key, value);
        }
    }

    public static string Redact(string value) => TokenInPath().Replace(value, Redacted);

    // Токен бота: «<id бота>:<секрет>», секрет — base64url.
    [GeneratedRegex(@"/bot\d+:[A-Za-z0-9_-]+/")]
    private static partial Regex TokenInPath();
}

public static class TelegramTokenRedactionExtensions
{
    public static TracerProviderBuilder AddTelegramTokenRedaction(this TracerProviderBuilder builder) =>
        builder.AddProcessor(new TelegramTokenRedactionProcessor());
}
