using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Domain.Tests.Packaging;

public sealed class ItemPackagingTests
{
    [Fact]
    public void Packaging_ComputesVolumeAndPreservesRoleDefaults()
    {
        var packaging = new ItemPackaging(
            "PALLET",
            "CASE",
            10m,
            gtin: "0123456789012",
            lengthCm: 100,
            widthCm: 80,
            heightCm: 120,
            type: PackagingType.Pallet,
            isDefaultReceiving: true,
            isDefaultStorage: true,
            isDefaultPicking: true,
            isDefaultShipping: true);

        packaging.VolumeCubicMeters.Should().Be(0.96m);
        packaging.IsDefault.Should().BeTrue();
        packaging.IsDefaultReceiving.Should().BeTrue();
        packaging.IsDefaultShipping.Should().BeTrue();
        packaging.Gtin.Should().Be("0123456789012");
    }

    [Fact]
    public void Packaging_NestingAcceptsValidParentChain()
    {
        var @case = new ItemPackaging("CASE", "EA", 12m, type: PackagingType.Case);
        var pallet = new ItemPackaging(
            "PALLET",
            "CASE",
            10m,
            parentPackagingCode: "CASE",
            type: PackagingType.Pallet);
        var definitions = new Dictionary<string, ItemPackaging>
        {
            [@case.Code] = @case,
            [pallet.Code] = pallet
        };

        pallet.ValidateNesting(definitions);

        @case.ParentPackagingCode.Should().BeNull();
    }

    [Fact]
    public void Packaging_NestingRejectsMissingParentAndCycles()
    {
        var missing = new ItemPackaging(
            "PALLET",
            "CASE",
            10m,
            parentPackagingCode: "CASE");
        var missingAct = () => missing.ValidateNesting(
            new Dictionary<string, ItemPackaging> { [missing.Code] = missing });

        var first = new ItemPackaging("FIRST", "EA", 2m, parentPackagingCode: "SECOND");
        var second = new ItemPackaging("SECOND", "EA", 2m, parentPackagingCode: "FIRST");
        var cycleAct = () => first.ValidateNesting(new Dictionary<string, ItemPackaging>
        {
            [first.Code] = first,
            [second.Code] = second
        });

        missingAct.Should().Throw<ArgumentException>().WithMessage("*CASE*");
        cycleAct.Should().Throw<ArgumentException>().WithMessage("*cycle*");
    }

    [Fact]
    public void Packaging_RejectsInvalidGtinAndAllowsExplicitPartialPolicy()
    {
        var invalidGtin = () => new ItemPackaging("CASE", "EA", 12m, gtin: "12345");
        var partial = new ItemPackaging(
            "CASE",
            "EA",
            12m,
            partialPackagePolicy: PackagingPartialPolicy.Allow);

        invalidGtin.Should().Throw<ArgumentException>().WithMessage("*GTIN*");
        partial.PartialPackagePolicy.Should().Be(PackagingPartialPolicy.Allow);
    }
}
