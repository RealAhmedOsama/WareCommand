// Wms.Application.Tests/UseCases/Items/GetItemsUseCaseTests.cs

using Microsoft.Extensions.Logging;
using Wms.Application.Tests.Identity;
using Wms.Application.UseCases.Items;
using Wms.Domain.Entities;
using Wms.Domain.Repositories;
using Wms.Domain.ValueObjects;

namespace Wms.Application.Tests.UseCases.Items;

public class GetItemsUseCaseTests
{
    private readonly Mock<IItemRepository> _mockItemRepository;
    private readonly Mock<ILogger<GetItemsUseCase>> _mockLogger;
    private readonly Mock<IUnitOfWork> _mockUnitOfWork;
    private readonly GetItemsUseCase _useCase;

    public GetItemsUseCaseTests()
    {
        _mockUnitOfWork = new Mock<IUnitOfWork>();
        _mockItemRepository = new Mock<IItemRepository>();
        _mockLogger = new Mock<ILogger<GetItemsUseCase>>();

        _mockUnitOfWork.Setup(x => x.Items).Returns(_mockItemRepository.Object);

        _useCase = new GetItemsUseCase(
            _mockUnitOfWork.Object,
            _mockLogger.Object,
            new AllowAllWarehouseAccessService());
    }

    [Fact]
    public async Task ExecuteAsync_WithNoSearchTerm_ReturnsAllItems()
    {
        // Arrange
        var items = new List<Item>
        {
            new("WIDGET-001", "Widget A", "EA"),
            new("GADGET-001", "Gadget B", "EA")
        };

        _mockItemRepository.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(items);

        // Act
        var result = await _useCase.ExecuteAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value.First().Sku.Should().Be("WIDGET-001");
    }

    [Fact]
    public async Task ExecuteAsync_WithSearchTerm_ReturnsFilteredItems()
    {
        // Arrange
        var searchTerm = "WIDGET";
        var items = new List<Item>
        {
            new("WIDGET-001", "Widget A", "EA")
        };

        _mockItemRepository.Setup(x => x.SearchAsync(searchTerm, It.IsAny<CancellationToken>()))
            .ReturnsAsync(items);

        // Act
        var result = await _useCase.ExecuteAsync(searchTerm);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value.First().Sku.Should().Be("WIDGET-001");
    }

    [Fact]
    public async Task GetByIdAsync_WithExistingId_ReturnsItem()
    {
        // Arrange
        var itemId = 1;
        var item = new Item("WIDGET-001", "Widget A", "EA");

        _mockItemRepository.Setup(x => x.GetByIdAsync(itemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);

        // Act
        var result = await _useCase.GetByIdAsync(itemId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Sku.Should().Be("WIDGET-001");
    }

    [Fact]
    public async Task GetByIdAsync_WithNonExistingId_ReturnsFailure()
    {
        // Arrange
        var itemId = 999;

        _mockItemRepository.Setup(x => x.GetByIdAsync(itemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Item?)null);

        // Act
        var result = await _useCase.GetByIdAsync(itemId);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task GetBySkuAsync_WithExistingSku_ReturnsItem()
    {
        // Arrange
        var sku = "WIDGET-001";
        var item = new Item(sku, "Widget A", "EA");

        _mockItemRepository.Setup(x => x.GetBySkuAsync(sku, It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);

        // Act
        var result = await _useCase.GetBySkuAsync(sku);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Sku.Should().Be(sku);
    }

    [Fact]
    public async Task GetByBarcodeAsync_WithExistingBarcode_ReturnsItem()
    {
        // Arrange
        var barcodeValue = "123456789012";
        var item = new Item("WIDGET-001", "Widget A", "EA");
        item.AddBarcode(new Barcode(barcodeValue));

        _mockItemRepository.Setup(x => x.GetByBarcodeAsync(It.IsAny<Barcode>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);

        // Act
        var result = await _useCase.GetByBarcodeAsync(barcodeValue);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Sku.Should().Be("WIDGET-001");
    }

    [Fact]
    public async Task GetByBarcodeAsync_WithNonExistingBarcode_ReturnsFailure()
    {
        // Arrange
        var barcodeValue = "999999999999";

        _mockItemRepository.Setup(x => x.GetByBarcodeAsync(It.IsAny<Barcode>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Item?)null);

        // Act
        var result = await _useCase.GetByBarcodeAsync(barcodeValue);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task ExecuteAsync_WhenRepositoryThrowsException_ReturnsFailure()
    {
        // Arrange
        _mockItemRepository.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Database error"));

        // Act
        var result = await _useCase.ExecuteAsync();

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Error retrieving items");
    }

    [Fact]
    public async Task ExecuteAsync_WhenRequestIsCancelled_PropagatesCancellation()
    {
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();
        _mockItemRepository.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(cancellationSource.Token));

        var action = () => _useCase.ExecuteAsync(cancellationToken: cancellationSource.Token);

        await Assert.ThrowsAsync<OperationCanceledException>(action);
    }
}
