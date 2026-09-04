using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace FileAnalysis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEmbeddingAndDuplicateFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Vector>(
                name: "Embedding",
                table: "file_analyses",
                type: "vector(768)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDuplicateCandidate",
                table: "file_analyses",
                type: "boolean",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Embedding",
                table: "file_analyses");

            migrationBuilder.DropColumn(
                name: "IsDuplicateCandidate",
                table: "file_analyses");
        }
    }
}
