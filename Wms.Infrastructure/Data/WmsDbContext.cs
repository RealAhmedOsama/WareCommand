// Wms.Infrastructure/Data/WmsDbContext.cs

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Wms.Domain.Entities;
using Wms.Infrastructure.Auditing;
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

    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    public DbSet<WmsUserWarehouseAssignment> UserWarehouseAssignments => Set<WmsUserWarehouseAssignment>();

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

        builder.Entity<WmsUserWarehouseAssignment>(entity =>
        {
            entity.ToTable("WmsUserWarehouseAssignments");
            entity.HasKey(assignment => new { assignment.UserId, assignment.WarehouseId });
            entity.Property(assignment => assignment.UserId).HasMaxLength(450).IsRequired();
            entity.Property(assignment => assignment.AssignedAtUtc)
                .HasColumnType("timestamp with time zone")
                .IsRequired();
            entity.HasIndex(assignment => assignment.WarehouseId);
            entity.HasIndex(assignment => new { assignment.UserId, assignment.IsDefault });
            entity.HasOne(assignment => assignment.User)
                .WithMany()
                .HasForeignKey(assignment => assignment.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(assignment => assignment.Warehouse)
                .WithMany()
                .HasForeignKey(assignment => assignment.WarehouseId)
                .OnDelete(DeleteBehavior.Restrict);
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

        builder.Entity<AuditEntry>(entity =>
        {
            entity.ToTable("WmsAuditEntries");
            entity.HasKey(entry => entry.Id);
            entity.Property(entry => entry.Action).HasMaxLength(100).IsRequired();
            entity.Property(entry => entry.EntityType).HasMaxLength(100).IsRequired();
            entity.Property(entry => entry.OccurredAtUnixMilliseconds).IsRequired();
            entity.Property(entry => entry.EntityId).HasMaxLength(200);
            entity.Property(entry => entry.ActorUserId).HasMaxLength(450);
            entity.Property(entry => entry.ActorUserName).HasMaxLength(256);
            entity.Property(entry => entry.CorrelationId).HasMaxLength(100).IsRequired();
            entity.Property(entry => entry.SourceClient).HasMaxLength(50).IsRequired();
            entity.Property(entry => entry.RemoteIpAddress).HasMaxLength(64);
            entity.Property(entry => entry.UserAgent).HasMaxLength(512);
            entity.Property(entry => entry.Details).HasMaxLength(1_000);
            entity.Property(entry => entry.BeforeJson).HasMaxLength(8_000);
            entity.Property(entry => entry.AfterJson).HasMaxLength(8_000);
            entity.HasIndex(entry => entry.OccurredAtUtc);
            entity.HasIndex(entry => entry.OccurredAtUnixMilliseconds);
            entity.HasIndex(entry => new { entry.ActorUserId, entry.OccurredAtUtc });
            entity.HasIndex(entry => new { entry.WarehouseId, entry.OccurredAtUtc });
            entity.HasIndex(entry => new { entry.Action, entry.OccurredAtUtc });
            entity.HasIndex(entry => new { entry.EntityType, entry.EntityId, entry.OccurredAtUtc });
        });
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnsureAuditEntriesAreAppendOnly();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override int SaveChanges()
    {
        EnsureAuditEntriesAreAppendOnly();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        EnsureAuditEntriesAreAppendOnly();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    public override Task<int> SaveChangesAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureAuditEntriesAreAppendOnly();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void EnsureAuditEntriesAreAppendOnly()
    {
        if (ChangeTracker.Entries<AuditEntry>().Any(entry =>
                entry.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException("Audit entries are immutable and cannot be updated or deleted.");
        }
    }
}
