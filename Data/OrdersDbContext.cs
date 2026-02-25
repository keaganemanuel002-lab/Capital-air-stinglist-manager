using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Linq;
using StingListManager.Data.Entities;
using StingListManager.Services;

namespace StingListManager.Data;

public class OrdersDbContext : DbContext
{
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderLineItem> PurchaseOrderLineItems => Set<PurchaseOrderLineItem>();
    public DbSet<PurchaseOrderSupplier> PurchaseOrderSuppliers => Set<PurchaseOrderSupplier>();

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        Paths.Ensure();
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = Paths.OrdersDbPath,
            Cache = SqliteCacheMode.Shared,
            Mode = SqliteOpenMode.ReadWriteCreate,
            DefaultTimeout = 5
        };
        options.UseSqlite(builder.ToString());
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PurchaseOrder>().HasIndex(x => x.OrderNumber).IsUnique();
        modelBuilder.Entity<PurchaseOrder>().HasIndex(x => x.OrderDate);
        modelBuilder.Entity<PurchaseOrder>().HasIndex(x => x.Supplier);
        modelBuilder.Entity<PurchaseOrder>().HasIndex(x => x.Status);

        modelBuilder.Entity<PurchaseOrder>()
            .HasMany(x => x.LineItems)
            .WithOne(x => x.PurchaseOrder)
            .HasForeignKey(x => x.PurchaseOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<PurchaseOrderLineItem>().HasIndex(x => x.PurchaseOrderId);
        modelBuilder.Entity<PurchaseOrderLineItem>()
            .HasIndex(x => new { x.PurchaseOrderId, x.LineNumber })
            .IsUnique();

        modelBuilder.Entity<PurchaseOrderSupplier>().HasIndex(x => x.NameNorm).IsUnique();
        modelBuilder.Entity<PurchaseOrderSupplier>().HasIndex(x => x.Name);
    }

    public static void ConfigureSqlitePragmas(OrdersDbContext db)
    {
        db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
        db.Database.ExecuteSqlRaw("PRAGMA synchronous=NORMAL;");
        db.Database.ExecuteSqlRaw("PRAGMA busy_timeout=5000;");
    }

    public static void EnsureSchema(OrdersDbContext db)
    {
        ConfigureSqlitePragmas(db);
        db.Database.EnsureCreated();

        db.Database.ExecuteSqlRaw(@"
CREATE TABLE IF NOT EXISTS ""PurchaseOrderSuppliers"" (
  ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_PurchaseOrderSuppliers"" PRIMARY KEY AUTOINCREMENT,
  ""Name"" TEXT NOT NULL,
  ""NameNorm"" TEXT NOT NULL,
  ""QuoteIncludesVatDefault"" INTEGER NOT NULL DEFAULT 0
);");
        db.Database.ExecuteSqlRaw(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PurchaseOrderSuppliers_NameNorm"" ON ""PurchaseOrderSuppliers"" (""NameNorm"");");
        db.Database.ExecuteSqlRaw(@"CREATE INDEX IF NOT EXISTS ""IX_PurchaseOrderSuppliers_Name"" ON ""PurchaseOrderSuppliers"" (""Name"");");

        db.Database.ExecuteSqlRaw(@"
CREATE TABLE IF NOT EXISTS ""PurchaseOrderLineItems"" (
  ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_PurchaseOrderLineItems"" PRIMARY KEY AUTOINCREMENT,
  ""PurchaseOrderId"" INTEGER NOT NULL,
  ""LineNumber"" INTEGER NOT NULL,
  ""Description"" TEXT NOT NULL,
  ""Quantity"" TEXT NOT NULL,
  ""UnitPrice"" TEXT NOT NULL,
  ""AmountExVat"" TEXT NOT NULL,
  ""VatAmount"" TEXT NOT NULL,
  ""TotalAmountIncVat"" TEXT NOT NULL,
  CONSTRAINT ""FK_PurchaseOrderLineItems_PurchaseOrders_PurchaseOrderId"" FOREIGN KEY (""PurchaseOrderId"") REFERENCES ""PurchaseOrders"" (""Id"") ON DELETE CASCADE
);");
        db.Database.ExecuteSqlRaw(@"CREATE INDEX IF NOT EXISTS ""IX_PurchaseOrderLineItems_PurchaseOrderId"" ON ""PurchaseOrderLineItems"" (""PurchaseOrderId"");");
        db.Database.ExecuteSqlRaw(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PurchaseOrderLineItems_PurchaseOrderId_LineNumber"" ON ""PurchaseOrderLineItems"" (""PurchaseOrderId"", ""LineNumber"");");

        if (!HasColumn(db, "PurchaseOrders", "QuoteIncludesVat"))
            db.Database.ExecuteSqlRaw(@"ALTER TABLE ""PurchaseOrders"" ADD COLUMN ""QuoteIncludesVat"" INTEGER NOT NULL DEFAULT 0;");
    }

    private static bool HasColumn(OrdersDbContext db, string tableName, string columnName)
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
            if (string.Equals(name, columnName, System.StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
