using KnowledgeHub.Api.Infrastructure.Email;
using KnowledgeHub.Api.Modules.Jobs;

namespace KnowledgeHub.Api.Modules.Reminders;

public static class RemindersModule
{
    public static IServiceCollection AddRemindersModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.SectionName));
        services.AddSingleton<IEmailSender, AcsEmailSender>();
        services.AddScoped<INotificationDispatcher, NotificationDispatcher>();
        services.AddScoped<ReminderService>();
        services.AddJobHandler<SendReminderJob, SendReminderPayload>();
        services.AddRecurringTask<DueRemindersTask>();
        return services;
    }

    public static IEndpointRouteBuilder MapRemindersEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapReminderEndpoints();
        return routes;
    }
}
