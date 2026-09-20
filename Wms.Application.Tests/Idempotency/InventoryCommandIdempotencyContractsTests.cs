using Wms.Application.Context;
using Wms.Application.DTOs;
using Wms.Application.Idempotency;

namespace Wms.Application.Tests.Idempotency;

public sealed class InventoryCommandIdempotencyContractsTests
{
    [Fact]
    public void RequestHashIsStableAndPayloadSensitive()
    {
        var request = new ReceiveItemDto("ITEM-1", "RECEIVE", 10m);

        var first = InventoryCommandRequestHasher.Compute("inventory.receipt", request);
        var second = InventoryCommandRequestHasher.Compute(
            "inventory.receipt",
            new ReceiveItemDto("ITEM-1", "RECEIVE", 10m));
        var changed = InventoryCommandRequestHasher.Compute(
            "inventory.receipt",
            new ReceiveItemDto("ITEM-1", "RECEIVE", 11m));

        first.Should().Be(second);
        first.Should().HaveLength(64);
        first.Should().NotBe(changed);
    }

    [Fact]
    public void CallerScopeSeparatesSourceActorAndWarehouse()
    {
        var context = new TestRequestContext();
        context.Initialize("corr-1", "Web", idempotencyKey: "request-1");

        var warehouseOne = InventoryCommandIdempotencyScope.For(context, "USER-1", 1);
        var warehouseTwo = InventoryCommandIdempotencyScope.For(context, "USER-1", 2);
        var otherActor = InventoryCommandIdempotencyScope.For(context, "USER-2", 1);

        warehouseOne.Should().NotBe(warehouseTwo);
        warehouseOne.Should().NotBe(otherActor);
        context.IdempotencyKey.Should().Be("request-1");
    }

    [Fact]
    public void ReplayPayloadDeserializesIntoTheOriginalContract()
    {
        var expected = new ReceiptResultDto(
            14,
            "ITEM-1",
            "RECEIVE",
            10m,
            null,
            new DateTime(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc));
        var decision = new InventoryCommandIdempotencyDecision(
            ShouldExecute: false,
            ResultType: nameof(ReceiptResultDto),
            ResultPayloadJson: InventoryCommandJson.Serialize(expected),
            ResultReference: "14");

        var replay = InventoryCommandJson.DeserializeResult<ReceiptResultDto>(decision);

        replay.IsSuccess.Should().BeTrue();
        replay.Value.Should().BeEquivalentTo(expected);
    }

    private sealed class TestRequestContext : IRequestContext
    {
        public string CorrelationId { get; private set; } = string.Empty;
        public string SourceClient { get; private set; } = string.Empty;
        public string? IdempotencyKey { get; private set; }
        public string? RemoteIpAddress { get; private set; }
        public string? UserAgent { get; private set; }

        public void Initialize(
            string? correlationId,
            string sourceClient,
            string? remoteIpAddress = null,
            string? userAgent = null,
            string? idempotencyKey = null)
        {
            CorrelationId = correlationId ?? string.Empty;
            SourceClient = sourceClient;
            RemoteIpAddress = remoteIpAddress;
            UserAgent = userAgent;
            IdempotencyKey = idempotencyKey;
        }
    }
}
