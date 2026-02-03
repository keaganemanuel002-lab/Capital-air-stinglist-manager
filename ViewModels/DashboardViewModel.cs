using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using StingListManager.Data;
using StingListManager.Data.Entities;
using StingListManager.Services;

namespace StingListManager.ViewModels;

public class DashboardKpi
{
    public string Title { get; set; } = "";
    public string Value { get; set; } = "";
    public string? SubValue { get; set; }
    public string Target { get; set; } = "";
}

public enum DashboardNavTarget
{
    Quotes,
    QuoteValue,
    JobCards,
    RemovalRequests,
    ActiveBilling
}

public record DashboardNavRequest(DashboardNavTarget Target, DateTime StartDate, DateTime EndDate);

public class TrendPoint
{
    public string Label { get; set; } = "";
    public int QuoteCount { get; set; }
    public int JobCount { get; set; }
    public double QuoteBarWidth { get; set; }
    public double JobBarWidth { get; set; }
}

public class StatusBreakdownItem
{
    public string Label { get; set; } = "";
    public int Count { get; set; }
    public int Total { get; set; }
}

public class TopClientItem
{
    public string Client { get; set; } = "";
    public int Count { get; set; }
    public decimal TotalIncVat { get; set; }
}

public class ActivityItem
{
    public string When { get; set; } = "";
    public string Title { get; set; } = "";
    public string Detail { get; set; } = "";
}

public partial class DashboardViewModel : ViewModelBase
{
    private readonly AppState _appState;
    private readonly Action<DashboardNavRequest>? _navigate;

    public ObservableCollection<DashboardKpi> Kpis { get; } = new();
    public ObservableCollection<TrendPoint> Trends { get; } = new();
    public ObservableCollection<StatusBreakdownItem> QuoteStatusBreakdown { get; } = new();
    public ObservableCollection<StatusBreakdownItem> JobStatusBreakdown { get; } = new();
    public ObservableCollection<TopClientItem> TopClients { get; } = new();
    public ObservableCollection<ActivityItem> RecentActivity { get; } = new();

    public List<string> TimeRanges { get; } = new() { "This month", "Last 30 days", "This year" };

    [ObservableProperty]
    private string selectedTimeRange = "This month";

    [ObservableProperty]
    private string rangeLabel = "";

    public DashboardViewModel(AppState appState, Action<DashboardNavRequest>? navigate = null)
    {
        _appState = appState;
        _navigate = navigate;
        Load();
    }

    partial void OnSelectedTimeRangeChanged(string value) => Load();

    [RelayCommand]
    private void Refresh() => Load();

    [RelayCommand]
    private void KpiClick(string target)
    {
        if (!Enum.TryParse<DashboardNavTarget>(target, out var navTarget))
            return;

        var (start, end) = GetRange("Last 30 days");
        _navigate?.Invoke(new DashboardNavRequest(navTarget, start, end.AddDays(-1)));
    }

    private void Load()
    {
        var (start, end) = GetRange(SelectedTimeRange);
        RangeLabel = $"{start:yyyy-MM-dd} → {end.AddDays(-1):yyyy-MM-dd}";

        using var db = new AppDbContext();
        var pricing = new QuotePricingService(_appState.Settings);

        var quotes = db.Quotes
            .AsNoTracking()
            .Include(q => q.LineItems)
            .Where(q => q.CreatedAt >= start && q.CreatedAt < end)
            .ToList();

        var jobs = db.JobCards
            .AsNoTracking()
            .Where(j => j.CreatedAt >= start && j.CreatedAt < end)
            .ToList();

        var removals = db.CancellationEntries
            .AsNoTracking()
            .Where(c => c.DateRequestReceived != null && c.DateRequestReceived >= start && c.DateRequestReceived < end)
            .ToList();

        var activeBillingCount = db.BillingEntries
            .AsNoTracking()
            .Count(b => b.Status == BillingStatus.Active && b.ArchivedAt == null);

        var quoteTotals = quotes.Select(pricing.CalculatePrice).ToList();
        var quoteTotalInc = quoteTotals.Sum(x => x.AmountIncVat);
        var quoteTotalVat = quoteTotals.Sum(x => x.VatAmount);

        Kpis.Clear();
        Kpis.Add(new DashboardKpi { Title = "Quotes", Value = quotes.Count.ToString(), SubValue = $"Approved {quotes.Count(q => q.Status == QuoteStatus.Approved)}", Target = DashboardNavTarget.Quotes.ToString() });
        Kpis.Add(new DashboardKpi { Title = "Quote Value (Inc VAT)", Value = $"R{quoteTotalInc:0.00}", SubValue = $"VAT R{quoteTotalVat:0.00}", Target = DashboardNavTarget.QuoteValue.ToString() });
        Kpis.Add(new DashboardKpi { Title = "Job Cards", Value = jobs.Count.ToString(), SubValue = $"Completed {jobs.Count(j => j.Status == JobStatus.Completed)}", Target = DashboardNavTarget.JobCards.ToString() });
        Kpis.Add(new DashboardKpi { Title = "Removal Requests", Value = removals.Count.ToString(), SubValue = "This range", Target = DashboardNavTarget.RemovalRequests.ToString() });
        Kpis.Add(new DashboardKpi { Title = "Active Billing", Value = activeBillingCount.ToString(), SubValue = "Current", Target = DashboardNavTarget.ActiveBilling.ToString() });

        BuildTrends(quotes, jobs, start, end);
        BuildStatusBreakdowns(quotes, jobs);
        BuildTopClients(quotes, pricing);
        BuildRecentActivity(quotes, jobs, removals);
    }

    private void BuildTrends(List<Quote> quotes, List<JobCard> jobs, DateTime start, DateTime end)
    {
        Trends.Clear();
        var days = Enumerable.Range(0, (end - start).Days)
            .Select(i => start.AddDays(i))
            .ToList();

        var quoteCounts = days.ToDictionary(d => d, d => quotes.Count(q => q.CreatedAt.Date == d.Date));
        var jobCounts = days.ToDictionary(d => d, d => jobs.Count(j => j.CreatedAt.Date == d.Date));

        var max = Math.Max(1, quoteCounts.Values.Concat(jobCounts.Values).Max());

        foreach (var day in days)
        {
            var q = quoteCounts[day];
            var j = jobCounts[day];
            Trends.Add(new TrendPoint
            {
                Label = day.ToString("MM-dd"),
                QuoteCount = q,
                JobCount = j,
                QuoteBarWidth = 240.0 * q / max,
                JobBarWidth = 240.0 * j / max
            });
        }
    }

    private void BuildStatusBreakdowns(List<Quote> quotes, List<JobCard> jobs)
    {
        QuoteStatusBreakdown.Clear();
        JobStatusBreakdown.Clear();

        var quoteTotal = Math.Max(1, quotes.Count);
        foreach (var group in quotes.GroupBy(q => q.Status).OrderBy(g => g.Key))
        {
            QuoteStatusBreakdown.Add(new StatusBreakdownItem
            {
                Label = group.Key.ToString(),
                Count = group.Count(),
                Total = quoteTotal
            });
        }

        var jobTotal = Math.Max(1, jobs.Count);
        foreach (var group in jobs.GroupBy(j => j.Status).OrderBy(g => g.Key))
        {
            JobStatusBreakdown.Add(new StatusBreakdownItem
            {
                Label = group.Key.ToString(),
                Count = group.Count(),
                Total = jobTotal
            });
        }
    }

    private void BuildTopClients(List<Quote> quotes, QuotePricingService pricing)
    {
        TopClients.Clear();

        var groups = quotes
            .GroupBy(q => q.Company)
            .Select(g => new
            {
                Company = g.Key,
                Count = g.Count(),
                TotalIncVat = g.Sum(q => pricing.CalculatePrice(q).AmountIncVat)
            })
            .OrderByDescending(x => x.Count)
            .ThenByDescending(x => x.TotalIncVat)
            .Take(5)
            .ToList();

        foreach (var g in groups)
        {
            TopClients.Add(new TopClientItem
            {
                Client = g.Company,
                Count = g.Count,
                TotalIncVat = g.TotalIncVat
            });
        }
    }

    private void BuildRecentActivity(List<Quote> quotes, List<JobCard> jobs, List<CancellationEntry> removals)
    {
        RecentActivity.Clear();

        var items = new List<(DateTime when, ActivityItem item)>();

        items.AddRange(quotes.Select(q => (
            q.CreatedAt,
            new ActivityItem
            {
                When = q.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
                Title = $"Quote #{q.Id} • {q.Status}",
                Detail = $"{q.Company} • {q.Registration}"
            }
        )));

        items.AddRange(jobs.Select(j => (
            j.CreatedAt,
            new ActivityItem
            {
                When = j.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
                Title = $"Job Card #{j.Id} • {j.Status}",
                Detail = $"{j.Company} • {j.Registration}"
            }
        )));

        items.AddRange(removals.Select(r => (
            r.DateRequestReceived ?? DateTime.MinValue,
            new ActivityItem
            {
                When = r.DateRequestReceived?.ToString("yyyy-MM-dd") ?? "—",
                Title = $"Removal Request #{r.Id} • {r.Status}",
                Detail = $"{r.Client} • {r.Registration}"
            }
        )));

        foreach (var item in items.OrderByDescending(x => x.when).Take(10))
        {
            RecentActivity.Add(item.item);
        }
    }

    private static (DateTime start, DateTime end) GetRange(string range)
    {
        var today = DateTime.Today;
        return range switch
        {
            "Last 30 days" => (today.AddDays(-29), today.AddDays(1)),
            "This year" => (new DateTime(today.Year, 1, 1), new DateTime(today.Year + 1, 1, 1)),
            _ => (new DateTime(today.Year, today.Month, 1), new DateTime(today.Year, today.Month, 1).AddMonths(1))
        };
    }
}
