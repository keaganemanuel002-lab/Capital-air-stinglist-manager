using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using StingListManager.Data;
using StingListManager.Data.Entities;

namespace StingListManager.Services;

public class LocalSqliteDataStore : IDataStore
{
    public async Task<List<Client>> GetClientsAsync(
        string? searchText,
        HashSet<string>? activeWialonClientKeys,
        CancellationToken cancellationToken = default)
    {
        using var db = new AppDbContext();
        var query = db.Clients.AsNoTracking().OrderBy(c => c.Name).AsQueryable();

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            var s = searchText.Trim();
            query = query.Where(c => c.Name.Contains(s));
        }

        var rows = await query.ToListAsync(cancellationToken);
        if (activeWialonClientKeys is not null)
        {
            rows = rows
                .Where(c => activeWialonClientKeys.Contains(NormalizeComparableText(c.Name)))
                .ToList();
        }

        return rows;
    }

    public async Task<ClientSaveResult> SaveClientAsync(
        int? selectedClientId,
        string name,
        string? contactPerson,
        string? phoneNumber,
        string? emailAddress,
        string? address,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return new ClientSaveResult
            {
                Success = false,
                Message = "Client name is required."
            };
        }

        using var db = new AppDbContext();
        var normalizedName = name.Trim();
        var selectedId = selectedClientId ?? 0;
        var normalizedComparableName = NormalizeComparableText(normalizedName);

        var existing = await db.Clients.FirstOrDefaultAsync(c => c.Id == selectedId, cancellationToken);
        var duplicate = await db.Clients
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.NameNorm == normalizedComparableName, cancellationToken);

        if (existing == null && duplicate != null)
        {
            return new ClientSaveResult
            {
                Success = false,
                IsDuplicate = true,
                Message = "Client name already exists.",
                Client = duplicate
            };
        }

        if (existing == null)
        {
            existing = new Client
            {
                Name = normalizedName,
                ContactPerson = contactPerson?.Trim(),
                PhoneNumber = phoneNumber?.Trim(),
                EmailAddress = emailAddress?.Trim(),
                Address = address?.Trim(),
                CreatedAt = DateTime.UtcNow
            };
            db.Clients.Add(existing);
        }
        else
        {
            existing.Name = normalizedName;
            existing.ContactPerson = contactPerson?.Trim();
            existing.PhoneNumber = phoneNumber?.Trim();
            existing.EmailAddress = emailAddress?.Trim();
            existing.Address = address?.Trim();
        }

        await db.SaveChangesAsync(cancellationToken);

        var saved = await db.Clients.AsNoTracking().FirstAsync(c => c.Id == existing.Id, cancellationToken);
        return new ClientSaveResult
        {
            Success = true,
            Message = "Client saved.",
            Client = saved
        };
    }

    public async Task<bool> DeleteClientAsync(int clientId, CancellationToken cancellationToken = default)
    {
        using var db = new AppDbContext();
        var client = await db.Clients.FirstOrDefaultAsync(c => c.Id == clientId, cancellationToken);
        if (client == null)
            return false;

        db.Clients.Remove(client);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<int> InsertMissingClientsAsync(IEnumerable<string> clientNames, CancellationToken cancellationToken = default)
    {
        var names = clientNames
            .Select(x => x?.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .ToList();

        if (names.Count == 0)
            return 0;

        using var db = new AppDbContext();
        var existingKeys = new HashSet<string>(
            await db.Clients
                .AsNoTracking()
                .Select(c => c.NameNorm)
                .ToListAsync(cancellationToken),
            StringComparer.Ordinal);

        var inserted = 0;
        foreach (var name in names)
        {
            var key = NormalizeComparableText(name);
            if (string.IsNullOrWhiteSpace(key) || existingKeys.Contains(key))
                continue;

            db.Clients.Add(new Client
            {
                Name = name,
                CreatedAt = DateTime.UtcNow
            });
            existingKeys.Add(key);
            inserted++;
        }

        if (inserted > 0)
            await db.SaveChangesAsync(cancellationToken);

        return inserted;
    }

    public async Task<List<JobCardListItem>> GetJobCardsAsync(JobCardQuery query, CancellationToken cancellationToken = default)
    {
        using var db = new AppDbContext();
        var rows = await GetJobCardsInternalAsync(db, query, cancellationToken);
        return rows;
    }

    public async Task<List<JobCardListItem>> GetJobCardsSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var query = new JobCardQuery
        {
            SelectedStatus = "All",
            SelectedType = "All",
            CompanyFilter = null,
            RegistrationFilter = null,
            StartDate = null,
            EndDate = null,
            QuoteIdFilter = null
        };

        using var db = new AppDbContext();
        var rows = await GetJobCardsInternalAsync(db, query, cancellationToken);
        return rows;
    }

    public async Task<int> CountJobCardPhotosAsync(int jobCardId, CancellationToken cancellationToken = default)
    {
        using var db = new AppDbContext();
        return await db.Attachments.AsNoTracking().CountAsync(a =>
            a.OwnerType == AttachmentOwnerType.JobCard
            && a.OwnerId == jobCardId
            && a.Kind == AttachmentKind.JobPhoto, cancellationToken);
    }

    public async Task<QuotePageResult> GetQuotesAsync(QuoteQuery query, CancellationToken cancellationToken = default)
    {
        using var db = new AppDbContext();
        return await GetQuotesInternalAsync(db, query, cancellationToken);
    }

    public Task<QuoteApproveResult> ApproveQuoteAsync(
        int quoteId,
        string actor,
        DateTime? scheduleDate = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var workflow = new WorkflowService();
        var (jobId, errorMessage) = workflow.ApproveQuote(quoteId, actor, scheduleDate);
        if (jobId == 0)
        {
            return Task.FromResult(new QuoteApproveResult
            {
                Success = false,
                Message = string.IsNullOrWhiteSpace(errorMessage) ? "Quote approval failed." : errorMessage,
                JobId = 0
            });
        }

        var successMessage = string.IsNullOrWhiteSpace(errorMessage)
            ? (jobId > 0
                ? "Quote approved. Job card created. You can set the schedule from the Job Cards view."
                : "Quote approved. No job card created.")
            : errorMessage;

        return Task.FromResult(new QuoteApproveResult
        {
            Success = true,
            Message = successMessage,
            JobId = jobId
        });
    }

    public async Task<QuoteCancelResult> CancelDraftQuoteAsync(int quoteId, CancellationToken cancellationToken = default)
    {
        using var db = new AppDbContext();
        var quote = await db.Quotes.FirstOrDefaultAsync(q => q.Id == quoteId, cancellationToken);
        if (quote is null)
        {
            return new QuoteCancelResult
            {
                Success = false,
                Message = "Quote not found.",
                QuoteId = quoteId
            };
        }

        if (quote.Status == QuoteStatus.Approved)
        {
            return new QuoteCancelResult
            {
                Success = false,
                Message = "Cannot cancel: quote is already approved.",
                QuoteId = quote.Id,
                QuoteNumber = quote.QuoteNumber,
                Registration = quote.Registration
            };
        }

        if (quote.Status != QuoteStatus.Draft)
        {
            return new QuoteCancelResult
            {
                Success = false,
                Message = "Only draft quotes can be cancelled.",
                QuoteId = quote.Id,
                QuoteNumber = quote.QuoteNumber,
                Registration = quote.Registration
            };
        }

        quote.Status = QuoteStatus.Cancelled;
        await db.SaveChangesAsync(cancellationToken);

        return new QuoteCancelResult
        {
            Success = true,
            Message = "Quote cancelled.",
            QuoteId = quote.Id,
            QuoteNumber = quote.QuoteNumber,
            Registration = quote.Registration
        };
    }

    public async Task<QuoteDeleteResult> DeleteQuoteAsync(int quoteId, CancellationToken cancellationToken = default)
    {
        using var db = new AppDbContext();
        var quote = await db.Quotes
            .Include(q => q.LineItems)
            .FirstOrDefaultAsync(q => q.Id == quoteId, cancellationToken);
        if (quote is null)
        {
            return new QuoteDeleteResult
            {
                Success = false,
                Message = "Quote not found."
            };
        }

        var relatedJobs = await db.JobCards
            .Where(j => j.QuoteId == quote.Id)
            .ToListAsync(cancellationToken);
        if (relatedJobs.Any(j => j.Status == JobStatus.Completed))
        {
            return new QuoteDeleteResult
            {
                Success = false,
                Message = "Cannot delete: Quote has a completed job."
            };
        }

        if (relatedJobs.Count > 0)
            db.JobCards.RemoveRange(relatedJobs);

        var cancellations = await db.CancellationEntries
            .Where(c => c.QuoteId == quote.Id)
            .ToListAsync(cancellationToken);
        if (cancellations.Count > 0)
            db.CancellationEntries.RemoveRange(cancellations);

        var attachments = await db.Attachments
            .Where(a => a.OwnerType == AttachmentOwnerType.Quote && a.OwnerId == quote.Id)
            .ToListAsync(cancellationToken);
        if (attachments.Count > 0)
            db.Attachments.RemoveRange(attachments);

        db.Quotes.Remove(quote);
        await db.SaveChangesAsync(cancellationToken);

        return new QuoteDeleteResult
        {
            Success = true,
            Message = "Quote deleted."
        };
    }

    public async Task<bool> HasRelatedJobCardsForQuoteAsync(int quoteId, CancellationToken cancellationToken = default)
    {
        using var db = new AppDbContext();
        return await db.JobCards
            .AsNoTracking()
            .AnyAsync(j => j.QuoteId == quoteId, cancellationToken);
    }

    public async Task<Quote?> GetQuoteWithLineItemsAsync(int quoteId, CancellationToken cancellationToken = default)
    {
        using var db = new AppDbContext();
        return await db.Quotes
            .AsNoTracking()
            .Include(q => q.LineItems)
            .FirstOrDefaultAsync(q => q.Id == quoteId, cancellationToken);
    }

    public async Task<DateTime?> GetJobCardScheduledForAsync(int jobCardId, CancellationToken cancellationToken = default)
    {
        using var db = new AppDbContext();
        return await db.JobCards
            .AsNoTracking()
            .Where(j => j.Id == jobCardId)
            .Select(j => j.ScheduledFor)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> UpdateJobCardScheduleAsync(int jobCardId, DateTime? scheduledFor, CancellationToken cancellationToken = default)
    {
        using var db = new AppDbContext();
        var job = await db.JobCards.FirstOrDefaultAsync(j => j.Id == jobCardId, cancellationToken);
        if (job is null)
            return false;

        job.ScheduledFor = scheduledFor;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<List<Client>> GetClientsSnapshotAsync(CancellationToken cancellationToken = default)
    {
        using var db = new AppDbContext();
        return await db.Clients.AsNoTracking().OrderBy(c => c.Name).ToListAsync(cancellationToken);
    }

    private static async Task<List<JobCardListItem>> GetJobCardsInternalAsync(
        AppDbContext db,
        JobCardQuery query,
        CancellationToken cancellationToken)
    {
        var rows = db.JobCards.AsNoTracking().AsQueryable();

        if (!string.Equals(query.SelectedStatus, "All", StringComparison.OrdinalIgnoreCase)
            && Enum.TryParse<JobStatus>(query.SelectedStatus, out var status))
        {
            rows = rows.Where(j => j.Status == status);
        }

        if (!string.Equals(query.SelectedType, "All", StringComparison.OrdinalIgnoreCase)
            && Enum.TryParse<JobType>(query.SelectedType, out var type))
        {
            rows = rows.Where(j => j.Type == type);
        }

        if (!string.IsNullOrWhiteSpace(query.CompanyFilter))
        {
            var s = query.CompanyFilter.Trim();
            rows = rows.Where(j => j.Company.Contains(s));
        }

        if (!string.IsNullOrWhiteSpace(query.RegistrationFilter))
        {
            var s = query.RegistrationFilter.Trim();
            rows = rows.Where(j => j.Registration.Contains(s));
        }

        if (query.StartDate != null)
        {
            var start = query.StartDate.Value.Date;
            rows = rows.Where(j => j.CreatedAt >= start);
        }

        if (query.EndDate != null)
        {
            var endExclusive = query.EndDate.Value.Date.AddDays(1);
            rows = rows.Where(j => j.CreatedAt < endExclusive);
        }

        if (query.QuoteIdFilter.HasValue)
        {
            var quoteId = query.QuoteIdFilter.Value;
            rows = rows.Where(j => j.QuoteId == quoteId);
        }

        var items = query.QuoteIdFilter.HasValue
            ? await rows.OrderBy(j => j.JobCardNumber).ThenBy(j => j.CreatedAt).ToListAsync(cancellationToken)
            : await rows.OrderByDescending(j => j.CreatedAt).ToListAsync(cancellationToken);

        var quoteIds = items
            .Where(j => j.QuoteId.HasValue)
            .Select(j => j.QuoteId!.Value)
            .Distinct()
            .ToList();

        var quoteRefById = await db.Quotes.AsNoTracking()
            .Where(q => quoteIds.Contains(q.Id))
            .Select(q => new { q.Id, q.QuoteNumber })
            .ToDictionaryAsync(q => q.Id, q => QuoteReferenceFormatter.Format(q.QuoteNumber), cancellationToken);

        var jobCardIds = items.Select(j => j.Id).ToList();
        var photoSummaryByJobId = await db.Attachments.AsNoTracking()
            .Where(a => a.OwnerType == AttachmentOwnerType.JobCard
                        && a.Kind == AttachmentKind.JobPhoto
                        && jobCardIds.Contains(a.OwnerId))
            .GroupBy(a => a.OwnerId)
            .Select(g => new
            {
                JobCardId = g.Key,
                Count = g.Count(),
                LastPhotoAt = g.Max(a => (DateTime?)a.AddedAt)
            })
            .ToDictionaryAsync(x => x.JobCardId, x => (x.Count, x.LastPhotoAt), cancellationToken);

        var output = new List<JobCardListItem>(items.Count);
        foreach (var j in items)
        {
            var quoteRef = "-";
            if (j.QuoteId.HasValue && quoteRefById.TryGetValue(j.QuoteId.Value, out var formattedRef))
                quoteRef = formattedRef;

            photoSummaryByJobId.TryGetValue(j.Id, out var photoSummary);

            output.Add(new JobCardListItem
            {
                Id = j.Id,
                JobCardNumber = j.JobCardNumber,
                JobCardReference = JobCardReferenceFormatter.Format(j.Type, j.JobCardNumber),
                QuoteId = j.QuoteId,
                QuoteReference = quoteRef,
                JobTypeValue = j.Type,
                Type = j.Type.ToString(),
                Status = j.Status.ToString(),
                PhotoCount = photoSummary.Count,
                LastPhotoAt = photoSummary.LastPhotoAt,
                Company = j.Company,
                Registration = j.Registration,
                Make = j.Make,
                Model = j.Model,
                Imei = j.Imei,
                SerialNumber = j.SerialNumber,
                Iccid = j.Iccid,
                ScheduledFor = j.ScheduledFor,
                CreatedAt = j.CreatedAt
            });
        }

        return output;
    }

    private static async Task<QuotePageResult> GetQuotesInternalAsync(
        AppDbContext db,
        QuoteQuery query,
        CancellationToken cancellationToken)
    {
        var pageNumber = query.PageNumber < 1 ? 1 : query.PageNumber;
        var pageSize = query.PageSize < 1 ? 100 : query.PageSize;
        var skip = (pageNumber - 1) * pageSize;

        var rows = db.Quotes.AsNoTracking().AsQueryable();

        if (!string.Equals(query.SelectedStatus, "All", StringComparison.OrdinalIgnoreCase)
            && Enum.TryParse<QuoteStatus>(query.SelectedStatus, out var status))
        {
            rows = rows.Where(q => q.Status == status);
        }

        if (!string.Equals(query.SelectedType, "All", StringComparison.OrdinalIgnoreCase)
            && Enum.TryParse<QuoteType>(query.SelectedType, out var type))
        {
            rows = rows.Where(q => q.Type == type);
        }

        if (!string.IsNullOrWhiteSpace(query.CompanyFilter))
        {
            var s = query.CompanyFilter.Trim();
            rows = rows.Where(q => q.Company.Contains(s));
        }

        if (!string.IsNullOrWhiteSpace(query.RegistrationFilter))
        {
            var s = query.RegistrationFilter.Trim();
            rows = rows.Where(q => q.Registration != null && q.Registration.Contains(s));
        }

        if (query.StartDate != null)
        {
            var start = query.StartDate.Value.Date;
            rows = rows.Where(q => q.CreatedAt >= start);
        }

        if (query.EndDate != null)
        {
            var endExclusive = query.EndDate.Value.Date.AddDays(1);
            rows = rows.Where(q => q.CreatedAt < endExclusive);
        }

        var totalCount = await rows.CountAsync(cancellationToken);
        var pageItems = await rows
            .OrderByDescending(q => q.CreatedAt)
            .Skip(skip)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var mapped = pageItems.Select(q => new QuoteListItem
        {
            Id = q.Id,
            QuoteNumber = q.QuoteNumber,
            Type = q.Type.ToString(),
            Status = q.Status.ToString(),
            Company = q.Company,
            Registration = q.Registration,
            ProductType = q.ProductType,
            AmountExVat = q.AmountExVat,
            CreatedAt = q.CreatedAt
        }).ToList();

        return new QuotePageResult
        {
            TotalCount = totalCount,
            Items = mapped
        };
    }

    private static string NormalizeComparableText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return new string(value
            .Trim()
            .ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray());
    }
}
