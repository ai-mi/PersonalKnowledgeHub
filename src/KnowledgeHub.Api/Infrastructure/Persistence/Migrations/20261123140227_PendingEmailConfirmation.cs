using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgeHub.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PendingEmailConfirmation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "pending_email",
                table: "user_settings",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "pending_email_attempts",
                table: "user_settings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "pending_email_code_hash",
                table: "user_settings",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "pending_email_expires_at",
                table: "user_settings",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "pending_email",
                table: "user_settings");

            migrationBuilder.DropColumn(
                name: "pending_email_attempts",
                table: "user_settings");

            migrationBuilder.DropColumn(
                name: "pending_email_code_hash",
                table: "user_settings");

            migrationBuilder.DropColumn(
                name: "pending_email_expires_at",
                table: "user_settings");
        }
    }
}
