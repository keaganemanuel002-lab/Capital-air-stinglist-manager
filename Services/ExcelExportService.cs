using System;
using System.Collections.Generic;
using System.Linq;
using ClosedXML.Excel;
using StingListManager.Data;
using StingListManager.Data.Entities;

namespace StingListManager.Services;

public class ExcelExportService
{
    public void ExportMonthly(string filePath, int year, int month)
    {
        using var db = new AppDbContext();

        var start = new DateTime(year, month, 1);
        var end = start.AddMonths(1);

        var active = db.BillingEntries
            .Where(b => b.Status == BillingStatus.Active && b.ArchivedAt == null)
            .OrderBy(b => b.Company).ThenBy(b => b.Registration)
            .ToList();

        var removedThisMonth = db.BillingEntries
            .Where(b => b.Status == BillingStatus.Removed && b.ActiveTo != null && b.ActiveTo >= start && b.ActiveTo < end)
            .OrderBy(b => b.Company).ThenBy(b => b.Registration)
            .ToList();

        using var wb = new XLWorkbook();

        // Billing List sheet
        var ws = wb.Worksheets.Add("Billing List");
        WriteBillingHeader(ws);
        WriteBillingRows(ws, active);

        // Cancellations/Removals sheet
        var wr = wb.Worksheets.Add("Cancellations");
        WriteRemovalHeader(wr);
        WriteRemovalRows(wr, removedThisMonth);

        ws.Columns().AdjustToContents();
        wr.Columns().AdjustToContents();

        wb.SaveAs(filePath);
    }

    public void ExportCancellationsOnly(string filePath, int year, int month)
    {
        using var db = new AppDbContext();

        var start = new DateTime(year, month, 1);
        var end = start.AddMonths(1);

        var cancels = db.CancellationEntries
            .Where(c => c.DateRequestReceived != null
                     && c.DateRequestReceived >= start
                     && c.DateRequestReceived < end)
            .OrderBy(c => c.Client).ThenBy(c => c.Registration)
            .ToList();

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Cancellations");

        ws.Cell(1, 1).Value = "CLIENT";
        ws.Cell(1, 2).Value = "REGISTRATION";
        ws.Cell(1, 3).Value = "FLEET NUMBER";
        ws.Cell(1, 4).Value = "MAKE & MODEL";
        ws.Cell(1, 5).Value = "UNIT MODEL";
        ws.Cell(1, 6).Value = "Date Request received";
        ws.Cell(1, 7).Value = "Reason";
        ws.Cell(1, 8).Value = "Notes";
        ws.Cell(1, 9).Value = "Status";

        int r = 2;
        foreach (var c in cancels)
        {
            ws.Cell(r, 1).Value = c.Client;
            ws.Cell(r, 2).Value = c.Registration;
            ws.Cell(r, 3).Value = c.FleetNumber ?? "";
            ws.Cell(r, 4).Value = c.MakeModel ?? "";
            ws.Cell(r, 5).Value = c.UnitModel ?? "";
            ws.Cell(r, 6).Value = c.DateRequestReceived?.ToString("yyyy-MM-dd") ?? "";
            ws.Cell(r, 7).Value = c.Reason ?? "";
            ws.Cell(r, 8).Value = c.Notes ?? "";
            ws.Cell(r, 9).Value = c.Status.ToString();
            r++;
        }

        ws.Columns().AdjustToContents();
        wb.SaveAs(filePath);
    }

    private static void WriteBillingHeader(IXLWorksheet ws)
    {
        ws.Cell(1, 1).Value = "COMPANY";
        ws.Cell(1, 2).Value = "REG.";
        ws.Cell(1, 3).Value = "FLT. NO";
        ws.Cell(1, 4).Value = "TRACKING UNIT MAKE";
        ws.Cell(1, 5).Value = "NOTES";
        ws.Cell(1, 6).Value = "Reason";
    }

    private static void WriteBillingRows(IXLWorksheet ws, List<BillingEntry> rows)
    {
        int r = 2;
        foreach (var x in rows)
        {
            ws.Cell(r, 1).Value = x.Company;
            ws.Cell(r, 2).Value = x.Registration;
            ws.Cell(r, 3).Value = x.FleetNumber ?? "";
            ws.Cell(r, 4).Value = x.TrackingUnitMake ?? "";
            ws.Cell(r, 5).Value = x.Notes ?? "";
            ws.Cell(r, 6).Value = x.Reason ?? "";
            r++;
        }
    }

    private static void WriteRemovalHeader(IXLWorksheet ws)
    {
        ws.Cell(1, 1).Value = "CLIENT";
        ws.Cell(1, 2).Value = "REGISTRATION";
        ws.Cell(1, 3).Value = "FLEET NUMBER";
        ws.Cell(1, 4).Value = "MAKE & MODEL";
        ws.Cell(1, 5).Value = "UNIT MODEL";
        ws.Cell(1, 6).Value = "Date Request received";
        ws.Cell(1, 7).Value = "Reason";
        ws.Cell(1, 8).Value = "Notes";
    }

    private static void WriteRemovalRows(IXLWorksheet ws, List<BillingEntry> rows)
    {
        int r = 2;
        foreach (var x in rows)
        {
            ws.Cell(r, 1).Value = x.Company;
            ws.Cell(r, 2).Value = x.Registration;
            ws.Cell(r, 3).Value = x.FleetNumber ?? "";
            ws.Cell(r, 4).Value = x.TrackingUnitMake ?? "";
            ws.Cell(r, 5).Value = x.ActiveTo?.ToString("yyyy-MM-dd") ?? "";
            ws.Cell(r, 6).Value = x.Reason ?? "Removed";
            ws.Cell(r, 7).Value = x.Notes ?? "";
            r++;
        }
    }
}
