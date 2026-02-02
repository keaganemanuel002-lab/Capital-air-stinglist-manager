using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StingListManager.Migrations
{
    /// <inheritdoc />
    public partial class AddTrackingUnitFieldsSplit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SimNumber",
                table: "JobCards",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TrackingUnitMake",
                table: "JobCards",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SimNumber",
                table: "BillingEntries",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TrackingUnitMake",
                table: "BillingEntries",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SimNumber",
                table: "JobCards");

            migrationBuilder.DropColumn(
                name: "TrackingUnitMake",
                table: "JobCards");

            migrationBuilder.DropColumn(
                name: "SimNumber",
                table: "BillingEntries");

            migrationBuilder.DropColumn(
                name: "TrackingUnitMake",
                table: "BillingEntries");
        }
    }
}
