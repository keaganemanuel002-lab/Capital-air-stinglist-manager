using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using StingListManager.Data.Entities;

namespace StingListManager.Services;

public sealed class MongoDataStore : IDataStore
{
    private readonly LocalSqliteDataStore _local = new();

    public MongoDataStore(AppSettings settings)
    {
        _ = settings;
    }

    public Task<List<Client>> GetClientsAsync(
        string? searchText,
        HashSet<string>? activeWialonClientKeys,
        CancellationToken cancellationToken = default)
        => _local.GetClientsAsync(searchText, activeWialonClientKeys, cancellationToken);

    public Task<ClientSaveResult> SaveClientAsync(
        int? selectedClientId,
        string name,
        string? contactPerson,
        string? phoneNumber,
        string? emailAddress,
        string? address,
        CancellationToken cancellationToken = default)
        => _local.SaveClientAsync(
            selectedClientId,
            name,
            contactPerson,
            phoneNumber,
            emailAddress,
            address,
            cancellationToken);

    public Task<bool> DeleteClientAsync(int clientId, CancellationToken cancellationToken = default)
        => _local.DeleteClientAsync(clientId, cancellationToken);

    public Task<int> InsertMissingClientsAsync(IEnumerable<string> clientNames, CancellationToken cancellationToken = default)
        => _local.InsertMissingClientsAsync(clientNames, cancellationToken);

    public Task<List<JobCardListItem>> GetJobCardsAsync(JobCardQuery query, CancellationToken cancellationToken = default)
        => _local.GetJobCardsAsync(query, cancellationToken);

    public Task<int> CountJobCardPhotosAsync(int jobCardId, CancellationToken cancellationToken = default)
        => _local.CountJobCardPhotosAsync(jobCardId, cancellationToken);

    public Task<QuotePageResult> GetQuotesAsync(QuoteQuery query, CancellationToken cancellationToken = default)
        => _local.GetQuotesAsync(query, cancellationToken);

    public Task<QuoteApproveResult> ApproveQuoteAsync(
        int quoteId,
        string actor,
        DateTime? scheduleDate = null,
        CancellationToken cancellationToken = default)
        => _local.ApproveQuoteAsync(quoteId, actor, scheduleDate, cancellationToken);

    public Task<QuoteCancelResult> CancelDraftQuoteAsync(int quoteId, CancellationToken cancellationToken = default)
        => _local.CancelDraftQuoteAsync(quoteId, cancellationToken);

    public Task<QuoteDeleteResult> DeleteQuoteAsync(int quoteId, CancellationToken cancellationToken = default)
        => _local.DeleteQuoteAsync(quoteId, cancellationToken);

    public Task<bool> HasRelatedJobCardsForQuoteAsync(int quoteId, CancellationToken cancellationToken = default)
        => _local.HasRelatedJobCardsForQuoteAsync(quoteId, cancellationToken);

    public Task<Quote?> GetQuoteWithLineItemsAsync(int quoteId, CancellationToken cancellationToken = default)
        => _local.GetQuoteWithLineItemsAsync(quoteId, cancellationToken);

    public Task<DateTime?> GetJobCardScheduledForAsync(int jobCardId, CancellationToken cancellationToken = default)
        => _local.GetJobCardScheduledForAsync(jobCardId, cancellationToken);

    public Task<bool> UpdateJobCardScheduleAsync(int jobCardId, DateTime? scheduledFor, CancellationToken cancellationToken = default)
        => _local.UpdateJobCardScheduleAsync(jobCardId, scheduledFor, cancellationToken);

    public Task<List<BillingEntry>> GetActiveBillingEntriesAsync(CancellationToken cancellationToken = default)
        => _local.GetActiveBillingEntriesAsync(cancellationToken);

    public Task<BillingEntry?> GetBillingEntryByIdAsync(int billingEntryId, CancellationToken cancellationToken = default)
        => _local.GetBillingEntryByIdAsync(billingEntryId, cancellationToken);

    public Task<BillingEntrySaveResult> SaveBillingEntryAsync(
        int? billingEntryId,
        BillingEntrySaveRequest request,
        CancellationToken cancellationToken = default)
        => _local.SaveBillingEntryAsync(billingEntryId, request, cancellationToken);
}
