using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StingListManager.Migrations
{
    /// <inheritdoc />
    public partial class PreventDuplicateEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NameNorm",
                table: "Clients",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "IccidNorm",
                table: "BillingEntries",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ImeiNorm",
                table: "BillingEntries",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SerialNumberNorm",
                table: "BillingEntries",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            // Backfill normalized fields for existing data.
            migrationBuilder.Sql(
                """
                UPDATE Clients
                SET NameNorm = lower(
                    replace(
                        replace(
                            replace(
                                replace(
                                    replace(trim(ifnull(Name, '')), ' ', ''),
                                '-', ''),
                            '_', ''),
                        '.', ''),
                    '/', '')
                );
                """);

            migrationBuilder.Sql(
                """
                UPDATE Clients
                SET Name = CASE
                        WHEN trim(ifnull(Name, '')) = '' THEN 'Client ' || Id
                        ELSE trim(Name)
                    END,
                    NameNorm = CASE
                        WHEN NameNorm = '' THEN 'client' || Id
                        ELSE NameNorm
                    END;
                """);

            migrationBuilder.Sql(
                """
                UPDATE BillingEntries
                SET RegistrationNorm = upper(trim(ifnull(Registration, ''))),
                    ImeiNorm = replace(replace(replace(replace(trim(ifnull(Imei, '')), ' ', ''), '-', ''), '+', ''), '.', ''),
                    IccidNorm = replace(replace(replace(replace(trim(ifnull(Iccid, '')), ' ', ''), '-', ''), '+', ''), '.', ''),
                    SerialNumberNorm = upper(trim(ifnull(SerialNumber, '')));
                """);

            // Remove duplicate active/local units so STING and billing lists remain unique.
            migrationBuilder.Sql(
                """
                DELETE FROM BillingEntries
                WHERE Id IN (
                    SELECT Id
                    FROM (
                        SELECT Id,
                               ROW_NUMBER() OVER (
                                   PARTITION BY ImeiNorm
                                   ORDER BY ActiveFrom DESC, Id DESC
                               ) AS rn
                        FROM BillingEntries
                        WHERE ArchivedAt IS NULL
                          AND (Status = 0 OR Status = 2)
                          AND ImeiNorm <> ''
                    ) ranked
                    WHERE rn > 1
                );
                """);

            migrationBuilder.Sql(
                """
                DELETE FROM BillingEntries
                WHERE Id IN (
                    SELECT Id
                    FROM (
                        SELECT Id,
                               ROW_NUMBER() OVER (
                                   PARTITION BY IccidNorm
                                   ORDER BY ActiveFrom DESC, Id DESC
                               ) AS rn
                        FROM BillingEntries
                        WHERE ArchivedAt IS NULL
                          AND (Status = 0 OR Status = 2)
                          AND IccidNorm <> ''
                    ) ranked
                    WHERE rn > 1
                );
                """);

            migrationBuilder.Sql(
                """
                DELETE FROM BillingEntries
                WHERE Id IN (
                    SELECT Id
                    FROM (
                        SELECT Id,
                               ROW_NUMBER() OVER (
                                   PARTITION BY SerialNumberNorm
                                   ORDER BY ActiveFrom DESC, Id DESC
                               ) AS rn
                        FROM BillingEntries
                        WHERE ArchivedAt IS NULL
                          AND (Status = 0 OR Status = 2)
                          AND SerialNumberNorm <> ''
                    ) ranked
                    WHERE rn > 1
                );
                """);

            // Collapse duplicate clients by normalized name, keeping oldest record.
            migrationBuilder.Sql(
                """
                DELETE FROM Clients
                WHERE Id IN (
                    SELECT Id
                    FROM (
                        SELECT Id,
                               ROW_NUMBER() OVER (
                                   PARTITION BY NameNorm
                                   ORDER BY CreatedAt ASC, Id ASC
                               ) AS rn
                        FROM Clients
                        WHERE NameNorm <> ''
                    ) ranked
                    WHERE rn > 1
                );
                """);

            // Repair any duplicate quote/job-card numbers by reassigning later numbers.
            migrationBuilder.Sql(
                """
                WITH ranked AS (
                    SELECT Id,
                           ROW_NUMBER() OVER (PARTITION BY QuoteNumber ORDER BY Id ASC) AS rn
                    FROM Quotes
                ),
                dupes AS (
                    SELECT Id,
                           ROW_NUMBER() OVER (ORDER BY Id ASC) AS seq
                    FROM ranked
                    WHERE rn > 1
                ),
                mx AS (
                    SELECT ifnull(MAX(QuoteNumber), 0) AS max_num
                    FROM Quotes
                )
                UPDATE Quotes
                SET QuoteNumber = (SELECT max_num FROM mx) + (SELECT seq FROM dupes WHERE dupes.Id = Quotes.Id)
                WHERE Id IN (SELECT Id FROM dupes);
                """);

            migrationBuilder.Sql(
                """
                WITH ranked AS (
                    SELECT Id,
                           ROW_NUMBER() OVER (PARTITION BY JobCardNumber ORDER BY Id ASC) AS rn
                    FROM JobCards
                ),
                dupes AS (
                    SELECT Id,
                           ROW_NUMBER() OVER (ORDER BY Id ASC) AS seq
                    FROM ranked
                    WHERE rn > 1
                ),
                mx AS (
                    SELECT ifnull(MAX(JobCardNumber), 0) AS max_num
                    FROM JobCards
                )
                UPDATE JobCards
                SET JobCardNumber = (SELECT max_num FROM mx) + (SELECT seq FROM dupes WHERE dupes.Id = JobCards.Id)
                WHERE Id IN (SELECT Id FROM dupes);
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Quotes_QuoteNumber",
                table: "Quotes",
                column: "QuoteNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JobCards_JobCardNumber",
                table: "JobCards",
                column: "JobCardNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Clients_NameNorm",
                table: "Clients",
                column: "NameNorm",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillingEntries_IccidNorm",
                table: "BillingEntries",
                column: "IccidNorm",
                unique: true,
                filter: "\"ArchivedAt\" IS NULL AND (\"Status\" = 0 OR \"Status\" = 2) AND \"IccidNorm\" <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_BillingEntries_ImeiNorm",
                table: "BillingEntries",
                column: "ImeiNorm",
                unique: true,
                filter: "\"ArchivedAt\" IS NULL AND (\"Status\" = 0 OR \"Status\" = 2) AND \"ImeiNorm\" <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_BillingEntries_SerialNumberNorm",
                table: "BillingEntries",
                column: "SerialNumberNorm",
                unique: true,
                filter: "\"ArchivedAt\" IS NULL AND (\"Status\" = 0 OR \"Status\" = 2) AND \"SerialNumberNorm\" <> ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Quotes_QuoteNumber",
                table: "Quotes");

            migrationBuilder.DropIndex(
                name: "IX_JobCards_JobCardNumber",
                table: "JobCards");

            migrationBuilder.DropIndex(
                name: "IX_Clients_NameNorm",
                table: "Clients");

            migrationBuilder.DropIndex(
                name: "IX_BillingEntries_IccidNorm",
                table: "BillingEntries");

            migrationBuilder.DropIndex(
                name: "IX_BillingEntries_ImeiNorm",
                table: "BillingEntries");

            migrationBuilder.DropIndex(
                name: "IX_BillingEntries_SerialNumberNorm",
                table: "BillingEntries");

            migrationBuilder.DropColumn(
                name: "NameNorm",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "IccidNorm",
                table: "BillingEntries");

            migrationBuilder.DropColumn(
                name: "ImeiNorm",
                table: "BillingEntries");

            migrationBuilder.DropColumn(
                name: "SerialNumberNorm",
                table: "BillingEntries");
        }
    }
}
