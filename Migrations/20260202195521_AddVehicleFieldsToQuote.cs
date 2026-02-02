using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StingListManager.Migrations
{
    /// <inheritdoc />
    public partial class AddVehicleFieldsToQuote : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Colour",
                table: "Quotes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Iccid",
                table: "Quotes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Imei",
                table: "Quotes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Make",
                table: "Quotes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Model",
                table: "Quotes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SerialNumber",
                table: "Quotes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SimNumber",
                table: "Quotes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TrackingUnitMake",
                table: "Quotes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VinNumber",
                table: "Quotes",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Colour",
                table: "Quotes");

            migrationBuilder.DropColumn(
                name: "Iccid",
                table: "Quotes");

            migrationBuilder.DropColumn(
                name: "Imei",
                table: "Quotes");

            migrationBuilder.DropColumn(
                name: "Make",
                table: "Quotes");

            migrationBuilder.DropColumn(
                name: "Model",
                table: "Quotes");

            migrationBuilder.DropColumn(
                name: "SerialNumber",
                table: "Quotes");

            migrationBuilder.DropColumn(
                name: "SimNumber",
                table: "Quotes");

            migrationBuilder.DropColumn(
                name: "TrackingUnitMake",
                table: "Quotes");

            migrationBuilder.DropColumn(
                name: "VinNumber",
                table: "Quotes");
        }
    }
}
