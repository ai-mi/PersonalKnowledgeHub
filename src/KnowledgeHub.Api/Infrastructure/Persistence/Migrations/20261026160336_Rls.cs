using Microsoft.EntityFrameworkCore.Migrations;

using KnowledgeHub.Api.Infrastructure.Persistence;

#nullable disable

namespace KnowledgeHub.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Rls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnableUserRls("user_settings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DisableUserRls("user_settings");
        }
    }
}
