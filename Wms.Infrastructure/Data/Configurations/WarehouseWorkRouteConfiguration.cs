using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class WarehouseWorkRouteConfiguration : IEntityTypeConfiguration<WarehouseWorkRoute>
{
    public void Configure(EntityTypeBuilder<WarehouseWorkRoute> builder)
    {
        builder.ToTable("WarehouseWorkRoutes");
        builder.HasKey(route => route.Id);
        builder.Property(route => route.RouteCode).HasMaxLength(50).IsRequired();
        builder.Property(route => route.TravelMinutes).HasColumnType("decimal(18,6)").IsRequired();
        builder.Property(route => route.DistanceMeters).HasColumnType("decimal(18,6)");
        builder.Property(route => route.Notes).HasMaxLength(1_000);
        builder.Property(route => route.Revision).IsRequired().IsConcurrencyToken();
        builder.Property(route => route.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(route => route.UpdatedAt).HasColumnType("timestamp with time zone");

        builder.HasOne(route => route.Warehouse)
            .WithMany()
            .HasForeignKey(route => route.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(route => route.FromLocation)
            .WithMany()
            .HasForeignKey(route => new { route.WarehouseId, route.FromLocationId })
            .HasPrincipalKey(location => new { location.WarehouseId, location.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(route => route.ToLocation)
            .WithMany()
            .HasForeignKey(route => new { route.WarehouseId, route.ToLocationId })
            .HasPrincipalKey(location => new { location.WarehouseId, location.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(route => new
        {
            route.WarehouseId,
            route.RouteCode,
            route.FromLocationId,
            route.ToLocationId,
            route.Sequence
        }).IsUnique();
        builder.HasIndex(route => new { route.WarehouseId, route.RouteCode, route.IsActive });
    }
}
