using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LupiraCalApi.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialSubscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "cal_subscriptions");

            migrationBuilder.CreateTable(
                name: "subscription",
                schema: "cal_subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    calendar_id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    color = table.Column<string>(type: "text", nullable: true),
                    feed_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    next_refresh_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    leased_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_synced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "text", nullable: true),
                    consecutive_failures = table.Column<int>(type: "integer", nullable: false),
                    feed_etag = table.Column<string>(type: "text", nullable: true),
                    feed_last_modified = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    content_hash = table.Column<string>(type: "text", nullable: true),
                    event_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subscription", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "subscribed_event",
                schema: "cal_subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    subscription_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_key = table.Column<string>(type: "text", nullable: false),
                    first_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    content_hash = table.Column<string>(type: "text", nullable: false),
                    series = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subscribed_event", x => x.id);
                    table.ForeignKey(
                        name: "FK_subscribed_event_subscription_subscription_id",
                        column: x => x.subscription_id,
                        principalSchema: "cal_subscriptions",
                        principalTable: "subscription",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_subscribed_event_first_start",
                schema: "cal_subscriptions",
                table: "subscribed_event",
                column: "first_start");

            migrationBuilder.CreateIndex(
                name: "IX_subscribed_event_subscription_id",
                schema: "cal_subscriptions",
                table: "subscribed_event",
                column: "subscription_id");

            migrationBuilder.CreateIndex(
                name: "IX_subscription_calendar_id",
                schema: "cal_subscriptions",
                table: "subscription",
                column: "calendar_id");

            migrationBuilder.CreateIndex(
                name: "IX_subscription_next_refresh_at",
                schema: "cal_subscriptions",
                table: "subscription",
                column: "next_refresh_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "subscribed_event",
                schema: "cal_subscriptions");

            migrationBuilder.DropTable(
                name: "subscription",
                schema: "cal_subscriptions");
        }
    }
}
