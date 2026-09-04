using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoteManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNotesFullTextSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // AB-1007: full-text catalog + index on Notes(Title, Content), keyed on the table's
            // existing primary key (PK_Notes — AddNotes migration). EF Core has no native
            // full-text-index API, so this is raw SQL (AGENTS.md §6/§11 — no application-layer
            // query construction here, just server-level DDL). IF NOT EXISTS guards are defensive:
            // this DDL is server-level, outside EF's own __EFMigrationsHistory tracking.
            //
            // suppressTransaction: true on every statement below — found while actually applying
            // this migration (not caught by build/review): SQL Server rejects
            // "CREATE FULLTEXT CATALOG ... statement cannot be used inside a user transaction"
            // (Error 574), and EF Core wraps a migration's commands in one transaction by default.
            // Applied to all four statements (catalog+index, Up+Down) for consistency, since
            // CREATE/DROP FULLTEXT INDEX are also server-level DDL of the same kind.
            migrationBuilder.Sql(
                @"
IF NOT EXISTS (SELECT 1 FROM sys.fulltext_catalogs WHERE name = 'NotesFullTextCatalog')
BEGIN
    CREATE FULLTEXT CATALOG NotesFullTextCatalog AS DEFAULT;
END",
                suppressTransaction: true);

            migrationBuilder.Sql(
                @"
IF NOT EXISTS (SELECT 1 FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID('dbo.Notes'))
BEGIN
    CREATE FULLTEXT INDEX ON dbo.Notes(Title LANGUAGE 1033, Content LANGUAGE 1033)
        KEY INDEX PK_Notes ON NotesFullTextCatalog
        WITH CHANGE_TRACKING AUTO;
END",
                suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                @"
IF EXISTS (SELECT 1 FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID('dbo.Notes'))
BEGIN
    DROP FULLTEXT INDEX ON dbo.Notes;
END",
                suppressTransaction: true);

            migrationBuilder.Sql(
                @"
IF EXISTS (SELECT 1 FROM sys.fulltext_catalogs WHERE name = 'NotesFullTextCatalog')
BEGIN
    DROP FULLTEXT CATALOG NotesFullTextCatalog;
END",
                suppressTransaction: true);
        }
    }
}
