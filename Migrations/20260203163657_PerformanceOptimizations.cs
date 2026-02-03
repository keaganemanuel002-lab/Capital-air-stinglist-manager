using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StingListManager.Migrations
{
    /// <inheritdoc />
    public partial class PerformanceOptimizations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Quotes_Company",
                table: "Quotes",
                column: "Company");

            migrationBuilder.CreateIndex(
                name: "IX_Quotes_CreatedAt",
                table: "Quotes",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Quotes_Status",
                table: "Quotes",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_JobCards_Company",
                table: "JobCards",
                column: "Company");

            migrationBuilder.CreateIndex(
                name: "IX_JobCards_CreatedAt",
                table: "JobCards",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_JobCards_Status",
                table: "JobCards",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_CancellationEntries_DateRequestReceived",
                table: "CancellationEntries",
                column: "DateRequestReceived");

            migrationBuilder.CreateIndex(
                name: "IX_BillingEntries_ActiveFrom",
                table: "BillingEntries",
                column: "ActiveFrom");

            migrationBuilder.CreateIndex(
                name: "IX_BillingEntries_ArchivedAt",
                table: "BillingEntries",
                column: "ArchivedAt");

            migrationBuilder.CreateIndex(
                name: "IX_BillingEntries_Company",
                table: "BillingEntries",
                column: "Company");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Quotes_Company",
                table: "Quotes");

            migrationBuilder.DropIndex(
                name: "IX_Quotes_CreatedAt",
                table: "Quotes");

            migrationBuilder.DropIndex(
                name: "IX_Quotes_Status",
                table: "Quotes");

            migrationBuilder.DropIndex(
                name: "IX_JobCards_Company",
                table: "JobCards");

            migrationBuilder.DropIndex(
                name: "IX_JobCards_CreatedAt",
                table: "JobCards");

            migrationBuilder.DropIndex(
                name: "IX_JobCards_Status",
                table: "JobCards");

            migrationBuilder.DropIndex(
                name: "IX_CancellationEntries_DateRequestReceived",
                table: "CancellationEntries");

            migrationBuilder.DropIndex(
                name: "IX_BillingEntries_ActiveFrom",
                table: "BillingEntries");

            migrationBuilder.DropIndex(
                name: "IX_BillingEntries_ArchivedAt",
                table: "BillingEntries");

            migrationBuilder.DropIndex(
                name: "IX_BillingEntries_Company",
                table: "BillingEntries");
        }
    }
}
