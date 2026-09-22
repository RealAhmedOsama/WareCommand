using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Infrastructure.B2bDocuments;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class WmsB2bMappingProfileConfiguration
    : IEntityTypeConfiguration<WmsB2bMappingProfileEntity>
{
    public void Configure(EntityTypeBuilder<WmsB2bMappingProfileEntity> builder)
    {
        builder.ToTable("WmsB2bMappingProfiles");
        builder.HasKey(profile => profile.Id);
        builder.Property(profile => profile.DocumentType).HasMaxLength(100).IsRequired();
        builder.Property(profile => profile.Name).HasMaxLength(200).IsRequired();
        builder.Property(profile => profile.Standard).HasMaxLength(30).IsRequired();
        builder.Property(profile => profile.RulesJson).HasMaxLength(200_000).IsRequired();
        builder.Property(profile => profile.CreatedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(profile => profile.UpdatedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasIndex(profile => new { profile.DocumentType, profile.Name, profile.Version }).IsUnique();
    }
}

public sealed class WmsTradingPartnerConfiguration
    : IEntityTypeConfiguration<WmsTradingPartnerEntity>
{
    public void Configure(EntityTypeBuilder<WmsTradingPartnerEntity> builder)
    {
        builder.ToTable("WmsTradingPartners");
        builder.HasKey(partner => partner.Id);
        builder.Property(partner => partner.Code).HasMaxLength(100).IsRequired();
        builder.Property(partner => partner.Name).HasMaxLength(200).IsRequired();
        builder.Property(partner => partner.Standard).HasMaxLength(30).IsRequired();
        builder.Property(partner => partner.CredentialReference).HasMaxLength(250).IsRequired();
        builder.Property(partner => partner.AllowedWarehouseIdsJson).HasMaxLength(4_000).IsRequired();
        builder.Property(partner => partner.DocumentTypesJson).HasMaxLength(4_000).IsRequired();
        builder.Property(partner => partner.MappingProfileName).HasMaxLength(200).IsRequired();
        builder.Property(partner => partner.Status).HasMaxLength(30).IsRequired();
        builder.Property(partner => partner.CreatedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(partner => partner.UpdatedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasIndex(partner => partner.Code).IsUnique();
    }
}

public sealed class WmsB2bDocumentConfiguration : IEntityTypeConfiguration<WmsB2bDocumentEntity>
{
    public void Configure(EntityTypeBuilder<WmsB2bDocumentEntity> builder)
    {
        builder.ToTable("WmsB2bDocuments");
        builder.HasKey(document => document.Id);
        builder.Property(document => document.DocumentType).HasMaxLength(100).IsRequired();
        builder.Property(document => document.Version).HasMaxLength(50).IsRequired();
        builder.Property(document => document.Direction).HasMaxLength(30).IsRequired();
        builder.Property(document => document.TransportMode).HasMaxLength(30).IsRequired();
        builder.Property(document => document.InterchangeControlNumber).HasMaxLength(100).IsRequired();
        builder.Property(document => document.GroupControlNumber).HasMaxLength(100).IsRequired();
        builder.Property(document => document.DocumentControlNumber).HasMaxLength(100).IsRequired();
        builder.Property(document => document.ExternalIdentityKey).HasMaxLength(64).IsRequired();
        builder.Property(document => document.IdempotencyKey).HasMaxLength(250);
        builder.Property(document => document.Status).HasMaxLength(30).IsRequired();
        builder.Property(document => document.PayloadJson).HasMaxLength(1_000_000).IsRequired();
        builder.Property(document => document.PayloadHash).HasMaxLength(64).IsRequired();
        builder.Property(document => document.ValidationErrorsJson).HasMaxLength(100_000).IsRequired();
        builder.Property(document => document.AcknowledgementStatus).HasMaxLength(30).IsRequired();
        builder.Property(document => document.CorrelationId).HasMaxLength(100).IsRequired();
        builder.Property(document => document.CreatedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(document => document.ProcessedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(document => document.ErrorCode).HasMaxLength(150);
        builder.Property(document => document.ErrorMessage).HasMaxLength(2_000);
        builder.HasOne(document => document.TradingPartner)
            .WithMany(partner => partner.Documents)
            .HasForeignKey(document => document.TradingPartnerId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(document => new { document.TradingPartnerId, document.ExternalIdentityKey }).IsUnique();
        builder.HasIndex(document => new { document.TradingPartnerId, document.IdempotencyKey }).IsUnique();
        builder.HasIndex(document => new { document.TradingPartnerId, document.MessageId }).IsUnique();
        builder.HasIndex(document => new { document.TradingPartnerId, document.InterchangeControlNumber, document.DocumentControlNumber }).IsUnique();
        builder.HasIndex(document => new { document.Status, document.CreatedAtUtc });
        builder.HasIndex(document => new { document.WarehouseId, document.CreatedAtUtc });
    }
}

public sealed class WmsB2bAcknowledgementConfiguration
    : IEntityTypeConfiguration<WmsB2bAcknowledgementEntity>
{
    public void Configure(EntityTypeBuilder<WmsB2bAcknowledgementEntity> builder)
    {
        builder.ToTable("WmsB2bAcknowledgements");
        builder.HasKey(acknowledgement => acknowledgement.Id);
        builder.Property(acknowledgement => acknowledgement.AcknowledgementType).HasMaxLength(50).IsRequired();
        builder.Property(acknowledgement => acknowledgement.Status).HasMaxLength(30).IsRequired();
        builder.Property(acknowledgement => acknowledgement.ControlNumber).HasMaxLength(100).IsRequired();
        builder.Property(acknowledgement => acknowledgement.ReasonCode).HasMaxLength(50);
        builder.Property(acknowledgement => acknowledgement.ReasonMessage).HasMaxLength(2_000);
        builder.Property(acknowledgement => acknowledgement.CreatedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(acknowledgement => acknowledgement.SentAtUtc).HasColumnType("timestamp with time zone");
        builder.HasOne(acknowledgement => acknowledgement.Document)
            .WithMany(document => document.Acknowledgements)
            .HasForeignKey(acknowledgement => acknowledgement.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(acknowledgement => new { acknowledgement.DocumentId, acknowledgement.AcknowledgementType }).IsUnique();
        builder.HasIndex(acknowledgement => acknowledgement.ControlNumber).IsUnique();
    }
}
