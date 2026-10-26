using KnowledgeHub.Api.Infrastructure.Persistence;

namespace KnowledgeHub.UnitTests.Persistence;

public class DataSourceFactoryTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingConnectionString_Throws(string connectionString)
    {
        var options = new DatabaseOptions { ConnectionString = connectionString };

        Should.Throw<InvalidOperationException>(() => DataSourceFactory.Create(options))
            .Message.ShouldContain("Database:ConnectionString");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ValidConnectionString_BuildsDataSourceWithoutConnecting(bool useEntraAuth)
    {
        var options = new DatabaseOptions
        {
            ConnectionString = "Host=localhost;Database=knowledgehub;Username=kh_app",
            UseEntraAuth = useEntraAuth,
        };

        using var dataSource = DataSourceFactory.Create(options);

        dataSource.ConnectionString.ShouldContain("knowledgehub");
    }
}