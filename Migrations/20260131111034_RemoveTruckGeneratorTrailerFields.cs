using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StingListManager.Migrations
{
    /// <inheritdoc />
    public partial class RemoveTruckGeneratorTrailerFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GeneratorMake",
                table: "JobCards");

            migrationBuilder.DropColumn(
                name: "GeneratorModel",
                table: "JobCards");

            migrationBuilder.DropColumn(
                name: "TrailerModel",
                table: "JobCards");

            migrationBuilder.DropColumn(
                name: "TrailerType",
                table: "JobCards");

            migrationBuilder.DropColumn(
                name: "TruckMake",
                table: "JobCards");

            migrationBuilder.DropColumn(
                name: "TruckModel",
                table: "JobCards");

            migrationBuilder.DropColumn(
                name: "GeneratorMake",
                table: "BillingEntries");

            migrationBuilder.DropColumn(
                name: "GeneratorModel",
                table: "BillingEntries");

            migrationBuilder.DropColumn(
                name: "TrailerModel",
                table: "BillingEntries");

            migrationBuilder.DropColumn(
                name: "TrailerType",
                table: "BillingEntries");

            migrationBuilder.DropColumn(
                name: "TruckMake",
                table: "BillingEntries");

            migrationBuilder.DropColumn(
                name: "TruckModel",
                table: "BillingEntries");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GeneratorMake",
                table: "JobCards",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GeneratorModel",
                table: "JobCards",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TrailerModel",
                table: "JobCards",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TrailerType",
                table: "JobCards",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TruckMake",
                table: "JobCards",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TruckModel",
                table: "JobCards",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GeneratorMake",
                table: "BillingEntries",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GeneratorModel",
                table: "BillingEntries",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TrailerModel",
                table: "BillingEntries",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TrailerType",
                table: "BillingEntries",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TruckMake",
                table: "BillingEntries",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TruckModel",
                table: "BillingEntries",
                type: "TEXT",
                nullable: true);
        }
    }
}
