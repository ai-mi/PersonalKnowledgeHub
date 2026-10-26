using KnowledgeHub.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace KnowledgeHub.UnitTests.Persistence;

public sealed class RlsMigrationExtensionsTests
{
    private static string Sql(Action<MigrationBuilder> build)
    {
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        build(builder);
        return builder.Operations.OfType<SqlOperation>().Single().Sql;
    }

    [Fact]
    public void Enable_TurnsOnForcedRowLevelSecurityWithPolicy()
    {
        var sql = Sql(b => b.EnableUserRls("items"));

        sql.ShouldContain("ENABLE ROW LEVEL SECURITY");
        sql.ShouldContain("FORCE ROW LEVEL SECURITY");
        sql.ShouldContain("CREATE POLICY \"items_user_isolation\" ON \"items\"");
    }

    [Fact]
    public void Disable_DropsPolicyAndTurnsOffSecurity()
    {
        var sql = Sql(b => b.DisableUserRls("items"));

        sql.ShouldContain("DROP POLICY \"items_user_isolation\" ON \"items\"");
        sql.ShouldContain("NO FORCE ROW LEVEL SECURITY");
        sql.ShouldContain("DISABLE ROW LEVEL SECURITY");
    }
}
