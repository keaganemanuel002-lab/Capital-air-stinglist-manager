using System;
using Microsoft.Data.Sqlite;

namespace StingListManager.Services;

public static class DbRetry
{
    public static void Run(Action action, int retries = 5, int delayMs = 200)
    {
        for (var i = 0; i < retries; i++)
        {
            try
            {
                action();
                return;
            }
            catch (SqliteException ex) when (ex.Message.Contains("locked", StringComparison.OrdinalIgnoreCase))
            {
                System.Threading.Thread.Sleep(delayMs);
            }
        }

        action();
    }
}