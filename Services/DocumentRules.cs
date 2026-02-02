using System;
using System.Collections.Generic;
using System.Linq;
using StingListManager.Data;
using StingListManager.Data.Entities;

namespace StingListManager.Services;

public class DocumentRules
{
    public bool HasRequiredDocsForJobCompletion(int jobCardId, out string message)
    {
        using var db = new AppDbContext();
        var job = db.JobCards.FirstOrDefault(j => j.Id == jobCardId);
        if (job is null)
        {
            message = "Job card not found.";
            return false;
        }

        // Collect attachment kinds for JobCard
        var jobAtts = db.Attachments
            .Where(a => a.OwnerType == AttachmentOwnerType.JobCard && a.OwnerId == jobCardId)
            .Select(a => a.Kind)
            .ToList();

        // Collect attachment kinds for linked Quote (if any)
        var quoteAtts = new List<AttachmentKind>();
        if (job.QuoteId != null)
        {
            quoteAtts = db.Attachments
                .Where(a => a.OwnerType == AttachmentOwnerType.Quote && a.OwnerId == job.QuoteId.Value)
                .Select(a => a.Kind)
                .ToList();
        }

        bool Has(AttachmentKind k) => jobAtts.Contains(k) || quoteAtts.Contains(k);

        if (job.Type == JobType.Install)
        {
            // Require Signed Quote OR Invoice
            var ok = Has(AttachmentKind.QuoteSigned) || Has(AttachmentKind.Invoice);
            message = ok ? "Required documents: OK"
                         : "Install completion requires a Signed Quote or Invoice (on Quote or Job Card).";
            return ok;
        }

        if (job.Type == JobType.Removal)
        {
            // Require Invoice OR some confirmation
            var ok = Has(AttachmentKind.Invoice) || Has(AttachmentKind.Other);
            message = ok ? "Required documents: OK"
                         : "Removal completion requires an attachment (Invoice or confirmation).";
            return ok;
        }

        message = "Required documents: OK";
        return true;
    }

    public bool HasRequiredDocsForQuoteApproval(int quoteId, QuoteType type, out string message)
    {
        using var db = new AppDbContext();

        // No documents are required for quote approval
        message = "OK";
        return true;
    }
}
