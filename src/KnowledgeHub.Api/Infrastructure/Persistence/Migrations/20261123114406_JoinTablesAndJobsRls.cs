using Microsoft.EntityFrameworkCore.Migrations;

using KnowledgeHub.Api.Infrastructure.Persistence;

#nullable disable

namespace KnowledgeHub.Api.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// RLS на таблицах, которые раньше защищал только код: связи item_tags и collection_items и очередь jobs
    /// (её payload хранит исходные сообщения Telegram всех пользователей).
    /// </summary>
    public partial class JoinTablesAndJobsRls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnableLinkRls("item_tags", ("item_id", "items"), ("tag_id", "tags"));
            migrationBuilder.EnableLinkRls("collection_items", ("collection_id", "collections"), ("item_id", "items"));
            // Задачу видит и ставит её пользователь или система; задачу без пользователя — только система
            // (webhook Telegram ставит её в системном режиме, проверив секрет).
            migrationBuilder.EnableUserRls("jobs");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DisableUserRls("jobs");
            migrationBuilder.DisableUserRls("collection_items");
            migrationBuilder.DisableUserRls("item_tags");
        }
    }
}
