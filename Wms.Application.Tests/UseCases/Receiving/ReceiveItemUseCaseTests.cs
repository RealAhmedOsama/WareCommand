// Wms.Application.Tests/UseCases/Receiving/ReceiveItemUseCaseTests.cs

using Microsoft.Extensions.Logging;
using Wms.Application.DTOs;
using Wms.Application.Tests.Identity;
using Wms.Application.UseCases.Receiving;
using Wms.Domain.Entities;
using Wms.Domain.Repositories;
using Wms.Domain.Services;
using Wms.Domain.ValueObjects;

namespace Wms.Application.Tests.UseCases.Receiving;

public class ReceiveItemUseCaseTests
{
    private readonly Mock<IItemRepository> _mockItemRepository;
    private readonly Mock<ILocationRepository> _mockLocationRepository;
    private readonly Mock<ILogger<ReceiveItemUseCase>> _mockLogger;
    private readonly Mock<IStockMovementService> _mockStockMovementService;
    private readonly Mock<IUnitOfWork> _mockUnitOfWork;
    private readonly ReceiveItemUseCase _useCase;

    public ReceiveItemUseCaseTests()
    {
        _mockUnitOfWork = new Mock<IUnitOfWork>();
        _mockItemRepository = new Mock<IItemRepository>();
        _mockLocationRepository = new Mock<ILocationRepository>();
        _mockStockMovementService = new Mock<IStockMovementService>();
        _mockLogger = new Mock<ILogger<ReceiveItemUseCase>>();

        _mockUnitOfWork.Setup(x => x.Items).Returns(_mockItemRepository.Object);
        _mockUnitOfWork.Setup(x => x.Locations).Returns(_mockLocationRepository.Object);

        _useCase = new ReceiveItemUseCase(
            _mockUnitOfWork.Object,
            _mockStockMovementService.Object,
            _mockLogger.Object,
            new AllowAllWarehouseAccessService());
    }

    [Fact]
    public async Task ExecuteAsync_WithValidRequest_ReturnsSuccess()
    {
        // Arrange
        var request = new ReceiveItemDto(
            "WIDGET-001",
            "RECEIVE",
            10.0m,
            null,
            null,
            "PO-001",
            "Test receipt"
        );

        var item = new Item("WIDGET-001", "Widget A", "EA");
        var location = new Location("RECEIVE", "Receiving Dock", 1);
        var movement = Movement.CreateReceipt(item.Id, location.Id, new Quantity(10.0m), "USER1",
            referenceNumber: request.ReferenceNumber, notes: request.Notes);

        _mockItemRepository.Setup(x => x.GetBySkuAsync(request.ItemSku, It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);
        _mockLocationRepository.Setup(x => x.GetByCodeAsync(request.LocationCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(location);
        _mockStockMovementService.Setup(x => x.ReceiveAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<Quantity>(), It.IsAny<string>(),
                It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(movement);

        // Act
        var result = await _useCase.ExecuteAsync(request, "USER1");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.ItemSku.Should().Be("WIDGET-001");
        result.Value.LocationCode.Should().Be("RECEIVE");
        result.Value.Quantity.Should().Be(10.0m);

        _mockUnitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WithNonExistentItem_ReturnsFailure()
    {
        // Arrange
        var request = new ReceiveItemDto(
            "NON-EXISTENT",
            "RECEIVE",
            10.0m
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
    public async Task ExecuteAsync_WithInactiveItem_ReturnsFailure()
    {
        // Arrange
        var request = new ReceiveItemDto(
            "WIDGET-001",
            "RECEIVE",
            10.0m
        );

        var item = new Item("WIDGET-001", "Widget A", "EA");
        item.Deactivate();

        _mockItemRepository.Setup(x => x.GetBySkuAsync(request.ItemSku, It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);

        // Act
        var result = await _useCase.ExecuteAsync(request, "USER1");

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("inactive");
    }

    [Fact]
    public async Task ExecuteAsync_WithNonReceivableLocation_ReturnsFailure()
    {
        // Arrange
        var request = new ReceiveItemDto(
            "WIDGET-001",
            "PICK-ONLY",
            10.0m
        );

        var item = new Item("WIDGET-001", "Widget A", "EA");
        var location = new Location("PICK-ONLY", "Pick Only Location", 1);
        location.SetReceivable(false);

        _mockItemRepository.Setup(x => x.GetBySkuAsync(request.ItemSku, It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);
        _mockLocationRepository.Setup(x => x.GetByCodeAsync(request.LocationCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(location);

        // Act
        var result = await _useCase.ExecuteAsync(request, "USER1");

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("not receivable");
    }

    [Fact]
    public async Task ExecuteAsync_WithLotRequiredButNoLotProvided_ReturnsFailure()
    {
        // Arrange
        var request = new ReceiveItemDto(
            "LOT-ITEM",
            "RECEIVE",
            10.0m // Missing lot number
        );

        var item = new Item("LOT-ITEM", "Lot Controlled Item", "EA", true);
        var location = new Location("RECEIVE", "Receiving Dock", 1);

        _mockItemRepository.Setup(x => x.GetBySkuAsync(request.ItemSku, It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);
        _mockLocationRepository.Setup(x => x.GetByCodeAsync(request.LocationCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(location);

        // Act
        var result = await _useCase.ExecuteAsync(request, "USER1");

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("requires a lot number");
    }

    [Fact]
    public async Task ExecuteAsync_WithSerialRequiredButNoSerialProvided_ReturnsFailure()
    {
        // Arrange
        var request = new ReceiveItemDto(
            "SERIAL-ITEM",
            "RECEIVE",
            1.0m,
            SerialNumber: null // Missing serial number
        );

        var item = new Item("SERIAL-ITEM", "Serial Controlled Item", "EA", requiresSerial: true);
        var location = new Location("RECEIVE", "Receiving Dock", 1);

        _mockItemRepository.Setup(x => x.GetBySkuAsync(request.ItemSku, It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);
        _mockLocationRepository.Setup(x => x.GetByCodeAsync(request.LocationCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(location);

        // Act
        var result = await _useCase.ExecuteAsync(request, "USER1");

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("requires a serial number");
    }

    [Fact]
    public async Task ExecuteAsync_WhenRepositoryThrowsException_ReturnsFailure()
    {
        // Arrange
        var request = new ReceiveItemDto(
            "WIDGET-001",
            "RECEIVE",
            10.0m
        );

        _mockItemRepository.Setup(x => x.GetBySkuAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Database error"));

        // Act
        var result = await _useCase.ExecuteAsync(request, "USER1");

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Error receiving item");
    }

    [Fact]
    public async Task ExecuteAsync_WithValidLotControlledItem_CallsReceiveWithLotId()
    {
        // Arrange
        var request = new ReceiveItemDto(
            "LOT-ITEM",
            "RECEIVE",
            10.0m,
            "LOT-001",
            ExpiryDate: DateTime.Today.AddDays(30)
        );

        var item = new Item("LOT-ITEM", "Lot Controlled Item", "EA", true);
        var location = new Location("RECEIVE", "Receiving Dock", 1);
        var movement = Movement.CreateReceipt(item.Id, location.Id, new Quantity(10.0m), "USER1");

        _mockItemRepository.Setup(x => x.GetBySkuAsync(request.ItemSku, It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);
        _mockLocationRepository.Setup(x => x.GetByCodeAsync(request.LocationCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(location);
        _mockStockMovementService.Setup(x => x.ReceiveAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<Quantity>(), It.IsAny<string>(),
                It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(movement);

        // Act
        var result = await _useCase.ExecuteAsync(request, "USER1");

        // Assert
        result.IsSuccess.Should().BeTrue();

        _mockStockMovementService.Verify(x => x.ReceiveAsync(
            item.Id, location.Id, It.IsAny<Quantity>(), "USER1",
            It.IsAny<int?>(), // Should have lot ID
            It.IsAny<string?>(), request.ReferenceNumber, request.Notes,
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
