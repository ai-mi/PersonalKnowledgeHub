namespace KnowledgeHub.Api.Infrastructure.Persistence;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public string ConnectionString { get; set; } = "";
    public bool UseEntraAuth { get; set; }
    public bool MigrateOnStartup { get; set; }
}