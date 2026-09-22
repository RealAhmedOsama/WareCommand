namespace Wms.Infrastructure.B2bDocuments;

public sealed class WmsB2bMappingProfileEntity
{
    public long Id { get; set; }

    public string DocumentType { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public int Version { get; set; }

    public string Standard { get; set; } = string.Empty;

    public string RulesJson { get; set; } = "[]";

    public bool IsActive { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

}

public sealed class WmsTradingPartnerEntity
{
    public long Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Standard { get; set; } = string.Empty;

    public string CredentialReference { get; set; } = string.Empty;

    public int CredentialVersion { get; set; }

    public string AllowedWarehouseIdsJson { get; set; } = "[]";

    public string DocumentTypesJson { get; set; } = "[]";

    public string MappingProfileName { get; set; } = string.Empty;

    public int MappingProfileVersion { get; set; }

    public bool RequireAcknowledgement { get; set; }

    public string Status { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public ICollection<WmsB2bDocumentEntity> Documents { get; set; } =
        new List<WmsB2bDocumentEntity>();
}

public sealed class WmsB2bDocumentEntity
{
    public long Id { get; set; }

    public long TradingPartnerId { get; set; }

    public Guid MessageId { get; set; }

    public string DocumentType { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    public string Direction { get; set; } = string.Empty;

    public string TransportMode { get; set; } = string.Empty;

    public string InterchangeControlNumber { get; set; } = string.Empty;

    public string GroupControlNumber { get; set; } = string.Empty;

    public string DocumentControlNumber { get; set; } = string.Empty;

    public string ExternalIdentityKey { get; set; } = string.Empty;

    public string? IdempotencyKey { get; set; }

    public int WarehouseId { get; set; }

    public string Status { get; set; } = string.Empty;

    public string PayloadJson { get; set; } = "{}";

    public string PayloadHash { get; set; } = string.Empty;

    public int LineCount { get; set; }

    public int? DeclaredLineCount { get; set; }

    public string ValidationErrorsJson { get; set; } = "[]";

    public string AcknowledgementStatus { get; set; } = string.Empty;

    public int ReplayCount { get; set; }

    public string CorrelationId { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? ProcessedAtUtc { get; set; }

    public string? ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }

    public WmsTradingPartnerEntity TradingPartner { get; set; } = null!;

    public ICollection<WmsB2bAcknowledgementEntity> Acknowledgements { get; set; } =
        new List<WmsB2bAcknowledgementEntity>();
}

public sealed class WmsB2bAcknowledgementEntity
{
    public long Id { get; set; }

    public long DocumentId { get; set; }

    public string AcknowledgementType { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string ControlNumber { get; set; } = string.Empty;

    public string? ReasonCode { get; set; }

    public string? ReasonMessage { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? SentAtUtc { get; set; }

    public WmsB2bDocumentEntity Document { get; set; } = null!;
}
