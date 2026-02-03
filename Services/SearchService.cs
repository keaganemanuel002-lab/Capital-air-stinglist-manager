using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using StingListManager.Data;
using StingListManager.Data.Entities;

namespace StingListManager.Services;

public enum SearchResultType
{
    BillingEntry,
    Quote,
    JobCard,
    Cancellation
}

public class SearchResult
{
    public SearchResultType Type { get; set; }
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public string? Registration { get; set; }
}

public class SearchService
{
    private readonly AppSettings _settings;

    public SearchService(AppSettings? settings = null)
    {
        _settings = settings ?? new AppSettings();
    }

    public List<SearchResult> Search(string input, int limit = 50)
    {
        input = (input ?? "").Trim();
        if (input.Length < 2) return new List<SearchResult>();

        var up = input.ToUpperInvariant();
        var pricingService = new QuotePricingService(_settings);

        using var db = new AppDbContext();

        var results = new List<SearchResult>();

        // BillingEntries (fast, most important)
        var billing = db.BillingEntries
            .AsNoTracking()
            .Where(b =>
                b.ArchivedAt == null &&
                (b.RegistrationNorm.Contains(up) ||
                 b.Company.Contains(input)))
            .OrderByDescending(b => b.ActiveFrom)
            .Take(limit)
            .ToList();

        results.AddRange(billing.Select(b => new SearchResult
        {
            Type = SearchResultType.BillingEntry,
            Id = b.Id,
            Registration = b.Registration,
            Title = $"{b.Registration} • {b.TrackingUnitMake ?? "—"}",
            Subtitle = $"{b.Company} • {b.Status}"
        }));

        // Quotes
        var quotes = db.Quotes
            .AsNoTracking()
            .Where(q =>
                q.Registration.Contains(input) ||
                q.Company.Contains(input))
            .OrderByDescending(q => q.CreatedAt)
            .Take(20)
            .ToList();

        results.AddRange(quotes.Select(q =>
        {
            var priceResult = pricingService.CalculatePrice(q);
            return new SearchResult
            {
                Type = SearchResultType.Quote,
                Id = q.Id,
                Registration = q.Registration,
                Title = $"Quote #{q.Id} • {q.Type} • {q.Status}",
                Subtitle = $"{q.Company} • {q.Registration} • R{priceResult.AmountIncVat:0.00} (inc VAT)"
            };
        }));

        // JobCards
        var jobs = db.JobCards
            .AsNoTracking()
            .Where(j =>
                j.Registration.Contains(input) ||
                j.Company.Contains(input))
            .OrderByDescending(j => j.CreatedAt)
            .Take(20)
            .ToList();

        results.AddRange(jobs.Select(j => new SearchResult
        {
            Type = SearchResultType.JobCard,
            Id = j.Id,
            Registration = j.Registration,
            Title = $"JobCard #{j.Id} • {j.Type} • {j.Status}",
            Subtitle = $"{j.Company} • {j.Registration} • Scheduled: {(j.ScheduledFor?.ToString("yyyy-MM-dd HH:mm") ?? "—")}"
        }));

        // Cancellations
        var cancels = db.CancellationEntries
            .AsNoTracking()
            .Where(c =>
                c.Registration.Contains(input) ||
                c.Client.Contains(input) ||
                (c.Reason != null && c.Reason.Contains(input)))
            .OrderByDescending(c => c.DateRequestReceived)
            .Take(20)
            .ToList();

        results.AddRange(cancels.Select(c => new SearchResult
        {
            Type = SearchResultType.Cancellation,
            Id = c.Id,
            Registration = c.Registration,
            Title = $"Removal Request #{c.Id} • {c.Status}",
            Subtitle = $"{c.Client} • {c.Registration} • {c.Reason}"
        }));

        // Scoring for better ranking
        int Score(SearchResult r)
        {
            var q = up;
            var reg = (r.Registration ?? "").ToUpperInvariant();

            if (reg == q) return 1000;
            if (reg.Contains(q)) return 800;
            if (r.Type == SearchResultType.BillingEntry) return 600;
            return 100;
        }

        // Return scored and ranked results
        return results
            .OrderByDescending(Score)
            .ThenBy(r => r.Type)
            .Take(limit)
            .ToList();
    }
}
