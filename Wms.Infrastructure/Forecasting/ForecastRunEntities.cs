namespace Wms.Infrastructure.Forecasting;

public sealed class ForecastRunEntity
{
    public int Id { get; set; }
    public int WarehouseId { get; set; }
    public int ItemId { get; set; }
    public string Granularity { get; set; } = string.Empty;
    public string DataStatus { get; set; } = string.Empty;
    public string? SelectedModel { get; set; }
    public string ModelVersion { get; set; } = string.Empty;
    public string InputFingerprint { get; set; } = string.Empty;
    public DateTime SourceCutoffUtc { get; set; }
    public DateOnly? InputPeriodStart { get; set; }
    public DateOnly? InputPeriodEnd { get; set; }
    public int HorizonPeriods { get; set; }
    public int TrainingWindowPeriods { get; set; }
    public int MovingAverageWindow { get; set; }
    public decimal OnHandQuantity { get; set; }
    public decimal OnOrderQuantity { get; set; }
    public decimal InTransitQuantity { get; set; }
    public int LeadTimePeriods { get; set; }
    public decimal? SafetyStockQuantity { get; set; }
    public decimal? MeanAbsoluteError { get; set; }
    public decimal? MeanAbsolutePercentageError { get; set; }
    public decimal? Bias { get; set; }
    public int BacktestEvaluatedPeriods { get; set; }
    public string BacktestScoresJson { get; set; } = "[]";
    public string DataQualityFlagsJson { get; set; } = "[]";
    public string BaseUnitOfMeasure { get; set; } = string.Empty;
    public string? InsufficientDataReason { get; set; }
    public DateOnly? ProjectedStockoutPeriod { get; set; }
    public decimal? DaysOfSupply { get; set; }
    public string RiskLevel { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }

    public Wms.Domain.Entities.Warehouse Warehouse { get; set; } = null!;
    public Wms.Domain.Entities.Item Item { get; set; } = null!;
    public ICollection<ForecastRunPointEntity> Points { get; set; } = new List<ForecastRunPointEntity>();
    public ICollection<ForecastOverrideEntity> Overrides { get; set; } = new List<ForecastOverrideEntity>();
}

public sealed class ForecastRunPointEntity
{
    public int Id { get; set; }
    public int ForecastRunId { get; set; }
    public DateOnly PeriodStart { get; set; }
    public decimal? ActualDemand { get; set; }
    public decimal? ForecastQuantity { get; set; }
    public decimal? LowerBound { get; set; }
    public decimal? UpperBound { get; set; }
    public bool WasStockoutCensored { get; set; }

    public ForecastRunEntity ForecastRun { get; set; } = null!;
}

public sealed class ForecastOverrideEntity
{
    public int Id { get; set; }
    public int ForecastRunId { get; set; }
    public DateOnly PeriodStart { get; set; }
    public int Version { get; set; }
    public decimal Quantity { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string CreatedByUserId { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }

    public ForecastRunEntity ForecastRun { get; set; } = null!;
}
