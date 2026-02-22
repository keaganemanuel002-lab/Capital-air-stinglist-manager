using Microsoft.EntityFrameworkCore.Design;
using StingListManager.Services;

namespace StingListManager.Data;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        Paths.Ensure();
        return new AppDbContext();
    }
}
