using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LupiraCalApi.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "cal");

            migrationBuilder.CreateTable(
                name: "relations",
                schema: "cal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_kind = table.Column<string>(type: "text", nullable: false),
                    from_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_kind = table.Column<string>(type: "text", nullable: false),
                    to_ref = table.Column<string>(type: "text", nullable: false),
                    relation_type = table.Column<string>(type: "text", nullable: false),
                    metadata = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_relations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                schema: "cal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    authentik_sub = table.Column<string>(type: "text", nullable: false),
                    email = table.Column<string>(type: "text", nullable: false),
                    is_shared = table.Column<bool>(type: "boolean", nullable: false),
                    display_name = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "address_books",
                schema: "cal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    slug = table.Column<string>(type: "text", nullable: false),
                    display_name = table.Column<string>(type: "text", nullable: true),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_address_books", x => x.id);
                    table.ForeignKey(
                        name: "fk_address_books_users_owner_id",
                        column: x => x.owner_id,
                        principalSchema: "cal",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "calendars",
                schema: "cal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    slug = table.Column<string>(type: "text", nullable: false),
                    display_name = table.Column<string>(type: "text", nullable: true),
                    color = table.Column<string>(type: "text", nullable: true),
                    default_timezone = table.Column<string>(type: "text", nullable: true),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_calendars", x => x.id);
                    table.ForeignKey(
                        name: "fk_calendars_users_owner_id",
                        column: x => x.owner_id,
                        principalSchema: "cal",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "address_book_shares",
                schema: "cal",
                columns: table => new
                {
                    address_book_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    access = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_address_book_shares", x => new { x.address_book_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_address_book_shares_address_books_address_book_id",
                        column: x => x.address_book_id,
                        principalSchema: "cal",
                        principalTable: "address_books",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_address_book_shares_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "cal",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "contact_changes",
                schema: "cal",
                columns: table => new
                {
                    address_book_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    item_vcard_uid = table.Column<string>(type: "text", nullable: false),
                    change_type = table.Column<string>(type: "text", nullable: false),
                    content_hash = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contact_changes", x => new { x.address_book_id, x.revision });
                    table.ForeignKey(
                        name: "fk_contact_changes_address_books_address_book_id",
                        column: x => x.address_book_id,
                        principalSchema: "cal",
                        principalTable: "address_books",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "contacts",
                schema: "cal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    address_book_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vcard_uid = table.Column<string>(type: "text", nullable: false),
                    full_name = table.Column<string>(type: "text", nullable: true),
                    given_name = table.Column<string>(type: "text", nullable: true),
                    family_name = table.Column<string>(type: "text", nullable: true),
                    organization = table.Column<string>(type: "text", nullable: true),
                    emails = table.Column<string>(type: "jsonb", nullable: true),
                    phones = table.Column<string>(type: "jsonb", nullable: true),
                    addresses = table.Column<string>(type: "jsonb", nullable: true),
                    birthday = table.Column<DateOnly>(type: "date", nullable: true),
                    photo_url = table.Column<string>(type: "text", nullable: true),
                    source_vcard = table.Column<string>(type: "text", nullable: false),
                    content_hash = table.Column<string>(type: "text", nullable: false),
                    tags = table.Column<string[]>(type: "text[]", nullable: true),
                    metadata = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contacts", x => x.id);
                    table.ForeignKey(
                        name: "fk_contacts_address_books_address_book_id",
                        column: x => x.address_book_id,
                        principalSchema: "cal",
                        principalTable: "address_books",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "calendar_changes",
                schema: "cal",
                columns: table => new
                {
                    calendar_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    item_ical_uid = table.Column<string>(type: "text", nullable: false),
                    change_type = table.Column<string>(type: "text", nullable: false),
                    content_hash = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_calendar_changes", x => new { x.calendar_id, x.revision });
                    table.ForeignKey(
                        name: "fk_calendar_changes_calendars_calendar_id",
                        column: x => x.calendar_id,
                        principalSchema: "cal",
                        principalTable: "calendars",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "calendar_shares",
                schema: "cal",
                columns: table => new
                {
                    calendar_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    access = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_calendar_shares", x => new { x.calendar_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_calendar_shares_calendars_calendar_id",
                        column: x => x.calendar_id,
                        principalSchema: "cal",
                        principalTable: "calendars",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_calendar_shares_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "cal",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "events",
                schema: "cal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    calendar_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ical_uid = table.Column<string>(type: "text", nullable: false),
                    title = table.Column<string>(type: "text", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    location = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "text", nullable: true),
                    organizer = table.Column<string>(type: "text", nullable: true),
                    attendees = table.Column<string>(type: "jsonb", nullable: true),
                    is_all_day = table.Column<bool>(type: "boolean", nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    start_timezone = table.Column<string>(type: "text", nullable: true),
                    end_timezone = table.Column<string>(type: "text", nullable: true),
                    start_date = table.Column<DateOnly>(type: "date", nullable: true),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    duration = table.Column<TimeSpan>(type: "interval", nullable: true),
                    recurrence_rule = table.Column<string>(type: "text", nullable: true),
                    recurrence_extra_dates = table.Column<DateTimeOffset[]>(type: "timestamp with time zone[]", nullable: true),
                    recurrence_excluded_dates = table.Column<DateTimeOffset[]>(type: "timestamp with time zone[]", nullable: true),
                    recurrence_overrides = table.Column<string>(type: "jsonb", nullable: true),
                    recurrence_ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    source_icalendar = table.Column<string>(type: "text", nullable: false),
                    content_hash = table.Column<string>(type: "text", nullable: false),
                    tags = table.Column<string[]>(type: "text[]", nullable: true),
                    metadata = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_events_calendars_calendar_id",
                        column: x => x.calendar_id,
                        principalSchema: "cal",
                        principalTable: "calendars",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_address_book_shares_user_id",
                schema: "cal",
                table: "address_book_shares",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_address_books_owner_id_slug",
                schema: "cal",
                table: "address_books",
                columns: new[] { "owner_id", "slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_calendar_shares_user_id",
                schema: "cal",
                table: "calendar_shares",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_calendars_owner_id_slug",
                schema: "cal",
                table: "calendars",
                columns: new[] { "owner_id", "slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_contacts_address_book_id_vcard_uid",
                schema: "cal",
                table: "contacts",
                columns: new[] { "address_book_id", "vcard_uid" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_events_calendar_id_ical_uid",
                schema: "cal",
                table: "events",
                columns: new[] { "calendar_id", "ical_uid" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_relations_from_kind_from_id",
                schema: "cal",
                table: "relations",
                columns: new[] { "from_kind", "from_id" });

            migrationBuilder.CreateIndex(
                name: "ix_users_authentik_sub",
                schema: "cal",
                table: "users",
                column: "authentik_sub",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_email",
                schema: "cal",
                table: "users",
                column: "email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "address_book_shares",
                schema: "cal");

            migrationBuilder.DropTable(
                name: "calendar_changes",
                schema: "cal");

            migrationBuilder.DropTable(
                name: "calendar_shares",
                schema: "cal");

            migrationBuilder.DropTable(
                name: "contact_changes",
                schema: "cal");

            migrationBuilder.DropTable(
                name: "contacts",
                schema: "cal");

            migrationBuilder.DropTable(
                name: "events",
                schema: "cal");

            migrationBuilder.DropTable(
                name: "relations",
                schema: "cal");

            migrationBuilder.DropTable(
                name: "address_books",
                schema: "cal");

            migrationBuilder.DropTable(
                name: "calendars",
                schema: "cal");

            migrationBuilder.DropTable(
                name: "users",
                schema: "cal");
        }
    }
}
