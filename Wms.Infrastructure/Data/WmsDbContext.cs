// Wms.Infrastructure/Data/WmsDbContext.cs

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Wms.Domain.Entities;
using Wms.Infrastructure.Data.Configurations;
using Wms.Infrastructure.Identity;

namespace Wms.Infrastructure.Data;

public class WmsDbContext : IdentityDbContext<WmsUser, IdentityRole, string>
{
    public WmsDbContext(DbContextOptions<WmsDbContext> options) : base(options)
    {
    }

    public DbSet<Item> Items => Set<Item>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<Lot> Lots => Set<Lot>();
    public DbSet<Stock> Stock => Set<Stock>();
    public DbSet<Movement> Movements => Set<Movement>();

    public DbSet<WmsAuthenticationEvent> AuthenticationEvents => Set<WmsAuthenticationEvent>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.ApplyConfiguration(new ItemConfiguration());
        builder.ApplyConfiguration(new WarehouseConfiguration());
        builder.ApplyConfiguration(new LocationConfiguration());
        builder.ApplyConfiguration(new LotConfiguration());
        builder.ApplyConfiguration(new StockConfiguration());
        builder.ApplyConfiguration(new MovementConfiguration());

        builder.Entity<WmsUser>(entity =>
        {
            entity.Property(user => user.DisplayName).HasMaxLength(200).IsRequired();
            entity.Property(user => user.EmployeeCode).HasMaxLength(50).IsRequired();
            entity.Property(user => user.Locale).HasMaxLength(20).IsRequired();
            entity.Property(user => user.TimeZone).HasMaxLength(100).IsRequired();
            entity.HasIndex(user => user.EmployeeCode).IsUnique();
        });

        builder.Entity<WmsAuthenticationEvent>(entity =>
        {
            entity.ToTable("WmsAuthenticationEvents");
            entity.HasKey(auditEvent => auditEvent.Id);
            entity.Property(auditEvent => auditEvent.EventType).HasMaxLength(100).IsRequired();
            entity.Property(auditEvent => auditEvent.UserId).HasMaxLength(450);
            entity.Property(auditEvent => auditEvent.UserName).HasMaxLength(256);
            entity.Property(auditEvent => auditEvent.RemoteIpAddress).HasMaxLength(64);
            entity.Property(auditEvent => auditEvent.UserAgent).HasMaxLength(512);
            entity.Property(auditEvent => auditEvent.Details).HasMaxLength(1000);
            entity.HasIndex(auditEvent => auditEvent.OccurredAtUtc);
            entity.HasIndex(auditEvent => new { auditEvent.UserId, auditEvent.OccurredAtUtc });
        });
    }
}
