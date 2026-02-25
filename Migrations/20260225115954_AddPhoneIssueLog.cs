using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StingListManager.Migrations
{
    /// <inheritdoc />
    public partial class AddPhoneIssueLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PhoneIssueLogEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TeamName = table.Column<string>(type: "TEXT", nullable: false),
                    VehicleRegistration = table.Column<string>(type: "TEXT", nullable: false),
                    TeamMemberOne = table.Column<string>(type: "TEXT", nullable: false),
                    TeamMemberTwo = table.Column<string>(type: "TEXT", nullable: false),
                    PhoneLabel = table.Column<string>(type: "TEXT", nullable: true),
                    PhoneNumber = table.Column<string>(type: "TEXT", nullable: true),
                    PhoneImei = table.Column<string>(type: "TEXT", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    IssuedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ReturnedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    TeamNameNorm = table.Column<string>(type: "TEXT", nullable: false),
                    VehicleRegistrationNorm = table.Column<string>(type: "TEXT", nullable: false),
                    PhoneImeiNorm = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhoneIssueLogEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PhoneIssueLogEntries_IssuedAt",
                table: "PhoneIssueLogEntries",
                column: "IssuedAt");

            migrationBuilder.CreateIndex(
                name: "IX_PhoneIssueLogEntries_PhoneImeiNorm",
                table: "PhoneIssueLogEntries",
                column: "PhoneImeiNorm");

            migrationBuilder.CreateIndex(
                name: "IX_PhoneIssueLogEntries_ReturnedAt",
                table: "PhoneIssueLogEntries",
                column: "ReturnedAt");

            migrationBuilder.CreateIndex(
                name: "IX_PhoneIssueLogEntries_TeamNameNorm",
                table: "PhoneIssueLogEntries",
                column: "TeamNameNorm");

            migrationBuilder.CreateIndex(
                name: "IX_PhoneIssueLogEntries_VehicleRegistrationNorm",
                table: "PhoneIssueLogEntries",
                column: "VehicleRegistrationNorm");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PhoneIssueLogEntries");
        }
    }
}
