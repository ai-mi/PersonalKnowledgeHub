using System;
using Microsoft.EntityFrameworkCore.Migrations;
using KnowledgeHub.Api.Infrastructure.Persistence;

#nullable disable

namespace KnowledgeHub.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Telegram : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "telegram_link_codes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_telegram_link_codes", x => x.id);
                    table.ForeignKey(
                        name: "fk_telegram_link_codes_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "telegram_links",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    chat_id = table.Column<long>(type: "bigint", nullable: false),
                    linked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_telegram_links", x => x.user_id);
                    table.ForeignKey(
                        name: "fk_telegram_links_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_telegram_link_codes_code_hash",
                table: "telegram_link_codes",
                column: "code_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_telegram_link_codes_user_id",
                table: "telegram_link_codes",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_telegram_links_chat_id",
                table: "telegram_links",
                column: "chat_id",
                unique: true);

            migrationBuilder.EnableUserRls("telegram_links");
            migrationBuilder.EnableUserRls("telegram_link_codes");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DisableUserRls("telegram_link_codes");
            migrationBuilder.DisableUserRls("telegram_links");

            migrationBuilder.DropTable(
                name: "telegram_link_codes");

            migrationBuilder.DropTable(
                name: "telegram_links");
        }
    }
}
