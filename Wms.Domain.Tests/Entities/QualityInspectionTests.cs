using FluentAssertions;
using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Domain.Tests.Entities;

public sealed class QualityInspectionTests
{
    [Fact]
    public void SamplingRules_CalculateBoundedSamples()
    {
        var fixedQuantity = new QualityProfile(
            "FIXED",
            "Fixed",
            "ثابت",
            QualityRiskLevel.High,
            QualitySamplingMethod.FixedQuantity,
            3m);
        var percentage = new QualityProfile(
            "PERCENT",
            "Percentage",
            "نسبة",
            QualityRiskLevel.Medium,
            QualitySamplingMethod.Percentage,
            20m);
        var everyThird = new QualityProfile(
            "EVERY3",
            "Every third LPN",
            "كل ثالثة",
            QualityRiskLevel.Low,
            QualitySamplingMethod.EveryNthLicensePlate,
            3m);

        fixedQuantity.CalculateSampleQuantity(2m, hasLicensePlate: false, licensePlateSequence: null).Should().Be(2m);
        percentage.CalculateSampleQuantity(11m, hasLicensePlate: false, licensePlateSequence: null).Should().Be(3m);
        everyThird.CalculateSampleQuantity(10m, hasLicensePlate: true, licensePlateSequence: 3).Should().Be(10m);
        everyThird.CalculateSampleQuantity(10m, hasLicensePlate: true, licensePlateSequence: 4).Should().Be(0m);
    }

    [Fact]
    public void Inspection_RequiresCompleteDispositionBeforeClose()
    {
        var inspection = CreateInspection();

        var act = () => inspection.Close("inspector", DateTime.UtcNow);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*dispose the complete selected sample*");
    }

    [Fact]
    public void Inspection_TracksPartialDispositionAndRejectsClosedEdits()
    {
        var inspection = CreateInspection();
        inspection.AddDisposition(
            new QualityInspectionDisposition(
                QualityDispositionType.Pass,
                6m,
                InventoryStatusSystemIds.Available,
                "Within specification",
                "inspector",
                DateTime.UtcNow),
            DateTime.UtcNow);
        inspection.AddDisposition(
            new QualityInspectionDisposition(
                QualityDispositionType.Fail,
                4m,
                InventoryStatusSystemIds.Quarantine,
                "Failed visual inspection",
                "inspector",
                DateTime.UtcNow),
            DateTime.UtcNow);

        inspection.Close("inspector", DateTime.UtcNow);

        inspection.Status.Should().Be(QualityInspectionStatus.Closed);
        inspection.IsClosed.Should().BeTrue();
        inspection.AcceptedBaseQuantity.Should().Be(6m);
        inspection.RejectedBaseQuantity.Should().Be(4m);
        var act = () => inspection.AddDisposition(
            new QualityInspectionDisposition(
                QualityDispositionType.Hold,
                1m,
                InventoryStatusSystemIds.Hold,
                "Late hold",
                "inspector",
                DateTime.UtcNow),
            DateTime.UtcNow);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*cannot be edited*");
    }

    private static QualityInspection CreateInspection() => new(
        "QI-TEST-1",
        receiptId: 1,
        receiptLineId: 1,
        warehouseId: 1,
        itemId: 1,
        itemSkuSnapshot: "ITEM-1",
        itemNameSnapshot: "Test item",
        receivedBaseQuantity: 10m,
        sampleBaseQuantity: 10m,
        inventoryStatusId: InventoryStatusSystemIds.QualityPending,
        createdByUserId: "receiver");
}
