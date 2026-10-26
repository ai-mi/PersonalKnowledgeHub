namespace KnowledgeHub.Api.Modules.Search;

public static class SearchModule
{
    public static IServiceCollection AddSearchModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ISearchIndexer, SearchIndexer>();
        services.AddScoped<ISearchService, SearchService>();
        return services;
    }
}
