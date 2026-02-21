using System.Linq;
using StingListManager.Data;

namespace StingListManager.Services;

public static class QuoteNumberAllocator
{
    public static int GetNext(AppDbContext db)
    {
        return (db.Quotes.Select(x => (int?)x.QuoteNumber).Max() ?? 0) + 1;
    }
}
