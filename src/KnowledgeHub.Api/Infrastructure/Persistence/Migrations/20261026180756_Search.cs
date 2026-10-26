using Microsoft.EntityFrameworkCore.Migrations;
using KnowledgeHub.Api.Modules.Search;
using NpgsqlTypes;

#nullable disable

namespace KnowledgeHub.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Search : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "search_vector",
                table: "items",
                type: "tsvector",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_items_search_vector",
                table: "items",
                column: "search_vector")
                .Annotation("Npgsql:IndexMethod", "GIN");

            // Пересчёт существующих записей. FORCE RLS действует и на владельца таблицы, поэтому включаем системный режим
            // на время этой команды (true = только в текущей транзакции).
            migrationBuilder.Sql($"SELECT set_config('app.is_system', 'on', true); {SearchSql.ReindexAll};");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_items_search_vector",
                table: "items");

            migrationBuilder.DropColumn(
                name: "search_vector",
                table: "items");
        }
    }
}
