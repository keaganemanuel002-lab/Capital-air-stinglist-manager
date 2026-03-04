using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using System;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
    public DbSet<PhoneIssueLogEntry> PhoneIssueLogEntries => Set<PhoneIssueLogEntry>();
    public DbSet<DriverTag> DriverTags => Set<DriverTag>();
    public DbSet<DriverTagTransfer> DriverTagTransfers => Set<DriverTagTransfer>();
    public DbSet<UserAccount> UserAccounts => Set<UserAccount>();

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

    public static void EnsureSchemaCompatibility(AppDbContext db)
    {
        ConfigureSqlitePragmas(db);

        if (!HasColumn(db, "JobCards", "InstallationTechnician"))
        {
            db.Database.ExecuteSqlRaw(@"ALTER TABLE ""JobCards"" ADD COLUMN ""InstallationTechnician"" TEXT;");
        }
    }

    public override int SaveChanges()
    {
        var jobCardsChanged = HasTrackedJobCardMutations();
        var result = 0;
        DbRetry.Run(() =>
        {
            NormalizeTrackedEntities();
            ConfigureSqlitePragmas(this);
            result = base.SaveChanges();
        });

        if (result > 0 && jobCardsChanged)
            LocalDataChangeNotifier.NotifyJobCardsChanged();

        return result;
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var jobCardsChanged = HasTrackedJobCardMutations();

        const int retries = 5;
        const int delayMs = 200;

        for (var i = 0; i < retries - 1; i++)
        {
            try
            {
                NormalizeTrackedEntities();
                ConfigureSqlitePragmas(this);
                var result = await base.SaveChangesAsync(cancellationToken);
                if (result > 0 && jobCardsChanged)
                    LocalDataChangeNotifier.NotifyJobCardsChanged();
                return result;
            }
            catch (SqliteException ex) when (ex.Message.Contains("locked", StringComparison.OrdinalIgnoreCase))
            {
                await Task.Delay(delayMs, cancellationToken);
            }
        }

        NormalizeTrackedEntities();
        ConfigureSqlitePragmas(this);
        var finalResult = await base.SaveChangesAsync(cancellationToken);
        if (finalResult > 0 && jobCardsChanged)
            LocalDataChangeNotifier.NotifyJobCardsChanged();
        return finalResult;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Fast search indexes
        modelBuilder.Entity<BillingEntry>().HasIndex(b => b.RegistrationNorm);
        modelBuilder.Entity<BillingEntry>().HasIndex(b => b.ImeiNorm)
            .IsUnique()
            .HasFilter("\"ArchivedAt\" IS NULL AND (\"Status\" = 0 OR \"Status\" = 2) AND \"ImeiNorm\" <> ''");
        modelBuilder.Entity<BillingEntry>().HasIndex(b => b.IccidNorm)
            .IsUnique()
            .HasFilter("\"ArchivedAt\" IS NULL AND (\"Status\" = 0 OR \"Status\" = 2) AND \"IccidNorm\" <> ''");
        modelBuilder.Entity<BillingEntry>().HasIndex(b => b.SerialNumberNorm)
            .IsUnique()
            .HasFilter("\"ArchivedAt\" IS NULL AND (\"Status\" = 0 OR \"Status\" = 2) AND \"SerialNumberNorm\" <> ''");
        modelBuilder.Entity<BillingEntry>().HasIndex(b => b.Company);
        modelBuilder.Entity<BillingEntry>().HasIndex(b => b.ArchivedAt);
        modelBuilder.Entity<BillingEntry>().HasIndex(b => b.ActiveFrom);
        
        modelBuilder.Entity<JobCard>().HasIndex(j => j.Registration);
        modelBuilder.Entity<JobCard>().HasIndex(j => j.Company);
        modelBuilder.Entity<JobCard>().HasIndex(j => j.CreatedAt);
        modelBuilder.Entity<JobCard>().HasIndex(j => j.Status);
        modelBuilder.Entity<JobCard>().HasIndex(j => j.JobCardNumber).IsUnique();
        
        modelBuilder.Entity<Quote>().HasIndex(q => q.Registration);
        modelBuilder.Entity<Quote>().HasIndex(q => q.Company);
        modelBuilder.Entity<Quote>().HasIndex(q => q.CreatedAt);
        modelBuilder.Entity<Quote>().HasIndex(q => q.Status);
        modelBuilder.Entity<Quote>().HasIndex(q => q.QuoteNumber).IsUnique();

        modelBuilder.Entity<Client>().HasIndex(c => c.NameNorm).IsUnique();
        modelBuilder.Entity<UserAccount>().HasIndex(u => u.UsernameNorm).IsUnique();
        modelBuilder.Entity<PhoneIssueLogEntry>().HasIndex(p => p.TeamNameNorm);
        modelBuilder.Entity<PhoneIssueLogEntry>().HasIndex(p => p.VehicleRegistrationNorm);
        modelBuilder.Entity<PhoneIssueLogEntry>().HasIndex(p => p.PhoneImeiNorm);
        modelBuilder.Entity<PhoneIssueLogEntry>().HasIndex(p => p.PhoneImeiSecondaryNorm);
        modelBuilder.Entity<PhoneIssueLogEntry>().HasIndex(p => p.IssuedAt);
        modelBuilder.Entity<PhoneIssueLogEntry>().HasIndex(p => p.ReturnedAt);
        modelBuilder.Entity<DriverTag>().HasIndex(t => t.TagCodeNorm).IsUnique();
        modelBuilder.Entity<DriverTag>().HasIndex(t => t.DriverNameNorm);
        modelBuilder.Entity<DriverTag>().HasIndex(t => t.IssuedAt);
        modelBuilder.Entity<DriverTag>().HasIndex(t => t.LostOrDamagedReportedAt);
        modelBuilder.Entity<DriverTag>().HasIndex(t => t.EmploymentExitAt);
        modelBuilder.Entity<DriverTag>().HasIndex(t => t.ReturnStatus);
        modelBuilder.Entity<DriverTagTransfer>().HasIndex(t => t.DriverTagId);
        modelBuilder.Entity<DriverTagTransfer>().HasIndex(t => t.TransferredAt);
        
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

    private void NormalizeTrackedEntities()
    {
        NormalizeBillingEntries();
        NormalizeClients();
        NormalizePhoneIssueLogEntries();
        NormalizeDriverTags();
        NormalizeDriverTagTransfers();
        NormalizeUsers();
    }

    private bool HasTrackedJobCardMutations()
    {
        foreach (var entry in ChangeTracker.Entries<JobCard>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                return true;
        }

        return false;
    }

    private void NormalizeBillingEntries()
    {
        foreach (var entry in ChangeTracker.Entries<BillingEntry>())
        {
            if (entry.State is not EntityState.Added and not EntityState.Modified)
                continue;

            var entity = entry.Entity;
            entity.Registration = NormalizeRegistration(entity.Registration);
            entity.RegistrationNorm = entity.Registration;
            entity.ImeiNorm = NormalizeDigits(entity.Imei);
            entity.IccidNorm = NormalizeDigits(entity.Iccid);
            entity.SerialNumberNorm = NormalizeText(entity.SerialNumber);
        }
    }

    private void NormalizeClients()
    {
        foreach (var entry in ChangeTracker.Entries<Client>())
        {
            if (entry.State is not EntityState.Added and not EntityState.Modified)
                continue;

            var entity = entry.Entity;
            entity.Name = NormalizeClientName(entity.Name);
            entity.NameNorm = NormalizeComparableText(entity.Name);
        }
    }

    private void NormalizeUsers()
    {
        foreach (var entry in ChangeTracker.Entries<UserAccount>())
        {
            if (entry.State is not EntityState.Added and not EntityState.Modified)
                continue;

            var entity = entry.Entity;
            entity.Username = NormalizeUserName(entity.Username);
            entity.UsernameNorm = NormalizeComparableText(entity.Username);
            entity.Role = NormalizeRole(entity.Role);
        }
    }

    private void NormalizePhoneIssueLogEntries()
    {
        foreach (var entry in ChangeTracker.Entries<PhoneIssueLogEntry>())
        {
            if (entry.State is not EntityState.Added and not EntityState.Modified)
                continue;

            var entity = entry.Entity;
            entity.TeamName = NormalizeClientName(entity.TeamName);
            entity.TeamMemberOne = NormalizeClientName(entity.TeamMemberOne);
            entity.TeamMemberTwo = NormalizeClientName(entity.TeamMemberTwo);
            entity.VehicleRegistration = NormalizeRegistration(entity.VehicleRegistration);
            entity.PhoneLabel = NormalizeOptionalSingleLine(entity.PhoneLabel);
            entity.PhoneNumber = NormalizeOptionalSingleLine(entity.PhoneNumber);
            entity.PhoneImei = NormalizeDigitsOrNull(entity.PhoneImei);
            entity.PhoneImeiSecondary = NormalizeDigitsOrNull(entity.PhoneImeiSecondary);
            entity.RepairDetails = NormalizeOptionalMultiline(entity.RepairDetails);
            entity.Notes = NormalizeOptionalMultiline(entity.Notes);
            entity.TeamNameNorm = NormalizeComparableText(entity.TeamName);
            entity.VehicleRegistrationNorm = entity.VehicleRegistration;
            entity.PhoneImeiNorm = NormalizeDigits(entity.PhoneImei);
            entity.PhoneImeiSecondaryNorm = NormalizeDigits(entity.PhoneImeiSecondary);

            if (entity.IssuedAt == default)
                entity.IssuedAt = DateTime.UtcNow;

            entity.IssuedAt = EnsureUtc(entity.IssuedAt);
            if (entity.ReturnedAt is DateTime returnedAt)
                entity.ReturnedAt = EnsureUtc(returnedAt);
        }
    }

    private void NormalizeDriverTags()
    {
        foreach (var entry in ChangeTracker.Entries<DriverTag>())
        {
            if (entry.State is not EntityState.Added and not EntityState.Modified)
                continue;

            var entity = entry.Entity;
            entity.TagCode = NormalizeOptionalSingleLine(entity.TagCode) ?? string.Empty;
            entity.TagCodeNorm = NormalizeComparableText(entity.TagCode);
            entity.DriverName = NormalizeClientName(entity.DriverName);
            entity.DriverNameNorm = NormalizeComparableText(entity.DriverName);
            entity.LostOrDamagedReason = NormalizeOptionalMultiline(entity.LostOrDamagedReason);
            entity.Notes = NormalizeOptionalMultiline(entity.Notes);

            if (entity.IssuedAt == default)
                entity.IssuedAt = DateTime.UtcNow;

            entity.IssuedAt = EnsureUtc(entity.IssuedAt);
            if (entity.LostOrDamagedReportedAt is DateTime lostAt)
                entity.LostOrDamagedReportedAt = EnsureUtc(lostAt);
            if (entity.EmploymentExitAt is DateTime exitAt)
                entity.EmploymentExitAt = EnsureUtc(exitAt);
            if (entity.ReturnedAt is DateTime returnedAt)
                entity.ReturnedAt = EnsureUtc(returnedAt);

            if (entity.ReturnStatus != DriverTagReturnStatus.Returned)
                entity.ReturnedAt = null;

            if (entity.EmploymentExitType == DriverEmploymentExitType.None)
            {
                entity.EmploymentExitAt = null;
                if (entity.ReturnStatus != DriverTagReturnStatus.Returned)
                    entity.ReturnStatus = DriverTagReturnStatus.Unknown;
            }
        }
    }

    private void NormalizeDriverTagTransfers()
    {
        foreach (var entry in ChangeTracker.Entries<DriverTagTransfer>())
        {
            if (entry.State is not EntityState.Added and not EntityState.Modified)
                continue;

            var entity = entry.Entity;
            entity.FromDriverName = NormalizeClientName(entity.FromDriverName);
            entity.ToDriverName = NormalizeClientName(entity.ToDriverName);
            entity.Reason = NormalizeOptionalMultiline(entity.Reason) ?? string.Empty;
            entity.TransferredBy = NormalizeOptionalSingleLine(entity.TransferredBy);

            if (entity.TransferredAt == default)
                entity.TransferredAt = DateTime.UtcNow;

            entity.TransferredAt = EnsureUtc(entity.TransferredAt);
        }
    }

    private static string NormalizeRegistration(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return value.Trim().ToUpperInvariant();
    }

    private static string NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return value.Trim().ToUpperInvariant();
    }

    private static string NormalizeDigits(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return new string(value.Where(char.IsDigit).ToArray());
    }

    private static string? NormalizeDigitsOrNull(string? value)
    {
        var digits = NormalizeDigits(value);
        return string.IsNullOrWhiteSpace(digits) ? null : digits;
    }

    private static string? NormalizeOptionalSingleLine(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return string.Join(" ", value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string? NormalizeOptionalMultiline(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string NormalizeClientName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return string.Join(" ", value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string NormalizeComparableText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return new string(value
            .Trim()
            .ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray());
    }

    private static string NormalizeUserName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return value.Trim();
    }

    private static DateTime EnsureUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }

    private static string NormalizeRole(string? role)
    {
        if (string.IsNullOrWhiteSpace(role))
            return "Tech";

        var value = role.Trim();
        return value switch
        {
            "Admin" => "Admin",
            "Ops" => "Ops",
            "Tech" => "Tech",
            "ReadOnly" => "ReadOnly",
            _ => "Tech"
        };
    }

    private static bool HasColumn(AppDbContext db, string tableName, string columnName)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{tableName}\");";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var name = reader["name"]?.ToString();
            if (string.Equals(name, columnName, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
