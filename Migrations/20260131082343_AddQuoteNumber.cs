using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StingListManager.Migrations
{
    /// <inheritdoc />
    public partial class AddQuoteNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "QuoteNumber",
                table: "Quotes",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Quotes_Registration",
                table: "Quotes",
                column: "Registration");

            migrationBuilder.CreateIndex(
                name: "IX_JobCards_Code",
                table: "JobCards",
                column: "Code");

            migrationBuilder.CreateIndex(
                name: "IX_JobCards_Registration",
                table: "JobCards",
                column: "Registration");

            migrationBuilder.CreateIndex(
                name: "IX_CancellationEntries_Registration",
                table: "CancellationEntries",
                column: "Registration");

            migrationBuilder.CreateIndex(
                name: "IX_BillingEntries_CodeNorm",
                table: "BillingEntries",
                column: "CodeNorm");

            migrationBuilder.CreateIndex(
                name: "IX_BillingEntries_RegistrationNorm",
                table: "BillingEntries",
                column: "RegistrationNorm");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Quotes_Registration",
                table: "Quotes");

            migrationBuilder.DropIndex(
                name: "IX_JobCards_Code",
                table: "JobCards");

            migrationBuilder.DropIndex(
                name: "IX_JobCards_Registration",
                table: "JobCards");

            migrationBuilder.DropIndex(
                name: "IX_CancellationEntries_Registration",
                table: "CancellationEntries");

            migrationBuilder.DropIndex(
                name: "IX_BillingEntries_CodeNorm",
                table: "BillingEntries");

            migrationBuilder.DropIndex(
                name: "IX_BillingEntries_RegistrationNorm",
                table: "BillingEntries");

            migrationBuilder.DropColumn(
                name: "QuoteNumber",
                table: "Quotes");
        }
    }
}
