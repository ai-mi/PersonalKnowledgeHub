using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace KnowledgeHub.Api.Infrastructure.Persistence;

public static class PersistenceExtensions
{
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.SectionName));

        // Настройки читаются лениво (при первом запросе источника данных), чтобы их можно было переопределить в тестах.
        services.AddSingleton(sp =>
            DataSourceFactory.Create(sp.GetRequiredService<IOptions<DatabaseOptions>>().Value));

        services.AddDbContext<AppDbContext>((sp, o) => o
            .UseNpgsql(sp.GetRequiredService<NpgsqlDataSource>())
            .UseSnakeCaseNamingConvention());

        services.AddHostedService<DatabaseRoleGuard>();
        services.AddHealthChecks().AddDbContextCheck<AppDbContext>();

        return services;
    }
}