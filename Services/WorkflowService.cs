using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using StingListManager.Data;
using StingListManager.Data.Entities;

namespace StingListManager.Services;

public class WorkflowService
{
    public (int jobId, string errorMessage) ApproveQuote(int quoteId, string actor, DateTime? scheduleDate = null)
    {
        try
        {
            using var db = new AppDbContext();

            var quote = db.Quotes.Include(q => q.LineItems).FirstOrDefault(q => q.Id == quoteId);
            if (quote is null) return (0, "Quote not found.");

            if (quote.Status == QuoteStatus.Approved)
            {
                return (0, "Quote is already approved.");
            }

            if (db.JobCards.Any(j => j.QuoteId == quote.Id))
            {
                return (0, "Job card already created for this quote.");
            }

            var logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sting_debug.log");
            var logMsg = $"[ApproveQuote] Quote fetched: Id={quote.Id}, Type={quote.Type}, Status={quote.Status}";
            System.IO.File.AppendAllText(logPath, logMsg + Environment.NewLine);
            
            logMsg = $"[ApproveQuote] Quote data: Company={quote.Company}, Reg={quote.Registration}, Fleet={quote.FleetNumber}";
            System.IO.File.AppendAllText(logPath, logMsg + Environment.NewLine);
            
            logMsg = $"[ApproveQuote] Quote vehicle fields: Make='{quote.Make}', Model='{quote.Model}', Colour='{quote.Colour}', VinNumber='{quote.VinNumber}'";
            System.IO.File.AppendAllText(logPath, logMsg + Environment.NewLine);
            
            logMsg = $"[ApproveQuote] Quote device fields: TrackingUnitMake='{quote.TrackingUnitMake}', Imei='{quote.Imei}', SerialNumber='{quote.SerialNumber}', Iccid='{quote.Iccid}', SimNumber='{quote.SimNumber}'";
            System.IO.File.AppendAllText(logPath, logMsg + Environment.NewLine);

            // Check document requirements
            var rules = new DocumentRules();
            if (!rules.HasRequiredDocsForQuoteApproval(quoteId, quote.Type, out var docMsg))
            {
                logMsg = $"[ApproveQuote] Document check failed: {docMsg}";
                System.IO.File.AppendAllText(logPath, logMsg + Environment.NewLine);
                return (0, docMsg);
            }

            // Guardrail: don't approve a removal quote without a linked cancellation request
            if (quote.Type == QuoteType.Removal)
            {
                var cancel = db.CancellationEntries.FirstOrDefault(c => c.QuoteId == quote.Id);
                if (cancel == null)
                {
                    logMsg = $"[ApproveQuote] Removal quote has no cancellation entry";
                    System.IO.File.AppendAllText(logPath, logMsg + Environment.NewLine);
                    return (0, "Removal quote must be linked to a cancellation request first.");
                }
            }

            quote.Status = QuoteStatus.Approved;
            quote.ApprovedAt = DateTime.UtcNow;
            logMsg = $"[ApproveQuote] Setting quote status to Approved";
            System.IO.File.AppendAllText(logPath, logMsg + Environment.NewLine);

            // Calculate total units from line items (STING, STING PLUS, STING FM only)
            int totalUnits = 0;
            if (quote.LineItems.Count > 0)
            {
                foreach (var item in quote.LineItems)
                {
                    if (IsProductTypeUnit(item.ProductType))
                    {
                        totalUnits += item.Quantity > 0 ? item.Quantity : 1;
                    }
                }
            }
            else if (!string.IsNullOrWhiteSpace(quote.ProductType) && IsProductTypeUnit(quote.ProductType))
            {
                totalUnits = 1;
            }

            // If no units found, default to 1 job card
            if (totalUnits == 0)
                totalUnits = 1;

            logMsg = $"[ApproveQuote] Total units calculated: {totalUnits}";
            System.IO.File.AppendAllText(logPath, logMsg + Environment.NewLine);

            // Reserve a contiguous block of job card numbers for this quote approval.
            var nextJobCardNumber = (db.JobCards.Select(x => (int?)x.JobCardNumber).Max() ?? 0) + 1;
            JobCard? firstJob = null;

            // Create job cards for each unit
            for (int i = 0; i < totalUnits; i++)
            {
                var job = new JobCard
                {
                    QuoteId = quote.Id,
                    Type = quote.Type == QuoteType.Install ? JobType.Install : JobType.Removal,
                    Status = JobStatus.Open,
                    Company = quote.Company,
                    Registration = quote.Registration ?? "",
                    FleetNumber = quote.FleetNumber,
                    Make = quote.Make,
                    Model = quote.Model,
                    Colour = quote.Colour,
                    VinNumber = quote.VinNumber,
                    TrackingUnitMake = quote.TrackingUnitMake,
                    Imei = quote.Imei,
                    SerialNumber = quote.SerialNumber,
                    Iccid = quote.Iccid,
                    SimNumber = quote.SimNumber,
                    ScheduledFor = scheduleDate,
                    Notes = quote.Notes
                };

                job.JobCardNumber = nextJobCardNumber + i;

                logMsg = $"[ApproveQuote] JobCard {i + 1}/{totalUnits} created. JobCardNumber={job.JobCardNumber}";
                System.IO.File.AppendAllText(logPath, logMsg + Environment.NewLine);

                db.JobCards.Add(job);
                
                if (i == 0)
                    firstJob = job;
            }

            logMsg = $"[ApproveQuote] Saving quote and {totalUnits} job cards...";
            System.IO.File.AppendAllText(logPath, logMsg + Environment.NewLine);
            
            var saved = db.SaveChanges();
            var firstJobId = firstJob?.Id ?? 0;
            logMsg = $"[ApproveQuote] SaveChanges returned: {saved}, FirstJobId={firstJobId}";
            System.IO.File.AppendAllText(logPath, logMsg + Environment.NewLine);

            // Log this approval
            new AuditService().Log(actor, "QUOTE_APPROVE", "Quote", quote.Id, quote.Registration, $"Quote {quote.Type} - Created {totalUnits} job cards");

            // Link cancellation workflow if this is a removal quote
            if (quote.Type == QuoteType.Removal)
            {
                var cancel = db.CancellationEntries.FirstOrDefault(c => c.QuoteId == quote.Id);
                if (cancel != null)
                {
                    cancel.Status = CancellationStatus.JobCreated;
                    cancel.JobCardId = firstJobId;
                    db.SaveChanges();
                }
            }

            logMsg = $"[ApproveQuote] Quote {quoteId} approved successfully, Created {totalUnits} job cards";
            System.IO.File.AppendAllText(logPath, logMsg + Environment.NewLine);
            
            return (firstJobId, "");
        }
        catch (Exception ex)
        {
            var logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sting_debug.log");
            var logMsg = $"[ApproveQuote] ERROR approving quote {quoteId}: {ex.Message}\n{ex.StackTrace}";
            System.IO.File.AppendAllText(logPath, logMsg + Environment.NewLine);
            return (0, $"Error approving quote: {ex.Message}");
        }
    }

    private static bool IsProductTypeUnit(string? productType)
    {
        if (string.IsNullOrWhiteSpace(productType))
            return false;

        var type = productType.Trim();
        return type.IndexOf("STING FM", StringComparison.OrdinalIgnoreCase) >= 0
            || type.IndexOf("STING PLUS", StringComparison.OrdinalIgnoreCase) >= 0
            || type.IndexOf("STING+", StringComparison.OrdinalIgnoreCase) >= 0
            || type.IndexOf("STING", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public (bool ok, string message) CompleteJobCard(int jobCardId, string actor, string? wialonToken = null)
    {
        return CompleteJobCardAsync(jobCardId, actor, wialonToken).GetAwaiter().GetResult();
    }

    public async Task<(bool ok, string message)> CompleteJobCardAsync(int jobCardId, string actor, string? wialonToken = null)
    {
        using var db = new AppDbContext();

        var job = db.JobCards.FirstOrDefault(j => j.Id == jobCardId);
        if (job is null) return (false, "Job card not found.");
        var jobReference = JobCardReferenceFormatter.Format(job.Type, job.JobCardNumber);

        if (job.Status == JobStatus.Completed)
            return (false, $"Job card {jobReference} is already completed.");

        if (job.Status == JobStatus.Cancelled)
            return (false, $"Job card {jobReference} is cancelled and cannot be completed.");

        if (job.Type == JobType.Install || job.Type == JobType.Transfer)
        {
            var missingTrackingFields = GetMissingTrackingFields(job);
            if (missingTrackingFields.Length > 0)
            {
                return (false, $"Cannot complete job card. Missing tracking unit information: {string.Join(", ", missingTrackingFields)}.");
            }
        }

        job.Status = JobStatus.Completed;
        job.CompletedAt = DateTime.UtcNow;

        if (job.Type == JobType.Install)
        {
            var installStatus = await ResolveInstallBillingStatusAsync(job.Imei, wialonToken);
            var be = FindActiveBillingEntryForUnit(db, job);
            var createdNewEntry = false;
            if (be is null)
            {
                be = new BillingEntry
                {
                    ActiveFrom = DateTime.UtcNow
                };
                db.BillingEntries.Add(be);
                createdNewEntry = true;
            }

            be.Company = job.Company;
            be.Registration = job.Registration;
            be.FleetNumber = job.FleetNumber;
            be.Make = job.Make;
            be.Model = job.Model;
            be.Colour = job.Colour;
            be.VinNumber = job.VinNumber;
            be.TrackingUnitMake = job.TrackingUnitMake;
            be.Imei = job.Imei;
            be.SerialNumber = job.SerialNumber;
            be.Iccid = job.Iccid;
            be.SimNumber = job.SimNumber;
            be.Notes = job.Notes;
            be.Status = installStatus;
            be.ActiveTo = null;
            be.ArchivedAt = null;
            be.Reason = createdNewEntry ? be.Reason : "Updated from completed install job";

            try
            {
                db.SaveChanges();
                // Log this billing add
                new AuditService().Log(
                    actor,
                    createdNewEntry ? "BILLING_ADD" : "BILLING_UPDATE",
                    "BillingEntry",
                    be.Id,
                    be.Registration,
                    createdNewEntry
                        ? "Created from install job completion"
                        : "Updated existing active unit from install job completion");

                var syncResult = await SyncInstallUnitToWialonAsync(job, wialonToken);
                if (syncResult.ok && be.Status != BillingStatus.Active)
                {
                    be.Status = BillingStatus.Active;
                    db.SaveChanges();
                }

                var message = be.Status == BillingStatus.NotLoaded
                    ? (createdNewEntry
                        ? "Install completed and billing entry created with status Not Loaded."
                        : "Install completed and existing billing entry updated with status Not Loaded.")
                    : (createdNewEntry
                        ? "Install completed and billing entry created with status Active."
                        : "Install completed and existing billing entry updated with status Active.");

                if (syncResult.attempted && !string.IsNullOrWhiteSpace(syncResult.message))
                    message = $"{message} {syncResult.message}";

                var flickswitchResult = await SyncSimDescriptionToFlickswitchAsync(job);
                if (flickswitchResult.attempted && !string.IsNullOrWhiteSpace(flickswitchResult.message))
                    message = $"{message} {flickswitchResult.message}";

                return (true, message);
            }
            catch (DbUpdateException)
            {
                return (false, "Duplicate unit detected. An active billing entry already exists for this IMEI/ICCID/Serial.");
            }
        }
        else if (job.Type == JobType.Transfer)
        {
            var missingTransferFields = GetMissingTransferFields(job);
            if (missingTransferFields.Length > 0)
                return (false, $"Cannot complete transfer. Missing destination details: {string.Join(", ", missingTransferFields)}.");

            var targetRegistrationNorm = NormalizeRegistration(job.Registration);
            if (string.IsNullOrWhiteSpace(targetRegistrationNorm))
                return (false, "Cannot complete transfer - destination registration is required.");

            var activeEntries = db.BillingEntries
                .Where(b => b.ArchivedAt == null && (b.Status == BillingStatus.Active || b.Status == BillingStatus.NotLoaded))
                .OrderByDescending(b => b.ActiveFrom)
                .ToList();

            var entry = activeEntries
                .FirstOrDefault(b => IsSameImei(b.Imei, job.Imei));

            entry ??= activeEntries
                .FirstOrDefault(b => string.Equals(NormalizeRegistration(b.Registration), targetRegistrationNorm, StringComparison.Ordinal));

            if (entry == null)
                return (false, "Transfer cannot complete - no matching active billing entry found for this unit.");

            var duplicateTargetEntry = activeEntries
                .FirstOrDefault(b =>
                    b.Id != entry.Id
                    && string.Equals(NormalizeRegistration(b.Registration), targetRegistrationNorm, StringComparison.Ordinal));

            if (duplicateTargetEntry != null)
                return (false, "Transfer cannot complete - another active unit already exists for the destination registration.");

            entry.Company = job.Company.Trim();
            entry.Registration = job.Registration.Trim().ToUpperInvariant();
            entry.RegistrationNorm = targetRegistrationNorm;
            entry.FleetNumber = TrimOrNull(job.FleetNumber);
            entry.Make = TrimOrNull(job.Make);
            entry.Model = TrimOrNull(job.Model);
            entry.Colour = TrimOrNull(job.Colour);
            entry.VinNumber = TrimOrNull(job.VinNumber);
            entry.TrackingUnitMake = TrimOrNull(job.TrackingUnitMake);
            entry.Imei = TrimOrNull(job.Imei);
            entry.SerialNumber = TrimOrNull(job.SerialNumber);
            entry.Iccid = TrimOrNull(job.Iccid);
            entry.SimNumber = TrimOrNull(job.SimNumber);
            entry.Notes = TrimOrNull(job.Notes);
            entry.Status = BillingStatus.Active;
            entry.ActiveTo = null;
            entry.ArchivedAt = null;
            entry.Reason = "Transferred";

            db.SaveChanges();

            new AuditService().Log(
                actor,
                "BILLING_TRANSFER",
                "BillingEntry",
                entry.Id,
                entry.Registration,
                $"Transferred unit {entry.Imei ?? "-"} to {entry.Company} / {entry.Registration}");

            var transferMessage = $"Transfer completed. Unit moved to {entry.Company} / {entry.Registration}.";
            var flickswitchResult = await SyncSimDescriptionToFlickswitchAsync(job);
            if (flickswitchResult.attempted && !string.IsNullOrWhiteSpace(flickswitchResult.message))
                transferMessage = $"{transferMessage} {flickswitchResult.message}";

            return (true, transferMessage);
        }
        else // Removal
        {
            var entry = db.BillingEntries
                .Where(b => b.Registration == job.Registration
                         && (b.Status == BillingStatus.Active || b.Status == BillingStatus.NotLoaded)
                         && b.ArchivedAt == null)
                .OrderByDescending(b => b.ActiveFrom)
                .FirstOrDefault();

            if (entry == null)
                return (false, "Removal cannot complete - no matching active billing entry found.");

            entry.Status = BillingStatus.Removed;
            entry.ActiveTo = DateTime.UtcNow;
            entry.ArchivedAt = DateTime.UtcNow;
            entry.Reason = "Removed";

            db.SaveChanges();

            // Log this removal
            new AuditService().Log(actor, "BILLING_REMOVE", "BillingEntry", entry.Id, entry.Registration, "Removed + archived via removal job");

            var cancel = db.CancellationEntries.FirstOrDefault(c => c.JobCardId == job.Id);
            if (cancel != null)
            {
                cancel.Status = CancellationStatus.Completed;
                db.SaveChanges();
            }

            return (true, "Removal completed and billing entry archived.");
        }
    }

    private static string[] GetMissingTrackingFields(JobCard job)
    {
        var missing = new System.Collections.Generic.List<string>();

        if (string.IsNullOrWhiteSpace(job.TrackingUnitMake))
            missing.Add("Tracking Unit Make");

        if (string.IsNullOrWhiteSpace(job.Imei))
            missing.Add("IMEI Number");

        if (string.IsNullOrWhiteSpace(job.SerialNumber))
            missing.Add("Serial Number");

        if (string.IsNullOrWhiteSpace(job.Iccid))
            missing.Add("ICCID");

        if (string.IsNullOrWhiteSpace(job.SimNumber))
            missing.Add("SIM Number");

        return missing.ToArray();
    }

    private static string[] GetMissingTransferFields(JobCard job)
    {
        var missing = new System.Collections.Generic.List<string>();

        if (string.IsNullOrWhiteSpace(job.Company))
            missing.Add("Company");

        if (string.IsNullOrWhiteSpace(job.Registration))
            missing.Add("Registration");

        return missing.ToArray();
    }

    private static string NormalizeRegistration(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return value.Trim().ToUpperInvariant();
    }

    private static string NormalizeDigits(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return new string(value.Where(char.IsDigit).ToArray());
    }

    private static bool IsSameImei(string? left, string? right)
    {
        var normalizedLeft = NormalizeDigits(left);
        var normalizedRight = NormalizeDigits(right);

        if (string.IsNullOrWhiteSpace(normalizedLeft) || string.IsNullOrWhiteSpace(normalizedRight))
            return false;

        return string.Equals(normalizedLeft, normalizedRight, StringComparison.Ordinal)
            || normalizedLeft.EndsWith(normalizedRight, StringComparison.Ordinal)
            || normalizedRight.EndsWith(normalizedLeft, StringComparison.Ordinal);
    }

    private static bool IsSameIccid(string? left, string? right)
    {
        var normalizedLeft = NormalizeDigits(left);
        var normalizedRight = NormalizeDigits(right);

        if (string.IsNullOrWhiteSpace(normalizedLeft) || string.IsNullOrWhiteSpace(normalizedRight))
            return false;

        return string.Equals(normalizedLeft, normalizedRight, StringComparison.Ordinal);
    }

    private static bool IsSameSerial(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;

        return string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static BillingEntry? FindActiveBillingEntryForUnit(AppDbContext db, JobCard job)
    {
        var activeEntries = db.BillingEntries
            .Where(b => b.ArchivedAt == null && (b.Status == BillingStatus.Active || b.Status == BillingStatus.NotLoaded))
            .OrderByDescending(b => b.ActiveFrom)
            .ToList();

        return activeEntries.FirstOrDefault(b => IsSameImei(b.Imei, job.Imei))
            ?? activeEntries.FirstOrDefault(b => IsSameIccid(b.Iccid, job.Iccid))
            ?? activeEntries.FirstOrDefault(b => IsSameSerial(b.SerialNumber, job.SerialNumber));
    }

    private static string? TrimOrNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static async Task<(bool attempted, bool ok, string message)> SyncSimDescriptionToFlickswitchAsync(JobCard job)
    {
        if (string.IsNullOrWhiteSpace(job.Iccid) && string.IsNullOrWhiteSpace(job.SimNumber))
            return (false, false, string.Empty);

        var company = string.IsNullOrWhiteSpace(job.Company) ? "Unknown Client" : job.Company.Trim();
        var vin = string.IsNullOrWhiteSpace(job.VinNumber) ? null : job.VinNumber.Trim();
        var description = string.IsNullOrWhiteSpace(vin)
            ? company
            : $"{company} - VIN {vin}";

        var flickswitch = new FlickswitchSimControlService();
        if (!flickswitch.IsConfigured())
            return (false, false, string.Empty);

        try
        {
            var update = await flickswitch.UpdateSimDescriptionAsync(job.Iccid, job.SimNumber, null, description);
            if (update.ok)
                return (true, true, $"Flickswitch description updated to '{description}'.");

            var reason = string.IsNullOrWhiteSpace(flickswitch.LastError)
                ? update.message
                : flickswitch.LastError;
            return (true, false, $"Flickswitch update failed: {reason}");
        }
        catch (Exception ex)
        {
            return (true, false, $"Flickswitch update failed: {ex.Message}");
        }
    }

    private static async Task<BillingStatus> ResolveInstallBillingStatusAsync(string? imei, string? wialonToken)
    {
        if (string.IsNullOrWhiteSpace(imei))
            return BillingStatus.NotLoaded;

        if (string.IsNullOrWhiteSpace(wialonToken))
            return BillingStatus.NotLoaded;

        WialonApiService? wialon = null;
        try
        {
            wialon = new WialonApiService(wialonToken.Trim());
            var connected = await wialon.TestConnectionAsync();
            if (!connected)
                return BillingStatus.NotLoaded;

            var isLoaded = await wialon.IsImeiLoadedAsync(imei);
            return isLoaded ? BillingStatus.Active : BillingStatus.NotLoaded;
        }
        catch
        {
            return BillingStatus.NotLoaded;
        }
        finally
        {
            if (wialon is not null)
            {
                await wialon.LogoutAndDisposeAsync();
            }
        }
    }

    private static async Task<(bool attempted, bool ok, string message)> SyncInstallUnitToWialonAsync(JobCard job, string? wialonToken)
    {
        if (string.IsNullOrWhiteSpace(wialonToken))
            return (false, false, string.Empty);

        WialonApiService? wialon = null;
        try
        {
            wialon = new WialonApiService(wialonToken.Trim());
            var connected = await wialon.TestConnectionAsync();
            if (!connected)
            {
                var reason = string.IsNullOrWhiteSpace(wialon.LastError) ? "failed to connect to Wialon." : wialon.LastError;
                return (true, false, $"Wialon sync failed: {reason}");
            }

            var syncResult = await wialon.SyncJobCardUnitAsync(job);
            return (true, syncResult.IsSuccess, syncResult.Message);
        }
        catch (Exception ex)
        {
            return (true, false, $"Wialon sync failed: {ex.Message}");
        }
        finally
        {
            if (wialon is not null)
            {
                await wialon.LogoutAndDisposeAsync();
            }
        }
    }
}
