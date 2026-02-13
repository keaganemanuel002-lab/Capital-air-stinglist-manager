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
    public DbSet<ClientQuoteSummary> ClientQuoteSummaries => Set<ClientQuoteSummary>();
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<Dashcam> Dashcams => Set<Dashcam>();
    public DbSet<SdCard> SdCards => Set<SdCard>();

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
        modelBuilder.Entity<BillingEntry>().HasIndex(b => b.Company);
        modelBuilder.Entity<BillingEntry>().HasIndex(b => b.ArchivedAt);
        modelBuilder.Entity<BillingEntry>().HasIndex(b => b.ActiveFrom);
        
        modelBuilder.Entity<JobCard>().HasIndex(j => j.Registration);
        modelBuilder.Entity<JobCard>().HasIndex(j => j.Company);
        modelBuilder.Entity<JobCard>().HasIndex(j => j.CreatedAt);
        modelBuilder.Entity<JobCard>().HasIndex(j => j.Status);
        
        modelBuilder.Entity<Quote>().HasIndex(q => q.Registration);
        modelBuilder.Entity<Quote>().HasIndex(q => q.Company);
        modelBuilder.Entity<Quote>().HasIndex(q => q.CreatedAt);
        modelBuilder.Entity<Quote>().HasIndex(q => q.Status);
        
        modelBuilder.Entity<CancellationEntry>().HasIndex(c => c.Registration);
        modelBuilder.Entity<CancellationEntry>().HasIndex(c => c.DateRequestReceived);

        modelBuilder.Entity<Dashcam>().HasIndex(d => d.SerialNumber);
        modelBuilder.Entity<Dashcam>().HasIndex(d => d.AllocatedVehicleRegistration);
        modelBuilder.Entity<SdCard>().HasIndex(s => s.SerialNumber);
        modelBuilder.Entity<SdCard>().Property(s => s.SlotNumber).HasDefaultValue(1);
        modelBuilder.Entity<SdCard>().HasIndex(s => new { s.DashcamId, s.SlotNumber });
        modelBuilder.Entity<SdCard>()
            .ToTable(t => t.HasCheckConstraint("CK_SdCards_SlotNumber", "\"SlotNumber\" IN (1, 2)"));
        
        // Quote relationships
        modelBuilder.Entity<Quote>()
            .HasMany(q => q.LineItems)
            .WithOne(li => li.Quote)
            .HasForeignKey(li => li.QuoteId)
            .OnDelete(DeleteBehavior.Cascade);

        base.OnModelCreating(modelBuilder);
    }
}
