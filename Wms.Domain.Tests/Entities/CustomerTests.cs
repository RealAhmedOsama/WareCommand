using FluentAssertions;
using Wms.Domain.Entities;

namespace Wms.Domain.Tests.Entities;

public sealed class CustomerTests
{
    [Fact]
    public void NormalizesIdentifiersAndKeepsOneDefaultShipTo()
    {
        var customer = new Customer(" acme ", " Acme Retail ", externalErpIdentifier: " erp-1 ");
        var first = new CustomerShipToAddress("main", "Main Recipient", null, "eg", null, "Cairo", null, "First line");
        var second = new CustomerShipToAddress("branch", "Branch Recipient", null, "EG", null, "Giza", null, "Second line", isDefault: true);

        customer.AddShipToAddress(first);
        customer.AddShipToAddress(second);

        customer.Code.Should().Be("ACME");
        customer.ExternalErpIdentifier.Should().Be("ERP-1");
        customer.ShipToAddresses.Should().ContainSingle(address => address.Code == "BRANCH" && address.IsDefault);
        customer.ShipToAddresses.Count(address => address.IsDefault).Should().Be(1);
    }

    [Fact]
    public void RejectsInvalidDeliveryWindowAndPriority()
    {
        var invalidWindow = () => new CustomerShipToAddress(
            "MAIN",
            "Recipient",
            null,
            "EG",
            null,
            "Cairo",
            null,
            "Address",
            deliveryWindowStart: TimeSpan.FromHours(18),
            deliveryWindowEnd: TimeSpan.FromHours(9));
        var invalidPriority = () => new Customer("ACME", "Acme", priority: 1_000);

        invalidWindow.Should().Throw<ArgumentException>();
        invalidPriority.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void CustomerItemReferenceNormalizesAndCanBeDeactivated()
    {
        var reference = new CustomerItemReference(4, " sku-4 ", " barcode-4 ");

        reference.CustomerSku.Should().Be("SKU-4");
        reference.CustomerBarcode.Should().Be("BARCODE-4");
        reference.SetActive(false);
        reference.IsActive.Should().BeFalse();
    }
}
