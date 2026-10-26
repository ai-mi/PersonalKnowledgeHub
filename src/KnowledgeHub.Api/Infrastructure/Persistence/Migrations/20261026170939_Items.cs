using System;
using Microsoft.EntityFrameworkCore.Migrations;
using KnowledgeHub.Api.Infrastructure.Persistence;

#nullable disable

namespace KnowledgeHub.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Items : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    body = table.Column<string>(type: "text", nullable: true),
                    user_note = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: true),
                    code_language = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    blob_path = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_items_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_items_user_id_content_hash",
                table: "items",
                columns: new[] { "user_id", "content_hash" },
                unique: true,
                filter: "content_hash IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_items_user_id_created_at",
                table: "items",
                columns: new[] { "user_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_items_user_id_url",
                table: "items",
                columns: new[] { "user_id", "url" },
                unique: true,
                filter: "url IS NOT NULL");

            migrationBuilder.EnableUserRls("items");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DisableUserRls("items");

            migrationBuilder.DropTable(
                name: "items");
        }
    }
}
