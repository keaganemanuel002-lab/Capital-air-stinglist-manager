using System;
using System.Collections.Generic;
using System.Linq;

namespace StingListManager.Services;

public sealed class JobCompletionNotificationParts
{
    public string PrimaryMessage { get; init; } = string.Empty;
    public IReadOnlyList<string> IntegrationWarnings { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> IntegrationInfo { get; init; } = Array.Empty<string>();
}

public static class JobCompletionNotificationParser
{
    private static readonly string[] ErrorMarkers =
    {
        "error",
        "failed",
        "cannot",
        "can't",
        "not permitted",
        "not found",
        "missing",
        "invalid",
        "blocked",
        "exception"
    };

    public static JobCompletionNotificationParts Parse(string? rawMessage)
    {
        if (string.IsNullOrWhiteSpace(rawMessage))
        {
            return new JobCompletionNotificationParts
            {
                PrimaryMessage = "Job card completed."
            };
        }

        var segments = SplitSegments(rawMessage);
        if (segments.Count == 0)
        {
            return new JobCompletionNotificationParts
            {
                PrimaryMessage = rawMessage.Trim()
            };
        }

        var primary = new List<string>();
        var warnings = new List<string>();
        var info = new List<string>();

        foreach (var segment in segments)
        {
            if (IsIntegrationSegment(segment))
            {
                if (LooksLikeFailure(segment))
                    warnings.Add(segment);
                else
                    info.Add(segment);

                continue;
            }

            primary.Add(segment);
        }

        var primaryMessage = primary.Count > 0
            ? string.Join(". ", primary)
            : segments[0];

        return new JobCompletionNotificationParts
        {
            PrimaryMessage = primaryMessage,
            IntegrationWarnings = warnings,
            IntegrationInfo = info
        };
    }

    private static List<string> SplitSegments(string message)
    {
        var normalized = message.Replace("\r\n", "\n", StringComparison.Ordinal);
        var lines = normalized
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var segments = new List<string>();
        foreach (var line in lines)
        {
            foreach (var part in line.Split(". ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var trimmed = part.Trim();
                if (string.IsNullOrWhiteSpace(trimmed))
                    continue;

                if (trimmed.EndsWith(".", StringComparison.Ordinal))
                    trimmed = trimmed[..^1].TrimEnd();

                if (!string.IsNullOrWhiteSpace(trimmed))
                    segments.Add(trimmed);
            }
        }

        return segments;
    }

    private static bool IsIntegrationSegment(string segment)
    {
        return segment.Contains("Wialon", StringComparison.OrdinalIgnoreCase)
               || segment.Contains("Flickswitch", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeFailure(string segment)
    {
        return ErrorMarkers.Any(marker => segment.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }
}

