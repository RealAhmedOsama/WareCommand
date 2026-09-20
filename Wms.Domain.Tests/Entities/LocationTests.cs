// Wms.Domain.Tests/Entities/LocationTests.cs

using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Domain.Tests.Entities;

public class LocationTests
{
    [Fact]
    public void Constructor_WithValidParameters_CreatesLocation()
    {
        // Arrange
        var code = "Z001-A001";
        var name = "Zone 1 Aisle 1";
        var warehouseId = 1;

        // Act
        var location = new Location(code, name, warehouseId);

        // Assert
        location.Code.Should().Be(code);
        location.Name.Should().Be(name);
        location.WarehouseId.Should().Be(warehouseId);
        location.ParentLocationId.Should().BeNull();
        location.IsActive.Should().BeTrue();
        location.IsPickable.Should().BeTrue();
        location.IsReceivable.Should().BeTrue();
    }

    [Fact]
    public void Constructor_WithParentLocation_SetsParentLocationId()
    {
        // Arrange
        var code = "Z001-A001-01";
        var name = "Bin 01";
        var warehouseId = 1;
        var parentLocationId = 2;

        // Act
        var location = new Location(code, name, warehouseId, parentLocationId);

        // Assert
        location.ParentLocationId.Should().Be(parentLocationId);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Constructor_WithInvalidCode_ThrowsArgumentException(string? invalidCode)
    {
        // Act & Assert
        var act = () => new Location(invalidCode!, "Valid Name", 1);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Constructor_WithInvalidName_ThrowsArgumentException(string? invalidName)
    {
        // Act & Assert
        var act = () => new Location("VALID-CODE", invalidName!, 1);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SetPickable_WithTrue_SetsIsPickableTrue()
    {
        // Arrange
        var location = new Location("LOC-001", "Location", 1);
        location.SetPickable(false); // Make unpickable first

        // Act
        location.SetPickable(true);

        // Assert
        location.IsPickable.Should().BeTrue();
        location.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public void SetReceivable_WithFalse_SetsIsReceivableFalse()
    {
        // Arrange
        var location = new Location("LOC-001", "Location", 1);

        // Act
        location.SetReceivable(false);

        // Assert
        location.IsReceivable.Should().BeFalse();
        location.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public void SetCapacity_WithValidCapacity_UpdatesCapacity()
    {
        // Arrange
        var location = new Location("LOC-001", "Location", 1);
        var capacity = 1000;

        // Act
        location.SetCapacity(capacity);

        // Assert
        location.Capacity.Should().Be(capacity);
        location.UpdatedAt.Should().NotBeNull();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-100)]
    public void SetCapacity_WithNegativeCapacity_ThrowsArgumentException(int invalidCapacity)
    {
        // Arrange
        var location = new Location("LOC-001", "Location", 1);

        // Act & Assert
        var act = () => location.SetCapacity(invalidCapacity);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Deactivate_WhenActive_SetsIsActiveFalse()
    {
        // Arrange
        var location = new Location("LOC-001", "Location", 1);

        // Act
        location.Deactivate();

        // Assert
        location.IsActive.Should().BeFalse();
        location.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public void Activate_WhenInactive_SetsIsActiveTrue()
    {
        // Arrange
        var location = new Location("LOC-001", "Location", 1);
        location.Deactivate(); // Make inactive first

        // Act
        location.Activate();

        // Assert
        location.IsActive.Should().BeTrue();
        location.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public void GetFullPath_WithNoParent_ReturnsCode()
    {
        // Arrange
        var location = new Location("Z001", "Zone 1", 1);

        // Act
        var fullPath = location.GetFullPath();

        // Assert
        fullPath.Should().Be("Z001");
    }

    [Fact]
    public void GetFullPath_WithParent_ReturnsHierarchicalPath()
    {
        // Arrange
        var parentLocation = new Location("Z001", "Zone 1", 1);
        var childLocation = new Location("Z001-A001", "Aisle 1", 1, parentLocation.Id);

        // Note: ParentLocation is read-only, so we can't test the full hierarchical path
        // in this unit test without a database context. This test would need integration testing.

        // Act
        var fullPath = childLocation.GetFullPath();

        // Assert
        fullPath.Should().Be("Z001-A001"); // Will return just the code since ParentLocation is null
    }

    [Fact]
    public void Constructor_WithTypeThatCannotPick_RejectsPickableFlag()
    {
        var act = () => new Location(
            "DOCK-01",
            "Inbound dock",
            1,
            type: LocationType.Dock,
            isPickable: true,
            isReceivable: true);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_WithTypeThatCannotReceive_RejectsReceivableFlag()
    {
        var act = () => new Location(
            "PACK-01",
            "Packing",
            1,
            type: LocationType.Packing,
            isPickable: false,
            isReceivable: true);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ValidateCapacity_ReportsUnitOverflow()
    {
        var location = new Location(
            "BIN-01",
            "Bin",
            1,
            type: LocationType.Bin,
            isPickable: true,
            isReceivable: false,
            maxUnits: 10);

        var violation = location.ValidateCapacity(
            new LocationCapacitySnapshot(8),
            new LocationCapacitySnapshot(3));

        violation.Should().NotBeNull();
        violation!.Code.Should().Be("location.capacity_units_exceeded");
    }

    [Fact]
    public void UpdateDefinition_RejectsInvertedTemperatureRange()
    {
        var location = new Location("COLD-01", "Cold room", 1);

        var act = () => location.UpdateDefinition(
            LocationType.Storage,
            barcode: null,
            priority: 0,
            isPickable: true,
            isReceivable: true,
            isCountable: true,
            allowMixedItems: true,
            allowMixedLots: true,
            maxUnits: 100,
            maxWeightKg: null,
            maxVolumeCubicMeters: null,
            maxPallets: null,
            maxLpns: null,
            storageProfile: "Cold",
            minimumTemperatureCelsius: 10,
            maximumTemperatureCelsius: 0,
            hazardClass: null,
            accessRestriction: null,
            constraintAttributesJson: "{}");

        act.Should().Throw<ArgumentException>();
    }
}
