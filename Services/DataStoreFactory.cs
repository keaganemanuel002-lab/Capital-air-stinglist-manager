namespace StingListManager.Services;

public static class DataStoreFactory
{
    public static IDataStore Create(AppSettings settings)
    {
        _ = settings;
        return new LocalSqliteDataStore();
    }
}
