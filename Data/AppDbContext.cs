using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using StingListManager.Data.Entities;
using StingListManager.Services;

namespace StingListManager.Data;

public class AppDbContext : DbContext
{
    public DbSet<BillingEntry> BillingEntries => Set<BillingEntry>();
    public DbSet<CancellationEntry> CancellationEntries => Set<CancellationEntry>();
    public DbSet<Quote> Quotes => Set<Quote>();
    public DbSet<JobCard> JobCards => Set<JobCard>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<QuoteLineItem> QuoteLineItems => Set<QuoteLineItem>();

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        Paths.Ensure();
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = Paths.DbPath,
            Cache = SqliteCacheMode.Shared,
            Mode = SqliteOpenMode.ReadWriteCreate,
            DefaultTimeout = 5
        };
        options.UseSqlite(builder.ToString());
    }

    public static void ConfigureSqlitePragmas(AppDbContext db)
    {
        db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
        db.Database.ExecuteSqlRaw("PRAGMA synchronous=NORMAL;");
        db.Database.ExecuteSqlRaw("PRAGMA busy_timeout=5000;");
    }

    public override int SaveChanges()
    {
        var result = 0;
        DbRetry.Run(() =>
        {
            ConfigureSqlitePragmas(this);
            result = base.SaveChanges();
        });

        return result;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Fast search indexes
        modelBuilder.Entity<BillingEntry>().HasIndex(b => b.RegistrationNorm);
        modelBuilder.Entity<JobCard>().HasIndex(j => j.Registration);
        modelBuilder.Entity<Quote>().HasIndex(q => q.Registration);
        modelBuilder.Entity<CancellationEntry>().HasIndex(c => c.Registration);
        
        // Quote relationships
        modelBuilder.Entity<Quote>()
            .HasMany(q => q.LineItems)
            .WithOne(li => li.Quote)
            .HasForeignKey(li => li.QuoteId)
            .OnDelete(DeleteBehavior.Cascade);

        base.OnModelCreating(modelBuilder);
    }
}
