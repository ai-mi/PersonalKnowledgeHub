using Microsoft.EntityFrameworkCore.Migrations;

namespace KnowledgeHub.Api.Infrastructure.Persistence;

public static class RlsMigrationExtensions
{
    // Пользователь берётся из app.user_id (его ставит UserContextConnectionInterceptor); пустая строка — «нет пользователя».
    private const string Predicate =
        "user_id = nullif(current_setting('app.user_id', true), '')::uuid OR current_setting('app.is_system', true) = 'on'";

    /// <summary>
    /// Включает для таблицы с колонкой <c>user_id</c> Row-Level Security. FORCE нужен, иначе владелец таблицы обходит политику.
    /// Вызывать в миграции каждой новой пользовательской таблицы.
    /// </summary>
    public static void EnableUserRls(this MigrationBuilder migrationBuilder, string table) =>
        migrationBuilder.Sql($"""
            ALTER TABLE "{table}" ENABLE ROW LEVEL SECURITY;
            ALTER TABLE "{table}" FORCE ROW LEVEL SECURITY;
            CREATE POLICY "{PolicyName(table)}" ON "{table}" USING ({Predicate}) WITH CHECK ({Predicate});
            """);

    public static void DisableUserRls(this MigrationBuilder migrationBuilder, string table) =>
        migrationBuilder.Sql($"""
            DROP POLICY "{PolicyName(table)}" ON "{table}";
            ALTER TABLE "{table}" NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE "{table}" DISABLE ROW LEVEL SECURITY;
            """);

    private static string PolicyName(string table) => $"{table}_user_isolation";
}
