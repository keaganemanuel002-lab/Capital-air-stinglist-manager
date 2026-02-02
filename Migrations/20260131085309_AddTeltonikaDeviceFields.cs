using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StingListManager.Migrations
{
    /// <inheritdoc />
    public partial class AddTeltonikaDeviceFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Iccid",
                table: "JobCards",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Imei",
                table: "JobCards",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SerialNumber",
                table: "JobCards",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Iccid",
                table: "BillingEntries",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Imei",
                table: "BillingEntries",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SerialNumber",
                table: "BillingEntries",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Iccid",
                table: "JobCards");

            migrationBuilder.DropColumn(
                name: "Imei",
                table: "JobCards");

            migrationBuilder.DropColumn(
                name: "SerialNumber",
                table: "JobCards");

            migrationBuilder.DropColumn(
                name: "Iccid",
                table: "BillingEntries");

            migrationBuilder.DropColumn(
                name: "Imei",
                table: "BillingEntries");

            migrationBuilder.DropColumn(
                name: "SerialNumber",
                table: "BillingEntries");
        }
    }
}
