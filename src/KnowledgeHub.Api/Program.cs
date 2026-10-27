using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using KnowledgeHub.Api.Infrastructure.Persistence;
using KnowledgeHub.Api.Infrastructure.Storage;
using KnowledgeHub.Api.Infrastructure.Web;
using KnowledgeHub.Api.Modules.Bot;
using KnowledgeHub.Api.Modules.Export;
using KnowledgeHub.Api.Modules.Identity;
using KnowledgeHub.Api.Modules.Ingestion;
using KnowledgeHub.Api.Modules.Items;
using KnowledgeHub.Api.Modules.Jobs;
using KnowledgeHub.Api.Modules.Reminders;
using KnowledgeHub.Api.Modules.Search;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetailsHandling();
builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddBlobStorage(builder.Configuration);
builder.Services.AddSingleton(TimeProvider.System);
// Перечисления в API — строки в camelCase ("weekly", "monday").
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));
builder.Services.AddIdentityModule(builder.Configuration);
builder.Services.AddItemsModule(builder.Configuration);
builder.Services.AddSearchModule(builder.Configuration);
builder.Services.AddIngestionModule(builder.Configuration);
builder.Services.AddBotModule(builder.Configuration);
builder.Services.AddRemindersModule(builder.Configuration);
builder.Services.AddExportModule(builder.Configuration);
// После AddPersistence: DatabaseRoleGuard (проверка роли и миграции) стартует раньше воркеров.
builder.Services.AddJobsModule(builder.Configuration);

if (!string.IsNullOrWhiteSpace(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
{
    builder.Services.AddOpenTelemetry().UseAzureMonitor();
}

var app = builder.Build();

app.UseProblemDetailsHandling();
app.UseDefaultFiles();
app.UseStaticFiles();
// UseRouting после статики: иначе fallback-endpoint перехватывает запросы к StaticFileMiddleware раньше времени.
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseXsrfCookie();

app.MapHealthChecks("/health");
app.MapOpenApi();

var api = app.MapGroup("/api").AddEndpointFilter<AntiforgeryFilter>();
api.MapIdentityEndpoints();
api.MapItemsEndpoints();
api.MapSearchEndpoints();
api.MapBotEndpoints();
api.MapRemindersEndpoints();
api.MapExportEndpoints();
// Webhook вне /api: без cookie-аутентификации и antiforgery, запрос подтверждает секретный заголовок.
app.MapWebhookEndpoint();

// SPA: неизвестные пути отдают index.html, но не /api/* — там нужен 404 ProblemDetails.
app.MapFallbackToFile("{**path:regex(^(?!api(/|$)).*)}", "index.html");

if (app.Environment.IsDevelopment())
{
    app.UseSwaggerUI(o => o.SwaggerEndpoint("/openapi/v1.json", "KnowledgeHub"));
}

app.Run();

public partial class Program;
