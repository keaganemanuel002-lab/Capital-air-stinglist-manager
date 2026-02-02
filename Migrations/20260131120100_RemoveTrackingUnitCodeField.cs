using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StingListManager.Migrations
{
    /// <inheritdoc />
    public partial class RemoveTrackingUnitCodeField : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Code",
                table: "JobCards");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "CancellationEntries");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "BillingEntries");

            migrationBuilder.DropColumn(
                name: "CodeNorm",
                table: "BillingEntries");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "AuditEvents");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "JobCards",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "CancellationEntries",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "BillingEntries",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CodeNorm",
                table: "BillingEntries",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "AuditEvents",
                type: "TEXT",
                nullable: true);
        }
    }
}
