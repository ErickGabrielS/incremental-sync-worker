using Microsoft.EntityFrameworkCore;

namespace SyncWorker.Data;

public sealed class SyncDbContext(DbContextOptions<SyncDbContext> options) : DbContext(options)
{
    public DbSet<StoreOrder> Orders => Set<StoreOrder>();
    public DbSet<SyncWatermark> SyncWatermarks => Set<SyncWatermark>();
    public DbSet<IntegrationLog> IntegrationLogs => Set<IntegrationLog>();
    public DbSet<StatusMapping> StatusMappings => Set<StatusMapping>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StoreOrder>(e =>
        {
            e.ToTable("StoreOrders");
            e.HasIndex(o => o.ErpOrderId).IsUnique();
            e.Property(o => o.ErpOrderId).HasMaxLength(50);
            e.Property(o => o.CustomerName).HasMaxLength(200);
            e.Property(o => o.Status).HasMaxLength(30);
            e.Property(o => o.Total).HasPrecision(18, 2);
        });

        modelBuilder.Entity<SyncWatermark>(e =>
        {
            e.ToTable("SyncWatermarks");
            e.HasKey(w => w.Name);
            e.Property(w => w.Name).HasMaxLength(50);
        });

        modelBuilder.Entity<IntegrationLog>(e =>
        {
            e.ToTable("IntegrationLogs");
            e.HasIndex(l => new { l.Entity, l.ExternalId }).IsUnique();
            e.HasIndex(l => new { l.Entity, l.Status });
            e.Property(l => l.Entity).HasMaxLength(50);
            e.Property(l => l.ExternalId).HasMaxLength(50);
            e.Property(l => l.Message).HasMaxLength(1000);
        });

        modelBuilder.Entity<StatusMapping>(e =>
        {
            e.ToTable("StatusMappings");
            e.HasKey(m => m.ErpStatus);
            e.Property(m => m.ErpStatus).HasMaxLength(30);
            e.Property(m => m.StoreStatus).HasMaxLength(30);
        });
    }
}
