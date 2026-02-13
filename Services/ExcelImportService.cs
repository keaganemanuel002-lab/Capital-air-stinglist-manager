using System;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using StingListManager.Data;
using StingListManager.Data.Entities;

namespace StingListManager.Services;

public class ExcelImportService
{
    public void ImportBillingAndCancellations(string filePath, string? actor = null)
    {
        actor ??= Environment.UserName;

        using var wb = new XLWorkbook(filePath);

        int billingAdded = ImportBillingSheet(wb, "Billing List");
        if (billingAdded == 0)
            billingAdded = ImportBillingSheet(wb, "Sheet1"); // fallback

        int cancellationsAdded = ImportCancellationsSheet(wb, "Cancellations");

        // Log the import
        var filename = Path.GetFileName(filePath);
        var details = $"Imported {billingAdded} billing + {cancellationsAdded} cancellations from {filename}";

        new AuditService().Log(actor, "IMPORT", "ExcelFile", null, null, details);
    }

    private int ImportBillingSheet(XLWorkbook wb, string sheetName)
    {
        if (!wb.Worksheets.TryGetWorksheet(sheetName, out var ws))
            return 0;

        using var db = new AppDbContext();

        int added = 0;
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;

        for (int r = 2; r <= lastRow; r++)
        {
            var company = ws.Cell(r, 1).GetString().Trim();
            var reg = ws.Cell(r, 2).GetString().Trim();
            var flt = ws.Cell(r, 3).GetString().Trim();
            var trackingUnitMake = ws.Cell(r, 5).GetString().Trim();
            var notes = ws.Cell(r, 6).GetString().Trim();
            var reason = ws.Cell(r, 7).GetString().Trim();

            // Skip empty rows
            if (string.IsNullOrWhiteSpace(company) && string.IsNullOrWhiteSpace(reg))
                continue;

            // Basic de-dupe: company + reg + active
            var exists = db.BillingEntries.Any(x =>
                x.Company == company &&
                x.Registration == reg &&
                (x.Status == BillingStatus.Active || x.Status == BillingStatus.NotLoaded));

            if (exists) continue;

            var be = new BillingEntry
            {
                Company = company,
                Registration = reg,
                FleetNumber = string.IsNullOrWhiteSpace(flt) ? null : flt,
                TrackingUnitMake = string.IsNullOrWhiteSpace(trackingUnitMake) ? null : trackingUnitMake,
                Notes = string.IsNullOrWhiteSpace(notes) ? null : notes,
                Reason = string.IsNullOrWhiteSpace(reason) ? null : reason,
                Status = BillingStatus.Active,
                ActiveFrom = DateTime.UtcNow
            };

            // Normalize for unique constraint
            be.RegistrationNorm = be.Registration.Trim().ToUpperInvariant();

            db.BillingEntries.Add(be);
            added++;
        }

        try
        {
            db.SaveChanges();
        }
        catch (DbUpdateException)
        {
            // Duplicates rejected by unique index, continue
        }
        return added;
    }

    private int ImportCancellationsSheet(XLWorkbook wb, string sheetName)
    {
        if (!wb.Worksheets.TryGetWorksheet(sheetName, out var ws))
            return 0;

        using var db = new AppDbContext();

        int added = 0;
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;

        // Your cancellations sheet columns are:
        // CLIENT, REGISTRATION, FLEET NUMBER, MAKE & MODEL, UNIT MODEL, Date Request received, Reason, Notes
        for (int r = 2; r <= lastRow; r++)
        {
            var client = ws.Cell(r, 1).GetString().Trim();
            var reg = ws.Cell(r, 2).GetString().Trim();

            if (string.IsNullOrWhiteSpace(client) && string.IsNullOrWhiteSpace(reg))
                continue;

            var fleet = ws.Cell(r, 3).GetString().Trim();
            var makeModel = ws.Cell(r, 4).GetString().Trim();
            var unitModel = ws.Cell(r, 5).GetString().Trim();

            DateTime? dateReceived = null;
            var cell = ws.Cell(r, 6);
            if (cell.DataType == XLDataType.DateTime)
                dateReceived = cell.GetDateTime();
            else if (DateTime.TryParse(cell.GetString(), out var parsed))
                dateReceived = parsed;

            var reason = ws.Cell(r, 7).GetString().Trim();
            var notes = ws.Cell(r, 8).GetString().Trim();

            // de-dupe: client + reg + date
            var exists = db.CancellationEntries.Any(x =>
                x.Client == client &&
                x.Registration == reg &&
                x.DateRequestReceived == dateReceived);

            if (exists) continue;

            db.CancellationEntries.Add(new CancellationEntry
            {
                Client = client,
                Registration = reg,
                FleetNumber = string.IsNullOrWhiteSpace(fleet) ? null : fleet,
                MakeModel = string.IsNullOrWhiteSpace(makeModel) ? null : makeModel,
                UnitModel = string.IsNullOrWhiteSpace(unitModel) ? null : unitModel,
                DateRequestReceived = dateReceived,
                Reason = string.IsNullOrWhiteSpace(reason) ? null : reason,
                Notes = string.IsNullOrWhiteSpace(notes) ? null : notes,
                Status = CancellationStatus.Requested
            });
            added++;
        }

        try
        {
            db.SaveChanges();
        }
        catch (DbUpdateException)
        {
            // Duplicates rejected, continue
        }
        return added;
    }
}
