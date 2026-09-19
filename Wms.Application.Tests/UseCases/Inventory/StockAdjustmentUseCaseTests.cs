// Wms.Application.Tests/UseCases/Inventory/StockAdjustmentUseCaseTests.cs

using Microsoft.Extensions.Logging;
using Wms.Application.UseCases.Inventory;
using Wms.Domain.Entities;
using Wms.Domain.Repositories;
using Wms.Domain.Services;
using Wms.Domain.ValueObjects;

namespace Wms.Application.Tests.UseCases.Inventory;

public class StockAdjustmentUseCaseTests
{
    private readonly Mock<IItemRepository> _mockItemRepository;
    private readonly Mock<ILocationRepository> _mockLocationRepository;
    private readonly Mock<ILogger<StockAdjustmentUseCase>> _mockLogger;
    private readonly Mock<IStockMovementService> _mockStockMovementService;
    private readonly Mock<IUnitOfWork> _mockUnitOfWork;
    private readonly StockAdjustmentUseCase _useCase;

    public StockAdjustmentUseCaseTests()
    {
        _mockUnitOfWork = new Mock<IUnitOfWork>();
        _mockItemRepository = new Mock<IItemRepository>();
        _mockLocationRepository = new Mock<ILocationRepository>();
        _mockStockMovementService = new Mock<IStockMovementService>();
        _mockLogger = new Mock<ILogger<StockAdjustmentUseCase>>();

        _mockUnitOfWork.Setup(x => x.Items).Returns(_mockItemRepository.Object);
        _mockUnitOfWork.Setup(x => x.Locations).Returns(_mockLocationRepository.Object);

        _useCase = new StockAdjustmentUseCase(_mockUnitOfWork.Object, _mockStockMovementService.Object,
            _mockLogger.Object);
    }

    [Fact]
    public async Task ExecuteAsync_WithValidRequest_ReturnsSuccess()
    {
        // Arrange
        var request = new StockAdjustmentDto(
            "WIDGET-001",
            "Z001",
            15.0m,
            "Physical count adjustment"
        );

        var item = new Item("WIDGET-001", "Widget A", "EA");
        var location = new Location("Z001", "Zone 1", 1);
        var movement = Movement.CreateAdjustment(item.Id, location.Id, new Quantity(15.0m), "USER1",
            notes: request.Reason);

        _mockItemRepository.Setup(x => x.GetBySkuAsync(request.ItemSku, It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);
        _mockLocationRepository.Setup(x => x.GetByCodeAsync(request.LocationCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(location);
        _mockStockMovementService.Setup(x => x.AdjustAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<Quantity>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(movement);

        // Act
        var result = await _useCase.ExecuteAsync(request, "USER1");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.ItemSku.Should().Be("WIDGET-001");
        result.Value.LocationCode.Should().Be("Z001");
        result.Value.Quantity.Should().Be(15.0m);

        _mockStockMovementService.Verify(x => x.AdjustAsync(
            item.Id, location.Id, It.Is<Quantity>(q => q.Value == 15.0m), "USER1",
            request.Reason, It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);

        _mockUnitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WithNonExistentItem_ReturnsFailure()
    {
        // Arrange
        var request = new StockAdjustmentDto(
            "NON-EXISTENT",
            "Z001",
            15.0m,
            "Adjustment"
        );

        _mockItemRepository.Setup(x => x.GetBySkuAsync(request.ItemSku, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Item?)null);

        // Act
        var result = await _useCase.ExecuteAsync(request, "USER1");

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task ExecuteAsync_WithInactiveLocation_ReturnsFailure()
    {
        // Arrange
        var request = new StockAdjustmentDto(
            "WIDGET-001",
            "INACTIVE-LOC",
            15.0m,
            "Adjustment"
        );

        var item = new Item("WIDGET-001", "Widget A", "EA");
        var location = new Location("INACTIVE-LOC", "Inactive Location", 1);
        location.Deactivate(); // Make location inactive

        _mockItemRepository.Setup(x => x.GetBySkuAsync(request.ItemSku, It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);
        _mockLocationRepository.Setup(x => x.GetByCodeAsync(request.LocationCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(location);

        // Act
        var result = await _useCase.ExecuteAsync(request, "USER1");

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("inactive");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task ExecuteAsync_WithInvalidReason_ReturnsFailure(string? invalidReason)
    {
        // Arrange
        var request = new StockAdjustmentDto(
            "WIDGET-001",
            "Z001",
            15.0m,
            invalidReason!
        );

        var item = new Item("WIDGET-001", "Widget A", "EA");
        var location = new Location("Z001", "Zone 1", 1);

        _mockItemRepository.Setup(x => x.GetBySkuAsync(request.ItemSku, It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);
        _mockLocationRepository.Setup(x => x.GetByCodeAsync(request.LocationCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(location);

        // Act
        var result = await _useCase.ExecuteAsync(request, "USER1");

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("reason is required");
    }

    [Fact]
    public async Task ExecuteAsync_WhenStockMovementServiceThrowsException_ReturnsFailure()
    {
        // Arrange
        var request = new StockAdjustmentDto(
            "WIDGET-001",
            "Z001",
            15.0m,
            "Valid reason"
        );

        var item = new Item("WIDGET-001", "Widget A", "EA");
        var location = new Location("Z001", "Zone 1", 1);

        _mockItemRepository.Setup(x => x.GetBySkuAsync(request.ItemSku, It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);
        _mockLocationRepository.Setup(x => x.GetByCodeAsync(request.LocationCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(location);
        _mockStockMovementService.Setup(x => x.AdjustAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<Quantity>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Stock movement error"));

        // Act
        var result = await _useCase.ExecuteAsync(request, "USER1");

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Error adjusting stock");
    }
}
