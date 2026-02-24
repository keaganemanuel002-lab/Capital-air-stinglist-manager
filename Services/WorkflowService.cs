using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using StingListManager.Data;
using StingListManager.Data.Entities;

namespace StingListManager.Services;

public class WorkflowService
{
    public const string NoRemovalJobCardMarker = "[NO_REMOVAL_JOB_CARD]";
    public const string TransferFeeOnlyQuoteMarker = "[TRANSFER_FEE_ONLY]";
    public const string TransferInstallFeeCode = "TRANSFER-INSTALL-FEE";
    public const decimal TransferInstallFeeExVat = 450m;
    public const string InspectionFeeCode = "AUTO-INSPECTION-FEE";
    public const decimal InspectionFeeExVat = 450m;

    public (int jobId, string errorMessage) ApproveQuote(int quoteId, string actor, DateTime? scheduleDate = null)
    {
        try
        {
            using var db = new AppDbContext();

            var quote = db.Quotes.Include(q => q.LineItems).FirstOrDefault(q => q.Id == quoteId);
            if (quote is null) return (0, "Quote not found.");
            var isLiveTrackingOnlyQuote = IsLiveTrackingOnlyQuote(quote);
            var isTransferFeeOnlyQuote = IsTransferFeeOnlyQuote(quote);

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

            // Guardrail: don't approve a removal quote without a linked cancellation request,
            // except when this is a live-tracking-only removal (no unit removal job card needed).
            if (quote.Type == QuoteType.Removal && !isLiveTrackingOnlyQuote)
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

            if (quote.Type == QuoteType.Install && isTransferFeeOnlyQuote)
            {
                var savedTransferFeeOnly = db.SaveChanges();
                logMsg = $"[ApproveQuote] SaveChanges returned: {savedTransferFeeOnly}, no job card created (transfer-fee-only quote).";
                System.IO.File.AppendAllText(logPath, logMsg + Environment.NewLine);

                new AuditService().Log(
                    actor,
                    "QUOTE_APPROVE",
                    "Quote",
                    quote.Id,
                    quote.Registration,
                    "Quote install - transfer fee only, no job card required");

                return (-1, "Quote approved. Transfer installation fee only, no job card required.");
            }

            if (quote.Type == QuoteType.Removal && !isLiveTrackingOnlyQuote)
            {
                var activeRemovalEntry = FindActiveBillingEntryForRemovalQuote(db, quote);
                var isOutOfWarranty = activeRemovalEntry is not null
                                      && !WarrantyService.IsWithinWarranty(activeRemovalEntry.ActiveFrom);

                if (isOutOfWarranty && HasWorkflowMarker(quote.Notes, NoRemovalJobCardMarker))
                {
                    if (activeRemovalEntry is null)
                        return (0, "Could not find an active STING entry for this removal quote.");

                    activeRemovalEntry.Status = BillingStatus.Removed;
                    activeRemovalEntry.ActiveTo = DateTime.UtcNow;
                    activeRemovalEntry.ArchivedAt = DateTime.UtcNow;
                    activeRemovalEntry.Reason = "Removed (out of warranty, no job card)";

                    var cancel = db.CancellationEntries.FirstOrDefault(c => c.QuoteId == quote.Id);
                    if (cancel is not null)
                    {
                        cancel.Status = CancellationStatus.Completed;
                        cancel.JobCardId = null;
                    }

                    var savedOutOfWarranty = db.SaveChanges();
                    logMsg = $"[ApproveQuote] SaveChanges returned: {savedOutOfWarranty}, no job card created (out-of-warranty removal).";
                    System.IO.File.AppendAllText(logPath, logMsg + Environment.NewLine);

                    new AuditService().Log(
                        actor,
                        "QUOTE_APPROVE",
                        "Quote",
                        quote.Id,
                        quote.Registration,
                        "Quote removal - out of warranty, approved without job card");

                    return (-1, "Quote approved. Unit is out of warranty and was removed without creating a job card.");
                }
            }

            var totalUnits = CalculateUnitCount(quote);

            // Live-tracking-only quotes are approved without creating job cards.
            if (totalUnits == 0 && isLiveTrackingOnlyQuote)
            {
                var savedNoJob = db.SaveChanges();
                logMsg = $"[ApproveQuote] SaveChanges returned: {savedNoJob}, no job card created (live tracking only quote).";
                System.IO.File.AppendAllText(logPath, logMsg + Environment.NewLine);

                var details = quote.Type == QuoteType.Removal
                    ? "Quote removal - live tracking disabled, no job card required"
                    : quote.Type == QuoteType.Inspection
                        ? "Quote inspection - live tracking only, no job card required"
                        : "Quote install - live tracking only, no job card required";

                new AuditService().Log(actor, "QUOTE_APPROVE", "Quote", quote.Id, quote.Registration, details);

                var message = quote.Type == QuoteType.Removal
                    ? "Quote approved. Live tracking removed for this client. No job card created."
                    : quote.Type == QuoteType.Inspection
                        ? "Quote approved. Inspection live-tracking-only quote does not require a job card."
                        : "Quote approved. Live tracking-only quote does not require a job card.";

                return (-1, message);
            }

            // If no units found, default to 1 job card (non-live-tracking-only flow).
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
                    Type = quote.Type switch
                    {
                        QuoteType.Removal => JobType.Removal,
                        QuoteType.Inspection => JobType.Inspection,
                        _ => JobType.Install
                    },
                    Status = JobStatus.Open,
                    Company = quote.Company,
                    Registration = quote.Registration ?? "",
                    FleetNumber = quote.FleetNumber,
                    Make = quote.Make,
                    Model = quote.Model,
                    Colour = quote.Colour,
                    VinNumber = quote.VinNumber,
                    TrackingUnitMake = TrackingUnitMakeCatalog.Normalize(quote.TrackingUnitMake),
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
            
            return (firstJobId, $"Quote approved. Created {totalUnits} job card(s).");
        }
        catch (Exception ex)
        {
            var logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sting_debug.log");
            var logMsg = $"[ApproveQuote] ERROR approving quote {quoteId}: {ex.Message}\n{ex.StackTrace}";
            System.IO.File.AppendAllText(logPath, logMsg + Environment.NewLine);
            return (0, $"Error approving quote: {ex.Message}");
        }
    }

    private static int CalculateUnitCount(Quote quote)
    {
        var totalUnits = 0;

        if (quote.LineItems.Count > 0)
        {
            foreach (var item in quote.LineItems)
            {
                if (!IsUnitLineItem(item))
                    continue;

                totalUnits += item.Quantity > 0 ? item.Quantity : 1;
            }

            return totalUnits;
        }

        if (!string.IsNullOrWhiteSpace(quote.ProductType) && IsProductTypeUnit(quote.ProductType))
            return 1;

        return 0;
    }

    private static bool IsLiveTrackingOnlyQuote(Quote quote)
    {
        if (!QuoteHasLiveTracking(quote))
            return false;

        if (quote.LineItems.Count > 0)
            return !quote.LineItems.Any(IsUnitLineItem);

        return !IsProductTypeUnit(quote.ProductType);
    }

    private static bool IsTransferFeeOnlyQuote(Quote quote)
    {
        if (quote.LineItems.Count == 0)
            return false;

        var meaningfulLines = quote.LineItems
            .Where(item =>
                item.Quantity > 0
                || item.UnitPriceExVat != 0m
                || item.LineTotalExVat != 0m
                || !string.IsNullOrWhiteSpace(item.ProductCode)
                || !string.IsNullOrWhiteSpace(item.ProductName)
                || !string.IsNullOrWhiteSpace(item.ProductType))
            .ToList();

        if (meaningfulLines.Count == 0)
            return false;

        return meaningfulLines.All(IsTransferInstallFeeLineItem);
    }

    private static bool QuoteHasLiveTracking(Quote quote)
    {
        if (quote.IncludesAppLiveTracking)
            return true;

        if (quote.LineItems.Count > 0)
            return quote.LineItems.Any(IsLiveTrackingLineItem);

        if (!string.IsNullOrWhiteSpace(quote.ProductType)
            && quote.ProductType.IndexOf("LIVE TRACKING", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        return false;
    }

    private static bool IsUnitLineItem(QuoteLineItem item)
    {
        if (IsLiveTrackingLineItem(item))
            return false;

        return IsProductTypeUnit(item.ProductType)
               || IsProductTypeUnit(item.ProductName)
               || IsProductTypeUnit(item.ProductCode);
    }

    private static bool IsLiveTrackingLineItem(QuoteLineItem item)
    {
        if (item.IncludesAppLiveTracking)
            return true;

        if (string.Equals(item.ProductCode, "APP-LIVE-TRACKING", StringComparison.OrdinalIgnoreCase))
            return true;

        return ContainsLiveTracking(item.ProductType)
               || ContainsLiveTracking(item.ProductName)
               || ContainsLiveTracking(item.ProductCode);
    }

    private static bool IsTransferInstallFeeLineItem(QuoteLineItem item)
    {
        if (string.Equals(item.ProductCode, TransferInstallFeeCode, StringComparison.OrdinalIgnoreCase))
            return true;

        return ContainsTransferInstallFee(item.ProductType)
               || ContainsTransferInstallFee(item.ProductName)
               || ContainsTransferInstallFee(item.Description);
    }

    private static bool IsProductTypeUnit(string? productType)
    {
        if (string.IsNullOrWhiteSpace(productType))
            return false;

        var type = productType.Trim().ToUpperInvariant();
        if (!type.Contains("STING", StringComparison.Ordinal))
            return false;

        if (type.Contains("LIVE TRACKING", StringComparison.Ordinal))
            return false;

        if (type.Contains("MONTHLY", StringComparison.Ordinal))
            return false;

        return true;
    }

    private static bool ContainsLiveTracking(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return value.IndexOf("LIVE TRACKING", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool ContainsTransferInstallFee(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var normalized = value.Trim().ToUpperInvariant();
        return normalized.Contains("TRANSFER", StringComparison.Ordinal)
               && normalized.Contains("INSTALL", StringComparison.Ordinal);
    }

    private static bool HasWorkflowMarker(string? notes, string marker)
    {
        if (string.IsNullOrWhiteSpace(notes))
            return false;

        return notes.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public (bool ok, string message) CompleteJobCard(int jobCardId, string actor, string? wialonToken = null)
    {
        return CompleteJobCardAsync(jobCardId, actor, wialonToken).GetAwaiter().GetResult();
    }

    public (bool ok, string message) UpdateCompletedJobCardRegistration(int jobCardId, string? newRegistration, string actor)
    {
        using var db = new AppDbContext();

        var job = db.JobCards.FirstOrDefault(j => j.Id == jobCardId);
        if (job is null)
            return (false, "Job card not found.");

        if (job.Status != JobStatus.Completed)
            return (false, "Only completed job cards can be updated this way.");

        if (job.Type == JobType.Removal)
            return (false, "Registration updates are only supported for completed install/transfer job cards.");

        var normalizedNewRegistration = NormalizeRegistration(newRegistration);
        if (string.IsNullOrWhiteSpace(normalizedNewRegistration))
            return (false, "Registration is required.");

        var normalizedCurrentRegistration = NormalizeRegistration(job.Registration);
        if (string.Equals(normalizedCurrentRegistration, normalizedNewRegistration, StringComparison.Ordinal))
            return (false, "Registration is already set to that value.");

        var activeEntries = db.BillingEntries
            .Where(b => b.ArchivedAt == null && (b.Status == BillingStatus.Active || b.Status == BillingStatus.NotLoaded))
            .OrderByDescending(b => b.ActiveFrom)
            .ToList();

        var billingEntry = FindActiveBillingEntryForUnit(db, job);
        if (billingEntry is null && !string.IsNullOrWhiteSpace(normalizedCurrentRegistration))
        {
            billingEntry = activeEntries.FirstOrDefault(b =>
                string.Equals(NormalizeRegistration(b.Registration), normalizedCurrentRegistration, StringComparison.Ordinal));
        }

        if (billingEntry is null)
            return (false, "Could not find the related active STING/Billing entry for this completed job card.");

        var duplicate = activeEntries.FirstOrDefault(b =>
            b.Id != billingEntry.Id
            && string.Equals(NormalizeRegistration(b.Registration), normalizedNewRegistration, StringComparison.Ordinal));

        if (duplicate is not null)
            return (false, "Another active billing entry already exists with that registration.");

        var oldRegistration = NormalizeRegistration(job.Registration);
        job.Registration = normalizedNewRegistration;
        billingEntry.Registration = normalizedNewRegistration;
        billingEntry.RegistrationNorm = normalizedNewRegistration;
        if (string.IsNullOrWhiteSpace(billingEntry.Reason))
            billingEntry.Reason = "Registration updated from completed job card";

        db.SaveChanges();

        new AuditService().Log(
            actor,
            "JOBCARD_REGISTRATION_UPDATE",
            "JobCard",
            job.Id,
            normalizedNewRegistration,
            $"Updated completed job card registration from {oldRegistration} to {normalizedNewRegistration}.");

        new AuditService().Log(
            actor,
            "BILLING_REGISTRATION_UPDATE",
            "BillingEntry",
            billingEntry.Id,
            normalizedNewRegistration,
            $"Updated linked billing registration from {oldRegistration} to {normalizedNewRegistration} via completed job card.");

        return (true, $"Registration updated to {normalizedNewRegistration}. STING and Billing lists will reflect this change.");
    }

    public (bool ok, string message) UpdateCompletedJobCardGridLocation(int jobCardId, string? newGridLocation, string actor)
    {
        using var db = new AppDbContext();

        var job = db.JobCards.FirstOrDefault(j => j.Id == jobCardId);
        if (job is null)
            return (false, "Job card not found.");

        if (job.Status != JobStatus.Completed)
            return (false, "Only completed job cards can be updated this way.");

        var normalizedCurrent = string.IsNullOrWhiteSpace(job.GridLocation)
            ? null
            : job.GridLocation.Trim().ToUpperInvariant();
        var normalizedNew = string.IsNullOrWhiteSpace(newGridLocation)
            ? null
            : newGridLocation.Trim().ToUpperInvariant();

        if (string.Equals(normalizedCurrent, normalizedNew, StringComparison.Ordinal))
            return (false, "Grid location is already set to that value.");

        job.GridLocation = normalizedNew;
        db.SaveChanges();

        var oldValue = string.IsNullOrWhiteSpace(normalizedCurrent) ? "<empty>" : normalizedCurrent;
        var newValue = string.IsNullOrWhiteSpace(normalizedNew) ? "<empty>" : normalizedNew;

        new AuditService().Log(
            actor,
            "JOBCARD_GRIDLOCATION_UPDATE",
            "JobCard",
            job.Id,
            job.Registration,
            $"Updated completed job card grid location from {oldValue} to {newValue}.");

        return (true, string.IsNullOrWhiteSpace(normalizedNew)
            ? "Grid location cleared for completed job card."
            : $"Grid location updated to {normalizedNew}.");
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

        var requiresTrackingFields =
            job.Type == JobType.Install
            || job.Type == JobType.Transfer
            || (job.Type == JobType.Inspection && job.InspectionOutcome == InspectionOutcome.UnitReplaced);

        if (requiresTrackingFields)
        {
            var missingTrackingFields = GetMissingTrackingFields(job);
            if (missingTrackingFields.Length > 0)
            {
                return (false, $"Cannot complete job card. Missing tracking unit information: {string.Join(", ", missingTrackingFields)}.");
            }
        }

        Quote? linkedQuote = null;
        if (job.QuoteId.HasValue)
        {
            linkedQuote = db.Quotes
                .Include(q => q.LineItems)
                .FirstOrDefault(q => q.Id == job.QuoteId.Value);
        }

        var linkedInspectionQuote = job.Type == JobType.Inspection && linkedQuote?.Type == QuoteType.Inspection
            ? linkedQuote
            : null;

        if (job.Type == JobType.Inspection
            && job.InspectionOutcome == InspectionOutcome.UnitReplaced
            && linkedInspectionQuote is not null)
        {
            var hasReplacementUnitLine = linkedInspectionQuote.LineItems.Any(IsUnitLineItem);
            if (!hasReplacementUnitLine)
            {
                return (false, "Inspection replacement requires a STING/STING PLUS/STING FM line item on the linked quote.");
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
            be.TrackingUnitMake = TrackingUnitMakeCatalog.Normalize(job.TrackingUnitMake);
            be.StingPackageType = ResolvePackageTypeFromQuote(linkedQuote, be.StingPackageType);
            be.Imei = job.Imei;
            be.SerialNumber = job.SerialNumber;
            be.Iccid = job.Iccid;
            be.SimNumber = job.SimNumber;
            be.Notes = job.Notes;
            be.Status = installStatus;
            be.ActiveFrom = job.CompletedAt ?? DateTime.UtcNow;
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
        else if (job.Type == JobType.Inspection)
        {
            if (job.InspectionOutcome == InspectionOutcome.InspectionOnly)
            {
                if (linkedInspectionQuote is not null)
                    EnsureInspectionFeeLineItem(linkedInspectionQuote);

                db.SaveChanges();

                new AuditService().Log(
                    actor,
                    "JOBCARD_INSPECTION_COMPLETE",
                    "JobCard",
                    job.Id,
                    job.Registration,
                    "Inspection completed without replacing unit.");

                return (true, "Inspection completed. Unit was repaired; only the inspection fee applies.");
            }

            var installStatus = await ResolveInstallBillingStatusAsync(job.Imei, wialonToken);
            var activeEntries = db.BillingEntries
                .Where(b => b.ArchivedAt == null && (b.Status == BillingStatus.Active || b.Status == BillingStatus.NotLoaded))
                .OrderByDescending(b => b.ActiveFrom)
                .ToList();

            var normalizedRegistration = NormalizeRegistration(job.Registration);
            var normalizedCompany = (job.Company ?? string.Empty).Trim();

            BillingEntry? be = null;
            if (!string.IsNullOrWhiteSpace(normalizedRegistration))
            {
                be = activeEntries.FirstOrDefault(b =>
                    string.Equals(NormalizeRegistration(b.Registration), normalizedRegistration, StringComparison.Ordinal)
                    && string.Equals((b.Company ?? string.Empty).Trim(), normalizedCompany, StringComparison.OrdinalIgnoreCase));
            }

            be ??= FindActiveBillingEntryForUnit(db, job);

            if (be is null)
            {
                return (false, "Inspection replacement cannot complete - no matching active STING/Billing entry found to update.");
            }

            var duplicateByIdentifiers = activeEntries.FirstOrDefault(b =>
                b.Id != be.Id
                && (IsSameImei(b.Imei, job.Imei)
                    || IsSameIccid(b.Iccid, job.Iccid)
                    || IsSameSerial(b.SerialNumber, job.SerialNumber)));

            if (duplicateByIdentifiers is not null)
            {
                return (false, "Inspection replacement cannot complete - another active entry already uses this IMEI/ICCID/Serial.");
            }

            be.Company = string.IsNullOrWhiteSpace(job.Company) ? be.Company : job.Company.Trim();
            be.Registration = string.IsNullOrWhiteSpace(job.Registration) ? be.Registration : job.Registration.Trim().ToUpperInvariant();
            be.RegistrationNorm = NormalizeRegistration(be.Registration);
            be.FleetNumber = TrimOrNull(job.FleetNumber);
            be.Make = TrimOrNull(job.Make);
            be.Model = TrimOrNull(job.Model);
            be.Colour = TrimOrNull(job.Colour);
            be.VinNumber = TrimOrNull(job.VinNumber);
            be.TrackingUnitMake = TrackingUnitMakeCatalog.Normalize(job.TrackingUnitMake);
            be.StingPackageType = ResolvePackageTypeFromQuote(linkedInspectionQuote, be.StingPackageType);
            be.Imei = TrimOrNull(job.Imei);
            be.SerialNumber = TrimOrNull(job.SerialNumber);
            be.Iccid = TrimOrNull(job.Iccid);
            be.SimNumber = TrimOrNull(job.SimNumber);
            be.Notes = TrimOrNull(job.Notes);
            be.Status = installStatus;
            be.ActiveFrom = job.CompletedAt ?? DateTime.UtcNow;
            be.ActiveTo = null;
            be.ArchivedAt = null;
            be.Reason = "Updated from completed inspection replacement";

            if (linkedInspectionQuote is not null)
            {
                RemoveInspectionFeeLineItems(linkedInspectionQuote);
                linkedInspectionQuote.AmountExVat = linkedInspectionQuote.LineItems.Sum(x => x.LineTotalExVat);
            }

            try
            {
                db.SaveChanges();

                new AuditService().Log(
                    actor,
                    "BILLING_INSPECTION_REPLACE",
                    "BillingEntry",
                    be.Id,
                    be.Registration,
                    "Updated existing active entry from completed inspection replacement.");

                var syncResult = await SyncInstallUnitToWialonAsync(job, wialonToken);
                if (syncResult.ok && be.Status != BillingStatus.Active)
                {
                    be.Status = BillingStatus.Active;
                    db.SaveChanges();
                }

                var message = be.Status == BillingStatus.NotLoaded
                    ? "Inspection completed. Unit replaced and existing billing entry updated with status Not Loaded."
                    : "Inspection completed. Unit replaced and existing billing entry updated with status Active.";

                if (syncResult.attempted && !string.IsNullOrWhiteSpace(syncResult.message))
                    message = $"{message} {syncResult.message}";

                var flickswitchResult = await SyncSimDescriptionToFlickswitchAsync(job);
                if (flickswitchResult.attempted && !string.IsNullOrWhiteSpace(flickswitchResult.message))
                    message = $"{message} {flickswitchResult.message}";

                return (true, message);
            }
            catch (DbUpdateException)
            {
                return (false, "Duplicate unit detected. Another active billing entry already exists for this IMEI/ICCID/Serial.");
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
            entry.TrackingUnitMake = TrackingUnitMakeCatalog.Normalize(job.TrackingUnitMake);
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

    private static void EnsureInspectionFeeLineItem(Quote quote)
    {
        if (quote.LineItems.Any(IsInspectionFeeLineItem))
        {
            quote.AmountExVat = quote.LineItems.Sum(x => x.LineTotalExVat);
            return;
        }

        var lineNumber = quote.LineItems.Count == 0
            ? 1
            : quote.LineItems.Max(x => x.LineNumber) + 1;

        var unitPrice = GetDefaultInspectionFeeExVat();
        quote.LineItems.Add(new QuoteLineItem
        {
            LineNumber = lineNumber,
            ProductType = "Inspection Fee",
            ProductCode = InspectionFeeCode,
            ProductName = "Inspection Fee",
            Quantity = 1,
            UnitPriceExVat = unitPrice,
            LineTotalExVat = unitPrice,
            IsVatExempt = false,
            Description = "Auto-added inspection fee"
        });

        quote.AmountExVat = quote.LineItems.Sum(x => x.LineTotalExVat);
    }

    private static void RemoveInspectionFeeLineItems(Quote quote)
    {
        var inspectionFeeRows = quote.LineItems
            .Where(IsInspectionFeeLineItem)
            .ToList();

        if (inspectionFeeRows.Count == 0)
            return;

        foreach (var row in inspectionFeeRows)
        {
            quote.LineItems.Remove(row);
        }

        var lineNumber = 1;
        foreach (var row in quote.LineItems.OrderBy(x => x.LineNumber))
        {
            row.LineNumber = lineNumber++;
        }
    }

    private static bool IsInspectionFeeLineItem(QuoteLineItem item)
    {
        if (string.Equals(item.ProductCode, InspectionFeeCode, StringComparison.OrdinalIgnoreCase))
            return true;

        if (string.Equals(item.ProductCode, "INSPECTION-FEE", StringComparison.OrdinalIgnoreCase))
            return true;

        if (string.Equals(item.ProductName, "Inspection Fee", StringComparison.OrdinalIgnoreCase))
            return true;

        return string.Equals(item.Description, "Auto-added inspection fee", StringComparison.OrdinalIgnoreCase);
    }

    private static decimal GetDefaultInspectionFeeExVat()
    {
        try
        {
            var settings = new SettingsService().Load();
            return settings.DefaultInspectionFeeExVat > 0
                ? settings.DefaultInspectionFeeExVat
                : InspectionFeeExVat;
        }
        catch
        {
            return InspectionFeeExVat;
        }
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

    private static BillingEntry? FindActiveBillingEntryForRemovalQuote(AppDbContext db, Quote quote)
    {
        var activeEntries = db.BillingEntries
            .Where(b => b.ArchivedAt == null && (b.Status == BillingStatus.Active || b.Status == BillingStatus.NotLoaded))
            .OrderByDescending(b => b.ActiveFrom)
            .ToList();

        var byUnit = activeEntries.FirstOrDefault(b => IsSameImei(b.Imei, quote.Imei))
                     ?? activeEntries.FirstOrDefault(b => IsSameIccid(b.Iccid, quote.Iccid))
                     ?? activeEntries.FirstOrDefault(b => IsSameSerial(b.SerialNumber, quote.SerialNumber));
        if (byUnit is not null)
            return byUnit;

        var registration = NormalizeRegistration(quote.Registration);
        if (string.IsNullOrWhiteSpace(registration))
            return null;

        return activeEntries.FirstOrDefault(b =>
            string.Equals(NormalizeRegistration(b.Registration), registration, StringComparison.Ordinal)
            && string.Equals((b.Company ?? string.Empty).Trim(), (quote.Company ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static string? TrimOrNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? ResolvePackageTypeFromQuote(Quote? quote, string? fallback)
    {
        if (quote is not null)
        {
            foreach (var line in quote.LineItems.OrderBy(x => x.LineNumber))
            {
                var packageType = FirstPackageType(
                    line.ProductCode,
                    line.ProductName,
                    line.ProductType,
                    line.Description);
                if (!string.IsNullOrWhiteSpace(packageType))
                    return packageType;
            }

            var fromQuoteProduct = StingPackageCatalog.Normalize(quote.ProductType);
            if (!string.IsNullOrWhiteSpace(fromQuoteProduct))
                return fromQuoteProduct;
        }

        return StingPackageCatalog.Normalize(fallback);
    }

    private static string? FirstPackageType(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            var normalized = StingPackageCatalog.Normalize(candidate);
            if (!string.IsNullOrWhiteSpace(normalized))
                return normalized;
        }

        return null;
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
