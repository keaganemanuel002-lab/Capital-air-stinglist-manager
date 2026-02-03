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

        ApplyFormatting(ws);
        ApplyFormatting(wr);

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

        ApplyFormatting(ws);
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

    public void ExportBillingList(string filePath)
    {
        using var db = new AppDbContext();

        var entries = db.BillingEntries
            .Where(e => e.ArchivedAt == null && e.Status == BillingStatus.Active)
            .OrderBy(e => e.Company)
            .ThenBy(e => e.Registration)
            .ToList();

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Billing List");

        // Header - matching the page display
        ws.Cell(1, 1).Value = "#";
        ws.Cell(1, 2).Value = "COMPANY";
        ws.Cell(1, 3).Value = "REG";
        ws.Cell(1, 4).Value = "FLEET";
        ws.Cell(1, 5).Value = "VEHICLE DESCRIPTION";
        ws.Cell(1, 6).Value = "CODE";
        ws.Cell(1, 7).Value = "STING";
        ws.Cell(1, 8).Value = "STING PLUS";
        ws.Cell(1, 9).Value = "STING FM";
        ws.Cell(1, 10).Value = "LIVE TRACKING";

        // Data rows - grouped by company like on the page
        int r = 2;
        var grouped = entries.GroupBy(e => e.Company).OrderBy(g => g.Key);
        
        foreach (var group in grouped)
        {
            var rowNumber = 1;
            var entryCount = group.Count();
            
            foreach (var entry in group)
            {
                ws.Cell(r, 1).Value = rowNumber;
                ws.Cell(r, 2).Value = entry.Company;
                ws.Cell(r, 3).Value = entry.Registration;
                ws.Cell(r, 4).Value = entry.FleetNumber ?? "";
                ws.Cell(r, 5).Value = BuildVehicleDescription(entry);
                ws.Cell(r, 6).Value = BuildCode(entry);
                ws.Cell(r, 7).Value = "";
                ws.Cell(r, 8).Value = "";
                ws.Cell(r, 9).Value = "";
                ws.Cell(r, 10).Value = "";
                r++;
                rowNumber++;
            }
            
            // Total row
            ws.Cell(r, 1).Value = "TOTAL";
            ws.Cell(r, 2).Value = group.Key;
            ws.Cell(r, 3).Value = "";
            ws.Cell(r, 4).Value = "";
            ws.Cell(r, 5).Value = "";
            ws.Cell(r, 6).Value = "";
            ws.Cell(r, 7).Value = entryCount;
            ws.Cell(r, 8).Value = "";
            ws.Cell(r, 9).Value = "";
            ws.Cell(r, 10).Value = entryCount;
            r++;
        }

        ApplyFormatting(ws);
        wb.SaveAs(filePath);
    }

    private static string BuildVehicleDescription(BillingEntry entry)
    {
        return string.IsNullOrWhiteSpace(entry.Make) || string.IsNullOrWhiteSpace(entry.Model)
            ? entry.Make ?? ""
            : $"{entry.Make} {entry.Model}";
    }

    private static string BuildCode(BillingEntry entry)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(entry.Imei)) parts.Add($"IMEI: {entry.Imei}");
        if (!string.IsNullOrWhiteSpace(entry.Iccid)) parts.Add($"ICCID: {entry.Iccid}");
        return string.Join(", ", parts);
    }

    public void ExportStingList(string filePath)
    {
        using var db = new AppDbContext();

        var entries = db.BillingEntries
            .Where(e => e.ArchivedAt == null && e.Status == BillingStatus.Active)
            .OrderByDescending(e => e.ActiveFrom)
            .ToList();

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("STING List");

        // Header - matching the page display exactly
        ws.Cell(1, 1).Value = "COMPANY";
        ws.Cell(1, 2).Value = "REG";
        ws.Cell(1, 3).Value = "FLEET";
        ws.Cell(1, 4).Value = "MAKE";
        ws.Cell(1, 5).Value = "MODEL";
        ws.Cell(1, 6).Value = "COLOUR";
        ws.Cell(1, 7).Value = "VIN";
        ws.Cell(1, 8).Value = "IMEI";
        ws.Cell(1, 9).Value = "SERIAL #";
        ws.Cell(1, 10).Value = "ICCID";
        ws.Cell(1, 11).Value = "NOTES";
        ws.Cell(1, 12).Value = "STATUS";
        ws.Cell(1, 13).Value = "ACTIVE FROM";

        // Data rows
        int r = 2;
        foreach (var e in entries)
        {
            ws.Cell(r, 1).Value = e.Company;
            ws.Cell(r, 2).Value = e.Registration;
            ws.Cell(r, 3).Value = e.FleetNumber ?? "";
            ws.Cell(r, 4).Value = e.Make ?? "";
            ws.Cell(r, 5).Value = e.Model ?? "";
            ws.Cell(r, 6).Value = e.Colour ?? "";
            ws.Cell(r, 7).Value = e.VinNumber ?? "";
            ws.Cell(r, 8).Value = e.Imei ?? "";
            ws.Cell(r, 9).Value = e.SerialNumber ?? "";
            ws.Cell(r, 10).Value = e.Iccid ?? "";
            ws.Cell(r, 11).Value = e.Notes ?? "";
            ws.Cell(r, 12).Value = e.Status.ToString();
            ws.Cell(r, 13).Value = e.ActiveFrom.ToString("yyyy-MM-dd HH:mm");
            r++;
        }

        ApplyFormatting(ws);
        wb.SaveAs(filePath);
    }

    private static void ApplyFormatting(IXLWorksheet ws)
    {
        // Set font to Arial size 10 for all cells
        ws.Cells().Style.Font.FontName = "Arial";
        ws.Cells().Style.Font.FontSize = 10;

        // Auto-fit columns
        ws.Columns().AdjustToContents();

        // Set minimum width to ensure columns are visible
        foreach (var col in ws.Columns())
        {
            if (col.Width < 8)
                col.Width = 8;
        }
    }
}
