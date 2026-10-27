using KnowledgeHub.Api.Modules.Jobs;

namespace KnowledgeHub.Api.Modules.Export;

public static class ExportModule
{
    public static IServiceCollection AddExportModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ExportService>();
        services.AddJobHandler<BuildExportJob, BuildExportPayload>();
        services.AddJobFailureHandler<ExportFailureHandler>();
        services.AddRecurringTask<ExpiredExportsCleanupTask>();
        return services;
    }

    public static IEndpointRouteBuilder MapExportEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapExports();
        return routes;
    }
}
