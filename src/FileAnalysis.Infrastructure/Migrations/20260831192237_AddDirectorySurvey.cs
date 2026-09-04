using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FileAnalysis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDirectorySurvey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "directory_survey_entries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Path = table.Column<string>(type: "text", nullable: false),
                    ScanRootPath = table.Column<string>(type: "text", nullable: false),
                    Depth = table.Column<int>(type: "integer", nullable: false),
                    DirectFileCount = table.Column<int>(type: "integer", nullable: false),
                    DirectSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    TotalFileCount = table.Column<int>(type: "integer", nullable: false),
                    TotalSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    TopExtensions = table.Column<string[]>(type: "text[]", nullable: false),
                    Classification = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ClassificationSource = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ClassificationReason = table.Column<string>(type: "text", nullable: true),
                    ExcludeDecision = table.Column<bool>(type: "boolean", nullable: true),
                    SurveyedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_directory_survey_entries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_directory_survey_entries_Path",
                table: "directory_survey_entries",
                column: "Path",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_directory_survey_entries_ScanRootPath",
                table: "directory_survey_entries",
                column: "ScanRootPath");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "directory_survey_entries");
        }
    }
}
