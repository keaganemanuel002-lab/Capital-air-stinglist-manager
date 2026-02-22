using System;

namespace StingListManager.ViewModels;

public sealed class AppNotificationItem
{
    public string Title { get; init; } = "Status";
    public string Message { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public bool IsError { get; init; }

    public string TimestampLabel => CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    public string RelativeLabel
    {
        get
        {
            var age = DateTimeOffset.UtcNow - CreatedAt;
            if (age.TotalSeconds < 60)
                return "just now";
            if (age.TotalMinutes < 60)
                return $"{Math.Max(1, (int)age.TotalMinutes)} minute(s) ago";
            if (age.TotalHours < 24)
                return $"{Math.Max(1, (int)age.TotalHours)} hour(s) ago";
            return $"{Math.Max(1, (int)age.TotalDays)} day(s) ago";
        }
    }

    public string AccentHex => IsError ? "#DC2626" : "#2563EB";
}
