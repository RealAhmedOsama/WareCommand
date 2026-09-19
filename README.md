# 📦 Warehouse Management System (WMS)

> An early-stage Warehouse Management System MVP built with .NET 8, featuring both WinForms desktop application and ASP.NET Core web interface using Clean Architecture principles. See `docs/modernization/BASELINE_STATUS.md` for verified status; production readiness is not claimed.

[![.NET](https://img.shields.io/badge/.NET-8.0-blue.svg)](https://dotnet.microsoft.com/download)
[![License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)
[![Build Status](https://img.shields.io/badge/build-passing-brightgreen.svg)]()
[![PRs Welcome](https://img.shields.io/badge/PRs-welcome-brightgreen.svg)]()

## ✨ Overview

This Warehouse Management System provides comprehensive inventory management capabilities through two interfaces:
- **🖥️ WinForms Desktop Application**: Professional, scanner-optimized interface for warehouse operations
- **🌐 ASP.NET Core Web Application**: Modern, responsive web interface with Bootstrap 5 styling

Built using Clean Architecture principles with Domain-Driven Design, the system ensures maintainability, testability, and scalability for enterprise-grade warehouse operations.

## 🎯 Core Features

### 📋 Inventory Management
- **Item Master Data**: Complete SKU lifecycle with barcode support
- **Location Hierarchy**: Zone → Aisle → Bin structure support
- **Stock Tracking**: Real-time inventory levels with location visibility
- **Lot & Serial Tracking**: Optional batch and serial number management
- **Multi-Barcode Support**: Multiple barcodes per item with validation

### 📦 Warehouse Operations
- **Receiving Process**: Scanner-optimized receiving with lot/expiry tracking
- **Putaway Operations**: Efficient location-to-location transfers
- **Picking Workflow**: Order-based picking with quantity validation
- **Stock Adjustments**: Comprehensive adjustment tracking with audit trail
- **Movement History**: Complete transaction audit with timestamps

### 📊 Reporting & Analytics
- **Dashboard KPIs**: Real-time metrics and low-stock alerts
- **Movement Reports**: Comprehensive filtering and export capabilities
- **Stock Reports**: Current levels, availability, and location details
- **Audit Trail**: Complete movement history with user tracking
- **CSV Export**: Data export for external analysis

### Movement Processing 🔄
- Receipt processing with validation
- Putaway with location verification
- Pick processing with availability checks
- Stock adjustments with audit trail
- Movement history preservation

### Business Rules Enforcement ✅
- Item validation (active status, barcode lookup)
- Location validation (active, receivable/pickable)
- Quantity validation (positive numbers, availability)
- Lot requirement enforcement
- Serial number requirement enforcement

### Reporting Capabilities 📈
- Movement reports by date range
- Filtering by item, type, and user
- Real-time stock level reporting
- CSV export functionality
- Complete audit trail visibility

### User Experience 🎨
- Scanner-optimized workflow
- Keyboard navigation (F1-F5 shortcuts)
- Audio feedback for success/error
- Responsive UI with progress indicators
- Clear error messages and validation

## 🏗️ Architecture

### Clean Architecture Implementation

```
┌─────────────────┐   ┌─────────────────┐
│   WinForms UI   │   │  ASP.NET Core   │
│   (Desktop)     │   │   (Web MVC)     │
└─────────────────┘   └─────────────────┘
         │                       │
         └───────────┬───────────┘
                     │
            ┌─────────────────┐
            │   Application   │  ← Use Cases, DTOs, Results
            │     Layer       │
            └─────────────────┘
                     │
            ┌─────────────────┐
            │     Domain      │  ← Entities, Value Objects, Services
            │     Layer       │
            └─────────────────┘
                     │
            ┌─────────────────┐
            │ Infrastructure  │  ← Data Access, External Services
            │     Layer       │
            └─────────────────┘
```

### Technical Implementation Highlights

#### Domain Layer
- **Value Objects**: Barcode, Quantity with business rules
- **Entities**: Rich domain models with encapsulated behavior
- **Repository Contracts**: Clean abstraction for data access
- **Domain Services**: Complex business operations

#### Application Layer  
- **Use Cases**: Single responsibility command handlers
- **Result Pattern**: Consistent error handling across operations
- **DTOs**: Clean data contracts between layers
- **Validation**: Input validation with detailed error messages

#### Infrastructure Layer
- **EF Core**: Code-first with fluent API configurations
- **Repository Pattern**: Generic base with specialized implementations
- **Unit of Work**: Transaction management and consistency
- **Stock Movement Service**: Centralized inventory operations

#### UI Layer
- **Scanner-First Design**: Optimized for barcode workflow
- **Service Integration**: Dependency injection for loose coupling
- **Error Handling**: User-friendly messages with logging
- **Async Operations**: Non-blocking UI with progress feedback

## 📂 Project Structure

```
📁 Warehouse Management System/
├── 🎯 Wms.Domain/                    # Domain Layer
│   ├── Entities/                     # Domain Entities (Item, Location, Stock, etc.)
│   ├── ValueObjects/                 # Value Objects (Barcode, Quantity)
│   ├── Enums/                       # Domain Enumerations (MovementType)
│   ├── Services/                    # Domain Services (IStockMovementService)
│   └── Repositories/                # Repository Interfaces
├── 🚀 Wms.Application/              # Application Layer
│   ├── UseCases/                    # Application Use Cases
│   │   ├── Receiving/               # Receiving Operations
│   │   ├── Inventory/               # Stock Management
│   │   ├── Items/                   # Item Management
│   │   ├── Locations/               # Location Management
│   │   ├── Picking/                 # Picking Operations
│   │   └── Reports/                 # Reporting
│   ├── DTOs/                        # Data Transfer Objects
│   └── Common/                      # Shared Application Logic
├── 🔧 Wms.Infrastructure/           # Infrastructure Layer
│   ├── Data/                        # Database Context & Configurations
│   ├── Repositories/                # Repository Implementations
│   └── Services/                    # External Service Implementations
├── 🖥️ Wms.WinForms/                 # WinForms Desktop Application
│   ├── Forms/                       # Application Forms
│   │   ├── DashboardForm.cs         # KPI Dashboard
│   │   ├── ReceivingForm.cs         # Item Receiving
│   │   ├── PutawayForm.cs           # Putaway Operations
│   │   ├── PickingForm.cs           # Order Picking
│   │   ├── InventoryForm.cs         # Stock Management
│   │   ├── ItemManagementForm.cs    # Item Master Data
│   │   └── LocationManagementForm.cs # Location Setup
│   ├── Common/                      # UI Helpers & Utilities
│   └── Program.cs                   # Application Entry Point
├── 🌐 Wms.ASP/                      # ASP.NET Core Web Application
│   ├── Controllers/                 # MVC Controllers
│   ├── Views/                       # Razor Views
│   ├── Models/                      # View Models
│   ├── wwwroot/                     # Static Assets
│   └── Program.cs                   # Web Application Entry Point
└── 🧪 Test Projects/                # Unit & Integration Tests
    ├── Wms.Domain.Tests/            # Domain Layer Tests
    ├── Wms.Application.Tests/       # Application Layer Tests
    └── Wms.Infrastructure.Tests/    # Infrastructure Layer Tests
```

## 🚀 Getting Started

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Visual Studio 2022](https://visualstudio.microsoft.com/) or [VS Code](https://code.visualstudio.com/)
- SQLite (embedded database - no additional setup required)

### 🖥️ Running the Desktop Application

```bash
# Clone the repository
git clone https://github.com/RealAhmedOsama/Warehouse-Management-System.git
cd "Warehouse Management System"

# Restore NuGet packages
dotnet restore

# Run the WinForms application
dotnet run --project "Warehouse Management System/Wms.WinForms.csproj"
```

### 🌐 Running the Web Application

```bash
# Navigate to the web project
cd Wms.ASP

# Run the web application
dotnet run

# Open browser to https://localhost:5001 or http://localhost:5000
```

### 🧪 Running Tests

```bash
# Run all tests
dotnet test

# Run tests with coverage
dotnet test --collect:"XPlat Code Coverage"

# Run specific test project
dotnet test Wms.Domain.Tests/
```

## 📊 Database Schema

### Core Entities

| Entity | Description | Key Properties |
|--------|-------------|---------------|
| **Warehouse** | Physical warehouse facilities | Code, Name, IsActive |
| **Item** | Product/SKU master data | SKU, Name, UOM, RequiresLot/Serial |
| **Location** | Hierarchical storage locations | Code, Name, IsReceivable/Pickable |
| **Stock** | Current inventory levels | Item + Location + Available/Allocated Qty |
| **Movement** | Transaction history | Type, Item, Location, Quantity, Timestamp |
| **Lot** | Batch/lot tracking | Number, ExpiryDate, ManufacturedDate |

### Relationships

- **Warehouse** → **Locations** (1:Many)
- **Item** → **Stock** (1:Many)
- **Location** → **Stock** (1:Many)
- **Item** → **Movements** (1:Many)
- **Location** → **Movements** (1:Many)
- **Item** → **Lots** (1:Many)

## Sample Data & Testing

### Test Items Available
- **WIDGET-001**: Basic item (Barcode: 123456789012)
- **GADGET-001**: Lot-controlled item (Barcode: 234567890123)  
- **TOOL-001**: Standard item (Barcode: 345678901234)
- **PART-001**: Serial-controlled item (Barcode: 456789012345)
- **CABLE-001**: Ethernet Cable 5ft (Barcode: 567890123456)
- **SENSOR-001**: Temperature Sensor with Serial (Barcode: 678901234567)

### Test Locations Available
- **RECEIVE**: Receiving dock (receivable only)
- **Z001**: Zone 1 (receivable and pickable)
- **Z001-A001**: Aisle 1 (receivable and pickable)
- **Z001-A002**: Aisle 2 (receivable and pickable)
- **Z001-A001-01**: Bin 01 (receivable and pickable)
- **Z001-A001-02**: Bin 02 (receivable and pickable)

### Complete Test Workflow
1. **Receive Items**: Use barcodes above to receive items to RECEIVE location
2. **Putaway Stock**: Move items from RECEIVE to storage locations (Z001-A001-01, etc.)
3. **Check Inventory**: View stock levels and locations in Inventory Management
4. **Pick Orders**: Pick items from storage locations for orders
5. **Adjust Stock**: Make quantity adjustments with reason tracking
6. **Generate Reports**: View complete movement history and export data

## 🎨 User Interface

### 🖥️ WinForms Desktop Features

- **Modern Bootstrap-Inspired Design**: Professional UI with consistent blue color scheme
- **Keyboard Shortcuts**: F1-F8 shortcuts for rapid warehouse operations
- **Real-time Updates**: Live dashboard with KPI monitoring every 5 minutes
- **Barcode Integration**: Optimized for handheld barcode scanners
- **Audio Feedback**: Success/error sounds for scanner operations
- **Print Support**: Label and report printing capabilities (planned)

### 🌐 Web Application Features

- **Responsive Design**: Mobile-first Bootstrap 5 implementation
- **Progressive Enhancement**: Works on all devices and screen sizes
- **Real-time Alerts**: Modern toast notifications for user feedback
- **AJAX Support**: Smooth user experience without page reloads
- **Export Capabilities**: CSV export for reports and data analysis
- **Container Layout**: Centered content for better user experience

### UI Enhancement Features

#### Modern Design System
- **Bootstrap 5-inspired color scheme** with consistent primary, secondary, success, warning, danger, and info colors
- **Typography system** with proper font hierarchy (H1-H6, body text, small text)
- **Card-based layouts** with shadows and rounded corners for better visual organization
- **Modern input controls** with focus states and validation styling
- **Responsive button styling** with proper hover and disabled states

#### Form Navigation Shortcuts

| Form | Shortcut | Primary Function |
|------|----------|------------------|
| Dashboard | - | System overview and KPIs |
| Receiving | F1=Receive, F2=Clear | Barcode-based receiving |
| Putaway | F1=Putaway, F2=Clear | Location transfers |
| Picking | F1=Pick, F2=Clear | Order fulfillment |
| Inventory | F1=Refresh, F2=Adjust, F3=Summary | Stock management |
| Items | F1=Refresh, F2=Add, F3=Edit | Item master data |
| Locations | F1=Refresh, F2=Add, F3=Edit | Location master data |
| Reports | F1=Generate, F2=Export | Movement reporting |

## ⚙️ Configuration

### Database Configuration

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=warehouse.db"
  }
}
```

### Application Settings

```json
{
  "WMS": {
    "Dashboard": {
      "RefreshIntervalSeconds": 300,
      "LowStockThreshold": 10
    },
    "Barcode": {
      "ScanTimeout": 5000,
      "ValidationEnabled": true
    }
  }
}
```

## 📚 API Reference

### Key Use Cases

```csharp
// Receiving an item
var result = await receiveItemUseCase.ExecuteAsync(new ReceiveItemDto
{
    ItemSku = "WIDGET-001",
    LocationCode = "RECEIVE",
    Quantity = 100,
    LotNumber = "LOT123",
    ReferenceNumber = "PO-456"
}, userId);

// Getting stock levels
var stock = await getStockUseCase.ExecuteAsync(new GetStockQuery
{
    ItemSku = "WIDGET-001",
    LocationCode = "A001"
});

// Creating a new item
var item = await createItemUseCase.ExecuteAsync(new CreateItemDto
{
    Sku = "NEW-001",
    Name = "New Product",
    UnitOfMeasure = "EA",
    RequiresLot = true
}, userId);
```

## 🔧 Development

### Adding New Features

1. **Domain First**: Define entities and business rules in `Wms.Domain`
2. **Use Cases**: Implement application logic in `Wms.Application`
3. **Data Layer**: Add repository methods in `Wms.Infrastructure`
4. **UI Layer**: Create forms/views in respective UI projects
5. **Testing**: Add unit tests for all layers

### Code Standards

- **C# 12** language features with nullable reference types enabled
- **Entity Framework Core** code-first approach with fluent API
- **SOLID Principles** applied throughout the architecture
- **Clean Code** practices with meaningful names and documentation
- **Async/Await** pattern for all I/O operations

## Architecture Benefits

### Maintainability
- Clear separation of concerns across layers
- Testable business logic isolated in domain layer
- Dependency injection enables flexible component composition
- Centralized error handling with Result pattern

### Extensibility  
- New use cases easily added to application layer
- Additional repositories and services integrated via interfaces
- UI layers can be added without affecting business logic
- Plugin architecture ready for third-party integrations

### Data Integrity
- Domain-driven validation rules prevent invalid states
- Immutable movement history ensures audit trail
- Transaction management with automatic rollback capability
- Concurrency handling prevents data conflicts

### Performance
- Async operations prevent UI thread blocking
- Efficient data access patterns with EF Core
- Minimal memory footprint with proper disposal
- Optimized queries with includes and projections

## 🚀 Deployment

### Desktop Application
- **Self-contained**: Single executable with all dependencies
- **ClickOnce**: Automatic updates and easy distribution (planned)
- **MSI Installer**: Traditional Windows installation package (planned)

### Web Application
- **Self-hosted**: Kestrel web server for development
- **IIS Integration**: Production deployment on Windows Server
- **Docker Support**: Containerized deployment (planned)
- **Cloud Ready**: Azure App Service compatible

## 📈 Performance & Monitoring

- **Serilog Integration**: Comprehensive structured logging
- **Performance Counters**: Database query and operation timing
- **Error Tracking**: Detailed exception logging with context
- **Health Checks**: Application health monitoring endpoints (web)

## 🧪 Testing Strategy

- **Unit Tests**: Domain logic and use case testing
- **Integration Tests**: Database and repository testing
- **UI Tests**: Form behavior and workflow testing (planned)
- **Performance Tests**: Load testing for web interface (planned)

## 🤝 Contributing

1. **Fork** the repository
2. **Create** a feature branch (`git checkout -b feature/AmazingFeature`)
3. **Commit** your changes (`git commit -m 'Add AmazingFeature'`)
4. **Push** to the branch (`git push origin feature/AmazingFeature`)
5. **Open** a Pull Request

### Development Guidelines
- Follow existing code style and architectural patterns
- Add comprehensive unit tests for new functionality
- Update documentation for API changes
- Ensure all tests pass before submitting PR
- Follow semantic versioning for breaking changes

## 📝 Changelog

### Version 2.0.0 (Current)
- ✅ Added ASP.NET Core web interface
- ✅ Modern Bootstrap 5 UI design system
- ✅ Responsive web design for mobile/tablet
- ✅ Enhanced error handling with Result pattern
- ✅ Comprehensive logging with Serilog
- ✅ Improved database seeding and initialization

### Version 1.0.0
- ✅ Initial WinForms desktop application
- ✅ Core warehouse operations (receive, putaway, pick)
- ✅ Item and location management
- ✅ Stock tracking and adjustments
- ✅ Movement reporting and audit trail

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## 👨‍💻 Author

**Ahmed Osama**
- GitHub: [@RealAhmedOsama](https://github.com/RealAhmedOsama)
- LinkedIn: [Ahmed Osama](https://www.linkedin.com/in/realahmedosama/)
- Email: Contact via GitHub

## 🙏 Acknowledgments

- **Microsoft** for the excellent .NET ecosystem and tooling
- **Entity Framework Team** for the powerful ORM capabilities
- **Bootstrap Team** for the amazing responsive CSS framework
- **Serilog Community** for structured logging excellence
- **Open Source Community** for inspiration and best practices

## 🔗 Related Documentation

- [UI Enhancement Guide](./Warehouse%20Management%20System/README-UI-ENHANCEMENTS.md)
- [Responsive Design Implementation](./Warehouse%20Management%20System/RESPONSIVE-UI-DOCUMENTATION.md)
- [Runtime Fixes Documentation](./Warehouse%20Management%20System/RUNTIME-FIXES-DOCUMENTATION.md)

---

<div align="center">
  
### 🚀 Ready to streamline your warehouse operations?

**[Get Started Now](#-getting-started)** • **[View Features](#-core-features)** • **[Check Architecture](#️-architecture)**

---

<p>Built with ❤️ using .NET 8, Clean Architecture, and modern design principles</p>
<p><strong>⭐ Star this repo if you find it helpful!</strong></p>

</div>
