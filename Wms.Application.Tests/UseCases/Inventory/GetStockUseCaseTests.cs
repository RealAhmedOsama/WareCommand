// Wms.Application.Tests/UseCases/Inventory/GetStockUseCaseTests.cs

using System.Reflection;
using Microsoft.Extensions.Logging;
using Wms.Application.Tests.Identity;
using Wms.Application.UseCases.Inventory;
using Wms.Domain.Entities;
using Wms.Domain.Repositories;
using Wms.Domain.ValueObjects;

namespace Wms.Application.Tests.UseCases.Inventory;

public class GetStockUseCaseTests
{
    private readonly Mock<ILogger<GetStockUseCase>> _mockLogger;
    private readonly Mock<IStockRepository> _mockStockRepository;
    private readonly Mock<IUnitOfWork> _mockUnitOfWork;
    private readonly GetStockUseCase _useCase;

    public GetStockUseCaseTests()
    {
        _mockUnitOfWork = new Mock<IUnitOfWork>();
        _mockStockRepository = new Mock<IStockRepository>();
        _mockLogger = new Mock<ILogger<GetStockUseCase>>();

        _mockUnitOfWork.Setup(x => x.Stock).Returns(_mockStockRepository.Object);

        _useCase = new GetStockUseCase(
            _mockUnitOfWork.Object,
            _mockLogger.Object,
            new AllowAllWarehouseAccessService());
    }

    [Fact]
    public async Task GetAllStockAsync_WithValidData_ReturnsAllStock()
    {
        // Arrange
        var item = new Item("WIDGET-001", "Widget A", "EA");
        var location = new Location("Z001", "Zone 1", 1);

        // Create mock stocks with proper setup
        var stocks = CreateMockStocksWithNavigationProperties(item, location);

        _mockStockRepository.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(stocks);

        // Act
        var result = await _useCase.GetAllStockAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value.First().ItemSku.Should().Be("WIDGET-001");
    }

    [Fact]
    public async Task GetStockByItemAsync_WithValidItemId_ReturnsItemStock()
    {
        // Arrange
        var itemId = 1;
        var item = new Item("WIDGET-001", "Widget A", "EA");
        var location = new Location("Z001", "Zone 1", 1);

        var stocks = CreateMockStocksWithNavigationProperties(item, location);

        _mockStockRepository.Setup(x => x.GetByItemIdAsync(itemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stocks.Take(1));

        // Act
        var result = await _useCase.GetStockByItemAsync(itemId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value.First().ItemSku.Should().Be("WIDGET-001");
    }

    [Fact]
    public async Task GetStockByLocationAsync_WithValidLocationId_ReturnsLocationStock()
    {
        // Arrange
        var locationId = 1;
        var item = new Item("WIDGET-001", "Widget A", "EA");
        var location = new Location("Z001", "Zone 1", 1);

        var stocks = CreateMockStocksWithNavigationProperties(item, location);

        _mockStockRepository.Setup(x => x.GetByLocationIdAsync(locationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stocks.Take(1));

        // Act
        var result = await _useCase.GetStockByLocationAsync(locationId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value.First().LocationCode.Should().Be("Z001");
    }

    [Fact]
    public async Task GetStockSummaryAsync_WithMultipleStockItems_ReturnsAggregatedSummary()
    {
        // Arrange
        var item1 = new Item("WIDGET-001", "Widget A", "EA");
        var item2 = new Item("GADGET-001", "Gadget B", "EA");
        var location1 = new Location("Z001", "Zone 1", 1);
        var location2 = new Location("Z002", "Zone 2", 1);

        // Create stocks with explicit location IDs
        var stocks = new List<Stock>
        {
            new(1, 1, new Quantity(10.0m)), // item1 in location1
            new(1, 2, new Quantity(5.0m)), // item1 in location2  
            new(2, 1, new Quantity(20.0m)) // item2 in location1
        };

        // Set up navigation properties using reflection
        SetStockNavigationProperties(stocks[0], item1, location1);
        SetStockNavigationProperties(stocks[1], item1, location2);
        SetStockNavigationProperties(stocks[2], item2, location1);

        // Reserve some quantity
        stocks[0].ReserveQuantity(new Quantity(2.0m));

        _mockStockRepository.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(stocks);

        // Act
        var result = await _useCase.GetStockSummaryAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2); // Two distinct items

        var widgetSummary = result.Value.First(s => s.ItemSku == "WIDGET-001");
        widgetSummary.TotalQuantity.Should().Be(15.0m); // 10 + 5
        widgetSummary.TotalReserved.Should().Be(2.0m);
        widgetSummary.TotalAvailable.Should().Be(13.0m); // 15 - 2
        widgetSummary.LocationCount.Should().Be(2);
    }

    [Fact]
    public async Task GetAllStockAsync_WhenRepositoryThrowsException_ReturnsFailure()
    {
        // Arrange
        _mockStockRepository.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Database error"));

        // Act
        var result = await _useCase.GetAllStockAsync();

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Error retrieving stock");
    }

    private static List<Stock> CreateMockStocksWithNavigationProperties(Item item, Location location)
    {
        var stocks = new List<Stock>
        {
            CreateMockStock(item, location, new Quantity(10.0m)),
            CreateMockStock(item, location, new Quantity(5.0m))
        };

        return stocks;
    }

    private static Stock CreateMockStock(Item item, Location location, Quantity quantity)
    {
        var stock = new Stock(item.Id, location.Id, quantity);

        // Use reflection to set the navigation properties for testing
        var itemProperty = typeof(Stock).GetProperty("Item");
        itemProperty?.SetValue(stock, item);

        var locationProperty = typeof(Stock).GetProperty("Location");
        locationProperty?.SetValue(stock, location);

        // Set the LocationId to match the location for proper grouping
        var locationIdField = typeof(Stock).GetField("_locationId", BindingFlags.NonPublic | BindingFlags.Instance);
        if (locationIdField == null)
        {
            var locationIdProperty = typeof(Stock).GetProperty("LocationId");
            locationIdProperty?.SetValue(stock, location.Id);
        }

        return stock;
    }

    private static void SetStockNavigationProperties(Stock stock, Item item, Location location)
    {
        var itemProperty = typeof(Stock).GetProperty("Item");
        itemProperty?.SetValue(stock, item);

        var locationProperty = typeof(Stock).GetProperty("Location");
        locationProperty?.SetValue(stock, location);
    }
}
