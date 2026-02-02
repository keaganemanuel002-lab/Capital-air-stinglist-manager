using System;
using System.IO;

namespace StingListManager.Services;

public sealed class InstanceLock : IDisposable
{
    private FileStream? _lock;

    public bool TryLock(string baseDir)
    {
        Directory.CreateDirectory(baseDir);
        var lockPath = Path.Combine(baseDir, "app.lock");
        try
        {
            _lock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose() => _lock?.Dispose();
}