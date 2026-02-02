using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StingListManager.Migrations
{
    /// <inheritdoc />
    public partial class RemoveVehicleDescriptionFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VehicleDescription",
                table: "Quotes");

            migrationBuilder.DropColumn(
                name: "VehicleDescription",
                table: "JobCards");

            migrationBuilder.DropColumn(
                name: "VehicleDescription",
                table: "BillingEntries");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "VehicleDescription",
                table: "Quotes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VehicleDescription",
                table: "JobCards",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "VehicleDescription",
                table: "BillingEntries",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }
    }
}
