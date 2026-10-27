using Microsoft.AspNetCore.Http.HttpResults;

namespace KnowledgeHub.Api.Modules.Reminders;

public static class ReminderEndpoints
{
    public static void MapReminderEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/reminders").RequireAuthorization();
        group.MapGet("", async (bool? upcoming, ReminderService reminders, CancellationToken ct) =>
            TypedResults.Ok(await reminders.ListAsync(upcoming ?? false, ct)));
        group.MapPost("", Create);
        group.MapDelete("/{id:guid}", Delete);

        routes.MapGet("/items/{id:guid}/reminders", ListForItem).RequireAuthorization();
    }

    private static async Task<Results<Created<ReminderResponse>, NotFound>> Create(
        CreateReminderRequest request, ReminderService reminders, CancellationToken ct) =>
        await reminders.CreateAsync(request, ct) is { } created
            ? TypedResults.Created($"/api/reminders/{created.Id}", created)
            : TypedResults.NotFound();

    private static async Task<Results<Ok<List<ReminderResponse>>, NotFound>> ListForItem(
        Guid id, ReminderService reminders, CancellationToken ct) =>
        await reminders.ListForItemAsync(id, ct) is { } list ? TypedResults.Ok(list) : TypedResults.NotFound();

    private static async Task<Results<NoContent, NotFound>> Delete(Guid id, ReminderService reminders, CancellationToken ct) =>
        await reminders.DeleteAsync(id, ct) ? TypedResults.NoContent() : TypedResults.NotFound();
}
