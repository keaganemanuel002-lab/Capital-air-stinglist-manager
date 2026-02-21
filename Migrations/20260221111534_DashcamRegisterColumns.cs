using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StingListManager.Migrations
{
    /// <inheritdoc />
    public partial class DashcamRegisterColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DeviceId",
                table: "Dashcams",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Installed",
                table: "Dashcams",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InteriorCam",
                table: "Dashcams",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Issue",
                table: "Dashcams",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IsupPassword",
                table: "Dashcams",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Location",
                table: "Dashcams",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RearCam",
                table: "Dashcams",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpgradeSteps",
                table: "Dashcams",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Vehicle",
                table: "Dashcams",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WifiPassword",
                table: "Dashcams",
                type: "TEXT",
                nullable: true);

            // Backfill spreadsheet-aligned columns from existing dashcam data.
            migrationBuilder.Sql(@"
UPDATE Dashcams
SET DeviceId = COALESCE(NULLIF(DeviceId, ''), NULLIF(SerialNumber, ''))
WHERE DeviceId IS NULL OR DeviceId = '';
");

            migrationBuilder.Sql(@"
UPDATE Dashcams
SET Vehicle = COALESCE(NULLIF(Vehicle, ''), NULLIF(AllocatedVehicleRegistration, ''), NULLIF(Model, ''))
WHERE Vehicle IS NULL OR Vehicle = '';
");

            migrationBuilder.Sql(@"
UPDATE Dashcams
SET Installed = CASE
    WHEN PurchasedAt IS NOT NULL THEN strftime('%d/%m/%Y', PurchasedAt)
    ELSE Installed
END
WHERE Installed IS NULL OR Installed = '';
");

            migrationBuilder.Sql(@"
UPDATE Dashcams
SET Location = COALESCE(NULLIF(Location, ''), NULLIF(TransferredToRegistration, ''), NULLIF(TransferredFromRegistration, ''))
WHERE Location IS NULL OR Location = '';
");

            migrationBuilder.Sql(@"
UPDATE Dashcams
SET Issue = COALESCE(NULLIF(Issue, ''), NULLIF(Notes, ''))
WHERE Issue IS NULL OR Issue = '';
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeviceId",
                table: "Dashcams");

            migrationBuilder.DropColumn(
                name: "Installed",
                table: "Dashcams");

            migrationBuilder.DropColumn(
                name: "InteriorCam",
                table: "Dashcams");

            migrationBuilder.DropColumn(
                name: "Issue",
                table: "Dashcams");

            migrationBuilder.DropColumn(
                name: "IsupPassword",
                table: "Dashcams");

            migrationBuilder.DropColumn(
                name: "Location",
                table: "Dashcams");

            migrationBuilder.DropColumn(
                name: "RearCam",
                table: "Dashcams");

            migrationBuilder.DropColumn(
                name: "UpgradeSteps",
                table: "Dashcams");

            migrationBuilder.DropColumn(
                name: "Vehicle",
                table: "Dashcams");

            migrationBuilder.DropColumn(
                name: "WifiPassword",
                table: "Dashcams");
        }
    }
}
