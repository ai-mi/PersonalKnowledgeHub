using System;
using Microsoft.EntityFrameworkCore.Migrations;
using KnowledgeHub.Api.Infrastructure.Persistence;

#nullable disable

namespace KnowledgeHub.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Export : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "exports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    blob_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exports", x => x.id);
                    table.ForeignKey(
                        name: "fk_exports_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_exports_expires_at",
                table: "exports",
                column: "expires_at",
                filter: "expires_at IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_exports_user_id_active",
                table: "exports",
                column: "user_id",
                unique: true,
                filter: "status IN ('pending', 'processing')");

            migrationBuilder.EnableUserRls("exports");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DisableUserRls("exports");

            migrationBuilder.DropTable(
                name: "exports");
        }
    }
}
