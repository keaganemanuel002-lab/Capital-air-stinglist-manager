using System;
using System.Linq;
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

            // Create job cards for each unit
            int firstJobId = 0;
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

                // Assign next job card number
                var maxJobCardNumber = db.JobCards.Any() ? db.JobCards.Max(x => x.JobCardNumber) : 0;
                job.JobCardNumber = maxJobCardNumber + 1;

                logMsg = $"[ApproveQuote] JobCard {i + 1}/{totalUnits} created. JobCardNumber={job.JobCardNumber}";
                System.IO.File.AppendAllText(logPath, logMsg + Environment.NewLine);

                db.JobCards.Add(job);
                
                if (i == 0)
                    firstJobId = job.Id;
            }

            logMsg = $"[ApproveQuote] Saving quote and {totalUnits} job cards...";
            System.IO.File.AppendAllText(logPath, logMsg + Environment.NewLine);
            
            var saved = db.SaveChanges();
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

    public (bool ok, string message) CompleteJobCard(int jobCardId, string actor)
    {
        using var db = new AppDbContext();

        var job = db.JobCards.FirstOrDefault(j => j.Id == jobCardId);
        if (job is null) return (false, "Job card not found.");

        job.Status = JobStatus.Completed;
        job.CompletedAt = DateTime.UtcNow;

        if (job.Type == JobType.Install)
        {
            // Create active billing entry
            var be = new BillingEntry
            {
                Company = job.Company,
                Registration = job.Registration,
                FleetNumber = job.FleetNumber,
                Make = job.Make,
                Model = job.Model,
                Colour = job.Colour,
                VinNumber = job.VinNumber,
                TrackingUnitMake = job.TrackingUnitMake,
                Imei = job.Imei,
                SerialNumber = job.SerialNumber,
                Iccid = job.Iccid,
                SimNumber = job.SimNumber,
                Notes = job.Notes,
                Status = BillingStatus.Active,
                ActiveFrom = DateTime.UtcNow
            };

            // Normalize fields for unique constraint
            be.RegistrationNorm = be.Registration.Trim().ToUpperInvariant();

            db.BillingEntries.Add(be);

            try
            {
                db.SaveChanges();
                // Log this billing add
                new AuditService().Log(actor, "BILLING_ADD", "BillingEntry", be.Id, be.Registration, "Created from install job completion");
                return (true, "Install completed and billing entry created.");
            }
            catch (DbUpdateException)
            {
                // Duplicate (registration already active)
                return (false, "Billing entry for this registration already exists.");
            }
        }
        else // Removal
        {
            var entry = db.BillingEntries
                .Where(b => b.Registration == job.Registration
                         && b.Status == BillingStatus.Active
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
}
