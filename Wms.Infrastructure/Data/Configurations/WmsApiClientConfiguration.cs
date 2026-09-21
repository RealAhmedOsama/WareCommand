using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Infrastructure.ApiClients;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class WmsApiClientConfiguration : IEntityTypeConfiguration<WmsApiClientEntity>
{
    public void Configure(EntityTypeBuilder<WmsApiClientEntity> builder)
    {
        builder.ToTable("WmsApiClients");
        builder.HasKey(client => client.Id);
        builder.Property(client => client.ClientId).HasMaxLength(100).IsRequired();
        builder.Property(client => client.Name).HasMaxLength(200).IsRequired();
        builder.Property(client => client.Owner).HasMaxLength(450).IsRequired();
        builder.Property(client => client.Status).HasMaxLength(30).IsRequired();
        builder.Property(client => client.ScopesJson).HasMaxLength(8_000).IsRequired();
        builder.Property(client => client.WarehouseIdsJson).HasMaxLength(8_000).IsRequired();
        builder.Property(client => client.IpRestrictionsJson).HasMaxLength(8_000).IsRequired();
        builder.Property(client => client.SecretHash).HasMaxLength(1_000).IsRequired();
        builder.Property(client => client.PreviousSecretHash).HasMaxLength(1_000);
        builder.HasIndex(client => client.ClientId).IsUnique();
        builder.HasIndex(client => new { client.Status, client.ExpiresAtUtc });
    }
}
