using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.DTOs;
using Wms.Application.Idempotency;
using Wms.Application.Receiving;
using Wms.Application.Tests.Identity;
using Wms.Application.UseCases.Receiving;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Receiving;
using Wms.Domain.Repositories;
using Wms.Domain.Services;
using Wms.Domain.ValueObjects;

namespace Wms.Application.Tests.UseCases.Receiving;

public sealed class ReceiveItemIdempotencyTests
{
    [Fact]
    public async Task RepeatedReceiptReplaysTheOriginalResultWithoutCallingMovementServiceTwice()
    {
        var unitOfWork = new Mock<IUnitOfWork>();
        var receiving = new Mock<IReceivingRepository>();
        var movementService = new Mock<IStockMovementService>();
        var idempotencyService = new Mock<IInventoryCommandIdempotencyService>();
        var receiptService = new Mock<IReceiptService>();
        var requestContext = new TestRequestContext();
        requestContext.Initialize("correlation-1", "Test", idempotencyKey: "receipt-1");

        unitOfWork.Setup(value => value.Receiving).Returns(receiving.Object);
        unitOfWork
            .Setup(value => value.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        unitOfWork
            .Setup(value => value.CommitTransactionAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        unitOfWork
            .Setup(value => value.RollbackTransactionAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        unitOfWork
            .Setup(value => value.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var item = new Item("ITEM-1", "Item 1", "EA");
        var location = new Location("RECEIVE", "Receiving", 7);
        var movement = Movement.CreateReceipt(item.Id, location.Id, new Quantity(10m), "USER1");
        var request = new ReceiveItemDto("ITEM-1", "RECEIVE", 10m);
        var lease = new InventoryCommandIdempotencyLease(
            11,
            "inventory.receipt",
            "Test:USER1:warehouse:7",
            "receipt-1",
            TimeSpan.FromDays(30));
        var firstDecision = new InventoryCommandIdempotencyDecision(
            ShouldExecute: true,
            Lease: lease);
        var replay = new ReceiptResultDto(
            movement.Id,
            request.ItemSku,
            request.LocationCode,
            request.Quantity,
            request.LotNumber,
            movement.Timestamp,
            ReceiptId: 1,
            ReceiptDocumentNumber: "RCPT-IDEMP");
        var replayDecision = new InventoryCommandIdempotencyDecision(
            ShouldExecute: false,
            ResultType: nameof(ReceiptResultDto),
            ResultPayloadJson: InventoryCommandJson.Serialize(replay),
            ResultReference: movement.Id.ToString(CultureInfo.InvariantCulture));

        receiving.Setup(value => value.GetTargetAsync(
                request.ItemSku,
                request.LocationCode,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReceivingTarget(item, location));
        movementService
            .Setup(value => value.ReceiveAsync(
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<Quantity>(),
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<int?>(),
                It.IsAny<int?>(),
                It.IsAny<int?>(),
                It.IsAny<InventoryOwnerKind>(),
                It.IsAny<int?>(),
                It.IsAny<string?>()))
            .ReturnsAsync(movement);
        receiptService
            .Setup(value => value.OpenForReceivingAsync(
                It.IsAny<ReceiptReceivingInput>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReceiptReceivingInput input, string userId, CancellationToken cancellationToken) =>
                Result.Success(new ReceiptReceivingPlan(
                    1,
                    1,
                    "RCPT-IDEMP",
                    input.WarehouseId,
                    input.ItemId,
                    input.Quantity.Value,
                    input.Quantity.Value,
                    0m,
                    0m,
                    0m,
                    input.PurchaseOrderPlan,
                    input.AdvanceShippingNoticePlan,
                    input.OwnerKind,
                    input.InventoryOwnerId,
                    input.OwnerCodeSnapshot ?? "COMPANY")));
        receiptService
            .Setup(value => value.FinalizeReceivingAsync(
                It.IsAny<ReceiptReceivingPlan>(),
                It.IsAny<Movement>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        idempotencyService
            .SetupSequence(value => value.BeginAsync(
                It.IsAny<InventoryCommandIdempotencyRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(firstDecision))
            .ReturnsAsync(Result.Success(replayDecision));

        var useCase = new ReceiveItemUseCase(
            unitOfWork.Object,
            movementService.Object,
            NullLogger<ReceiveItemUseCase>.Instance,
            new AllowAllWarehouseAccessService(),
            idempotencyService: idempotencyService.Object,
            requestContext: requestContext,
            receiptService: receiptService.Object);

        var first = await useCase.ExecuteAsync(request, "USER1");
        var second = await useCase.ExecuteAsync(request, "USER1");

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
        second.Value.Should().BeEquivalentTo(first.Value);
        movementService.Verify(
            value => value.ReceiveAsync(
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<Quantity>(),
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<int?>(),
                It.IsAny<int?>(),
                It.IsAny<int?>(),
                It.IsAny<InventoryOwnerKind>(),
                It.IsAny<int?>(),
                It.IsAny<string?>()),
            Times.Once);
        idempotencyService.Verify(
            value => value.CompleteAsync(
                lease,
                It.IsAny<InventoryCommandIdempotencyCompletion>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        unitOfWork.Verify(
            value => value.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Exactly(2));
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
