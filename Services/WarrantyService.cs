using System;

namespace StingListManager.Services;

public static class WarrantyService
{
    public const int WarrantyMonths = 24;

    public static bool IsWithinWarranty(DateTime installedAtUtc, DateTime? asOfUtc = null)
    {
        return GetMonthsRemaining(installedAtUtc, asOfUtc) > 0;
    }

    public static int GetMonthsRemaining(DateTime installedAtUtc, DateTime? asOfUtc = null)
    {
        var anchor = NormalizeUtc(installedAtUtc);
        if (anchor == default)
            return 0;

        var now = NormalizeUtc(asOfUtc ?? DateTime.UtcNow);
        var expiry = anchor.AddMonths(WarrantyMonths);
        if (now >= expiry)
            return 0;

        var months = (expiry.Year - now.Year) * 12 + (expiry.Month - now.Month);
        if (now.Day > expiry.Day)
            months--;

        return Math.Max(0, months);
    }

    public static string GetDisplayText(DateTime installedAtUtc, DateTime? asOfUtc = null)
    {
        var monthsRemaining = GetMonthsRemaining(installedAtUtc, asOfUtc);
        if (monthsRemaining <= 0)
            return "Out of warranty";

        return monthsRemaining == 1
            ? "1 month left"
            : $"{monthsRemaining} months left";
    }

    private static DateTime NormalizeUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }
}
