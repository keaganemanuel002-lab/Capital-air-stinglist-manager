namespace StingListManager.Services;

public static class DataStoreFactory
{
    public static IDataStore Create(AppSettings settings)
    {
        if (settings.FirestorePrimaryDataEnabled)
            return new FirestoreDataStore(settings);

        return new LocalSqliteDataStore();
    }
}

