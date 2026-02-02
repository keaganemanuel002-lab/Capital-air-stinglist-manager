using System.Collections.Generic;
using System.Linq;
using StingListManager.Data;
using StingListManager.Data.Entities;

namespace StingListManager.Services;

public class BillingLookupService
{
    public List<string> GetActiveCodesForRegistration(string registration)
    {
        // Legacy method - Code field has been removed from the database
        // Returning empty list for backwards compatibility
        return new List<string>();
    }
}
