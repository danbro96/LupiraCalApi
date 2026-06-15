using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace LupiraCalApi.Migrations
{
    /// <inheritdoc />
    public partial class Phase2Search : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,");

            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "search_vector",
                schema: "cal",
                table: "events",
                type: "tsvector",
                nullable: false)
                .Annotation("Npgsql:TsVectorConfig", "english")
                .Annotation("Npgsql:TsVectorProperties", new[] { "title", "description", "location" });

            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "search_vector",
                schema: "cal",
                table: "contacts",
                type: "tsvector",
                nullable: false)
                .Annotation("Npgsql:TsVectorConfig", "simple")
                .Annotation("Npgsql:TsVectorProperties", new[] { "full_name", "organization" });

            migrationBuilder.CreateIndex(
                name: "ix_events_metadata",
                schema: "cal",
                table: "events",
                column: "metadata")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "jsonb_path_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_events_search_vector",
                schema: "cal",
                table: "events",
                column: "search_vector")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_events_tags",
                schema: "cal",
                table: "events",
                column: "tags")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_events_title",
                schema: "cal",
                table: "events",
                column: "title")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_contacts_full_name",
                schema: "cal",
                table: "contacts",
                column: "full_name")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_contacts_metadata",
                schema: "cal",
                table: "contacts",
                column: "metadata")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "jsonb_path_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_contacts_search_vector",
                schema: "cal",
                table: "contacts",
                column: "search_vector")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_contacts_tags",
                schema: "cal",
                table: "contacts",
                column: "tags")
                .Annotation("Npgsql:IndexMethod", "gin");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_events_metadata",
                schema: "cal",
                table: "events");

            migrationBuilder.DropIndex(
                name: "ix_events_search_vector",
                schema: "cal",
                table: "events");

            migrationBuilder.DropIndex(
                name: "ix_events_tags",
                schema: "cal",
                table: "events");

            migrationBuilder.DropIndex(
                name: "ix_events_title",
                schema: "cal",
                table: "events");

            migrationBuilder.DropIndex(
                name: "ix_contacts_full_name",
                schema: "cal",
                table: "contacts");

            migrationBuilder.DropIndex(
                name: "ix_contacts_metadata",
                schema: "cal",
                table: "contacts");

            migrationBuilder.DropIndex(
                name: "ix_contacts_search_vector",
                schema: "cal",
                table: "contacts");

            migrationBuilder.DropIndex(
                name: "ix_contacts_tags",
                schema: "cal",
                table: "contacts");

            migrationBuilder.DropColumn(
                name: "search_vector",
                schema: "cal",
                table: "events");

            migrationBuilder.DropColumn(
                name: "search_vector",
                schema: "cal",
                table: "contacts");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:pg_trgm", ",,");
        }
    }
}
