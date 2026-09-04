using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FileAnalysis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSearchIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_file_analyses_Category",
                table: "file_analyses",
                column: "Category");

            // to_tsvector('english', ...) is STABLE, not IMMUTABLE (the config name is looked up at runtime),
            // so it can't be used directly in an index expression. Wrap it in an IMMUTABLE SQL function.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION file_analyses_search_vector(summary text, tags text[])
                RETURNS tsvector AS $$
                    SELECT to_tsvector('english', coalesce(summary,'') || ' ' || coalesce(array_to_string(tags,' '),''));
                $$ LANGUAGE sql IMMUTABLE;
                """);

            migrationBuilder.Sql("""
                CREATE INDEX idx_analyses_fts ON file_analyses USING GIN (file_analyses_search_vector("Summary", "Tags"));
                """);

            migrationBuilder.Sql("""
                CREATE INDEX idx_analyses_embedding ON file_analyses USING ivfflat ("Embedding" vector_cosine_ops) WITH (lists = 100);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS idx_analyses_embedding;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS idx_analyses_fts;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS file_analyses_search_vector(text, text[]);");

            migrationBuilder.DropIndex(
                name: "IX_file_analyses_Category",
                table: "file_analyses");
        }
    }
}
