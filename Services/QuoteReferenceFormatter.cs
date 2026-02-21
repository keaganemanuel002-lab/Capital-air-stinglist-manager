using System;

namespace StingListManager.Services;

public static class QuoteReferenceFormatter
{
    public static string Format(int quoteNumber)
    {
        var normalized = Math.Max(0, quoteNumber);
        return $"QUO{normalized:0000}";
    }
}
