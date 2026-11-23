using System.Diagnostics;
using KnowledgeHub.Api.Modules.Bot;
using OpenTelemetry;
using OpenTelemetry.Trace;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace KnowledgeHub.IntegrationTests.Infrastructure;

/// <summary>
/// Telegram Bot API принимает токен в пути (<c>/bot&lt;token&gt;/sendMessage</c>), а трассировка HttpClient,
/// которую включает UseAzureMonitor, записывает полный URL исходящего запроса. Токен не должен уходить в экспорт.
/// </summary>
public sealed class TelegramTokenRedactionTests
{
    private const string Token = "123456789:AAFakeTokenForTests_abcdefghijklmnop";

    private sealed class CollectingExporter(List<Activity> exported) : BaseExporter<Activity>
    {
        public override ExportResult Export(in Batch<Activity> batch)
        {
            foreach (var activity in batch)
            {
                exported.Add(activity);
            }

            return ExportResult.Success;
        }
    }

    [Theory]
    [InlineData("/bot" + Token + "/sendMessage")]
    [InlineData("/file/bot" + Token + "/documents/file_1.pdf")]
    public async Task ExportedHttpClientSpans_DoNotContainTheBotToken(string path)
    {
        using var server = WireMockServer.Start();
        server.Given(Request.Create().UsingAnyMethod()).RespondWith(Response.Create().WithStatusCode(200).WithBody("{}"));
        var exported = new List<Activity>();

        // Порядок как в приложении: редактор добавлен раньше экспортёра (Program.cs — до UseAzureMonitor).
        using (Sdk.CreateTracerProviderBuilder()
                   .AddTelegramTokenRedaction()
                   .AddHttpClientInstrumentation()
                   .AddProcessor(new SimpleActivityExportProcessor(new CollectingExporter(exported)))
                   .Build())
        using (var client = new HttpClient())
        {
            (await client.GetAsync($"http://127.0.0.1:{server.Port}{path}", TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        }

        var span = exported.ShouldHaveSingleItem();
        span.DisplayName.ShouldNotContain(Token);
        span.TagObjects.ShouldAllBe(t => !(t.Value is string && ((string)t.Value).Contains(Token)));
        span.GetTagItem("url.full").ShouldBeOfType<string>().ShouldContain("/bot[REDACTED]/");
    }
}
