namespace KnowledgeHub.Api.Modules.Items;

public static class ItemsModule
{
    public static IServiceCollection AddItemsModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IItemService, ItemService>();
        return services;
    }

    public static IEndpointRouteBuilder MapItemsEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapItemEndpoints();
        return routes;
    }
}
