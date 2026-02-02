using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StingListManager.Migrations
{
    /// <inheritdoc />
    public partial class AddVinAndVehicleDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Colour",
                table: "JobCards",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Make",
                table: "JobCards",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Model",
                table: "JobCards",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VinNumber",
                table: "JobCards",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Colour",
                table: "BillingEntries",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Make",
                table: "BillingEntries",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Model",
                table: "BillingEntries",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VinNumber",
                table: "BillingEntries",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Colour",
                table: "JobCards");

            migrationBuilder.DropColumn(
                name: "Make",
                table: "JobCards");

            migrationBuilder.DropColumn(
                name: "Model",
                table: "JobCards");

            migrationBuilder.DropColumn(
                name: "VinNumber",
                table: "JobCards");

            migrationBuilder.DropColumn(
                name: "Colour",
                table: "BillingEntries");

            migrationBuilder.DropColumn(
                name: "Make",
                table: "BillingEntries");

            migrationBuilder.DropColumn(
                name: "Model",
                table: "BillingEntries");

            migrationBuilder.DropColumn(
                name: "VinNumber",
                table: "BillingEntries");
        }
    }
}
