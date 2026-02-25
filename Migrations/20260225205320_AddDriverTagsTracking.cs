using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StingListManager.Migrations
{
    /// <inheritdoc />
    public partial class AddDriverTagsTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DriverTags",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TagCode = table.Column<string>(type: "TEXT", nullable: false),
                    TagCodeNorm = table.Column<string>(type: "TEXT", nullable: false),
                    DriverName = table.Column<string>(type: "TEXT", nullable: false),
                    DriverNameNorm = table.Column<string>(type: "TEXT", nullable: false),
                    IssuedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LostOrDamagedReportedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LostOrDamagedReason = table.Column<string>(type: "TEXT", nullable: true),
                    EmploymentExitType = table.Column<int>(type: "INTEGER", nullable: false),
                    EmploymentExitAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ReturnStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    ReturnedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DriverTags", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DriverTagTransfers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DriverTagId = table.Column<int>(type: "INTEGER", nullable: false),
                    FromDriverName = table.Column<string>(type: "TEXT", nullable: false),
                    ToDriverName = table.Column<string>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", nullable: false),
                    TransferredAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TransferredBy = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DriverTagTransfers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DriverTags_DriverNameNorm",
                table: "DriverTags",
                column: "DriverNameNorm");

            migrationBuilder.CreateIndex(
                name: "IX_DriverTags_EmploymentExitAt",
                table: "DriverTags",
                column: "EmploymentExitAt");

            migrationBuilder.CreateIndex(
                name: "IX_DriverTags_IssuedAt",
                table: "DriverTags",
                column: "IssuedAt");

            migrationBuilder.CreateIndex(
                name: "IX_DriverTags_LostOrDamagedReportedAt",
                table: "DriverTags",
                column: "LostOrDamagedReportedAt");

            migrationBuilder.CreateIndex(
                name: "IX_DriverTags_ReturnStatus",
                table: "DriverTags",
                column: "ReturnStatus");

            migrationBuilder.CreateIndex(
                name: "IX_DriverTags_TagCodeNorm",
                table: "DriverTags",
                column: "TagCodeNorm",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DriverTagTransfers_DriverTagId",
                table: "DriverTagTransfers",
                column: "DriverTagId");

            migrationBuilder.CreateIndex(
                name: "IX_DriverTagTransfers_TransferredAt",
                table: "DriverTagTransfers",
                column: "TransferredAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DriverTags");

            migrationBuilder.DropTable(
                name: "DriverTagTransfers");
        }
    }
}
