using System.Text.Json;

namespace KnowledgeHub.Api.Modules.Jobs;

public static class JobsModule
{
    public static IServiceCollection AddJobsModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<JobOptions>(configuration.GetSection(JobOptions.SectionName));
        services.AddScoped<JobQueue>();
        services.AddScoped<IJobQueue>(sp => sp.GetRequiredService<JobQueue>());

        services.AddSingleton<JobWorker>();
        services.AddHostedService(sp => sp.GetRequiredService<JobWorker>());
        services.AddSingleton<RecurringScheduler>();
        services.AddHostedService(sp => sp.GetRequiredService<RecurringScheduler>());

        services.AddRecurringTask<CompletedJobsCleanup>();
        return services;
    }

    /// <summary>Регистрирует обработчик задач типа <c>THandler.Type</c> с payload <typeparamref name="TPayload"/>.</summary>
    public static IServiceCollection AddJobHandler<THandler, TPayload>(this IServiceCollection services)
        where THandler : class, IJobHandler<TPayload>
    {
        services.AddScoped<THandler>();
        services.AddSingleton(new JobHandlerRegistration(THandler.Type, (sp, json, ct) =>
        {
            var payload = JsonSerializer.Deserialize<TPayload>(json)
                ?? throw new InvalidOperationException($"Пустой payload задачи «{THandler.Type}».");
            return sp.GetRequiredService<THandler>().HandleAsync(payload, ct);
        }));
        return services;
    }

    public static IServiceCollection AddJobFailureHandler<THandler>(this IServiceCollection services)
        where THandler : class, IJobFailureHandler =>
        services.AddScoped<IJobFailureHandler, THandler>();

    public static IServiceCollection AddRecurringTask<TTask>(this IServiceCollection services)
        where TTask : class, IRecurringTask =>
        services.AddScoped<IRecurringTask, TTask>();
}
