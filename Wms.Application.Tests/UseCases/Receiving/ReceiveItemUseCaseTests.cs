// Wms.Application.Tests/UseCases/Receiving/ReceiveItemUseCaseTests.cs

using Microsoft.Extensions.Logging;
using Wms.Application.Common;
using Wms.Application.DTOs;
using Wms.Application.Inbound;
using Wms.Application.Lots;
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
    private readonly Mock<ILotService> _mockLotService;
    private readonly Mock<IUnitOfWork> _mockUnitOfWork;
    private readonly ReceiveItemUseCase _useCase;

    public ReceiveItemUseCaseTests()
    {
        _mockUnitOfWork = new Mock<IUnitOfWork>();
        _mockItemRepository = new Mock<IItemRepository>();
        _mockLocationRepository = new Mock<ILocationRepository>();
        _mockStockMovementService = new Mock<IStockMovementService>();
        _mockLotService = new Mock<ILotService>();
        _mockLogger = new Mock<ILogger<ReceiveItemUseCase>>();

        _mockUnitOfWork.Setup(x => x.Items).Returns(_mockItemRepository.Object);
        _mockUnitOfWork.Setup(x => x.Locations).Returns(_mockLocationRepository.Object);

        _useCase = new ReceiveItemUseCase(
            _mockUnitOfWork.Object,
            _mockStockMovementService.Object,
            _mockLogger.Object,
            new AllowAllWarehouseAccessService(),
            lotService: _mockLotService.Object);
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
    public async Task ExecuteAsync_WithLotAndSerialRequiredButLotMissing_RollsBackIdentityTransaction()
    {
        var request = new ReceiveItemDto(
            "LOT-SERIAL-ITEM",
            "RECEIVE",
            1.0m,
            SerialNumber: "SN-001");

        var item = new Item(
            "LOT-SERIAL-ITEM",
            "Lot and serial controlled item",
            "EA",
            requiresLot: true,
            requiresSerial: true);
        var location = new Location("RECEIVE", "Receiving Dock", 1);

        _mockItemRepository.Setup(x => x.GetBySkuAsync(request.ItemSku, It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);
        _mockLocationRepository.Setup(x => x.GetByCodeAsync(request.LocationCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(location);
        _mockUnitOfWork.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockUnitOfWork.Setup(x => x.RollbackTransactionAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await _useCase.ExecuteAsync(request, "USER1");

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("requires a lot number");
        _mockUnitOfWork.Verify(
            x => x.RollbackTransactionAsync(It.IsAny<CancellationToken>()),
            Times.Once);
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
        _mockLotService.Setup(x => x.ResolveForReceiptAsync(
                item,
                request.LotNumber!,
                It.IsAny<LotDetailsRequest>(),
                "USER1",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new Lot(
                request.LotNumber!,
                item.Id,
                request.ExpiryDate)));
        _mockUnitOfWork.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockUnitOfWork.Setup(x => x.CommitTransactionAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
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

    [Fact]
    public async Task ExecuteAsync_WithAsnReferenceValidatesAndAllocatesTheSameReceiptMovement()
    {
        var request = new ReceiveItemDto(
            "WIDGET-001",
            "RECEIVE",
            10m,
            ReferenceNumber: "ASN-RECEIPT",
            AdvanceShippingNoticeId: 41,
            AdvanceShippingNoticeLineId: 42);
        var item = new Item("WIDGET-001", "Widget A", "EA");
        var location = new Location("RECEIVE", "Receiving Dock", 7);
        var movement = Movement.CreateReceipt(item.Id, location.Id, new Quantity(10m), "USER1");
        var receiptPlan = new AdvanceShippingNoticeReceiptPlan(
            41,
            42,
            "ASN-ASN-WH-000041",
            item.Id,
            location.WarehouseId,
            10m,
            null,
            null,
            null,
            null,
            null);
        var asnService = new Mock<IAdvanceShippingNoticeService>();

        _mockItemRepository.Setup(x => x.GetBySkuAsync(request.ItemSku, It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);
        _mockLocationRepository.Setup(x => x.GetByCodeAsync(request.LocationCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(location);
        _mockUnitOfWork.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockUnitOfWork.Setup(x => x.CommitTransactionAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockUnitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _mockStockMovementService.Setup(x => x.ReceiveAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<Quantity>(), It.IsAny<string>(),
                It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(movement);
        asnService.Setup(x => x.ValidateReceiptAsync(
                41,
                42,
                item.Id,
                location.WarehouseId,
                10m,
                null,
                null,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(receiptPlan));
        asnService.Setup(x => x.RecordReceiptAsync(
                receiptPlan,
                movement,
                "USER1",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var useCase = new ReceiveItemUseCase(
            _mockUnitOfWork.Object,
            _mockStockMovementService.Object,
            _mockLogger.Object,
            new AllowAllWarehouseAccessService(),
            advanceShippingNoticeService: asnService.Object);

        var result = await useCase.ExecuteAsync(request, "USER1");

        result.IsSuccess.Should().BeTrue(result.Error);
        asnService.Verify(x => x.ValidateReceiptAsync(
            41,
            42,
            item.Id,
            location.WarehouseId,
            10m,
            null,
            null,
            null,
            null,
            It.IsAny<CancellationToken>()), Times.Once);
        asnService.Verify(x => x.RecordReceiptAsync(
            receiptPlan,
            movement,
            "USER1",
            It.IsAny<CancellationToken>()), Times.Once);
        _mockUnitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
