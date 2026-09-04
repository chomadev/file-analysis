using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FileAnalysis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ContentOneToMany : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_file_contents_FileId",
                table: "file_contents");

            migrationBuilder.CreateIndex(
                name: "IX_file_contents_FileId",
                table: "file_contents",
                column: "FileId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_file_contents_FileId",
                table: "file_contents");

            migrationBuilder.CreateIndex(
                name: "IX_file_contents_FileId",
                table: "file_contents",
                column: "FileId",
                unique: true);
        }
    }
}
