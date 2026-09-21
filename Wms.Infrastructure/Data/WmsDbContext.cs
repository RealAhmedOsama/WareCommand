// Wms.Infrastructure/Data/WmsDbContext.cs

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Wms.Application.Context;
using Wms.Domain.Common;
using Wms.Domain.Entities;
using Wms.Infrastructure.Auditing;
using Wms.Infrastructure.Data.Configurations;
using Wms.Infrastructure.Identity;
using Wms.Infrastructure.Jobs;
using Wms.Infrastructure.Settings;
using Wms.Domain.Services;
using WarehouseWorkEntity = Wms.Domain.Entities.WarehouseWork;

namespace Wms.Infrastructure.Data;

public class WmsDbContext : IdentityDbContext<WmsUser, IdentityRole, string>
{
    private readonly IClock _clock;

    public WmsDbContext(DbContextOptions<WmsDbContext> options, IClock? clock = null) : base(options)
    {
        _clock = clock ?? new SystemClock();
    }

    public DbSet<Item> Items => Set<Item>();
    public DbSet<ItemPackaging> ItemPackagings => Set<ItemPackaging>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<SupplierItemReference> SupplierItemReferences => Set<SupplierItemReference>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderLine> PurchaseOrderLines => Set<PurchaseOrderLine>();
    public DbSet<PurchaseOrderReceiptAllocation> PurchaseOrderReceiptAllocations =>
        Set<PurchaseOrderReceiptAllocation>();
    public DbSet<AdvanceShippingNotice> AdvanceShippingNotices => Set<AdvanceShippingNotice>();
    public DbSet<AdvanceShippingNoticeLine> AdvanceShippingNoticeLines => Set<AdvanceShippingNoticeLine>();
    public DbSet<AdvanceShippingNoticeReceiptAllocation> AdvanceShippingNoticeReceiptAllocations =>
        Set<AdvanceShippingNoticeReceiptAllocation>();
    public DbSet<AdvanceShippingNoticeDiscrepancy> AdvanceShippingNoticeDiscrepancies =>
        Set<AdvanceShippingNoticeDiscrepancy>();
    public DbSet<Receipt> Receipts => Set<Receipt>();
    public DbSet<ReceiptLine> ReceiptLines => Set<ReceiptLine>();
    public DbSet<ReceiptLineMovement> ReceiptLineMovements => Set<ReceiptLineMovement>();
    public DbSet<ReceiptLineLink> ReceiptLineLinks => Set<ReceiptLineLink>();
    public DbSet<ReceivingSession> ReceivingSessions => Set<ReceivingSession>();
    public DbSet<ReceivingSessionLine> ReceivingSessionLines => Set<ReceivingSessionLine>();
    public DbSet<ReceivingSessionScan> ReceivingSessionScans => Set<ReceivingSessionScan>();
    public DbSet<QualityProfile> QualityProfiles => Set<QualityProfile>();
    public DbSet<QualityProfileTest> QualityProfileTests => Set<QualityProfileTest>();
    public DbSet<QualityInspection> QualityInspections => Set<QualityInspection>();
    public DbSet<QualityInspectionTestResult> QualityInspectionTestResults => Set<QualityInspectionTestResult>();
    public DbSet<QualityInspectionDisposition> QualityInspectionDispositions => Set<QualityInspectionDisposition>();
    public DbSet<WarehouseWorkEntity> WarehouseWorks => Set<WarehouseWorkEntity>();
    public DbSet<WarehouseWorkLine> WarehouseWorkLines => Set<WarehouseWorkLine>();
    public DbSet<WarehouseWorkCommand> WarehouseWorkCommands => Set<WarehouseWorkCommand>();
    public DbSet<UnitOfMeasure> UnitOfMeasures => Set<UnitOfMeasure>();
    public DbSet<ItemUnitConversion> ItemUnitConversions => Set<ItemUnitConversion>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<WarehouseOperationalLocation> WarehouseOperationalLocations =>
        Set<WarehouseOperationalLocation>();
    public DbSet<WarehouseNumberSequence> WarehouseNumberSequences =>
        Set<WarehouseNumberSequence>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<Lot> Lots => Set<Lot>();
    public DbSet<SerialNumber> SerialNumbers => Set<SerialNumber>();
    public DbSet<InventoryStatus> InventoryStatuses => Set<InventoryStatus>();
    public DbSet<InventoryStatusTransition> InventoryStatusTransitions => Set<InventoryStatusTransition>();
    public DbSet<LicensePlate> LicensePlates => Set<LicensePlate>();
    public DbSet<LicensePlateContent> LicensePlateContents => Set<LicensePlateContent>();
    public DbSet<LicensePlateHistory> LicensePlateHistories => Set<LicensePlateHistory>();
    public DbSet<LicensePlateNumberSequence> LicensePlateNumberSequences => Set<LicensePlateNumberSequence>();
    public DbSet<Stock> Stock => Set<Stock>();
    public DbSet<Movement> Movements => Set<Movement>();
    public DbSet<InventoryBalance> InventoryBalances => Set<InventoryBalance>();
    public DbSet<InventoryTransaction> InventoryTransactions => Set<InventoryTransaction>();
    public DbSet<InventoryCommandIdempotency> InventoryCommandIdempotencies => Set<InventoryCommandIdempotency>();
    public DbSet<InventoryReservation> InventoryReservations => Set<InventoryReservation>();
    public DbSet<InventoryReservationAllocation> InventoryReservationAllocations =>
        Set<InventoryReservationAllocation>();
    public DbSet<InventoryReservationEvent> InventoryReservationEvents =>
        Set<InventoryReservationEvent>();
    public DbSet<InventoryReplenishmentPolicy> InventoryReplenishmentPolicies =>
        Set<InventoryReplenishmentPolicy>();
    public DbSet<WmsIdentifier> WmsIdentifiers => Set<WmsIdentifier>();

    public DbSet<WmsAuthenticationEvent> AuthenticationEvents => Set<WmsAuthenticationEvent>();

    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    public DbSet<WmsUserWarehouseAssignment> UserWarehouseAssignments => Set<WmsUserWarehouseAssignment>();

    public DbSet<WmsGlobalSettingsEntity> GlobalSettings => Set<WmsGlobalSettingsEntity>();

    public DbSet<WmsWarehouseSettingsOverrideEntity> WarehouseSettingsOverrides =>
        Set<WmsWarehouseSettingsOverrideEntity>();

    public DbSet<WmsJobExecutionEntity> JobExecutions => Set<WmsJobExecutionEntity>();

    public DbSet<WmsJobNotificationEntity> JobNotifications => Set<WmsJobNotificationEntity>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.ApplyConfiguration(new ItemConfiguration());
        builder.ApplyConfiguration(new ItemPackagingConfiguration());
        builder.ApplyConfiguration(new SupplierConfiguration());
        builder.ApplyConfiguration(new SupplierItemReferenceConfiguration());
        builder.ApplyConfiguration(new PurchaseOrderConfiguration());
        builder.ApplyConfiguration(new PurchaseOrderLineConfiguration());
        builder.ApplyConfiguration(new PurchaseOrderReceiptAllocationConfiguration());
        builder.ApplyConfiguration(new AdvanceShippingNoticeConfiguration());
        builder.ApplyConfiguration(new AdvanceShippingNoticeLineConfiguration());
        builder.ApplyConfiguration(new AdvanceShippingNoticeReceiptAllocationConfiguration());
        builder.ApplyConfiguration(new AdvanceShippingNoticeDiscrepancyConfiguration());
        builder.ApplyConfiguration(new ReceiptConfiguration());
        builder.ApplyConfiguration(new ReceiptLineConfiguration());
        builder.ApplyConfiguration(new ReceiptLineMovementConfiguration());
        builder.ApplyConfiguration(new ReceiptLineLinkConfiguration());
        builder.ApplyConfiguration(new ReceivingSessionConfiguration());
        builder.ApplyConfiguration(new ReceivingSessionLineConfiguration());
        builder.ApplyConfiguration(new ReceivingSessionScanConfiguration());
        builder.ApplyConfiguration(new QualityProfileConfiguration());
        builder.ApplyConfiguration(new QualityProfileTestConfiguration());
        builder.ApplyConfiguration(new QualityInspectionConfiguration());
        builder.ApplyConfiguration(new QualityInspectionTestResultConfiguration());
        builder.ApplyConfiguration(new QualityInspectionDispositionConfiguration());
        builder.ApplyConfiguration(new WarehouseWorkConfiguration());
        builder.ApplyConfiguration(new WarehouseWorkLineConfiguration());
        builder.ApplyConfiguration(new WarehouseWorkCommandConfiguration());
        builder.ApplyConfiguration(new UnitOfMeasureConfiguration());
        builder.ApplyConfiguration(new ItemUnitConversionConfiguration());
        builder.ApplyConfiguration(new WarehouseConfiguration());
        builder.ApplyConfiguration(new WarehouseOperationalLocationConfiguration());
        builder.ApplyConfiguration(new WarehouseNumberSequenceConfiguration());
        builder.ApplyConfiguration(new LocationConfiguration());
        builder.ApplyConfiguration(new LotConfiguration());
        builder.ApplyConfiguration(new SerialNumberConfiguration());
        builder.ApplyConfiguration(new InventoryStatusConfiguration());
        builder.ApplyConfiguration(new InventoryStatusTransitionConfiguration());
        builder.ApplyConfiguration(new LicensePlateConfiguration());
        builder.ApplyConfiguration(new LicensePlateContentConfiguration());
        builder.ApplyConfiguration(new LicensePlateHistoryConfiguration());
        builder.ApplyConfiguration(new LicensePlateNumberSequenceConfiguration());
        builder.ApplyConfiguration(new StockConfiguration());
        builder.ApplyConfiguration(new MovementConfiguration());
        builder.ApplyConfiguration(new InventoryBalanceConfiguration());
        builder.ApplyConfiguration(new InventoryTransactionConfiguration());
        builder.ApplyConfiguration(new InventoryCommandIdempotencyConfiguration());
        builder.ApplyConfiguration(new InventoryReservationConfiguration());
        builder.ApplyConfiguration(new InventoryReservationAllocationConfiguration());
        builder.ApplyConfiguration(new InventoryReservationEventConfiguration());
        builder.ApplyConfiguration(new InventoryReplenishmentPolicyConfiguration());
        builder.ApplyConfiguration(new WmsIdentifierConfiguration());

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

        builder.Entity<WmsGlobalSettingsEntity>(entity =>
        {
            entity.ToTable("WmsGlobalSettings");
            entity.HasKey(settings => settings.Id);
            entity.Property(settings => settings.CompanyName).HasMaxLength(200).IsRequired();
            entity.Property(settings => settings.CompanyCode).HasMaxLength(50).IsRequired();
            entity.Property(settings => settings.DefaultReceivingLocationCode).HasMaxLength(50).IsRequired();
            entity.Property(settings => settings.DefaultShippingLocationCode).HasMaxLength(50);
            entity.Property(settings => settings.ReceivingPrefix).HasMaxLength(20).IsRequired();
            entity.Property(settings => settings.ShippingPrefix).HasMaxLength(20).IsRequired();
            entity.Property(settings => settings.AdjustmentPrefix).HasMaxLength(20).IsRequired();
            entity.Property(settings => settings.LowStockThreshold).HasColumnType("decimal(18,4)");
            entity.Property(settings => settings.MaximumAdjustmentQuantity).HasColumnType("decimal(18,4)");
            entity.Property(settings => settings.LabelTemplateName).HasMaxLength(100).IsRequired();
            entity.Property(settings => settings.LabelPaperSize).HasMaxLength(30).IsRequired();
            entity.Property(settings => settings.DefaultLocale).HasMaxLength(20).IsRequired();
            entity.Property(settings => settings.DefaultTimeZone).HasMaxLength(100).IsRequired();
            entity.Property(settings => settings.CurrencyCode).HasMaxLength(3).IsRequired();
            entity.Property(settings => settings.IntegrationEndpointUrl).HasMaxLength(2_000);
            entity.Property(settings => settings.UpdatedAtUtc)
                .HasColumnType("timestamp with time zone")
                .IsRequired();
            entity.Property(settings => settings.Revision).IsConcurrencyToken();
            entity.HasOne<Warehouse>()
                .WithMany()
                .HasForeignKey(settings => settings.DefaultWarehouseId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<WmsWarehouseSettingsOverrideEntity>(entity =>
        {
            entity.ToTable("WmsWarehouseSettingsOverrides");
            entity.HasKey(settings => settings.WarehouseId);
            entity.Property(settings => settings.DefaultReceivingLocationCode).HasMaxLength(50);
            entity.Property(settings => settings.DefaultShippingLocationCode).HasMaxLength(50);
            entity.Property(settings => settings.LowStockThreshold).HasColumnType("decimal(18,4)");
            entity.Property(settings => settings.Locale).HasMaxLength(20);
            entity.Property(settings => settings.TimeZone).HasMaxLength(100);
            entity.Property(settings => settings.UpdatedAtUtc)
                .HasColumnType("timestamp with time zone")
                .IsRequired();
            entity.Property(settings => settings.Revision).IsConcurrencyToken();
            entity.HasOne<Warehouse>()
                .WithMany()
                .HasForeignKey(settings => settings.WarehouseId)
                .OnDelete(DeleteBehavior.Cascade);
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

        builder.Entity<WmsJobExecutionEntity>(entity =>
        {
            entity.ToTable("WmsJobExecutions");
            entity.HasKey(execution => execution.Id);
            entity.Property(execution => execution.JobName).HasMaxLength(150).IsRequired();
            entity.Property(execution => execution.IdempotencyKey).HasMaxLength(250).IsRequired();
            entity.Property(execution => execution.Queue).HasMaxLength(50).IsRequired();
            entity.Property(execution => execution.Status).HasMaxLength(30).IsRequired();
            entity.Property(execution => execution.CorrelationId).HasMaxLength(100).IsRequired();
            entity.Property(execution => execution.ActorUserId).HasMaxLength(450);
            entity.Property(execution => execution.ActorUserName).HasMaxLength(256);
            entity.Property(execution => execution.LastErrorType).HasMaxLength(200);
            entity.Property(execution => execution.LastErrorMessage).HasMaxLength(2_000);
            entity.Property(execution => execution.ResultSummary).HasMaxLength(2_000);
            entity.Property(execution => execution.CreatedAtUtc)
                .HasColumnType("timestamp with time zone")
                .IsRequired();
            entity.Property(execution => execution.StartedAtUtc)
                .HasColumnType("timestamp with time zone");
            entity.Property(execution => execution.CompletedAtUtc)
                .HasColumnType("timestamp with time zone");
            entity.HasIndex(execution => new { execution.JobName, execution.IdempotencyKey })
                .IsUnique();
            entity.HasIndex(execution => new { execution.Status, execution.CompletedAtUtc });
        });

        builder.Entity<WmsJobNotificationEntity>(entity =>
        {
            entity.ToTable("WmsJobNotifications");
            entity.HasKey(notification => notification.Id);
            entity.Property(notification => notification.DeduplicationKey)
                .HasMaxLength(250)
                .IsRequired();
            entity.Property(notification => notification.Kind).HasMaxLength(100).IsRequired();
            entity.Property(notification => notification.Severity).HasMaxLength(30).IsRequired();
            entity.Property(notification => notification.Title).HasMaxLength(200).IsRequired();
            entity.Property(notification => notification.Message).HasMaxLength(2_000).IsRequired();
            entity.Property(notification => notification.JobName).HasMaxLength(150).IsRequired();
            entity.Property(notification => notification.JobIdempotencyKey)
                .HasMaxLength(250)
                .IsRequired();
            entity.Property(notification => notification.CorrelationId).HasMaxLength(100).IsRequired();
            entity.Property(notification => notification.CreatedAtUtc)
                .HasColumnType("timestamp with time zone")
                .IsRequired();
            entity.Property(notification => notification.ExpiresAtUtc)
                .HasColumnType("timestamp with time zone");
            entity.Property(notification => notification.ResolvedAtUtc)
                .HasColumnType("timestamp with time zone");
            entity.HasIndex(notification => notification.DeduplicationKey).IsUnique();
            entity.HasIndex(notification => new { notification.Kind, notification.CreatedAtUtc });
        });
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnsureAuditEntriesAreAppendOnly();
        EnsureDomainTimestamps();
        try
        {
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw CreateConcurrencyConflict(exception);
        }
    }

    public override int SaveChanges()
    {
        EnsureAuditEntriesAreAppendOnly();
        EnsureDomainTimestamps();
        try
        {
            return base.SaveChanges();
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw CreateConcurrencyConflict(exception);
        }
    }

    public override async Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        EnsureAuditEntriesAreAppendOnly();
        EnsureDomainTimestamps();
        try
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw CreateConcurrencyConflict(exception);
        }
    }

    public override async Task<int> SaveChangesAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureAuditEntriesAreAppendOnly();
        EnsureDomainTimestamps();
        try
        {
            return await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw CreateConcurrencyConflict(exception);
        }
    }

    private static ConcurrencyConflictException CreateConcurrencyConflict(
        DbUpdateConcurrencyException exception)
    {
        var entry = exception.Entries.Count == 0 ? null : exception.Entries[0];
        var resourceType = entry?.Metadata.ClrType.Name ?? "record";
        var resourceId = entry is null
            ? "unknown"
            : string.Join(
                ",",
                entry.Properties
                    .Where(property => property.Metadata.IsPrimaryKey())
                    .Select(property => $"{property.Metadata.Name}={property.CurrentValue}"));
        return new ConcurrencyConflictException(resourceType, resourceId, exception);
    }

    private void EnsureDomainTimestamps()
    {
        var nowUtc = DateTime.SpecifyKind(_clock.UtcNow.UtcDateTime, DateTimeKind.Utc);
        foreach (var entry in ChangeTracker.Entries<Entity>())
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Property<DateTime>(nameof(Entity.CreatedAt)).CurrentValue == DateTime.UnixEpoch)
                {
                    entry.Property<DateTime>(nameof(Entity.CreatedAt)).CurrentValue = nowUtc;
                }

                var addedUpdatedAt = entry.Property<DateTime?>(nameof(Entity.UpdatedAt)).CurrentValue;
                if (addedUpdatedAt == DateTime.UnixEpoch)
                {
                    entry.Property<DateTime?>(nameof(Entity.UpdatedAt)).CurrentValue = nowUtc;
                }
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Property<DateTime?>(nameof(Entity.UpdatedAt)).CurrentValue = nowUtc;
            }

            if (entry.Entity is Movement &&
                entry.State == EntityState.Added &&
                entry.Property<DateTime>(nameof(Movement.Timestamp)).CurrentValue == DateTime.UnixEpoch)
            {
                entry.Property<DateTime>(nameof(Movement.Timestamp)).CurrentValue = nowUtc;
            }
        }

        foreach (var entry in ChangeTracker.Entries<WmsUserWarehouseAssignment>())
        {
            if (entry.State == EntityState.Added &&
                entry.Property<DateTimeOffset>(nameof(WmsUserWarehouseAssignment.AssignedAtUtc)).CurrentValue ==
                DateTimeOffset.UnixEpoch)
            {
                entry.Property<DateTimeOffset>(nameof(WmsUserWarehouseAssignment.AssignedAtUtc)).CurrentValue =
                    _clock.UtcNow;
            }
        }
    }

    private void EnsureAuditEntriesAreAppendOnly()
    {
        if (ChangeTracker.Entries<AuditEntry>().Any(entry =>
                entry.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException("Audit entries are immutable and cannot be updated or deleted.");
        }

        if (ChangeTracker.Entries<InventoryTransaction>().Any(entry =>
                entry.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException(
                "Inventory transactions are immutable and cannot be updated or deleted.");
        }

        if (ChangeTracker.Entries<InventoryReservationEvent>().Any(entry =>
                entry.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException(
                "Inventory reservation events are immutable and cannot be updated or deleted.");
        }
    }
}
