using Azure.Core;
using Azure.Identity;
using Npgsql;

namespace KnowledgeHub.Api.Infrastructure.Persistence;

public static class DataSourceFactory
{
    private const string EntraScope = "https://ossrdbms-aad.database.windows.net/.default";
    private static readonly TimeSpan TokenRefreshInterval = TimeSpan.FromMinutes(50);
    private static readonly TimeSpan TokenRetryInterval = TimeSpan.FromSeconds(10);

    public static NpgsqlDataSource Create(DatabaseOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            throw new InvalidOperationException("Database:ConnectionString is not configured.");
        }

        var builder = new NpgsqlDataSourceBuilder(options.ConnectionString);

        if (options.UseEntraAuth)
        {
            var credential = new DefaultAzureCredential();
            builder.UsePeriodicPasswordProvider(
                async (_, ct) => (await credential.GetTokenAsync(new TokenRequestContext([EntraScope]), ct)).Token,
                TokenRefreshInterval,
                TokenRetryInterval);
        }

        return builder.Build();
    }
}