using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FileAnalysis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCleanupSuggestions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cleanup_suggestions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FileId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Details = table.Column<string>(type: "text", nullable: true),
                    DuplicateOfFileId = table.Column<Guid>(type: "uuid", nullable: true),
                    SuggestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DecidedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AppliedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    QuarantinePath = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cleanup_suggestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_cleanup_suggestions_files_DuplicateOfFileId",
                        column: x => x.DuplicateOfFileId,
                        principalTable: "files",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_cleanup_suggestions_files_FileId",
                        column: x => x.FileId,
                        principalTable: "files",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_files_Md5Hash",
                table: "files",
                column: "Md5Hash",
                filter: "\"Md5Hash\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_cleanup_suggestions_DuplicateOfFileId",
                table: "cleanup_suggestions",
                column: "DuplicateOfFileId");

            migrationBuilder.CreateIndex(
                name: "IX_cleanup_suggestions_FileId",
                table: "cleanup_suggestions",
                column: "FileId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cleanup_suggestions_Status",
                table: "cleanup_suggestions",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cleanup_suggestions");

            migrationBuilder.DropIndex(
                name: "IX_files_Md5Hash",
                table: "files");
        }
    }
}
