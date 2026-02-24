using System;
using System.Linq;

namespace StingListManager.Services;

public enum StingPackageFamily
{
    Unknown = 0,
    Sting = 1,
    StingPlus = 2,
    StingFm = 3
}

public static class StingPackageClassifier
{
    public static StingPackageFamily Classify(string? value)
    {
        var normalized = NormalizeToken(value);
        if (string.IsNullOrWhiteSpace(normalized))
            return StingPackageFamily.Unknown;

        if (!normalized.Contains("STING", StringComparison.Ordinal))
            return StingPackageFamily.Unknown;

        if (normalized.Contains("STINGFM", StringComparison.Ordinal))
            return StingPackageFamily.StingFm;

        if (normalized.Contains("STINGPLUS", StringComparison.Ordinal)
            || normalized.Contains("STING+", StringComparison.Ordinal))
        {
            return StingPackageFamily.StingPlus;
        }

        return StingPackageFamily.Sting;
    }

    public static string? NormalizeLabel(string? value)
    {
        return Classify(value) switch
        {
            StingPackageFamily.Sting => "STING",
            StingPackageFamily.StingPlus => "STING PLUS",
            StingPackageFamily.StingFm => "STING FM",
            _ => NormalizeFreeText(value)
        };
    }

    public static (string? unitType, string? uniqueId) ParseCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return (null, null);

        var normalized = code.Trim()
            .Replace('\u2013', '-')
            .Replace('\u2014', '-');

        var splitIndex = normalized.IndexOf(" - ", StringComparison.Ordinal);
        if (splitIndex < 0)
            splitIndex = normalized.IndexOf('-', StringComparison.Ordinal);

        if (splitIndex <= 0)
            return (NormalizeLabel(normalized), null);

        var unitPart = normalized[..splitIndex].Trim();
        var idPart = normalized[(splitIndex + 1)..].Trim().TrimStart('-').Trim();

        return (
            NormalizeLabel(unitPart),
            string.IsNullOrWhiteSpace(idPart) ? null : idPart);
    }

    private static string NormalizeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return new string(value
            .Trim()
            .ToUpperInvariant()
            .Where(ch => char.IsLetterOrDigit(ch) || ch == '+')
            .ToArray());
    }

    private static string? NormalizeFreeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return string.Join(" ", value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
    }
}
