using System;

namespace StingListManager.Services;

public static class LocalDataChangeNotifier
{
    public static event Action? JobCardsChanged;

    public static void NotifyJobCardsChanged()
    {
        try
        {
            JobCardsChanged?.Invoke();
        }
        catch
        {
            // Never allow notification faults to block local database commits.
        }
    }
}
