using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StingListManager.Migrations
{
    /// <inheritdoc />
    public partial class RemoveCodeFromQuoteAddProductFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Code",
                table: "Quotes",
                newName: "ProductType");

            migrationBuilder.AddColumn<bool>(
                name: "IncludesAppLiveTracking",
                table: "Quotes",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IncludesPanicButton",
                table: "Quotes",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IncludesAppLiveTracking",
                table: "Quotes");

            migrationBuilder.DropColumn(
                name: "IncludesPanicButton",
                table: "Quotes");

            migrationBuilder.RenameColumn(
                name: "ProductType",
                table: "Quotes",
                newName: "Code");
        }
    }
}
