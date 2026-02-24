using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using StingListManager.Data;
using StingListManager.Data.Entities;

namespace StingListManager.Services;

public sealed class ExcelImportResult
{
    public int BillingAdded { get; init; }
    public int BillingSkippedDuplicates { get; init; }
    public int BillingStingCount { get; init; }
    public int BillingStingPlusCount { get; init; }
    public int BillingStingFmCount { get; init; }
    public int BillingUnknownPackageCount { get; init; }
    public int CancellationsAdded { get; init; }
    public string BillingSourceSheet { get; init; } = "";
}

public class ExcelImportService
{
    public ExcelImportResult ImportBillingAndCancellations(string filePath, string? actor = null)
    {
        actor ??= Environment.UserName;

        using var wb = new XLWorkbook(filePath);

        var billingSummary = ImportBillingSheet(wb, "STING List");
        if (billingSummary.Added == 0)
            billingSummary = ImportBillingSheet(wb, "Billing List");
        if (billingSummary.Added == 0)
            billingSummary = ImportBillingSheet(wb, "Sheet1"); // fallback

        int cancellationsAdded = ImportCancellationsSheet(wb, "Cancellations");
        var result = new ExcelImportResult
        {
            BillingAdded = billingSummary.Added,
            BillingSkippedDuplicates = billingSummary.SkippedDuplicates,
            BillingStingCount = billingSummary.StingCount,
            BillingStingPlusCount = billingSummary.StingPlusCount,
            BillingStingFmCount = billingSummary.StingFmCount,
            BillingUnknownPackageCount = billingSummary.UnknownCount,
            CancellationsAdded = cancellationsAdded,
            BillingSourceSheet = billingSummary.SourceSheet ?? string.Empty
        };

        // Log the import
        var filename = Path.GetFileName(filePath);
        var sourceSheet = string.IsNullOrWhiteSpace(result.BillingSourceSheet) ? "n/a" : result.BillingSourceSheet;
        var details =
            $"Imported {result.BillingAdded} billing (+{result.BillingSkippedDuplicates} skipped duplicates) + {result.CancellationsAdded} cancellations from {filename}. " +
            $"Billing source sheet: {sourceSheet}. " +
            $"Packages: STING {result.BillingStingCount}, STING PLUS {result.BillingStingPlusCount}, STING FM {result.BillingStingFmCount}, Unknown {result.BillingUnknownPackageCount}.";

        new AuditService().Log(actor, "IMPORT", "ExcelFile", null, null, details);
        return result;
    }

    private sealed class BillingImportSummary
    {
        public int Added { get; set; }
        public int SkippedDuplicates { get; set; }
        public int StingCount { get; set; }
        public int StingPlusCount { get; set; }
        public int StingFmCount { get; set; }
        public int UnknownCount { get; set; }
        public string? SourceSheet { get; set; }
    }

    private BillingImportSummary ImportBillingSheet(XLWorkbook wb, string sheetName)
    {
        var summary = new BillingImportSummary();
        if (!wb.Worksheets.TryGetWorksheet(sheetName, out var ws))
            return summary;

        summary.SourceSheet = sheetName;

        using var db = new AppDbContext();

        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
        if (lastRow < 2)
            return summary;

        var headers = BuildHeaderMap(ws);
        var companyCol = FindColumn(headers, "COMPANY", "CLIENT", "CLIENTACCOUNT", "ACCOUNT") ?? 1;
        var registrationCol = FindColumn(headers, "REGISTRATION", "REG", "VEHICLEREGISTRATION") ?? 2;
        var fleetCol = FindColumn(headers, "FLEETNUMBER", "FLEETNO", "FLTNO", "FLEET");
        var makeCol = FindColumn(headers, "MAKE");
        var modelCol = FindColumn(headers, "MODEL");
        var colourCol = FindColumn(headers, "COLOUR", "COLOR");
        var vinCol = FindColumn(headers, "VIN", "VINNUMBER");
        var trackingUnitMakeCol = FindColumn(headers, "TRACKINGUNITMAKE", "TRACKINGUNIT", "UNITMODEL", "DEVICEMODEL");
        var packageTypeCol = FindColumn(headers, "PACKAGETYPE", "PACKAGE", "STINGPACKAGE", "UNITTYPE", "UNITPACKAGE", "PRODUCTTYPE");
        var codeCol = FindColumn(headers, "CODE");
        var imeiCol = FindColumn(headers, "IMEI");
        var serialCol = FindColumn(headers, "SERIAL", "SERIALNUMBER");
        var iccidCol = FindColumn(headers, "ICCID");
        var simCol = FindColumn(headers, "SIM", "SIMNUMBER");
        var notesCol = FindColumn(headers, "NOTES", "NOTE");
        var reasonCol = FindColumn(headers, "REASON");
        var statusCol = FindColumn(headers, "STATUS");
        var activeFromCol = FindColumn(headers, "ACTIVEFROM", "INSTALLEDAT", "DATEINSTALLED", "CREATED");

        var existingActive = db.BillingEntries
            .Where(x => x.ArchivedAt == null && (x.Status == BillingStatus.Active || x.Status == BillingStatus.NotLoaded))
            .Select(x => new
            {
                x.RegistrationNorm,
                x.ImeiNorm,
                x.IccidNorm,
                x.SerialNumberNorm
            })
            .ToList();

        var registrationNorms = existingActive
            .Select(x => x.RegistrationNorm)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.Ordinal);
        var imeiNorms = existingActive
            .Select(x => x.ImeiNorm)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.Ordinal);
        var iccidNorms = existingActive
            .Select(x => x.IccidNorm)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.Ordinal);
        var serialNorms = existingActive
            .Select(x => x.SerialNumberNorm)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.Ordinal);

        for (int r = 2; r <= lastRow; r++)
        {
            var company = GetCellString(ws, r, companyCol);
            var reg = GetCellString(ws, r, registrationCol);
            var fleet = GetCellString(ws, r, fleetCol);
            var make = GetCellString(ws, r, makeCol);
            var model = GetCellString(ws, r, modelCol);
            var colour = GetCellString(ws, r, colourCol);
            var vin = GetCellString(ws, r, vinCol);
            var trackingUnitRaw = GetCellString(ws, r, trackingUnitMakeCol);
            var packageRaw = GetCellString(ws, r, packageTypeCol);
            var code = GetCellString(ws, r, codeCol);
            var imei = GetCellString(ws, r, imeiCol);
            var serial = GetCellString(ws, r, serialCol);
            var iccid = GetCellString(ws, r, iccidCol);
            var sim = GetCellString(ws, r, simCol);
            var notes = GetCellString(ws, r, notesCol);
            var reason = GetCellString(ws, r, reasonCol);
            var statusText = GetCellString(ws, r, statusCol);
            var activeFrom = ParseDateCell(ws, r, activeFromCol) ?? DateTime.UtcNow;
            var packageHint = ResolvePackageHint(ws, r);

            if (IsSummaryRow(company, reg))
                continue;

            // Skip empty rows
            if (string.IsNullOrWhiteSpace(company) && string.IsNullOrWhiteSpace(reg))
                continue;

            company = NormalizeText(company);
            reg = NormalizeText(reg).ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(reg))
                continue;

            var (unitFromCode, uniqueFromCode) = StingPackageClassifier.ParseCode(code);
            var trackingFromCode = string.IsNullOrWhiteSpace(StingPackageCatalog.Normalize(unitFromCode))
                ? unitFromCode
                : null;
            var trackingUnitMake = TrackingUnitMakeCatalog.Normalize(FirstNonEmpty(trackingUnitRaw, trackingFromCode));
            var packageType = StingPackageCatalog.Normalize(
                FirstNonEmpty(packageRaw, unitFromCode, packageHint, trackingUnitRaw, notes, reason));

            if (string.IsNullOrWhiteSpace(serial) && !string.IsNullOrWhiteSpace(uniqueFromCode))
                serial = uniqueFromCode;
            if (string.IsNullOrWhiteSpace(imei) && LooksLikeImei(uniqueFromCode))
                imei = uniqueFromCode;
            if (string.IsNullOrWhiteSpace(notes) && !string.IsNullOrWhiteSpace(packageHint))
                notes = packageHint;

            var registrationNorm = NormalizeComparable(reg);
            var imeiNorm = NormalizeDigits(imei);
            var iccidNorm = NormalizeDigits(iccid);
            var serialNorm = NormalizeComparable(serial);

            if (registrationNorms.Contains(registrationNorm)
                || (!string.IsNullOrWhiteSpace(imeiNorm) && imeiNorms.Contains(imeiNorm))
                || (!string.IsNullOrWhiteSpace(iccidNorm) && iccidNorms.Contains(iccidNorm))
                || (!string.IsNullOrWhiteSpace(serialNorm) && serialNorms.Contains(serialNorm)))
            {
                summary.SkippedDuplicates++;
                continue;
            }

            var status = ParseBillingStatus(statusText);

            var be = new BillingEntry
            {
                Company = company,
                Registration = reg,
                FleetNumber = string.IsNullOrWhiteSpace(fleet) ? null : fleet,
                Make = string.IsNullOrWhiteSpace(make) ? null : make,
                Model = string.IsNullOrWhiteSpace(model) ? null : model,
                Colour = string.IsNullOrWhiteSpace(colour) ? null : colour,
                VinNumber = string.IsNullOrWhiteSpace(vin) ? null : vin,
                TrackingUnitMake = string.IsNullOrWhiteSpace(trackingUnitMake) ? null : trackingUnitMake,
                StingPackageType = string.IsNullOrWhiteSpace(packageType) ? null : packageType,
                Imei = string.IsNullOrWhiteSpace(imei) ? null : imei,
                SerialNumber = string.IsNullOrWhiteSpace(serial) ? null : serial,
                Iccid = string.IsNullOrWhiteSpace(iccid) ? null : iccid,
                SimNumber = string.IsNullOrWhiteSpace(sim) ? null : sim,
                Notes = string.IsNullOrWhiteSpace(notes) ? null : notes,
                Reason = string.IsNullOrWhiteSpace(reason) ? null : reason,
                Status = status,
                ActiveFrom = activeFrom,
                RegistrationNorm = registrationNorm,
                ImeiNorm = imeiNorm,
                IccidNorm = iccidNorm,
                SerialNumberNorm = serialNorm
            };

            db.BillingEntries.Add(be);
            summary.Added++;
            registrationNorms.Add(registrationNorm);
            if (!string.IsNullOrWhiteSpace(imeiNorm)) imeiNorms.Add(imeiNorm);
            if (!string.IsNullOrWhiteSpace(iccidNorm)) iccidNorms.Add(iccidNorm);
            if (!string.IsNullOrWhiteSpace(serialNorm)) serialNorms.Add(serialNorm);

            switch (ResolvePackageFamilyForCounts(packageType, trackingUnitRaw, notes, reason, packageHint))
            {
                case StingPackageFamily.Sting:
                    summary.StingCount++;
                    break;
                case StingPackageFamily.StingPlus:
                    summary.StingPlusCount++;
                    break;
                case StingPackageFamily.StingFm:
                    summary.StingFmCount++;
                    break;
                default:
                    summary.UnknownCount++;
                    break;
            }
        }

        db.SaveChanges();
        return summary;
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

    private static bool IsSummaryRow(string company, string registration)
    {
        if (string.Equals(company, "TOTAL", StringComparison.OrdinalIgnoreCase))
            return true;

        if (string.Equals(registration, "TOTAL", StringComparison.OrdinalIgnoreCase))
            return true;

        return registration.EndsWith(" units", StringComparison.OrdinalIgnoreCase);
    }

    private static BillingStatus ParseBillingStatus(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return BillingStatus.Active;

        var normalized = value.Trim().ToUpperInvariant();
        if (normalized.Contains("REMOV", StringComparison.Ordinal))
            return BillingStatus.Removed;

        if (normalized.Contains("NOT LOADED", StringComparison.Ordinal)
            || normalized.Contains("NOTLOADED", StringComparison.Ordinal)
            || normalized.Contains("INACTIVE", StringComparison.Ordinal))
        {
            return BillingStatus.NotLoaded;
        }

        return BillingStatus.Active;
    }

    private static DateTime? ParseDateCell(IXLWorksheet ws, int row, int? col)
    {
        if (col is null || col <= 0)
            return null;

        var cell = ws.Cell(row, col.Value);
        if (cell.DataType == XLDataType.DateTime)
            return cell.GetDateTime();

        var text = cell.GetString().Trim();
        if (string.IsNullOrWhiteSpace(text))
            return null;

        if (DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out var parsed))
            return parsed;
        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
            return parsed;

        return null;
    }

    private static Dictionary<string, int> BuildHeaderMap(IXLWorksheet ws)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
        for (var col = 1; col <= lastCol; col++)
        {
            var key = NormalizeHeader(ws.Cell(1, col).GetString());
            if (string.IsNullOrWhiteSpace(key))
                continue;
            if (!map.ContainsKey(key))
                map[key] = col;
        }
        return map;
    }

    private static int? FindColumn(IReadOnlyDictionary<string, int> map, params string[] aliases)
    {
        foreach (var alias in aliases)
        {
            var normalizedAlias = NormalizeHeader(alias);
            if (map.TryGetValue(normalizedAlias, out var col))
                return col;
        }
        return null;
    }

    private static string NormalizeHeader(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return new string(value
            .Trim()
            .ToUpperInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray());
    }

    private static string GetCellString(IXLWorksheet ws, int row, int? col)
    {
        if (col is null || col <= 0)
            return string.Empty;
        return ws.Cell(row, col.Value).GetString().Trim();
    }

    private static string NormalizeComparable(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        return value.Trim().ToUpperInvariant();
    }

    private static string NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return string.Join(" ", value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string NormalizeDigits(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return new string(value.Where(char.IsDigit).ToArray());
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return null;
    }

    private static bool LooksLikeImei(string? value)
    {
        var digits = NormalizeDigits(value);
        return digits.Length is >= 14 and <= 17;
    }

    private static string? ResolvePackageHint(IXLWorksheet ws, int row)
    {
        var usedCells = ws.Row(row).CellsUsed();
        foreach (var cell in usedCells)
        {
            var text = cell.GetString().Trim();
            if (string.IsNullOrWhiteSpace(text))
                continue;

            if (StingPackageClassifier.Classify(text) != StingPackageFamily.Unknown)
                return text;
        }

        return null;
    }

    private static StingPackageFamily ResolvePackageFamilyForCounts(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            var family = StingPackageClassifier.Classify(candidate);
            if (family != StingPackageFamily.Unknown)
                return family;
        }

        return StingPackageFamily.Unknown;
    }
}
