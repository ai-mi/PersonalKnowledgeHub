using KnowledgeHub.Api.Infrastructure.Email;
using KnowledgeHub.Api.Modules.Jobs;

namespace KnowledgeHub.Api.Modules.Reminders;

public static class RemindersModule
{
    public static IServiceCollection AddRemindersModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.SectionName));
        if (configuration.GetSection(EmailOptions.SectionName).GetValue<bool>(nameof(EmailOptions.LogOnly)))
        {
            services.AddSingleton<IEmailSender, LoggingEmailSender>();
        }
        else
        {
            services.AddSingleton<IEmailSender, AcsEmailSender>();
        }
        services.AddScoped<INotificationDispatcher, NotificationDispatcher>();
        services.AddScoped<ReminderService>();
        services.AddJobHandler<SendReminderJob, SendReminderPayload>();
        services.AddRecurringTask<DueRemindersTask>();
        services.AddScoped<DigestBuilder>();
        services.AddJobHandler<SendDigestJob, SendDigestPayload>();
        services.AddRecurringTask<DigestTask>();
        return services;
    }

    public static IEndpointRouteBuilder MapRemindersEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapReminderEndpoints();
        return routes;
    }
}
