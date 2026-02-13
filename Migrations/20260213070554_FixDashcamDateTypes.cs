using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StingListManager.Migrations
{
    /// <inheritdoc />
    public partial class FixDashcamDateTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Dashcams",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SerialNumber = table.Column<string>(type: "TEXT", nullable: true),
                    Model = table.Column<string>(type: "TEXT", nullable: true),
                    PurchasedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    AllocatedVehicleRegistration = table.Column<string>(type: "TEXT", nullable: true),
                    TransferredFromRegistration = table.Column<string>(type: "TEXT", nullable: true),
                    TransferredToRegistration = table.Column<string>(type: "TEXT", nullable: true),
                    TransferredAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Dashcams", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SdCards",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SerialNumber = table.Column<string>(type: "TEXT", nullable: true),
                    InstalledAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    DashcamId = table.Column<int>(type: "INTEGER", nullable: true),
                    InstalledInVehicleRegistration = table.Column<string>(type: "TEXT", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SdCards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SdCards_Dashcams_DashcamId",
                        column: x => x.DashcamId,
                        principalTable: "Dashcams",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Dashcams_AllocatedVehicleRegistration",
                table: "Dashcams",
                column: "AllocatedVehicleRegistration");

            migrationBuilder.CreateIndex(
                name: "IX_Dashcams_SerialNumber",
                table: "Dashcams",
                column: "SerialNumber");

            migrationBuilder.CreateIndex(
                name: "IX_SdCards_DashcamId",
                table: "SdCards",
                column: "DashcamId");

            migrationBuilder.CreateIndex(
                name: "IX_SdCards_SerialNumber",
                table: "SdCards",
                column: "SerialNumber");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SdCards");

            migrationBuilder.DropTable(
                name: "Dashcams");
        }
    }
}
