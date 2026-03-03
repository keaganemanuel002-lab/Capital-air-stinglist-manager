using System;
using System.Collections.Generic;
using System.Linq;

namespace StingListManager.Services;

public static class TrackingUnitMakeCatalog
{
    private static readonly string[] StandardOptions =
    [
        "FMB 120",
        "FMC 130",
        "FMC 150",
        "FMC 920",
        "FMT 100",
        "GL33",
        "GL521",
        "TMT 250",
        "OYSTER YABBY 2G"
    ];

    public static IReadOnlyList<string> Options => StandardOptions;

    public static IReadOnlyList<string> BuildOptionsIncluding(params string?[] values)
    {
        var merged = new List<string>(StandardOptions);
        foreach (var value in values)
        {
            var normalized = Normalize(value);
            if (string.IsNullOrWhiteSpace(normalized))
                continue;

            if (merged.Any(x => string.Equals(x, normalized, StringComparison.OrdinalIgnoreCase)))
                continue;

            merged.Add(normalized);
        }

        return merged;
    }

    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = string.Join(" ", value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
        var compact = new string(trimmed.Where(char.IsLetterOrDigit).ToArray());

        foreach (var option in StandardOptions)
        {
            var optionCompact = new string(option.Where(char.IsLetterOrDigit).ToArray());
            if (string.Equals(optionCompact, compact, StringComparison.OrdinalIgnoreCase))
                return option;
        }

        return trimmed;
    }
}

public static class StingPackageCatalog
{
    private static readonly string[] StandardOptions =
    [
        "STING",
        "STING PLUS",
        "STING FM"
    ];

    public static IReadOnlyList<string> Options => StandardOptions;

    public static IReadOnlyList<string> BuildOptionsIncluding(params string?[] values)
    {
        var merged = new List<string>(StandardOptions);
        foreach (var value in values)
        {
            var normalized = Normalize(value);
            if (string.IsNullOrWhiteSpace(normalized))
                continue;

            if (merged.Any(x => string.Equals(x, normalized, StringComparison.OrdinalIgnoreCase)))
                continue;

            merged.Add(normalized);
        }

        return merged;
    }

    public static string? Normalize(string? value)
    {
        var family = StingPackageClassifier.Classify(value);
        return family switch
        {
            StingPackageFamily.Sting => "STING",
            StingPackageFamily.StingPlus => "STING PLUS",
            StingPackageFamily.StingFm => "STING FM",
            _ => null
        };
    }
}
