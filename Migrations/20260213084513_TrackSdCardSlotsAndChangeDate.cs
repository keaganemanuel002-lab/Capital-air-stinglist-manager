using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StingListManager.Migrations
{
    /// <inheritdoc />
    public partial class TrackSdCardSlotsAndChangeDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SdCards_DashcamId",
                table: "SdCards");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ChangedAt",
                table: "SdCards",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SlotNumber",
                table: "SdCards",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateIndex(
                name: "IX_SdCards_DashcamId_SlotNumber",
                table: "SdCards",
                columns: new[] { "DashcamId", "SlotNumber" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_SdCards_SlotNumber",
                table: "SdCards",
                sql: "\"SlotNumber\" IN (1, 2)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SdCards_DashcamId_SlotNumber",
                table: "SdCards");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SdCards_SlotNumber",
                table: "SdCards");

            migrationBuilder.DropColumn(
                name: "ChangedAt",
                table: "SdCards");

            migrationBuilder.DropColumn(
                name: "SlotNumber",
                table: "SdCards");

            migrationBuilder.CreateIndex(
                name: "IX_SdCards_DashcamId",
                table: "SdCards",
                column: "DashcamId");
        }
    }
}
