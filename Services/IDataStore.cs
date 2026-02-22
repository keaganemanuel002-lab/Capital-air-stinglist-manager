using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using StingListManager.Data.Entities;

namespace StingListManager.Services;

public sealed class ClientSaveResult
{
    public bool Success { get; init; }
    public bool IsDuplicate { get; init; }
    public string Message { get; init; } = string.Empty;
    public Client? Client { get; init; }
}

public sealed class JobCardQuery
{
    public string SelectedStatus { get; init; } = "All";
    public string SelectedType { get; init; } = "All";
    public string? CompanyFilter { get; init; }
    public string? RegistrationFilter { get; init; }
    public DateTimeOffset? StartDate { get; init; }
    public DateTimeOffset? EndDate { get; init; }
    public int? QuoteIdFilter { get; init; }
}

public sealed class JobCardListItem
{
    public int Id { get; init; }
    public int JobCardNumber { get; init; }
    public string JobCardReference { get; init; } = string.Empty;
    public int? QuoteId { get; init; }
    public string QuoteReference { get; init; } = "-";
    public JobType JobTypeValue { get; init; }
    public string Type { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public int PhotoCount { get; init; }
    public DateTime? LastPhotoAt { get; init; }
    public string Company { get; init; } = string.Empty;
    public string Registration { get; init; } = string.Empty;
    public string? Make { get; init; }
    public string? Model { get; init; }
    public string? Imei { get; init; }
    public string? SerialNumber { get; init; }
    public string? Iccid { get; init; }
    public DateTime? ScheduledFor { get; init; }
    public DateTime CreatedAt { get; init; }
}

public sealed class QuoteQuery
{
    public string SelectedStatus { get; init; } = "All";
    public string SelectedType { get; init; } = "All";
    public string? CompanyFilter { get; init; }
    public string? RegistrationFilter { get; init; }
    public DateTimeOffset? StartDate { get; init; }
    public DateTimeOffset? EndDate { get; init; }
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 100;
}

public sealed class QuoteListItem
{
    public int Id { get; init; }
    public int QuoteNumber { get; init; }
    public string Type { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Company { get; init; } = string.Empty;
    public string? Registration { get; init; }
    public string? ProductType { get; init; }
    public decimal AmountExVat { get; init; }
    public DateTime CreatedAt { get; init; }
}

public sealed class QuotePageResult
{
    public int TotalCount { get; init; }
    public List<QuoteListItem> Items { get; init; } = new();
}

public sealed class QuoteCancelResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public int QuoteId { get; init; }
    public int QuoteNumber { get; init; }
    public string? Registration { get; init; }
}

public sealed class QuoteApproveResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public int JobId { get; init; }
}

public sealed class QuoteDeleteResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
}

public interface IDataStore
{
    Task<List<Client>> GetClientsAsync(
        string? searchText,
        HashSet<string>? activeWialonClientKeys,
        CancellationToken cancellationToken = default);

    Task<ClientSaveResult> SaveClientAsync(
        int? selectedClientId,
        string name,
        string? contactPerson,
        string? phoneNumber,
        string? emailAddress,
        string? address,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteClientAsync(int clientId, CancellationToken cancellationToken = default);
    Task<int> InsertMissingClientsAsync(IEnumerable<string> clientNames, CancellationToken cancellationToken = default);

    Task<List<JobCardListItem>> GetJobCardsAsync(JobCardQuery query, CancellationToken cancellationToken = default);
    Task<int> CountJobCardPhotosAsync(int jobCardId, CancellationToken cancellationToken = default);
    Task<QuotePageResult> GetQuotesAsync(QuoteQuery query, CancellationToken cancellationToken = default);
    Task<QuoteApproveResult> ApproveQuoteAsync(int quoteId, string actor, DateTime? scheduleDate = null, CancellationToken cancellationToken = default);
    Task<QuoteCancelResult> CancelDraftQuoteAsync(int quoteId, CancellationToken cancellationToken = default);
    Task<QuoteDeleteResult> DeleteQuoteAsync(int quoteId, CancellationToken cancellationToken = default);
    Task<bool> HasRelatedJobCardsForQuoteAsync(int quoteId, CancellationToken cancellationToken = default);
    Task<Quote?> GetQuoteWithLineItemsAsync(int quoteId, CancellationToken cancellationToken = default);
    Task<DateTime?> GetJobCardScheduledForAsync(int jobCardId, CancellationToken cancellationToken = default);
    Task<bool> UpdateJobCardScheduleAsync(int jobCardId, DateTime? scheduledFor, CancellationToken cancellationToken = default);
}
