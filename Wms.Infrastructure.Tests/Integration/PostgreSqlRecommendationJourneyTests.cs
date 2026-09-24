using System.Globalization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wms.Application.Auditing;
using Wms.Application.DataGeneration;
using Wms.Application.Identity;
using Wms.Application.Inventory;
using Wms.Application.Recommendations;
using Wms.Application.WarehouseWork;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Inventory;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Identity;
using Wms.Infrastructure.Recommendations;

namespace Wms.Infrastructure.Tests.Integration;

public sealed partial class PostgreSqlJourneyTests
{
    [PostgreSqlFact]
    public async Task GovernedReplenishmentRecommendationPersistsApprovalAndCreatesOneNormalWorkCommand()
    {
        await using var target = new PostgreSqlTestDatabase();
        await target.InitializeAsync();
        var seeded = await DeterministicPostgreSqlDataGenerationFixture.WriteWithActorCredentialsAsync(
            target,
            new DataGenerationRequest(
                WmsDataGenerationProfiles.IntegrationTest,
                "issue-128-recommendation-journey",
                Environment: "Testing",
                Locale: "en-US"));

        using var provider = DeterministicPostgreSqlDataGenerationFixture.CreateServiceProviderForExistingTarget(
            target,
            services => services.Configure<RecommendationGovernanceOptions>(options =>
            {
                options.Enabled = true;
                options.KillSwitchEnabled = false;
                options.ProviderAvailable = true;
                options.ShadowMode = true;
            }));
        using var scope = provider.CreateScope();
        var services = scope.ServiceProvider;
        var userManager = services.GetRequiredService<UserManager<WmsUser>>();
        var actor = await userManager.FindByIdAsync(seeded.ActorCredentials.UserId);
        Assert.NotNull(actor);
        services.GetRequiredService<DesktopUserSession>().SignIn(actor);

        var context = services.GetRequiredService<WmsDbContext>();
        var reconciliation = services.GetRequiredService<IInventoryReconciliationService>();
        var checkpoints = new List<PostgreSqlJourneyCheckpointEvidence>();
        var outcomes = new List<string>();
        var warehouseId = await context.UserWarehouseAssignments.AsNoTracking()
            .Where(assignment => assignment.UserId == actor.Id)
            .Select(assignment => assignment.WarehouseId)
            .OrderBy(value => value)
            .FirstAsync();
        var candidates = await context.InventoryBalances.AsNoTracking()
            .Include(value => value.Item)
            .Include(value => value.Location)
            .Where(value => value.WarehouseId == warehouseId &&
                            value.Location.Type == LocationType.Storage &&
                            value.Location.IsActive &&
                            value.Location.IsPickable &&
                            value.Item.IsActive &&
                            !value.Item.RequiresLot &&
                            !value.Item.RequiresSerial &&
                            value.LotId == null &&
                            value.SerialNumberId == null &&
                            value.LicensePlateId == null &&
                            value.InventoryStatusId == InventoryStatusSystemIds.Available &&
                            value.OwnerKind == InventoryOwnerKind.CompanyOwned &&
                            value.InventoryOwnerId == null &&
                            value.OwnerCodeSnapshot == InventoryOwnershipDimension.CompanyOwnerCode)
            .OrderBy(value => value.ItemId)
            .ThenBy(value => value.LocationId)
            .ToArrayAsync();
        var source = candidates.FirstOrDefault(value => value.OnHandQuantity - value.ReservedQuantity >= 1m);
        Assert.NotNull(source);

        var destination = await context.Locations.AsNoTracking()
            .Where(value => value.WarehouseId == warehouseId &&
                            value.Type == LocationType.Storage &&
                            value.Id != source.LocationId &&
                            value.IsActive &&
                            value.IsPickable)
            .OrderBy(value => value.Id)
            .FirstAsync();
        var destinationQuantityBefore = await SumLocationQuantityAsync(context, destination.Id, source.ItemId);
        var targetQuantity = destinationQuantityBefore + 1m;
        var policies = services.GetRequiredService<IInventoryReplenishmentPolicyService>();
        var policy = await policies.SaveAsync(
            null,
            new InventoryReplenishmentPolicyInput(
                source.ItemId,
                warehouseId,
                destination.Id,
                MinimumQuantity: 0m,
                MaximumQuantity: targetQuantity + 100m,
                SafetyStockQuantity: 0m,
                ReorderPointQuantity: targetQuantity,
                TargetQuantity: targetQuantity,
                QuantityBasis: InventoryPolicyQuantityBasis.PhysicalAvailable,
                EffectiveFromUtc: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            actor.Id);
        Assert.True(policy.IsSuccess, policy.FirstError?.Message);
        await ReconcileAndRecordAsync("recommendation-policy-created", context, reconciliation, checkpoints, CancellationToken.None);

        var originalItemQuantity = await SumItemQuantityAsync(context, source.ItemId);
        var originalReservedQuantity = await SumReservedQuantityAsync(context, source.ItemId);
        var governance = services.GetRequiredService<IRecommendationGovernanceService>();
        var generated = await governance.GenerateReplenishmentAsync(new RecommendationGenerationRequest(warehouseId, 10));
        Assert.True(generated.IsSuccess, generated.FirstError?.Message);
        var proposal = Assert.Single(generated.Value);
        Assert.Equal(RecommendationType.Replenishment, proposal.Type);
        Assert.Equal(RecommendationStatus.Proposed, proposal.Status);
        Assert.True(proposal.ShadowMode);
        Assert.False(proposal.CanMutateInventory);
        Assert.Equal(policy.Value.Id.ToString(CultureInfo.InvariantCulture), proposal.Action.TargetReference);
        var recommendationDbId = await context.GovernedRecommendations.AsNoTracking()
            .Where(value => value.RecommendationId == proposal.RecommendationId)
            .Select(value => value.Id)
            .SingleAsync();
        await ReconcileAndRecordAsync("recommendation-proposed", context, reconciliation, checkpoints, CancellationToken.None);

        var generationReplay = await governance.GenerateReplenishmentAsync(new RecommendationGenerationRequest(warehouseId, 10));
        Assert.True(generationReplay.IsSuccess, generationReplay.FirstError?.Message);
        Assert.Equal(proposal.RecommendationId, Assert.Single(generationReplay.Value).RecommendationId);
        Assert.Equal(1, await context.GovernedRecommendations.AsNoTracking()
            .CountAsync(value => value.RecommendationId == proposal.RecommendationId));
        outcomes.Add("proposal=generation-replay-returned-the-same-persisted-record");
        await ReconcileAndRecordAsync("recommendation-generation-replayed", context, reconciliation, checkpoints, CancellationToken.None);

        var reviewed = await governance.ReviewAsync(
            proposal.RecommendationId,
            new RecommendationLifecycleCommand(
                proposal.Revision,
                "issue-128-review",
                "Reviewed; token=secret-value contact=reviewer@example.test."));
        Assert.True(reviewed.IsSuccess, reviewed.FirstError?.Message);
        Assert.Equal(RecommendationStatus.Reviewed, reviewed.Value.Status);
        await ReconcileAndRecordAsync("recommendation-reviewed", context, reconciliation, checkpoints, CancellationToken.None);

        using var approvalScopeA = provider.CreateScope();
        using var approvalScopeB = provider.CreateScope();
        var approvalServiceA = await CreateSignedInGovernanceServiceAsync(approvalScopeA.ServiceProvider, actor.Id);
        var approvalServiceB = await CreateSignedInGovernanceServiceAsync(approvalScopeB.ServiceProvider, actor.Id);
        var approvalResults = await Task.WhenAll(
            approvalServiceA.Service.ApproveAsync(
                proposal.RecommendationId,
                new RecommendationLifecycleCommand(reviewed.Value.Revision, "issue-128-approve-a")),
            approvalServiceB.Service.ApproveAsync(
                proposal.RecommendationId,
                new RecommendationLifecycleCommand(reviewed.Value.Revision, "issue-128-approve-b")));
        var winningApproval = Assert.Single(approvalResults, value => value.IsSuccess);
        var losingApproval = Assert.Single(approvalResults, value => value.IsFailure);
        Assert.Equal("recommendation.revision_conflict", losingApproval.ErrorCode);
        var approval = winningApproval.Value;
        Assert.Equal(RecommendationStatus.Approved, approval.Status);
        Assert.Equal(1, await context.GovernedRecommendationEvents.AsNoTracking()
            .CountAsync(value => value.RecommendationId == recommendationDbId && value.EventType == "approved"));
        outcomes.Add("approval=concurrent-postgresql-decisions-committed-once");
        await ReconcileAndRecordAsync("recommendation-approved", context, reconciliation, checkpoints, CancellationToken.None);

        var executingService = approvalResults[0].IsSuccess
            ? approvalServiceA.Service
            : approvalServiceB.Service;
        var executed = await executingService.ExecuteAsync(
            proposal.RecommendationId,
            new RecommendationLifecycleCommand(approval.Revision, "issue-128-execute"));
        Assert.True(executed.IsSuccess, executed.FirstError?.Message);
        Assert.Equal(RecommendationStatus.Executed, executed.Value.Status);
        var executionReference = Assert.IsType<string>(executed.Value.ExecutionReference);
        Assert.StartsWith("warehouse-work:", executionReference, StringComparison.Ordinal);
        var workId = int.Parse(
            executionReference["warehouse-work:".Length..],
            CultureInfo.InvariantCulture);
        var workService = services.GetRequiredService<IWarehouseWorkService>();
        var work = await workService.GetAsync(workId);
        Assert.True(work.IsSuccess, work.FirstError?.Message);
        Assert.Equal(WarehouseWorkType.Replenishment, work.Value.Type);
        Assert.Equal(warehouseId, work.Value.WarehouseId);
        Assert.Equal("InventoryReplenishmentPolicy", work.Value.SourceEntityType);
        Assert.Equal(policy.Value.Id.ToString(CultureInfo.InvariantCulture), work.Value.SourceEntityId);
        Assert.Single(await context.WarehouseWorks.AsNoTracking()
            .Where(value => value.SourceEntityType == "InventoryReplenishmentPolicy" &&
                            value.SourceEntityId == policy.Value.Id.ToString(CultureInfo.InvariantCulture))
            .ToArrayAsync());
        Assert.Equal(originalItemQuantity, await SumItemQuantityAsync(context, source.ItemId));
        Assert.Equal(originalReservedQuantity, await SumReservedQuantityAsync(context, source.ItemId));
        await ReconcileAndRecordAsync("recommendation-normal-work-created", context, reconciliation, checkpoints, CancellationToken.None);

        var executeReplay = await executingService.ExecuteAsync(
            proposal.RecommendationId,
            new RecommendationLifecycleCommand(approval.Revision, "issue-128-execute"));
        Assert.True(executeReplay.IsSuccess, executeReplay.FirstError?.Message);
        Assert.Equal(executed.Value.ExecutionReference, executeReplay.Value.ExecutionReference);
        Assert.Single(await context.WarehouseWorks.AsNoTracking()
            .Where(value => value.SourceEntityType == "InventoryReplenishmentPolicy" &&
                            value.SourceEntityId == policy.Value.Id.ToString(CultureInfo.InvariantCulture))
            .ToArrayAsync());
        await ReconcileAndRecordAsync("recommendation-execution-replayed", context, reconciliation, checkpoints, CancellationToken.None);

        var history = await executingService.GetHistoryAsync(proposal.RecommendationId);
        Assert.True(history.IsSuccess, history.FirstError?.Message);
        Assert.Equal(["created", "reviewed", "approved", "executed"], history.Value.Select(value => value.EventType));
        Assert.All(history.Value, value => Assert.Equal(actor.Id, value.ActorUserId));
        Assert.DoesNotContain(history.Value.Select(value => value.Comment),
            comment => comment?.Contains("secret-value", StringComparison.Ordinal) == true ||
                       comment?.Contains("reviewer@example.test", StringComparison.Ordinal) == true);
        using var restartedProvider = DeterministicPostgreSqlDataGenerationFixture.CreateServiceProviderForExistingTarget(
            target,
            services => services.Configure<RecommendationGovernanceOptions>(options =>
            {
                options.Enabled = true;
                options.KillSwitchEnabled = false;
                options.ProviderAvailable = true;
                options.ShadowMode = true;
            }));
        using var restartedScope = restartedProvider.CreateScope();
        var restartedService = await CreateSignedInGovernanceServiceAsync(
            restartedScope.ServiceProvider,
            actor.Id);
        var persistedRecord = await restartedService.Service.GetAsync(proposal.RecommendationId);
        Assert.True(persistedRecord.IsSuccess, persistedRecord.FirstError?.Message);
        Assert.Equal(RecommendationStatus.Executed, persistedRecord.Value.Status);
        Assert.Equal(executionReference, persistedRecord.Value.ExecutionReference);
        var persistedHistory = await restartedService.Service.GetHistoryAsync(proposal.RecommendationId);
        Assert.True(persistedHistory.IsSuccess, persistedHistory.FirstError?.Message);
        Assert.Equal(["created", "reviewed", "approved", "executed"],
            persistedHistory.Value.Select(value => value.EventType));
        var persistedSearch = await restartedService.Service.SearchAsync(new RecommendationQuery(
            WarehouseId: warehouseId,
            Type: RecommendationType.Replenishment,
            Status: RecommendationStatus.Executed));
        Assert.True(persistedSearch.IsSuccess, persistedSearch.FirstError?.Message);
        Assert.Contains(persistedSearch.Value.Items, value => value.RecommendationId == proposal.RecommendationId);
        Assert.Equal(1, await context.UserWarehouseAssignments
            .Where(value => value.UserId == actor.Id && value.WarehouseId == warehouseId)
            .ExecuteDeleteAsync());
        using var outOfScope = restartedProvider.CreateScope();
        var outOfScopeService = await CreateSignedInGovernanceServiceAsync(
            outOfScope.ServiceProvider,
            actor.Id);
        var hiddenSearch = await outOfScopeService.Service.SearchAsync(new RecommendationQuery(
            Type: RecommendationType.Replenishment,
            Status: RecommendationStatus.Executed));
        Assert.True(hiddenSearch.IsSuccess, hiddenSearch.FirstError?.Message);
        Assert.DoesNotContain(hiddenSearch.Value.Items, value => value.RecommendationId == proposal.RecommendationId);
        var deniedRecord = await outOfScopeService.Service.GetAsync(proposal.RecommendationId);
        Assert.True(deniedRecord.IsFailure);
        var deniedWarehouseSearch = await outOfScopeService.Service.SearchAsync(new RecommendationQuery(
            WarehouseId: warehouseId,
            Type: RecommendationType.Replenishment,
            Status: RecommendationStatus.Executed));
        Assert.True(deniedWarehouseSearch.IsFailure);
        outcomes.Add("execution=authorized-normal-work-created-and-response-loss-replay-reused-one-command");
        outcomes.Add("persistence=fresh-service-provider-reloaded-record-history-and-authorized-search");
        outcomes.Add("authorization=warehouse-unassignment-hides-search-and-denies-record-and-warehouse-query");
        outcomes.Add("review-history=persisted-with-sensitive-comment-values-redacted");

        WriteJourneyEvidence(new PostgreSqlJourneyEvidence(
            "governed-replenishment-recommendation-postgresql-approval-execution",
            target.TargetIdentifier,
            outcomes,
            [
                "slotting, workload-priority, exception-resolution, and risk-summary command adapters",
                "enabled recommendation HTTP generation, approval, and execution against PostgreSQL",
                "calibrated historical backtesting and model-quality/drift monitoring"
            ],
            checkpoints,
            new Dictionary<string, decimal>(StringComparer.Ordinal)
            {
                ["itemQuantityBeforeAndAfterWorkCreation"] = originalItemQuantity,
                ["reservedQuantityBeforeAndAfterWorkCreation"] = originalReservedQuantity,
                ["destinationQuantityBeforeWorkCompletion"] = destinationQuantityBefore
            },
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["recommendationIdCount"] = await context.GovernedRecommendations.AsNoTracking()
                    .CountAsync(value => value.RecommendationId == proposal.RecommendationId),
                ["recommendationEventCount"] = await context.GovernedRecommendationEvents.AsNoTracking()
                    .CountAsync(value => value.RecommendationId == recommendationDbId),
                ["normalWorkId"] = workId,
                ["normalWorkLineCount"] = work.Value.Lines.Count
            },
            ReconciliationClean: checkpoints.All(value => value.ExpectedClean == (value.IssueCount == 0)),
            UnexpectedReconciliationIssueCount: checkpoints
                .Where(value => value.ExpectedClean)
                .Sum(value => value.IssueCount),
            ExpectedReconciliationIssueCount: checkpoints
                .Where(value => !value.ExpectedClean)
                .Sum(value => value.IssueCount)));
    }

    private static async Task<(IRecommendationGovernanceService Service, WmsUser Actor)> CreateSignedInGovernanceServiceAsync(
        IServiceProvider services,
        string actorUserId)
    {
        var userManager = services.GetRequiredService<UserManager<WmsUser>>();
        var actor = await userManager.FindByIdAsync(actorUserId);
        Assert.NotNull(actor);
        services.GetRequiredService<DesktopUserSession>().SignIn(actor);
        return (services.GetRequiredService<IRecommendationGovernanceService>(), actor);
    }
}
