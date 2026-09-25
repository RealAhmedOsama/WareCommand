using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.DataGeneration;
using Wms.Application.Integrations;
using Wms.Application.Inventory;
using Wms.Application.Jobs;
using Wms.Application.Performance;
using Wms.Application.SalesOrders;
using Wms.Application.Transfers;
using Wms.Application.WarehouseWork;
using Wms.ASP.Jobs;
using Wms.ASP.Security;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Services;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Jobs;
using Wms.Infrastructure.Tests.Integration;
using Xunit;

namespace Wms.ASP.Tests;

public sealed class PostgreSqlPerformanceQualificationTests
{
    private static readonly JsonSerializerOptions EvidenceJsonOptions = new()
    {
        WriteIndented = true
    };

    [PostgreSqlDashboardFact]
    public async Task RepresentativeAuthenticatedWorkloadRecordsBoundedPostgreSqlEvidence()
    {
        var repeats = ReadBoundedInteger("WARECOMMAND_PERF_REPEATS", fallback: 2, minimum: 2, maximum: 5);
        var samplesPerLevel = ReadBoundedInteger("WARECOMMAND_PERF_SAMPLES", fallback: 40, minimum: 10, maximum: 100);
        var extendedContention = string.Equals(
            Environment.GetEnvironmentVariable("WARECOMMAND_PERF_EXTENDED_CONTENTION"),
            "true",
            StringComparison.OrdinalIgnoreCase);
        var revision = Environment.GetEnvironmentVariable("WARECOMMAND_VERIFICATION_REVISION");
        if (string.IsNullOrWhiteSpace(revision))
        {
            revision = "unrecorded";
        }

        var requestedLevels = new List<int> { 1, 4, 20 };
        var resourceProfile = GetResourceProfile();
        var extendedDecision = extendedContention
            ? EvaluateExtendedContentionSupport(resourceProfile)
            : new ExtendedContentionDecision(false, []);
        if (extendedContention)
        {
            requestedLevels.AddRange(
                extendedDecision.Levels
                    .Where(value => value.Supported)
                    .Select(value => value.Concurrency));
        }

        var startedAtUtc = DateTimeOffset.UtcNow;
        var repeatEvidence = new List<RepeatEvidence>();
        var evaluations = new List<EvaluationEvidence>();
        var partialBatches = new List<RepeatBatchEvidence>();
        var requestLimiterEvidence = new List<RequestLimiterEvidence>();
        Exception? runFailure = null;

        try
        {
            for (var repeat = 1; repeat <= repeats; repeat++)
            {
                var result = await RunRepeatAsync(
                    repeat,
                    samplesPerLevel,
                    requestedLevels,
                    revision,
                    resourceProfile,
                    partialBatches,
                    requestLimiterEvidence,
                    evaluations);
                repeatEvidence.Add(result);
            }
        }
        catch (Exception exception)
        {
            runFailure = exception;
        }

        var variance = BuildRepeatVariance(evaluations);
        var evaluationFailures = evaluations
            .Where(value => value.Evaluation is { Passed: false })
            .Select(value => $"repeat={value.Repeat};scenario={value.Scenario};workload={value.Workload};concurrency={value.Concurrency};breaches={string.Join(',', value.Evaluation!.Breaches)}")
            .ToArray();
        var evidence = new
        {
            schema = "wms-performance-qualification-v1",
            qualificationBoundary = "local PostgreSQL/TestServer evidence only; not production capacity approval",
            startedAtUtc,
            completedAtUtc = DateTimeOffset.UtcNow,
            revision,
            runtime = new
            {
                framework = RuntimeInformation.FrameworkDescription,
                operatingSystem = RuntimeInformation.OSDescription,
                processArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
                resourceProfile
            },
            requestMix = new
            {
                profile = "authenticated-mixed-operations-v1",
                samplesPerConcurrencyLevel = samplesPerLevel,
                concurrencyLevels = requestedLevels,
                operations = new[]
                {
                    "dashboard page and refresh",
                    "inventory page",
                    "receiving scan",
                    "movement report query",
                    "CSV item import",
                    "same-stock internal movement",
                    "sales order allocation and reservation",
                    "pick work completion",
                    "durable inventory reconciliation job",
                    "integration outbox dispatch without external subscribers"
                },
                warmUp = "one authenticated dashboard, inventory, report and receiving route plus one item CSV import per repeat; warm-up samples excluded"
            },
            requestedExtendedContention = extendedContention,
            requestLimiter = new
            {
                expectedTestHostPermitLimitOverride = 100_000,
                observed = requestLimiterEvidence,
                boundary = "rate limiting remains active; isolated test-host buckets exceed this workload's volume; production defaults are unchanged"
            },
            extendedContention = extendedDecision,
            repeatCountRequested = repeats,
            repeats = repeatEvidence.Select(repeat => new
            {
                repeat.Repeat,
                repeat.Dataset,
                repeat.Diagnostics,
                repeat.Reconciliation,
                batches = repeat.Batches.Select(SummarizeBatch).ToArray()
            }).ToArray(),
            partialBatches = partialBatches.Select(value => new
            {
                value.Repeat,
                batch = SummarizeBatch(value.Batch),
                failedSamples = value.Batch.Samples.Where(sample => !sample.Succeeded).ToArray()
            }).ToArray(),
            evaluations,
            repeatVariance = variance,
            evaluationFailures,
            fatalError = runFailure is null
                ? null
                : new { type = runFailure.GetType().Name, message = Redact(runFailure.Message) },
            budgets = PerformanceWorkloadCatalog.Default,
            unbudgetedWorkloads = new[]
            {
                "Transfer/internal movement: no default PerformanceWorkloadCatalog budget is defined.",
                "Pick completion: no default PerformanceWorkloadCatalog budget is defined.",
                "Integration outbox dispatch: no default PerformanceWorkloadCatalog budget is defined."
            }
        };

        WriteEvidence(evidence);

        if (runFailure is not null)
        {
            ExceptionDispatchInfo.Capture(runFailure).Throw();
        }

        Assert.Empty(evaluationFailures);
        Assert.Equal(repeats, repeatEvidence.Count);
        Assert.All(repeatEvidence, repeat => Assert.True(repeat.Reconciliation.IsClean));
    }

    private static async Task<RepeatEvidence> RunRepeatAsync(
        int repeat,
        int samplesPerLevel,
        IReadOnlyList<int> concurrencyLevels,
        string revision,
        ResourceProfile resourceProfile,
        ICollection<RepeatBatchEvidence> partialBatches,
        ICollection<RequestLimiterEvidence> requestLimiterEvidence,
        ICollection<EvaluationEvidence> evaluations)
    {
        var runStartedAtUtc = DateTimeOffset.UtcNow;
        var baseConnectionString = Environment.GetEnvironmentVariable(
            "WARECOMMAND_TEST_POSTGRES_CONNECTION")
            ?? throw new InvalidOperationException("PostgreSQL is required for performance qualification.");
        await using var target = new PostgreSqlTestDatabase();
        await target.InitializeAsync();
        if (!target.IsAvailable)
        {
            throw new InvalidOperationException("The isolated PostgreSQL schema was not created.");
        }

        var dataset = await DeterministicPostgreSqlDataGenerationFixture.WriteWithActorCredentialsAsync(
            target,
            new DataGenerationRequest(
                WmsDataGenerationProfiles.MinimalDevelopment,
                "issue-132-performance-reproducible-seed",
                Environment: "Testing",
                Locale: "en-US"));
        if (!dataset.Report.ReconciliationClean)
        {
            throw new InvalidOperationException(
                $"Repeat {repeat} generated dataset has {dataset.Report.ReconciliationIssueCount} reconciliation issues.");
        }

        using var measurements = new PerformanceMeasurements();
        var process = Process.GetCurrentProcess();
        process.Refresh();
        var processCpuBefore = process.TotalProcessorTime.TotalMilliseconds;
        var workingSetBefore = process.WorkingSet64;
        var privateMemoryBefore = process.PrivateMemorySize64;
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: false);
        var gcBefore = new[]
        {
            GC.CollectionCount(0),
            GC.CollectionCount(1),
            GC.CollectionCount(2)
        };

        var factory = new PostgreSqlDashboardFlowTests.PostgreSqlDashboardApplicationFactory(baseConnectionString);
        var maxPoolSize = Math.Clamp(Math.Max(16, concurrencyLevels.Max() + 8), 16, 128);
        factory.UseExistingSchema(
            target.TestConnectionString,
            "WareCommand.PerformanceQualification",
            pooling: true,
            maximumPoolSize: maxPoolSize);
        factory.RateLimitPermitLimitOverride = 100_000;
        WmsRateLimitingOptions? observedRateLimits = null;
        try
        {
            using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                HandleCookies = true
            });
            observedRateLimits = factory.Services.GetRequiredService<WmsSecurityOptions>().RateLimiting;
            requestLimiterEvidence.Add(new RequestLimiterEvidence(
                repeat,
                observedRateLimits.GlobalPermitLimit,
                observedRateLimits.ReportPermitLimit,
                observedRateLimits.ApiPermitLimit,
                observedRateLimits.ScanningPermitLimit,
                observedRateLimits.ImportPermitLimit));
            Assert.Equal(100_000, observedRateLimits.GlobalPermitLimit);
            Assert.Equal(100_000, observedRateLimits.ReportPermitLimit);
            Assert.Equal(100_000, observedRateLimits.ApiPermitLimit);
            Assert.Equal(100_000, observedRateLimits.ScanningPermitLimit);
            Assert.Equal(100_000, observedRateLimits.ImportPermitLimit);
            using var login = await AuthenticationFlowTests.PostLoginAsync(
                client,
                dataset.ActorCredentials.UserName,
                dataset.ActorCredentials.Password);
            if (login.StatusCode != System.Net.HttpStatusCode.Redirect)
            {
                throw new InvalidOperationException($"Repeat {repeat} generated operator login returned HTTP {(int)login.StatusCode}.");
            }

            var receiveToken = await GetAntiforgeryTokenAsync(client, "/Receiving/Receive");
            var importToken = await GetAntiforgeryTokenAsync(client, "/Items/Import");
            int warehouseId;
            int customerId;
            int stagingLocationId;
            int storageLocationId;
            string receivingLocationCode;
            int receivingLocationId;
            string existingItemSku;
            string itemUnit;
            int loadItemId;
            int capacityItemId;
            string loadSku;
            string capacitySku;
            using (var context = target.CreateContext())
            {
                warehouseId = await context.Warehouses.AsNoTracking()
                    .OrderBy(value => value.Code)
                    .Select(value => value.Id)
                    .FirstAsync();
                customerId = await context.Customers.AsNoTracking()
                    .OrderBy(value => value.Code)
                    .Select(value => value.Id)
                    .FirstAsync();
                var receiving = await context.Locations.AsNoTracking()
                    .Where(value => value.WarehouseId == warehouseId && value.Type == LocationType.Receiving)
                    .OrderBy(value => value.Code)
                    .Select(value => new { value.Id, value.Code })
                    .FirstAsync();
                receivingLocationCode = receiving.Code;
                receivingLocationId = receiving.Id;
                storageLocationId = await context.Locations.AsNoTracking()
                    .Where(value => value.WarehouseId == warehouseId &&
                                    value.Type == LocationType.Storage &&
                                    value.IsActive && value.IsPickable)
                    .OrderBy(value => value.Code)
                    .Select(value => value.Id)
                    .FirstAsync();
                stagingLocationId = await context.Locations.AsNoTracking()
                    .Where(value => value.WarehouseId == warehouseId && value.Type == LocationType.Staging)
                    .OrderBy(value => value.Code)
                    .Select(value => value.Id)
                    .FirstAsync();
                var item = await context.Items.AsNoTracking()
                    .Where(value => !value.RequiresLot && !value.RequiresSerial && !value.RequiresExpiry)
                    .OrderBy(value => value.Sku)
                    .Where(value => value.IsActive && value.PurchaseUnit == value.UnitOfMeasure)
                    .Select(value => new { value.Sku, value.UnitOfMeasure, value.PurchaseUnit })
                    .FirstAsync();
                existingItemSku = item.Sku;
                itemUnit = item.PurchaseUnit ?? item.UnitOfMeasure;
            }

            var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
            loadSku = $"PERF-{suffix}-LOAD";
            capacitySku = $"PERF-{suffix}-CAP";
            var warmupOutcomes = await WarmUpAsync(
                client,
                receiveToken,
                importToken,
                warehouseId,
                receivingLocationCode,
                existingItemSku,
                itemUnit,
                repeat,
                suffix);

            var seededSkuImport = await MeasureHttpAsync(
                "item-import-seed",
                1,
                () => ImportItemsAsync(client, importToken, [loadSku, capacitySku]));
            if (!seededSkuImport.Succeeded)
            {
                throw new InvalidOperationException("The authenticated item import route could not create bounded performance SKUs.");
            }
            using (var context = target.CreateContext())
            {
                loadItemId = await context.Items.AsNoTracking()
                    .Where(value => value.Sku == loadSku)
                    .Select(value => value.Id)
                    .SingleAsync();
                capacityItemId = await context.Items.AsNoTracking()
                    .Where(value => value.Sku == capacitySku)
                    .Select(value => value.Id)
                    .SingleAsync();
            }

            var receivingWorkloadSkus = Enumerable.Range(1, samplesPerLevel)
                .Select(index => $"PERF-{suffix}-HTTP-{index:D4}")
                .ToArray();
            var receivingWorkloadImport = await MeasureHttpAsync(
                "item-import-seed",
                1,
                () => ImportItemsAsync(client, importToken, receivingWorkloadSkus));
            if (!receivingWorkloadImport.Succeeded)
            {
                throw new InvalidOperationException(
                    $"The authenticated item import route could not seed {receivingWorkloadSkus.Length} distinct receiving workload items: " +
                    $"{receivingWorkloadImport.ErrorType}: {receivingWorkloadImport.ErrorMessage}");
            }
            using (var context = target.CreateContext())
            {
                var importedReceivingItems = await context.Items.AsNoTracking()
                    .CountAsync(value => receivingWorkloadSkus.Contains(value.Sku));
                if (importedReceivingItems != receivingWorkloadSkus.Length)
                {
                    throw new InvalidOperationException(
                        $"Authenticated item import returned HTTP {receivingWorkloadImport.StatusCode} but persisted {importedReceivingItems} of " +
                        $"{receivingWorkloadSkus.Length} distinct receiving workload items.");
                }
            }

            var postgresVersion = await ReadPostgreSqlVersionAsync(target.TestConnectionString);
            await using var activityMonitor = new PostgreSqlActivityMonitor(
                target.TestConnectionString,
                "WareCommand.PerformanceQualification");
            activityMonitor.Start();

            var routeSamples = await RunMixedHttpWorkloadAsync(
                client,
                receiveToken,
                importToken,
                warehouseId,
                receivingLocationCode,
                receivingWorkloadSkus,
                repeat,
                samplesPerLevel,
                concurrencyLevels);
            var isolatedRouteBatches = await RunIsolatedHttpWorkloadsAsync(
                client,
                receiveToken,
                importToken,
                warehouseId,
                receivingLocationCode,
                receivingWorkloadSkus,
                repeat,
                samplesPerLevel,
                concurrencyLevels);
            foreach (var batch in routeSamples.Concat(isolatedRouteBatches))
            {
                partialBatches.Add(new RepeatBatchEvidence(repeat, batch));
            }
            foreach (var batch in routeSamples.Concat(isolatedRouteBatches))
            {
                EvaluateBatch(repeat, batch, dataset.Report.LogicalDatasetFingerprint, revision, evaluations);
            }

            var loadSeedQuantity = concurrencyLevels.Sum();
            var loadSeedReceipt = await MeasureHttpAsync(
                "receiving-load-seed",
                1,
                () => ReceiveAsync(client, receiveToken, loadSku, receivingLocationCode, loadSeedQuantity, "EA", $"PERF-{suffix}-LOAD-RECEIPT"));
            if (!loadSeedReceipt.Succeeded)
            {
                throw new InvalidOperationException("The authenticated receiving route could not seed the bounded contention SKUs.");
            }

            int loadSourceInventoryStatusId;
            using (var context = target.CreateContext())
            {
                var sourceStocks = await context.Stock.AsNoTracking()
                    .Where(value => value.ItemId == loadItemId && value.LocationId == receivingLocationId)
                    .Select(value => new { value.QuantityAvailable.Value, value.InventoryStatusId })
                    .ToArrayAsync();
                var receivedQuantity = sourceStocks.Sum(value => value.Value);
                if (receivedQuantity != loadSeedQuantity || sourceStocks.Length != 1)
                {
                    throw new InvalidOperationException(
                        $"Authenticated receiving returned HTTP {loadSeedReceipt.StatusCode} but persisted {receivedQuantity} units in {sourceStocks.Length} source-stock dimensions; expected {loadSeedQuantity} units in one dimension.");
                }

                loadSourceInventoryStatusId = sourceStocks[0].InventoryStatusId;
            }

            IReadOnlyList<MeasurementBatch> movementBatches;
            using (WmsActorContext.Begin(new WmsActor(
                       dataset.ActorCredentials.UserId,
                       dataset.ActorCredentials.UserName,
                       dataset.ActorCredentials.UserName)))
            {
                movementBatches = await RunInternalMovementContentionAsync(
                    factory.Services,
                    dataset.ActorCredentials.UserId,
                    warehouseId,
                    loadItemId,
                    loadSourceInventoryStatusId,
                    loadSeedQuantity,
                    receivingLocationId,
                    storageLocationId,
                    suffix,
                    concurrencyLevels);
            }
            foreach (var batch in movementBatches)
            {
                partialBatches.Add(new RepeatBatchEvidence(repeat, batch));
                EvaluateBatch(repeat, batch, dataset.Report.LogicalDatasetFingerprint, revision, evaluations);
            }
            var failedMovementSamples = movementBatches.SelectMany(batch => batch.Samples)
                .Where(sample => !sample.Succeeded && !sample.Conflict)
                .ToArray();
            var failedRecoverySamples = movementBatches
                .Where(batch => string.Equals(batch.Scenario, "movement-conflict-recovery", StringComparison.Ordinal))
                .SelectMany(batch => batch.Samples)
                .Where(sample => !sample.Succeeded)
                .ToArray();
            if (failedMovementSamples.Length > 0 || failedRecoverySamples.Length > 0)
            {
                var summary = string.Join(
                    "; ",
                    failedMovementSamples.Concat(failedRecoverySamples).Select(sample =>
                        $"{sample.ErrorType ?? "unknown"}: {sample.ErrorMessage ?? "no error detail"}"));
                throw new InvalidOperationException(
                    $"Same-stock internal movement had {failedMovementSamples.Length} non-conflict failures and {failedRecoverySamples.Length} unrecovered contention outcomes. {summary}");
            }

            IReadOnlyList<MeasurementBatch> allocationAndPickBatches;
            using (WmsActorContext.Begin(new WmsActor(
                       dataset.ActorCredentials.UserId,
                       dataset.ActorCredentials.UserName,
                       dataset.ActorCredentials.UserName)))
            {
                allocationAndPickBatches = await RunAllocationAndPickingContentionAsync(
                    factory.Services,
                    target,
                    dataset.ActorCredentials.UserId,
                    warehouseId,
                    customerId,
                    loadSku,
                    suffix,
                    concurrencyLevels,
                    stagingLocationId,
                    repeat,
                    partialBatches);
            }
            foreach (var batch in allocationAndPickBatches)
            {
                EvaluateBatch(repeat, batch, dataset.Report.LogicalDatasetFingerprint, revision, evaluations);
            }

            LimitedStockEvidence capacityEvidence;
            using (WmsActorContext.Begin(new WmsActor(
                       dataset.ActorCredentials.UserId,
                       dataset.ActorCredentials.UserName,
                       dataset.ActorCredentials.UserName)))
            {
                capacityEvidence = await RunLimitedStockContentionAsync(
                    factory.Services,
                    client,
                    receiveToken,
                    receivingLocationCode,
                    target,
                    dataset.ActorCredentials.UserId,
                    warehouseId,
                    customerId,
                    capacitySku,
                    capacityItemId,
                    suffix,
                    concurrencyLevels.Max(),
                    receivingLocationId,
                    storageLocationId,
                    stagingLocationId,
                    repeat,
                    partialBatches);
            }
            foreach (var batch in capacityEvidence.MovementBatches)
            {
                EvaluateBatch(repeat, batch, dataset.Report.LogicalDatasetFingerprint, revision, evaluations);
            }
            foreach (var batch in capacityEvidence.AllocationBatches)
            {
                EvaluateBatch(repeat, batch, dataset.Report.LogicalDatasetFingerprint, revision, evaluations);
            }
            foreach (var batch in capacityEvidence.PickBatches)
            {
                EvaluateBatch(repeat, batch, dataset.Report.LogicalDatasetFingerprint, revision, evaluations);
            }

            WorkerEvidence workerEvidence;
            using (WmsActorContext.Begin(new WmsActor(
                       dataset.ActorCredentials.UserId,
                       dataset.ActorCredentials.UserName,
                       dataset.ActorCredentials.UserName)))
            {
                workerEvidence = await RunBackgroundAndOutboxWorkloadAsync(
                    factory.Services,
                    target,
                    warehouseId,
                    dataset.ActorCredentials.UserId,
                    repeat);
            }
            EvaluateBatch(repeat, workerEvidence.JobBatch, dataset.Report.LogicalDatasetFingerprint, revision, evaluations);

            var reconciliationStartedAt = DateTimeOffset.UtcNow;
            var reconciliationWatch = Stopwatch.StartNew();
            InventoryReconciliationReportDto reconciliation;
            using (WmsActorContext.Begin(new WmsActor(
                       dataset.ActorCredentials.UserId,
                       dataset.ActorCredentials.UserName,
                       dataset.ActorCredentials.UserName)))
            {
                using (var scope = factory.Services.CreateScope())
                {
                    var service = scope.ServiceProvider.GetRequiredService<IInventoryReconciliationService>();
                    var result = await service.ReconcileAsync(new InventoryReconciliationQuery(Deep: true));
                    if (result.IsFailure)
                    {
                        throw new InvalidOperationException($"Repeat {repeat} deep reconciliation failed: {result.Error}");
                    }
                    reconciliation = result.Value;
                }
            }
            reconciliationWatch.Stop();
            var reconciliationCompletedAt = DateTimeOffset.UtcNow;
            var reconciliationBatch = new MeasurementBatch(
                "deep-reconciliation",
                "reconciliation",
                1,
                reconciliationWatch.Elapsed.TotalMilliseconds,
                [new PerformanceSample("reconciliation", "deep-reconciliation", 1, reconciliationWatch.Elapsed.TotalMilliseconds, reconciliationWatch.Elapsed.TotalMilliseconds, true, false, null, null, null)],
                reconciliationStartedAt,
                reconciliationCompletedAt);
            EvaluateBatch(repeat, reconciliationBatch, dataset.Report.LogicalDatasetFingerprint, revision, evaluations, reconciliation.IssueCount);
            var plan = await ExplainInventoryBalanceQueryAsync(target, warehouseId, loadItemId);
            var finalCounts = await ReadFinalCountsAsync(target);
            var poolAndLockEvidence = await activityMonitor.StopAsync();
            if (poolAndLockEvidence.SampleCount == 0 || poolAndLockEvidence.Error is not null)
            {
                throw new InvalidOperationException("PostgreSQL connection/lock monitoring produced no usable samples.");
            }
            process.Refresh();
            var processCpuAfter = process.TotalProcessorTime.TotalMilliseconds;
            var gcAfter = new[]
            {
                GC.CollectionCount(0),
                GC.CollectionCount(1),
                GC.CollectionCount(2)
            };
            var runEvidence = new RepeatEvidence(
                repeat,
                dataset.Report,
                new
                {
                    postgresVersion,
                    applicationName = "WareCommand.PerformanceQualification",
                    maxPoolSize,
                    schema = target.TargetIdentifier,
                    databaseResourceLimits = resourceProfile.PostgreSqlContainer,
                    elapsedRunSeconds = Math.Round((DateTimeOffset.UtcNow - runStartedAtUtc).TotalSeconds, 3),
                    warmupOutcomes,
                    sampleCountPerConcurrencyLevel = samplesPerLevel,
                    concurrencyLevels,
                    requestMix = new Dictionary<string, int>
                    {
                        ["dashboard"] = samplesPerLevel / 5,
                        ["inventory"] = samplesPerLevel / 5,
                        ["report"] = samplesPerLevel / 5,
                        ["receiving"] = samplesPerLevel / 5,
                        ["itemImport"] = samplesPerLevel - 4 * (samplesPerLevel / 5)
                    },
                    capacityContention = capacityEvidence,
                    workerAndOutbox = workerEvidence,
                    process = new
                    {
                        cpuMilliseconds = Math.Round(processCpuAfter - processCpuBefore, 3),
                        workingSetBytesBefore = workingSetBefore,
                        workingSetBytesAfter = process.WorkingSet64,
                        privateMemoryBytesBefore = privateMemoryBefore,
                        privateMemoryBytesAfter = process.PrivateMemorySize64,
                        allocatedBytes = GC.GetTotalAllocatedBytes(precise: false) - allocatedBefore,
                        gcCollections = new
                        {
                            generation0 = gcAfter[0] - gcBefore[0],
                            generation1 = gcAfter[1] - gcBefore[1],
                            generation2 = gcAfter[2] - gcBefore[2]
                        }
                    },
                    telemetry = measurements.Snapshot(),
                    postgresActivityAndLocks = poolAndLockEvidence,
                    representativeInventoryQueryPlan = plan,
                    finalCounts
                },
                reconciliation,
                routeSamples.Concat(isolatedRouteBatches).Concat(movementBatches).Concat(allocationAndPickBatches)
                    .Concat(capacityEvidence.MovementBatches)
                    .Concat(capacityEvidence.AllocationBatches)
                .Concat(capacityEvidence.PickBatches)
                    .Append(workerEvidence.JobBatch)
                    .Append(reconciliationBatch)
                    .ToArray());
            return runEvidence;
        }
        finally
        {
            await factory.DisposeDatabaseAsync();
            factory.Dispose();
            NpgsqlConnection.ClearAllPools();
        }
    }

    private static async Task<IReadOnlyList<string>> WarmUpAsync(
        HttpClient client,
        string receiveToken,
        string importToken,
        int warehouseId,
        string receivingLocationCode,
        string itemSku,
        string unit,
        int repeat,
        string suffix)
    {
        var outcomes = new List<string>();
        foreach (var path in new[]
                 {
                     "/Dashboard",
                     $"/Dashboard/RefreshData?warehouseId={warehouseId}",
                     "/Inventory?showSummary=true",
                     $"/Reports?warehouseId={warehouseId}&Page=1&PageSize=50"
                 })
        {
            using var response = await client.GetAsync(path);
            outcomes.Add($"warmup-get:{path}:{(int)response.StatusCode}");
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Authenticated warm-up route '{path}' returned HTTP {(int)response.StatusCode}.");
            }
        }

        var receive = await MeasureHttpAsync(
            "warmup-receiving",
            1,
            () => ReceiveAsync(client, receiveToken, itemSku, receivingLocationCode, 1m, unit, $"PERF-WARM-{repeat}-{suffix}"));
        outcomes.Add($"warmup-receiving:{receive.StatusCode}");
        if (!receive.Succeeded)
        {
            throw new InvalidOperationException(
                $"Authenticated receiving warm-up failed with {receive.ErrorType}: {receive.ErrorMessage ?? "no response detail"}.");
        }

        var import = await MeasureHttpAsync(
            "warmup-item-import",
            1,
            () => ImportItemsAsync(client, importToken, [$"PERF-{suffix}-WARM"]));
        outcomes.Add($"warmup-item-import:{import.StatusCode}");
        if (!import.Succeeded)
        {
            throw new InvalidOperationException("Authenticated item import warm-up failed.");
        }

        return outcomes;
    }

    private static async Task<IReadOnlyList<MeasurementBatch>> RunMixedHttpWorkloadAsync(
        HttpClient client,
        string receiveToken,
        string importToken,
        int warehouseId,
        string receivingLocationCode,
        string[] receivingItemSkus,
        int repeat,
        int samplesPerLevel,
        IReadOnlyList<int> concurrencyLevels)
    {
        var batches = new List<MeasurementBatch>();
        foreach (var concurrency in concurrencyLevels)
        {
            var samples = new ConcurrentBag<PerformanceSample>();
            var startedAt = DateTimeOffset.UtcNow;
            var stopwatch = Stopwatch.StartNew();
            await Parallel.ForEachAsync(
                Enumerable.Range(0, samplesPerLevel),
                new ParallelOptions { MaxDegreeOfParallelism = concurrency },
                async (index, cancellationToken) =>
                {
                    var sampleNumber = repeat * 1_000_000 + concurrency * 10_000 + index;
                    var sample = (index % 5) switch
                    {
                        0 => await MeasureHttpAsync(
                            "dashboard",
                            concurrency,
                            () => client.GetAsync("/Dashboard", cancellationToken)),
                        1 => await MeasureHttpAsync(
                            "inventory",
                            concurrency,
                            () => client.GetAsync("/Inventory?showSummary=true", cancellationToken)),
                        2 => await MeasureHttpAsync(
                            "report",
                            concurrency,
                            () => client.GetAsync($"/Reports?warehouseId={warehouseId}&Page=1&PageSize=50", cancellationToken)),
                        3 => await MeasureHttpAsync(
                            "receiving",
                            concurrency,
                            () => ReceiveAsync(
                                client,
                                receiveToken,
                                receivingItemSkus[index % receivingItemSkus.Length],
                                receivingLocationCode,
                                1m,
                                "EA",
                                $"PERF-MIX-{repeat}-{concurrency}-{sampleNumber}")),
                        _ => await MeasureHttpAsync(
                            "item-import",
                            concurrency,
                            () => ImportItemsAsync(
                                client,
                                importToken,
                                [$"PERF-{repeat}-{concurrency}-{index:D4}"]))
                    };
                    samples.Add(sample);
                });
            stopwatch.Stop();
            var completedAt = DateTimeOffset.UtcNow;
            var normalized = samples.Select(sample => sample with
            {
                BatchDurationMilliseconds = stopwatch.Elapsed.TotalMilliseconds
            }).ToArray();
            batches.Add(new MeasurementBatch(
                "authenticated-mixed-http",
                "mixed-http",
                concurrency,
                stopwatch.Elapsed.TotalMilliseconds,
                normalized,
                startedAt,
                completedAt));
        }

        return batches;
    }

    private static async Task<IReadOnlyList<MeasurementBatch>> RunIsolatedHttpWorkloadsAsync(
        HttpClient client,
        string receiveToken,
        string importToken,
        int warehouseId,
        string receivingLocationCode,
        string[] receivingItemSkus,
        int repeat,
        int samplesPerWorkload,
        IReadOnlyList<int> concurrencyLevels)
    {
        var batches = new List<MeasurementBatch>();
        foreach (var concurrency in concurrencyLevels)
        {
            foreach (var workload in new[] { "dashboard", "inventory", "report", "receiving", "item-import" })
            {
                var samples = new ConcurrentBag<PerformanceSample>();
                var startedAt = DateTimeOffset.UtcNow;
                var stopwatch = Stopwatch.StartNew();
                await Parallel.ForEachAsync(
                    Enumerable.Range(0, samplesPerWorkload),
                    new ParallelOptions { MaxDegreeOfParallelism = concurrency },
                    async (index, cancellationToken) =>
                    {
                        var uniqueToken = Guid.NewGuid().ToString("N")[..8];
                        var uniqueId = $"{repeat}-{concurrency}-{index:D4}-{uniqueToken}";
                        var sample = workload switch
                        {
                            "dashboard" => await MeasureHttpAsync(
                                workload,
                                concurrency,
                                () => client.GetAsync("/Dashboard", cancellationToken)),
                            "inventory" => await MeasureHttpAsync(
                                workload,
                                concurrency,
                                () => client.GetAsync("/Inventory?showSummary=true", cancellationToken)),
                            "report" => await MeasureHttpAsync(
                                workload,
                                concurrency,
                                () => client.GetAsync($"/Reports?warehouseId={warehouseId}&Page=1&PageSize=50", cancellationToken)),
                            "receiving" => await MeasureHttpAsync(
                                workload,
                                concurrency,
                                () => ReceiveAsync(
                                    client,
                                    receiveToken,
                                    receivingItemSkus[index],
                                    receivingLocationCode,
                                    1m,
                                    "EA",
                                    $"PERF-ISO-{uniqueId}")),
                            _ => await MeasureHttpAsync(
                                workload,
                                concurrency,
                                () => ImportItemsAsync(
                                    client,
                                    importToken,
                                    [$"PERF-ISO-{uniqueId}"]))
                        };
                        samples.Add(sample);
                    });
                stopwatch.Stop();
                var completedAt = DateTimeOffset.UtcNow;
                batches.Add(new MeasurementBatch(
                    $"isolated-{workload}-{concurrency}",
                    "isolated-http",
                    concurrency,
                    stopwatch.Elapsed.TotalMilliseconds,
                    samples.Select(sample => sample with
                    {
                        BatchDurationMilliseconds = stopwatch.Elapsed.TotalMilliseconds
                    }).ToArray(),
                    startedAt,
                    completedAt));
            }
        }

        return batches;
    }

    private static async Task<IReadOnlyList<MeasurementBatch>> RunInternalMovementContentionAsync(
        IServiceProvider services,
        string actorUserId,
        int warehouseId,
        int itemId,
        int sourceInventoryStatusId,
        int totalQuantity,
        int sourceLocationId,
        int destinationLocationId,
        string suffix,
        IReadOnlyList<int> concurrencyLevels)
    {
        var batches = new List<MeasurementBatch>();
        var sequence = 0;
        foreach (var concurrency in concurrencyLevels)
        {
            var samples = new ConcurrentBag<PerformanceSample>();
            var startedAt = DateTimeOffset.UtcNow;
            var stopwatch = Stopwatch.StartNew();
            await Parallel.ForEachAsync(
                Enumerable.Range(0, concurrency),
                new ParallelOptions { MaxDegreeOfParallelism = concurrency },
                async (_, cancellationToken) =>
                {
                    var operation = Interlocked.Increment(ref sequence);
                    var elapsed = Stopwatch.StartNew();
                    var succeeded = false;
                    var conflict = false;
                    string? errorType = null;
                    string? errorMessage = null;
                    try
                    {
                        await using var scope = services.CreateAsyncScope();
                        var transferService = scope.ServiceProvider.GetRequiredService<ITransferService>();
                        var result = await transferService.MoveAsync(
                            new InternalMovementInput(
                                warehouseId,
                                itemId,
                                1m,
                                "EA",
                                sourceLocationId,
                                destinationLocationId,
                                $"perf-{suffix}-move-{operation}",
                                InventoryStatusId: sourceInventoryStatusId),
                            actorUserId,
                            cancellationToken);
                        succeeded = result.IsSuccess;
                        if (result.IsFailure)
                        {
                            var failure = result.FirstError;
                            errorType = failure is null
                                ? "internal-movement-failure"
                                : $"{failure.Type}:{failure.Code}";
                            errorMessage = Redact(failure?.Message ?? result.Error);
                            conflict = failure?.Type is Wms.Application.Common.ErrorType.Conflict or Wms.Application.Common.ErrorType.Concurrency ||
                                failure?.Code.Contains("conflict", StringComparison.OrdinalIgnoreCase) == true;
                        }
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        errorType = exception.GetType().Name;
                        errorMessage = Redact(exception.Message);
                    }
                    finally
                    {
                        elapsed.Stop();
                    }

                    samples.Add(new PerformanceSample(
                        "internal-movement",
                        "same-stock-movement",
                        concurrency,
                        elapsed.Elapsed.TotalMilliseconds,
                        0,
                        succeeded,
                        conflict,
                        null,
                        errorType,
                        errorMessage));
                });
            stopwatch.Stop();
            var completedAt = DateTimeOffset.UtcNow;
            var normalized = samples.Select(sample => sample with
            {
                BatchDurationMilliseconds = stopwatch.Elapsed.TotalMilliseconds
            }).ToArray();
            batches.Add(new MeasurementBatch(
                $"same-stock-internal-movement-{concurrency}",
                "same-stock-movement",
                concurrency,
                stopwatch.Elapsed.TotalMilliseconds,
                normalized,
                startedAt,
                completedAt));

            var failedNonConflicts = normalized.Where(sample => !sample.Succeeded && !sample.Conflict).ToArray();
            if (failedNonConflicts.Length > 0)
            {
                continue;
            }

            var conflicts = normalized.Where(sample => !sample.Succeeded && sample.Conflict).ToArray();
            if (conflicts.Length == 0)
            {
                continue;
            }

            var recoverySamples = new List<PerformanceSample>(conflicts.Length);
            var recoveryStartedAt = DateTimeOffset.UtcNow;
            var recoveryWatch = Stopwatch.StartNew();
            for (var retry = 0; retry < conflicts.Length; retry++)
            {
                var elapsed = Stopwatch.StartNew();
                var succeeded = false;
                var conflict = false;
                string? errorType = null;
                string? errorMessage = null;
                try
                {
                    await using var scope = services.CreateAsyncScope();
                    var transferService = scope.ServiceProvider.GetRequiredService<ITransferService>();
                    var result = await transferService.MoveAsync(
                        new InternalMovementInput(
                            warehouseId,
                            itemId,
                            1m,
                            "EA",
                            sourceLocationId,
                            destinationLocationId,
                            $"perf-{suffix}-move-recovery-{concurrency}-{retry + 1}",
                            InventoryStatusId: sourceInventoryStatusId),
                        actorUserId);
                    succeeded = result.IsSuccess;
                    if (result.IsFailure)
                    {
                        var failure = result.FirstError;
                        errorType = failure is null
                            ? "internal-movement-recovery-failure"
                            : $"{failure.Type}:{failure.Code}";
                        errorMessage = Redact(failure?.Message ?? result.Error);
                        conflict = failure?.Type is Wms.Application.Common.ErrorType.Conflict or Wms.Application.Common.ErrorType.Concurrency ||
                            failure?.Code.Contains("conflict", StringComparison.OrdinalIgnoreCase) == true;
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    errorType = exception.GetType().Name;
                    errorMessage = Redact(exception.Message);
                }
                finally
                {
                    elapsed.Stop();
                }

                recoverySamples.Add(new PerformanceSample(
                    "internal-movement",
                    "movement-conflict-recovery",
                    1,
                    elapsed.Elapsed.TotalMilliseconds,
                    0,
                    succeeded,
                    conflict,
                    null,
                    errorType,
                    errorMessage));
            }
            recoveryWatch.Stop();
            var recoveryCompletedAt = DateTimeOffset.UtcNow;
            batches.Add(new MeasurementBatch(
                $"same-stock-movement-recovery-{concurrency}",
                "movement-conflict-recovery",
                1,
                recoveryWatch.Elapsed.TotalMilliseconds,
                recoverySamples.Select(sample => sample with
                {
                    BatchDurationMilliseconds = recoveryWatch.Elapsed.TotalMilliseconds
                }).ToArray(),
                recoveryStartedAt,
                recoveryCompletedAt));
        }

        if (sequence != totalQuantity)
        {
            throw new InvalidOperationException($"Movement workload executed {sequence} operations for {totalQuantity} received units.");
        }

        return batches;
    }

    private static async Task<IReadOnlyList<MeasurementBatch>> RunAllocationAndPickingContentionAsync(
        IServiceProvider services,
        PostgreSqlTestDatabase target,
        string actorUserId,
        int warehouseId,
        int customerId,
        string itemSku,
        string suffix,
        IReadOnlyList<int> concurrencyLevels,
        int stagingLocationId,
        int repeat,
        ICollection<RepeatBatchEvidence> partialBatches)
    {
        var batches = new List<MeasurementBatch>();
        var orderSequence = 0;
        foreach (var concurrency in concurrencyLevels)
        {
            var orderRecords = new List<OrderWorkRecord>();
            var allocationConflicts = new ConcurrentBag<OrderWorkRecord>();
            for (var index = 0; index < concurrency; index++)
            {
                await using var scope = services.CreateAsyncScope();
                var salesOrders = scope.ServiceProvider.GetRequiredService<ISalesOrderService>();
                var created = await salesOrders.CreateAsync(
                    new SalesOrderInput(
                        warehouseId,
                        customerId,
                        OrderDate: new DateOnly(2026, 1, 15),
                        RequestedShipDate: new DateOnly(2026, 1, 16),
                        ExternalReference: $"PERF-{suffix}-SO-{++orderSequence:D4}",
                        SourceType: "PERF-QUALIFICATION",
                        AllowPartialShipment: true,
                        Lines: [new SalesOrderLineInput(itemSku, 1m, "EA")]),
                    actorUserId);
                if (created.IsFailure)
                {
                    throw new InvalidOperationException($"Performance sales order creation failed: {created.Error}");
                }
                var confirmed = await salesOrders.ConfirmAsync(created.Value.Id, actorUserId);
                if (confirmed.IsFailure)
                {
                    throw new InvalidOperationException($"Performance sales order confirmation failed: {confirmed.Error}");
                }
                orderRecords.Add(new OrderWorkRecord(
                    confirmed.Value.Id,
                    confirmed.Value.DocumentNumber,
                    $"perf-{suffix}-allocate-{orderSequence}"));
            }

            var allocationSamples = new ConcurrentBag<PerformanceSample>();
            var allocationResults = new ConcurrentBag<AllocationWorkRecord>();
            var allocationStartedAt = DateTimeOffset.UtcNow;
            var allocationWatch = Stopwatch.StartNew();
            await Parallel.ForEachAsync(
                orderRecords,
                new ParallelOptions { MaxDegreeOfParallelism = concurrency },
                async (order, cancellationToken) =>
                {
                    var elapsed = Stopwatch.StartNew();
                    var succeeded = false;
                    var conflict = false;
                    string? error = null;
                    AllocationWorkRecord? allocationRecord = null;
                    try
                    {
                        await using var scope = services.CreateAsyncScope();
                        var allocation = scope.ServiceProvider.GetRequiredService<ISalesOrderAllocationService>();
                        var command = new SalesOrderAllocationCommand(
                            ReleaseToWarehouse: true,
                            IdempotencyKey: order.AllocationKey);
                        var result = await allocation.AllocateAsync(
                            order.SalesOrderId,
                            command,
                            actorUserId,
                            cancellationToken);
                        succeeded = result.IsSuccess;
                        if (result.IsSuccess)
                        {
                            var line = Assert.Single(result.Value.Lines);
                            allocationRecord = new AllocationWorkRecord(
                                order.SalesOrderId,
                                order.SalesOrderNumber,
                                command,
                                result.Value.AllocatedBaseQuantity,
                                result.Value.BackorderBaseQuantity,
                                result.Value.Work.Select(value => value.WorkId).ToArray());
                        }
                        else
                        {
                            var failure = result.FirstError;
                            error = Redact(failure?.Message ?? result.Error);
                            conflict = failure?.Type is Wms.Application.Common.ErrorType.Conflict or Wms.Application.Common.ErrorType.Concurrency ||
                                failure?.Code.Contains("conflict", StringComparison.OrdinalIgnoreCase) == true;
                            if (conflict)
                            {
                                allocationConflicts.Add(order);
                            }
                        }
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        error = exception.GetType().Name + ": " + Redact(exception.Message);
                    }
                    finally
                    {
                        elapsed.Stop();
                    }

                    if (allocationRecord is not null)
                    {
                        allocationResults.Add(allocationRecord);
                    }
                    allocationSamples.Add(new PerformanceSample(
                        "allocation",
                        "same-stock-allocation",
                        concurrency,
                        elapsed.Elapsed.TotalMilliseconds,
                        0,
                        succeeded,
                        conflict,
                        null,
                        error,
                        null));
                });
            allocationWatch.Stop();
            var allocationCompletedAt = DateTimeOffset.UtcNow;
            var allocationBatch = new MeasurementBatch(
                $"same-stock-allocation-{concurrency}",
                "same-stock-allocation",
                concurrency,
                allocationWatch.Elapsed.TotalMilliseconds,
                allocationSamples.Select(sample => sample with
                {
                    BatchDurationMilliseconds = allocationWatch.Elapsed.TotalMilliseconds
                }).ToArray(),
                allocationStartedAt,
                allocationCompletedAt);
            batches.Add(allocationBatch);
            partialBatches.Add(new RepeatBatchEvidence(repeat, allocationBatch));

            if (!allocationConflicts.IsEmpty)
            {
                var recoverySamples = new List<PerformanceSample>(allocationConflicts.Count);
                var recoveryStartedAt = DateTimeOffset.UtcNow;
                var recoveryWatch = Stopwatch.StartNew();
                foreach (var order in allocationConflicts.OrderBy(value => value.SalesOrderId))
                {
                    var elapsed = Stopwatch.StartNew();
                    var succeeded = false;
                    var conflict = false;
                    string? error = null;
                    AllocationWorkRecord? allocationRecord = null;
                    try
                    {
                        await using var scope = services.CreateAsyncScope();
                        var allocation = scope.ServiceProvider.GetRequiredService<ISalesOrderAllocationService>();
                        var command = new SalesOrderAllocationCommand(
                            ReleaseToWarehouse: true,
                            IdempotencyKey: order.AllocationKey);
                        var result = await allocation.AllocateAsync(
                            order.SalesOrderId,
                            command,
                            actorUserId);
                        succeeded = result.IsSuccess;
                        if (result.IsSuccess)
                        {
                            _ = Assert.Single(result.Value.Lines);
                            allocationRecord = new AllocationWorkRecord(
                                order.SalesOrderId,
                                order.SalesOrderNumber,
                                command,
                                result.Value.AllocatedBaseQuantity,
                                result.Value.BackorderBaseQuantity,
                                result.Value.Work.Select(value => value.WorkId).ToArray());
                        }
                        else
                        {
                            var failure = result.FirstError;
                            error = Redact(failure?.Message ?? result.Error);
                            conflict = failure?.Type is Wms.Application.Common.ErrorType.Conflict or Wms.Application.Common.ErrorType.Concurrency ||
                                failure?.Code.Contains("conflict", StringComparison.OrdinalIgnoreCase) == true;
                        }
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        error = exception.GetType().Name + ": " + Redact(exception.Message);
                    }
                    finally
                    {
                        elapsed.Stop();
                    }

                    if (allocationRecord is not null)
                    {
                        allocationResults.Add(allocationRecord);
                    }
                    recoverySamples.Add(new PerformanceSample(
                        "allocation",
                        "allocation-conflict-recovery",
                        1,
                        elapsed.Elapsed.TotalMilliseconds,
                        0,
                        succeeded,
                        conflict,
                        null,
                        error,
                        null));
                }

                recoveryWatch.Stop();
                var recoveryCompletedAt = DateTimeOffset.UtcNow;
                var recoveryBatch = new MeasurementBatch(
                    $"same-stock-allocation-recovery-{concurrency}",
                    "allocation-conflict-recovery",
                    1,
                    recoveryWatch.Elapsed.TotalMilliseconds,
                    recoverySamples.Select(sample => sample with
                    {
                        BatchDurationMilliseconds = recoveryWatch.Elapsed.TotalMilliseconds
                    }).ToArray(),
                    recoveryStartedAt,
                    recoveryCompletedAt);
                batches.Add(recoveryBatch);
                partialBatches.Add(new RepeatBatchEvidence(repeat, recoveryBatch));
            }

            var expectedAllocated = concurrency;
            if (allocationResults.Count != concurrency ||
                allocationResults.Sum(value => value.AllocatedQuantity) != expectedAllocated ||
                allocationResults.Sum(value => value.BackorderQuantity) != 0m ||
                allocationResults.Any(value => value.WorkIds.Length != 1))
            {
                throw new InvalidOperationException(
                    $"Same-stock allocation at concurrency {concurrency} did not allocate and release every backed order once. " +
                    $"allocatedOrders={allocationResults.Count}; allocatedQuantity={allocationResults.Sum(value => value.AllocatedQuantity)}; " +
                    $"backorderQuantity={allocationResults.Sum(value => value.BackorderQuantity)}; conflicts={allocationConflicts.Count}; failures=" +
                    string.Join(" | ", allocationSamples.Where(sample => !sample.Succeeded)
                        .Select(sample => $"{sample.ErrorType ?? "unknown"}: {sample.ErrorMessage ?? "no detail"}")
                        .Concat(batches.Where(batch => batch.Scenario == "allocation-conflict-recovery")
                            .SelectMany(batch => batch.Samples)
                            .Where(sample => !sample.Succeeded)
                            .Select(sample => $"recovery {sample.ErrorType ?? "unknown"}: {sample.ErrorMessage ?? "no detail"}"))
                        .Take(6)));
            }

            using (var context = target.CreateContext())
            {
                var itemId = await context.Items.AsNoTracking()
                    .Where(value => value.Sku == itemSku)
                    .Select(value => value.Id)
                    .SingleAsync();
                var allocated = allocationResults.Sum(value => value.AllocatedQuantity);
                var reserved = await context.InventoryBalances.AsNoTracking()
                    .Where(value => value.WarehouseId == warehouseId && value.ItemId == itemId)
                    .SumAsync(value => value.ReservedQuantity);
                if (reserved != allocated)
                {
                    throw new InvalidOperationException(
                        $"Same-stock allocation reserved {reserved} units for {allocated} allocated units.");
                }
            }

            var pickSamples = new ConcurrentBag<PerformanceSample>();
            var pickConflicts = new ConcurrentBag<AllocationWorkRecord>();
            var pickStartedAt = DateTimeOffset.UtcNow;
            var pickWatch = Stopwatch.StartNew();
            await Parallel.ForEachAsync(
                allocationResults,
                new ParallelOptions { MaxDegreeOfParallelism = concurrency },
                async (allocationRecord, cancellationToken) =>
                {
                    var elapsed = Stopwatch.StartNew();
                    var succeeded = false;
                    var conflict = false;
                    string? errorType = null;
                    string? error = null;
                    try
                    {
                        var completion = await CompleteAllocationWorkAsync(
                            services,
                            allocationRecord,
                            actorUserId,
                            stagingLocationId,
                            cancellationToken);
                        succeeded = completion.IsSuccess;
                        if (completion.IsFailure)
                        {
                            var failure = completion.FirstError;
                            errorType = failure is null ? "pick-completion-failure" : $"{failure.Type}:{failure.Code}";
                            error = Redact(failure?.Message ?? completion.Error);
                            conflict = failure?.Type is Wms.Application.Common.ErrorType.Conflict or Wms.Application.Common.ErrorType.Concurrency ||
                                failure?.Code.Contains("conflict", StringComparison.OrdinalIgnoreCase) == true;
                            if (conflict)
                            {
                                pickConflicts.Add(allocationRecord);
                            }
                        }
                    }
                    catch (ConcurrencyConflictException exception)
                    {
                        conflict = true;
                        errorType = exception.Code;
                        error = Redact(exception.Message);
                        pickConflicts.Add(allocationRecord);
                    }
                    catch (DbUpdateConcurrencyException exception)
                    {
                        conflict = true;
                        errorType = "data.concurrency_conflict";
                        error = Redact(exception.Message);
                        pickConflicts.Add(allocationRecord);
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        errorType = exception.GetType().Name;
                        error = exception.GetType().Name + ": " + Redact(exception.Message);
                    }
                    finally
                    {
                        elapsed.Stop();
                    }

                    pickSamples.Add(new PerformanceSample(
                        "pick-completion",
                        "same-stock-pick",
                        concurrency,
                        elapsed.Elapsed.TotalMilliseconds,
                        0,
                        succeeded,
                        conflict,
                        null,
                        errorType,
                        error));
                });
            pickWatch.Stop();
            var pickCompletedAt = DateTimeOffset.UtcNow;
            batches.Add(new MeasurementBatch(
                $"same-stock-pick-{concurrency}",
                "same-stock-pick",
                concurrency,
                pickWatch.Elapsed.TotalMilliseconds,
                pickSamples.Select(sample => sample with
                {
                    BatchDurationMilliseconds = pickWatch.Elapsed.TotalMilliseconds
                }).ToArray(),
                pickStartedAt,
                pickCompletedAt));
            partialBatches.Add(new RepeatBatchEvidence(repeat, batches[^1]));

            if (!pickConflicts.IsEmpty)
            {
                var recoverySamples = new List<PerformanceSample>(pickConflicts.Count);
                var recoveryStartedAt = DateTimeOffset.UtcNow;
                var recoveryWatch = Stopwatch.StartNew();
                foreach (var allocationRecord in pickConflicts.OrderBy(value => value.SalesOrderId))
                {
                    var elapsed = Stopwatch.StartNew();
                    var succeeded = false;
                    var conflict = false;
                    string? errorType = null;
                    string? error = null;
                    try
                    {
                        var completion = await CompleteAllocationWorkAsync(
                            services,
                            allocationRecord,
                            actorUserId,
                            stagingLocationId,
                            CancellationToken.None);
                        succeeded = completion.IsSuccess;
                        if (completion.IsFailure)
                        {
                            var failure = completion.FirstError;
                            errorType = failure is null ? "pick-recovery-failure" : $"{failure.Type}:{failure.Code}";
                            error = Redact(failure?.Message ?? completion.Error);
                            conflict = failure?.Type is Wms.Application.Common.ErrorType.Conflict or Wms.Application.Common.ErrorType.Concurrency ||
                                failure?.Code.Contains("conflict", StringComparison.OrdinalIgnoreCase) == true;
                        }
                    }
                    catch (ConcurrencyConflictException exception)
                    {
                        conflict = true;
                        errorType = exception.Code;
                        error = Redact(exception.Message);
                    }
                    catch (DbUpdateConcurrencyException exception)
                    {
                        conflict = true;
                        errorType = "data.concurrency_conflict";
                        error = Redact(exception.Message);
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        errorType = exception.GetType().Name;
                        error = exception.GetType().Name + ": " + Redact(exception.Message);
                    }
                    finally
                    {
                        elapsed.Stop();
                    }

                    recoverySamples.Add(new PerformanceSample(
                        "pick-completion",
                        "pick-conflict-recovery",
                        1,
                        elapsed.Elapsed.TotalMilliseconds,
                        0,
                        succeeded,
                        conflict,
                        null,
                        errorType,
                        error));
                }

                recoveryWatch.Stop();
                var recoveryCompletedAt = DateTimeOffset.UtcNow;
                var recoveryBatch = new MeasurementBatch(
                    $"same-stock-pick-recovery-{concurrency}",
                    "pick-conflict-recovery",
                    1,
                    recoveryWatch.Elapsed.TotalMilliseconds,
                    recoverySamples.Select(sample => sample with
                    {
                        BatchDurationMilliseconds = recoveryWatch.Elapsed.TotalMilliseconds
                    }).ToArray(),
                    recoveryStartedAt,
                    recoveryCompletedAt);
                batches.Add(recoveryBatch);
                partialBatches.Add(new RepeatBatchEvidence(repeat, recoveryBatch));
            }

            if (pickSamples.Any(sample => !sample.Succeeded) &&
                pickConflicts.Count != pickSamples.Count(sample => !sample.Succeeded) ||
                batches.Where(batch => batch.Scenario == "pick-conflict-recovery")
                    .SelectMany(batch => batch.Samples)
                    .Any(sample => !sample.Succeeded))
            {
                throw new InvalidOperationException(
                    $"Pick completion failed under concurrency {concurrency}. failures=" +
                    string.Join(" | ", pickSamples.Where(sample => !sample.Succeeded)
                        .Select(sample => $"{sample.ErrorType ?? "unknown"}: {sample.ErrorMessage ?? "no detail"}")
                        .Concat(batches.Where(batch => batch.Scenario == "pick-conflict-recovery")
                            .SelectMany(batch => batch.Samples)
                            .Where(sample => !sample.Succeeded)
                            .Select(sample => $"recovery {sample.ErrorType ?? "unknown"}: {sample.ErrorMessage ?? "no detail"}"))
                        .Take(6)));
            }
            if (allocationResults.SelectMany(value => value.WorkIds).Distinct().Count() != allocationResults.Count)
            {
                throw new InvalidOperationException("Two sales-order lines shared a pick work item under contention.");
            }

            var replayWork = allocationResults.OrderBy(value => value.SalesOrderId).First();
            using (var context = target.CreateContext())
            {
                var replayItemId = await context.Items.AsNoTracking()
                    .Where(value => value.Sku == itemSku)
                    .Select(value => value.Id)
                    .SingleAsync();
                var transactionsBeforeReplay = await context.InventoryTransactions.AsNoTracking()
                    .CountAsync(value => value.WarehouseId == warehouseId && value.ItemId == replayItemId);
                await using var scope = services.CreateAsyncScope();
                var workService = scope.ServiceProvider.GetRequiredService<IWarehouseWorkService>();
                var completedWork = await workService.GetAsync(replayWork.WorkIds.Single());
                if (completedWork.IsFailure)
                {
                    throw new InvalidOperationException(completedWork.Error);
                }
                var replay = await workService.CompleteAsync(
                    completedWork.Value.Id,
                    new WarehouseWorkCompletionInput(
                        $"{replayWork.Command.IdempotencyKey}-complete",
                        CompletionReference: replayWork.SalesOrderNumber,
                        Scans: completedWork.Value.Lines.Select(line => new WarehouseWorkScanInput(
                            line.Id,
                            line.ItemId,
                            line.SourceLocationId!.Value,
                            stagingLocationId,
                            line.PlannedQuantity,
                            line.LicensePlateId,
                            LotId: line.LotId,
                            SerialNumberId: line.SerialNumberId,
                            SerialNumber: line.SerialNumber)).ToArray()),
                    actorUserId);
                if (replay.IsFailure)
                {
                    throw new InvalidOperationException($"Pick completion replay failed: {replay.Error}");
                }
                context.ChangeTracker.Clear();
                var transactionsAfterReplay = await context.InventoryTransactions.AsNoTracking()
                    .CountAsync(value => value.WarehouseId == warehouseId && value.ItemId == replayItemId);
                if (transactionsAfterReplay != transactionsBeforeReplay)
                {
                    throw new InvalidOperationException("Pick completion replay created duplicate inventory ledger transactions.");
                }
            }
        }

        using (var context = target.CreateContext())
        {
            var itemId = await context.Items.AsNoTracking()
                .Where(value => value.Sku == itemSku)
                .Select(value => value.Id)
                .SingleAsync();
            var balances = await context.InventoryBalances.AsNoTracking()
                .Where(value => value.WarehouseId == warehouseId && value.ItemId == itemId)
                .ToArrayAsync();
            var expectedStagedQuantity = concurrencyLevels.Sum();
            var onHandQuantity = balances.Sum(value => value.OnHandQuantity);
            var stagedQuantity = balances
                .Where(value => value.LocationId == stagingLocationId)
                .Sum(value => value.OnHandQuantity);
            var otherLocationQuantity = onHandQuantity - stagedQuantity;
            var reservedQuantity = balances.Sum(value => value.ReservedQuantity);
            if (onHandQuantity != expectedStagedQuantity ||
                stagedQuantity != expectedStagedQuantity ||
                otherLocationQuantity != 0m ||
                reservedQuantity != 0m ||
                balances.Any(value => value.AvailableQuantity < 0m))
            {
                throw new InvalidOperationException(
                    $"Same-stock pick completion left inventory inconsistent: expected all {expectedStagedQuantity} units at staging with no reservation, " +
                    $"observed onHand={onHandQuantity}, staged={stagedQuantity}, otherLocations={otherLocationQuantity}, reserved={reservedQuantity}.");
            }
        }

        return batches;
    }

    private static async Task<Result<WarehouseWorkDto>> CompleteAllocationWorkAsync(
        IServiceProvider services,
        AllocationWorkRecord allocationRecord,
        string actorUserId,
        int stagingLocationId,
        CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var workService = scope.ServiceProvider.GetRequiredService<IWarehouseWorkService>();
        var work = await workService.GetAsync(allocationRecord.WorkIds.Single(), cancellationToken);
        if (work.IsFailure)
        {
            return work;
        }

        var assigned = await workService.AssignAsync(
            work.Value.Id,
            new WarehouseWorkAssignmentInput(
                actorUserId,
                null,
                $"{allocationRecord.Command.IdempotencyKey}-assign"),
            actorUserId,
            cancellationToken);
        if (assigned.IsFailure)
        {
            return assigned;
        }

        var started = await workService.StartAsync(
            work.Value.Id,
            new WarehouseWorkCommandInput($"{allocationRecord.Command.IdempotencyKey}-start"),
            actorUserId,
            cancellationToken);
        if (started.IsFailure)
        {
            return started;
        }

        var line = Assert.Single(work.Value.Lines);
        return await workService.CompleteAsync(
            work.Value.Id,
            new WarehouseWorkCompletionInput(
                $"{allocationRecord.Command.IdempotencyKey}-complete",
                CompletionReference: allocationRecord.SalesOrderNumber,
                Scans:
                [
                    new WarehouseWorkScanInput(
                        line.Id,
                        line.ItemId,
                        line.SourceLocationId!.Value,
                        stagingLocationId,
                        line.PlannedQuantity,
                        line.LicensePlateId,
                        LotId: line.LotId,
                        SerialNumberId: line.SerialNumberId,
                        SerialNumber: line.SerialNumber)
                ]),
            actorUserId,
            cancellationToken);
    }

    private static async Task<Result<WarehouseWorkDto>> CompleteLimitedStockAllocationWorkAsync(
        IServiceProvider services,
        AllocationWorkRecord allocationRecord,
        string actorUserId,
        int stagingLocationId,
        CancellationToken cancellationToken)
    {
        int workId;
        await using (var scope = services.CreateAsyncScope())
        {
            var allocation = scope.ServiceProvider.GetRequiredService<ISalesOrderAllocationService>();
            var released = await allocation.ReleaseAsync(
                allocationRecord.SalesOrderId,
                new SalesOrderAllocationCommand(
                    IdempotencyKey: $"{allocationRecord.Command.IdempotencyKey}-release"),
                actorUserId,
                cancellationToken);
            if (released.IsFailure)
            {
                return released.ToFailure<WarehouseWorkDto>();
            }

            workId = Assert.Single(released.Value.Work).WorkId;
        }

        return await CompleteAllocationWorkAsync(
            services,
            allocationRecord with { WorkIds = [workId] },
            actorUserId,
            stagingLocationId,
            cancellationToken);
    }

    private static async Task<LimitedStockEvidence> RunLimitedStockContentionAsync(
        IServiceProvider services,
        HttpClient client,
        string receiveToken,
        string sourceLocationCode,
        PostgreSqlTestDatabase target,
        string actorUserId,
        int warehouseId,
        int customerId,
        string itemSku,
        int itemId,
        string suffix,
        int concurrency,
        int sourceLocationId,
        int destinationLocationId,
        int stagingLocationId,
        int repeat,
        ICollection<RepeatBatchEvidence> partialBatches)
    {
        var receive = await MeasureHttpAsync(
            "capacity-receiving-seed",
            1,
            () => ReceiveAsync(client, receiveToken, itemSku, sourceLocationCode, 4m, "EA", $"PERF-{suffix}-CAPACITY"));
        if (!receive.Succeeded)
        {
            throw new InvalidOperationException("Could not seed four units for over-allocation safety evidence.");
        }

        int sourceInventoryStatusId;
        using (var context = target.CreateContext())
        {
            var sourceStocks = await context.Stock.AsNoTracking()
                .Where(value => value.ItemId == itemId && value.LocationId == sourceLocationId)
                .Select(value => new { value.QuantityAvailable.Value, value.InventoryStatusId })
                .ToArrayAsync();
            var receivedQuantity = sourceStocks.Sum(value => value.Value);
            if (receivedQuantity != 4m || sourceStocks.Length != 1)
            {
                throw new InvalidOperationException(
                    $"Capacity receiving returned HTTP {receive.StatusCode} but persisted {receivedQuantity} units in {sourceStocks.Length} source-stock dimensions; expected 4 units in one dimension.");
            }

            sourceInventoryStatusId = sourceStocks[0].InventoryStatusId;
        }

        var moveSamples = new ConcurrentBag<PerformanceSample>();
        var movementStartedAt = DateTimeOffset.UtcNow;
        var movementWatch = Stopwatch.StartNew();
        await Parallel.ForEachAsync(
            Enumerable.Range(0, 4),
            new ParallelOptions { MaxDegreeOfParallelism = 4 },
            async (index, cancellationToken) =>
            {
                var elapsed = Stopwatch.StartNew();
                var succeeded = false;
                var conflict = false;
                string? errorType = null;
                string? errorMessage = null;
                try
                {
                    await using var scope = services.CreateAsyncScope();
                    var transfer = scope.ServiceProvider.GetRequiredService<ITransferService>();
                    var result = await transfer.MoveAsync(
                        new InternalMovementInput(
                            warehouseId,
                            itemId,
                            1m,
                            "EA",
                            sourceLocationId,
                            destinationLocationId,
                            $"perf-{suffix}-capacity-move-{index}",
                            InventoryStatusId: sourceInventoryStatusId),
                        actorUserId,
                        cancellationToken);
                    succeeded = result.IsSuccess;
                    if (result.IsFailure)
                    {
                        var failure = result.FirstError;
                        errorType = failure is null
                            ? "internal-movement-failure"
                            : $"{failure.Type}:{failure.Code}";
                        errorMessage = Redact(failure?.Message ?? result.Error);
                        conflict = failure?.Type is Wms.Application.Common.ErrorType.Conflict or Wms.Application.Common.ErrorType.Concurrency ||
                            failure?.Code.Contains("conflict", StringComparison.OrdinalIgnoreCase) == true;
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    errorType = exception.GetType().Name;
                    errorMessage = Redact(exception.Message);
                }
                finally
                {
                    elapsed.Stop();
                }
                moveSamples.Add(new PerformanceSample(
                    "internal-movement",
                    "limited-stock-preparation",
                    4,
                    elapsed.Elapsed.TotalMilliseconds,
                    0,
                    succeeded,
                    conflict,
                    null,
                    errorType,
                    errorMessage));
            });
        movementWatch.Stop();
        var movementCompletedAt = DateTimeOffset.UtcNow;
        var normalizedMoveSamples = moveSamples.Select(sample => sample with
        {
            BatchDurationMilliseconds = movementWatch.Elapsed.TotalMilliseconds
        }).ToArray();
        var movementBatches = new List<MeasurementBatch>
        {
            new(
                "limited-stock-movement",
                "limited-stock-movement",
                4,
                movementWatch.Elapsed.TotalMilliseconds,
                normalizedMoveSamples,
                movementStartedAt,
                movementCompletedAt)
        };
        partialBatches.Add(new RepeatBatchEvidence(repeat, movementBatches[0]));
        var nonConflictMovementFailures = normalizedMoveSamples
            .Where(sample => !sample.Succeeded && !sample.Conflict)
            .ToArray();
        if (nonConflictMovementFailures.Length > 0)
        {
            var failure = nonConflictMovementFailures[0];
            throw new InvalidOperationException(
                $"Could not move a limited-stock unit to a pickable location: {failure.ErrorType}: {failure.ErrorMessage}");
        }

        var capacityMovementConflicts = normalizedMoveSamples
            .Where(sample => !sample.Succeeded && sample.Conflict)
            .ToArray();
        if (capacityMovementConflicts.Length > 0)
        {
            var recoverySamples = new List<PerformanceSample>(capacityMovementConflicts.Length);
            var recoveryStartedAt = DateTimeOffset.UtcNow;
            var recoveryWatch = Stopwatch.StartNew();
            for (var retry = 0; retry < capacityMovementConflicts.Length; retry++)
            {
                var elapsed = Stopwatch.StartNew();
                var succeeded = false;
                var conflict = false;
                string? errorType = null;
                string? errorMessage = null;
                try
                {
                    await using var scope = services.CreateAsyncScope();
                    var transfer = scope.ServiceProvider.GetRequiredService<ITransferService>();
                    var result = await transfer.MoveAsync(
                        new InternalMovementInput(
                            warehouseId,
                            itemId,
                            1m,
                            "EA",
                            sourceLocationId,
                            destinationLocationId,
                            $"perf-{suffix}-capacity-recovery-{retry + 1}",
                            InventoryStatusId: sourceInventoryStatusId),
                        actorUserId);
                    succeeded = result.IsSuccess;
                    if (result.IsFailure)
                    {
                        var failure = result.FirstError;
                        errorType = failure is null
                            ? "internal-movement-recovery-failure"
                            : $"{failure.Type}:{failure.Code}";
                        errorMessage = Redact(failure?.Message ?? result.Error);
                        conflict = failure?.Type is Wms.Application.Common.ErrorType.Conflict or Wms.Application.Common.ErrorType.Concurrency ||
                            failure?.Code.Contains("conflict", StringComparison.OrdinalIgnoreCase) == true;
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    errorType = exception.GetType().Name;
                    errorMessage = Redact(exception.Message);
                }
                finally
                {
                    elapsed.Stop();
                }

                recoverySamples.Add(new PerformanceSample(
                    "internal-movement",
                    "limited-stock-movement-recovery",
                    1,
                    elapsed.Elapsed.TotalMilliseconds,
                    0,
                    succeeded,
                    conflict,
                    null,
                    errorType,
                    errorMessage));
            }
            recoveryWatch.Stop();
            var recoveryCompletedAt = DateTimeOffset.UtcNow;
            movementBatches.Add(new MeasurementBatch(
                "limited-stock-movement-recovery",
                "limited-stock-movement-recovery",
                1,
                recoveryWatch.Elapsed.TotalMilliseconds,
                recoverySamples.Select(sample => sample with
                {
                    BatchDurationMilliseconds = recoveryWatch.Elapsed.TotalMilliseconds
                }).ToArray(),
                recoveryStartedAt,
                recoveryCompletedAt));
            partialBatches.Add(new RepeatBatchEvidence(repeat, movementBatches[^1]));
            if (recoverySamples.Any(sample => !sample.Succeeded))
            {
                var failure = recoverySamples.First(sample => !sample.Succeeded);
                throw new InvalidOperationException(
                    $"Could not recover a limited-stock movement conflict: {failure.ErrorType}: {failure.ErrorMessage}");
            }
        }

        var orders = new List<OrderWorkRecord>();
        for (var index = 0; index < concurrency; index++)
        {
            await using var scope = services.CreateAsyncScope();
            var salesOrders = scope.ServiceProvider.GetRequiredService<ISalesOrderService>();
            var created = await salesOrders.CreateAsync(
                new SalesOrderInput(
                    warehouseId,
                    customerId,
                    OrderDate: new DateOnly(2026, 1, 15),
                    RequestedShipDate: new DateOnly(2026, 1, 16),
                    ExternalReference: $"PERF-{suffix}-CAP-SO-{index:D4}",
                    SourceType: "PERF-OVERALLOC",
                    AllowPartialShipment: true,
                    Lines: [new SalesOrderLineInput(itemSku, 1m, "EA")]),
                actorUserId);
            if (created.IsFailure)
            {
                throw new InvalidOperationException($"Limited-stock sales order creation failed: {created.Error}");
            }
            var confirmed = await salesOrders.ConfirmAsync(created.Value.Id, actorUserId);
            if (confirmed.IsFailure)
            {
                throw new InvalidOperationException($"Limited-stock sales order confirmation failed: {confirmed.Error}");
            }
            orders.Add(new OrderWorkRecord(
                confirmed.Value.Id,
                confirmed.Value.DocumentNumber,
                $"perf-{suffix}-capacity-allocate-{index}"));
        }

        var allocationSamples = new ConcurrentBag<PerformanceSample>();
        var allocations = new ConcurrentBag<AllocationWorkRecord>();
        var allocationConflicts = new ConcurrentBag<OrderWorkRecord>();
        var allocationStartedAt = DateTimeOffset.UtcNow;
        var allocationWatch = Stopwatch.StartNew();
        await Parallel.ForEachAsync(
            orders,
            new ParallelOptions { MaxDegreeOfParallelism = concurrency },
            async (order, cancellationToken) =>
            {
                var elapsed = Stopwatch.StartNew();
                var succeeded = false;
                var conflict = false;
                string? error = null;
                string? errorType = null;
                AllocationWorkRecord? allocationRecord = null;
                try
                {
                    await using var scope = services.CreateAsyncScope();
                    var allocation = scope.ServiceProvider.GetRequiredService<ISalesOrderAllocationService>();
                    var command = new SalesOrderAllocationCommand(
                        ReleaseToWarehouse: false,
                        IdempotencyKey: order.AllocationKey);
                    var result = await allocation.AllocateAsync(order.SalesOrderId, command, actorUserId, cancellationToken);
                    succeeded = result.IsSuccess;
                    if (result.IsSuccess)
                    {
                        allocationRecord = new AllocationWorkRecord(
                            order.SalesOrderId,
                            order.SalesOrderNumber,
                            command,
                            result.Value.AllocatedBaseQuantity,
                            result.Value.BackorderBaseQuantity,
                            []);
                    }
                    else
                    {
                        var failure = result.FirstError;
                        errorType = failure is null ? "limited-stock-allocation-failure" : $"{failure.Type}:{failure.Code}";
                        error = Redact(failure?.Message ?? result.Error);
                        conflict = failure?.Type is Wms.Application.Common.ErrorType.Conflict or Wms.Application.Common.ErrorType.Concurrency ||
                            failure?.Code.Contains("conflict", StringComparison.OrdinalIgnoreCase) == true;
                        if (conflict)
                        {
                            allocationConflicts.Add(order);
                        }
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    errorType = exception.GetType().Name;
                    error = Redact(exception.Message);
                }
                finally
                {
                    elapsed.Stop();
                }
                if (allocationRecord is not null)
                {
                    allocations.Add(allocationRecord);
                }
                allocationSamples.Add(new PerformanceSample(
                    "allocation",
                    "limited-stock-allocation",
                    concurrency,
                    elapsed.Elapsed.TotalMilliseconds,
                    0,
                    succeeded,
                    conflict,
                    null,
                    errorType,
                    error));
            });
        allocationWatch.Stop();

        var allocationBatch = new MeasurementBatch(
            "limited-stock-over-allocation-safety",
            "limited-stock-allocation",
            concurrency,
            allocationWatch.Elapsed.TotalMilliseconds,
            allocationSamples.Select(sample => sample with
            {
                BatchDurationMilliseconds = allocationWatch.Elapsed.TotalMilliseconds
            }).ToArray(),
            allocationStartedAt,
            DateTimeOffset.UtcNow);
        partialBatches.Add(new RepeatBatchEvidence(repeat, allocationBatch));
        var allocationBatches = new List<MeasurementBatch> { allocationBatch };

        if (!allocationConflicts.IsEmpty)
        {
            var recoverySamples = new List<PerformanceSample>(allocationConflicts.Count);
            var recoveryStartedAt = DateTimeOffset.UtcNow;
            var recoveryWatch = Stopwatch.StartNew();
            foreach (var order in allocationConflicts.OrderBy(value => value.SalesOrderId))
            {
                var elapsed = Stopwatch.StartNew();
                var succeeded = false;
                var conflict = false;
                string? errorType = null;
                string? error = null;
                AllocationWorkRecord? allocationRecord = null;
                try
                {
                    await using var scope = services.CreateAsyncScope();
                    var allocation = scope.ServiceProvider.GetRequiredService<ISalesOrderAllocationService>();
                    var command = new SalesOrderAllocationCommand(
                        ReleaseToWarehouse: false,
                        IdempotencyKey: order.AllocationKey);
                    var result = await allocation.AllocateAsync(order.SalesOrderId, command, actorUserId);
                    succeeded = result.IsSuccess;
                    if (result.IsSuccess)
                    {
                        allocationRecord = new AllocationWorkRecord(
                            order.SalesOrderId,
                            order.SalesOrderNumber,
                            command,
                            result.Value.AllocatedBaseQuantity,
                            result.Value.BackorderBaseQuantity,
                            []);
                    }
                    else
                    {
                        var failure = result.FirstError;
                        errorType = failure is null ? "limited-stock-allocation-recovery-failure" : $"{failure.Type}:{failure.Code}";
                        error = Redact(failure?.Message ?? result.Error);
                        conflict = failure?.Type is Wms.Application.Common.ErrorType.Conflict or Wms.Application.Common.ErrorType.Concurrency ||
                            failure?.Code.Contains("conflict", StringComparison.OrdinalIgnoreCase) == true;
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    errorType = exception.GetType().Name;
                    error = Redact(exception.Message);
                }
                finally
                {
                    elapsed.Stop();
                }

                if (allocationRecord is not null)
                {
                    allocations.Add(allocationRecord);
                }
                recoverySamples.Add(new PerformanceSample(
                    "allocation",
                    "limited-stock-allocation-conflict-recovery",
                    1,
                    elapsed.Elapsed.TotalMilliseconds,
                    0,
                    succeeded,
                    conflict,
                    null,
                    errorType,
                    error));
            }

            recoveryWatch.Stop();
            var recoveryBatch = new MeasurementBatch(
                "limited-stock-allocation-conflict-recovery",
                "allocation-conflict-recovery",
                1,
                recoveryWatch.Elapsed.TotalMilliseconds,
                recoverySamples.Select(sample => sample with
                {
                    BatchDurationMilliseconds = recoveryWatch.Elapsed.TotalMilliseconds
                }).ToArray(),
                recoveryStartedAt,
                DateTimeOffset.UtcNow);
            allocationBatches.Add(recoveryBatch);
            partialBatches.Add(new RepeatBatchEvidence(repeat, recoveryBatch));
            if (recoverySamples.Any(sample => !sample.Succeeded))
            {
                var failure = recoverySamples.First(sample => !sample.Succeeded);
                throw new InvalidOperationException(
                    $"Limited-stock allocation conflict recovery failed: {failure.ErrorType}: {failure.ErrorMessage}");
            }
        }

        var allocatedQuantity = allocations.Sum(value => value.AllocatedQuantity);
        var backorderQuantity = allocations.Sum(value => value.BackorderQuantity);
        if (allocations.Count != concurrency || allocatedQuantity != 4m || backorderQuantity != concurrency - 4m)
        {
            throw new InvalidOperationException(
                $"Limited-stock contention at {concurrency} requests allocated {allocatedQuantity}, backordered {backorderQuantity}, expected 4 allocated and {concurrency - 4} backordered. " +
                $"allocationConflicts={allocationConflicts.Count}; allocationFailures=" +
                string.Join(" | ", allocationSamples.Where(sample => !sample.Succeeded)
                    .Select(sample => $"{sample.ErrorType ?? "unknown"}: {sample.ErrorMessage ?? "no detail"}")
                    .Concat(allocationBatches.Where(batch => batch.Scenario == "allocation-conflict-recovery")
                        .SelectMany(batch => batch.Samples)
                        .Where(sample => !sample.Succeeded)
                        .Select(sample => $"recovery {sample.ErrorType ?? "unknown"}: {sample.ErrorMessage ?? "no detail"}"))
                    .Take(6)));
        }

        using (var context = target.CreateContext())
        {
            var balance = await context.InventoryBalances.AsNoTracking()
                .Where(value => value.WarehouseId == warehouseId && value.ItemId == itemId)
                .ToArrayAsync();
            if (balance.Sum(value => value.OnHandQuantity) != 4m ||
                balance.Sum(value => value.ReservedQuantity) != 4m ||
                balance.Any(value => value.AvailableQuantity < 0m))
            {
                throw new InvalidOperationException("Limited-stock allocation oversold, duplicated or lost inventory.");
            }
        }

        var first = allocations.OrderBy(value => value.SalesOrderId).First();
        await using (var scope = services.CreateAsyncScope())
        {
            var allocation = scope.ServiceProvider.GetRequiredService<ISalesOrderAllocationService>();
            var replay = await allocation.AllocateAsync(
                first.SalesOrderId,
                first.Command,
                actorUserId);
            if (replay.IsFailure ||
                replay.Value.AllocatedBaseQuantity != first.AllocatedQuantity ||
                replay.Value.BackorderBaseQuantity != first.BackorderQuantity)
            {
                throw new InvalidOperationException("Allocation idempotency replay changed the limited-stock outcome.");
            }
        }

        var pickSamples = new ConcurrentBag<PerformanceSample>();
        var pickConflicts = new ConcurrentBag<AllocationWorkRecord>();
        var pickStartedAt = DateTimeOffset.UtcNow;
        var pickWatch = Stopwatch.StartNew();
        await Parallel.ForEachAsync(
            allocations.Where(value => value.AllocatedQuantity > 0m),
            new ParallelOptions { MaxDegreeOfParallelism = 4 },
            async (allocationRecord, cancellationToken) =>
            {
                var elapsed = Stopwatch.StartNew();
                var succeeded = false;
                var conflict = false;
                string? errorType = null;
                string? error = null;
                try
                {
                    var result = await CompleteLimitedStockAllocationWorkAsync(
                        services,
                        allocationRecord,
                        actorUserId,
                        stagingLocationId,
                        cancellationToken);
                    succeeded = result.IsSuccess;
                    if (result.IsFailure)
                    {
                        var failure = result.FirstError;
                        errorType = failure is null ? "limited-stock-pick-failure" : $"{failure.Type}:{failure.Code}";
                        error = Redact(failure?.Message ?? result.Error);
                        conflict = failure?.Type is Wms.Application.Common.ErrorType.Conflict or Wms.Application.Common.ErrorType.Concurrency ||
                            failure?.Code.Contains("conflict", StringComparison.OrdinalIgnoreCase) == true;
                        if (conflict)
                        {
                            pickConflicts.Add(allocationRecord);
                        }
                    }
                }
                catch (ConcurrencyConflictException exception)
                {
                    conflict = true;
                    errorType = exception.Code;
                    error = Redact(exception.Message);
                    pickConflicts.Add(allocationRecord);
                }
                catch (DbUpdateConcurrencyException exception)
                {
                    conflict = true;
                    errorType = "data.concurrency_conflict";
                    error = Redact(exception.Message);
                    pickConflicts.Add(allocationRecord);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    errorType = exception.GetType().Name;
                    error = Redact(exception.Message);
                }
                finally
                {
                    elapsed.Stop();
                }
                pickSamples.Add(new PerformanceSample(
                    "pick-completion",
                    "limited-stock-pick",
                    concurrency,
                    elapsed.Elapsed.TotalMilliseconds,
                    0,
                    succeeded,
                    conflict,
                    null,
                    errorType,
                    error));
            });
        pickWatch.Stop();
        var pickBatch = new MeasurementBatch(
            "limited-stock-pick",
            "limited-stock-pick",
            concurrency,
            pickWatch.Elapsed.TotalMilliseconds,
            pickSamples.Select(sample => sample with
            {
                BatchDurationMilliseconds = pickWatch.Elapsed.TotalMilliseconds
            }).ToArray(),
            pickStartedAt,
            DateTimeOffset.UtcNow);
        partialBatches.Add(new RepeatBatchEvidence(repeat, pickBatch));
        var pickBatches = new List<MeasurementBatch> { pickBatch };
        if (!pickConflicts.IsEmpty)
        {
            var recoverySamples = new List<PerformanceSample>(pickConflicts.Count);
            var recoveryStartedAt = DateTimeOffset.UtcNow;
            var recoveryWatch = Stopwatch.StartNew();
            foreach (var allocationRecord in pickConflicts.OrderBy(value => value.SalesOrderId))
            {
                var elapsed = Stopwatch.StartNew();
                var succeeded = false;
                var conflict = false;
                string? errorType = null;
                string? error = null;
                try
                {
                    var result = await CompleteLimitedStockAllocationWorkAsync(
                        services,
                        allocationRecord,
                        actorUserId,
                        stagingLocationId,
                        CancellationToken.None);
                    succeeded = result.IsSuccess;
                    if (result.IsFailure)
                    {
                        var failure = result.FirstError;
                        errorType = failure is null ? "limited-stock-pick-recovery-failure" : $"{failure.Type}:{failure.Code}";
                        error = Redact(failure?.Message ?? result.Error);
                        conflict = failure?.Type is Wms.Application.Common.ErrorType.Conflict or Wms.Application.Common.ErrorType.Concurrency ||
                            failure?.Code.Contains("conflict", StringComparison.OrdinalIgnoreCase) == true;
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    errorType = exception.GetType().Name;
                    error = Redact(exception.Message);
                }
                finally
                {
                    elapsed.Stop();
                }

                recoverySamples.Add(new PerformanceSample(
                    "pick-completion",
                    "limited-stock-pick-conflict-recovery",
                    1,
                    elapsed.Elapsed.TotalMilliseconds,
                    0,
                    succeeded,
                    conflict,
                    null,
                    errorType,
                    error));
            }

            recoveryWatch.Stop();
            var recoveryBatch = new MeasurementBatch(
                "limited-stock-pick-conflict-recovery",
                "pick-conflict-recovery",
                1,
                recoveryWatch.Elapsed.TotalMilliseconds,
                recoverySamples.Select(sample => sample with
                {
                    BatchDurationMilliseconds = recoveryWatch.Elapsed.TotalMilliseconds
                }).ToArray(),
                recoveryStartedAt,
                DateTimeOffset.UtcNow);
            pickBatches.Add(recoveryBatch);
            partialBatches.Add(new RepeatBatchEvidence(repeat, recoveryBatch));
            if (recoverySamples.Any(sample => !sample.Succeeded))
            {
                var failure = recoverySamples.First(sample => !sample.Succeeded);
                throw new InvalidOperationException(
                    $"Limited-stock pick conflict recovery failed: {failure.ErrorType}: {failure.ErrorMessage}");
            }
        }

        if (pickSamples.Count != 4 || pickSamples.Any(sample => !sample.Succeeded && !sample.Conflict))
        {
            throw new InvalidOperationException(
                "Limited-stock pick completion did not consume each allocated unit exactly once. failures=" +
                string.Join(" | ", pickSamples.Where(sample => !sample.Succeeded)
                    .Select(sample => $"{sample.ErrorType ?? "unknown"}: {sample.ErrorMessage ?? "no detail"}")
                    .Take(6)));
        }

        using (var context = target.CreateContext())
        {
            var balances = await context.InventoryBalances.AsNoTracking()
                .Where(value => value.WarehouseId == warehouseId && value.ItemId == itemId)
                .ToArrayAsync();
            var allocationCount = await context.InventoryReservationAllocations.AsNoTracking()
                .Where(value => value.ItemId == itemId && value.WarehouseId == warehouseId)
                .CountAsync();
            var onHandQuantity = balances.Sum(value => value.OnHandQuantity);
            var stagedQuantity = balances
                .Where(value => value.LocationId == stagingLocationId)
                .Sum(value => value.OnHandQuantity);
            var otherLocationQuantity = onHandQuantity - stagedQuantity;
            var reservedQuantity = balances.Sum(value => value.ReservedQuantity);
            if (onHandQuantity != 4m ||
                stagedQuantity != 4m ||
                otherLocationQuantity != 0m ||
                reservedQuantity != 0m ||
                balances.Any(value => value.AvailableQuantity < 0m) ||
                allocationCount != 4)
            {
                throw new InvalidOperationException(
                    $"Limited-stock picks left inconsistent inventory: expected four units at staging with no reservation and four allocation rows, " +
                    $"observed onHand={onHandQuantity}, staged={stagedQuantity}, otherLocations={otherLocationQuantity}, " +
                    $"reserved={reservedQuantity}, allocationRows={allocationCount}.");
            }
        }

        return new LimitedStockEvidence(
            movementBatches,
            allocationBatches,
            pickBatches,
            new
            {
                requestedOrders = concurrency,
                allocated = allocatedQuantity,
                expectedBackorders = backorderQuantity,
                stockBeforeAllocation = 4m,
                stockAfterPicking = 4m,
                stockAtStagingAfterPicking = 4m,
                allocationReplayWasIdempotent = true,
                uniqueAllocationRows = 4,
                noNegativeOrOverReservedBalances = true,
                preparationMovementSamples = moveSamples.Count,
                preparationMovementElapsedMilliseconds = Math.Round(movementWatch.Elapsed.TotalMilliseconds, 3)
            });
    }

    private static async Task<WorkerEvidence> RunBackgroundAndOutboxWorkloadAsync(
        IServiceProvider services,
        PostgreSqlTestDatabase target,
        int warehouseId,
        string actorUserId,
        int repeat)
    {
        var jobKey = $"issue-132-performance-reconciliation-{repeat}-{Guid.NewGuid():N}";
        var startedAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        await using (var scope = services.CreateAsyncScope())
        {
            var runner = scope.ServiceProvider.GetRequiredService<WmsJobRunner>();
            await runner.ExecuteAsync(WmsJobEnvelope.Create(
                WmsJobNames.InventoryReconciliation,
                jobKey,
                actorUserId: actorUserId,
                actorUserName: "performance-qualification",
                warehouseId: warehouseId));
        }
        stopwatch.Stop();
        using (var context = target.CreateContext())
        {
            var job = await context.JobExecutions.AsNoTracking()
                .SingleAsync(value => value.IdempotencyKey == jobKey);
            if (job.Status != WmsJobExecutionStatuses.Succeeded)
            {
                throw new InvalidOperationException($"Inventory reconciliation job ended with state '{job.Status}'.");
            }
        }
        var jobSample = new PerformanceSample(
            "background-job",
            "durable-background-job",
            1,
            stopwatch.Elapsed.TotalMilliseconds,
            stopwatch.Elapsed.TotalMilliseconds,
            true,
            false,
            null,
            null,
            null);
        var jobBatch = new MeasurementBatch(
            "durable-inventory-reconciliation-job",
            "durable-background-job",
            1,
            stopwatch.Elapsed.TotalMilliseconds,
            [jobSample],
            startedAt,
            DateTimeOffset.UtcNow);

        var before = await ReadOutboxStatusCountsAsync(target);
        var subscriptionCount = 0;
        IntegrationDispatchResult dispatch;
        var outboxStartedAt = DateTimeOffset.UtcNow;
        var outboxWatch = Stopwatch.StartNew();
        await using (var scope = services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<WmsDbContext>();
            subscriptionCount = await context.WebhookSubscriptions.AsNoTracking()
                .CountAsync(value => value.Status == WmsWebhookSubscriptionStatuses.Active);
            var dispatcher = scope.ServiceProvider.GetRequiredService<IIntegrationOutboxDispatcher>();
            dispatch = await dispatcher.DispatchAsync(100);
        }
        outboxWatch.Stop();
        if (subscriptionCount != 0 || dispatch.DeliveriesAttempted != 0)
        {
            throw new InvalidOperationException("Performance qualification unexpectedly attempted a live webhook delivery.");
        }
        var after = await ReadOutboxStatusCountsAsync(target);
        return new WorkerEvidence(
            jobBatch,
            new
            {
                jobName = WmsJobNames.InventoryReconciliation,
                jobIdempotencyKey = jobKey,
                jobStatus = WmsJobExecutionStatuses.Succeeded,
                jobDurationMilliseconds = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 3),
                outboxStartedAt,
                outboxDurationMilliseconds = Math.Round(outboxWatch.Elapsed.TotalMilliseconds, 3),
                outboxBefore = before,
                outboxAfter = after,
                dispatch,
                activeWebhookSubscriptions = subscriptionCount,
                externalDeliveryAttempted = false
            });
    }

    private static async Task<IReadOnlyDictionary<string, int>> ReadOutboxStatusCountsAsync(
        PostgreSqlTestDatabase target)
    {
        using var context = target.CreateContext();
        return await context.IntegrationOutbox.AsNoTracking()
            .GroupBy(value => value.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToDictionaryAsync(value => value.Status, value => value.Count, StringComparer.Ordinal);
    }

    private static async Task<string> ExplainInventoryBalanceQueryAsync(
        PostgreSqlTestDatabase target,
        int warehouseId,
        int itemId)
    {
        await using var context = target.CreateContext();
        var entity = context.Model.FindEntityType(typeof(InventoryBalance))
            ?? throw new InvalidOperationException("Inventory balance entity mapping is missing.");
        var tableName = entity.GetTableName()
            ?? throw new InvalidOperationException("Inventory balance table mapping is missing.");
        var table = StoreObjectIdentifier.Table(tableName, entity.GetSchema());
        var warehouse = QuoteIdentifier(entity.FindProperty(nameof(InventoryBalance.WarehouseId))!.GetColumnName(table)!);
        var item = QuoteIdentifier(entity.FindProperty(nameof(InventoryBalance.ItemId))!.GetColumnName(table)!);
        var location = QuoteIdentifier(entity.FindProperty(nameof(InventoryBalance.LocationId))!.GetColumnName(table)!);
        var onHand = QuoteIdentifier(entity.FindProperty(nameof(InventoryBalance.OnHandQuantity))!.GetColumnName(table)!);
        var reserved = QuoteIdentifier(entity.FindProperty(nameof(InventoryBalance.ReservedQuantity))!.GetColumnName(table)!);
        var sql = $"EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON) SELECT {location}, {onHand}, {reserved} FROM {QuoteIdentifier(tableName)} WHERE {warehouse} = @warehouseId AND {item} = @itemId ORDER BY {location} LIMIT 100";
        await using var connection = new NpgsqlConnection(target.TestConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("warehouseId", warehouseId);
        command.Parameters.AddWithValue("itemId", itemId);
        var result = await command.ExecuteScalarAsync();
        return result?.ToString() ?? "null";
    }

    private static async Task<object> ReadFinalCountsAsync(PostgreSqlTestDatabase target)
    {
        using var context = target.CreateContext();
        return new
        {
            warehouses = await context.Warehouses.CountAsync(),
            locations = await context.Locations.CountAsync(),
            items = await context.Items.CountAsync(),
            inventoryBalances = await context.InventoryBalances.CountAsync(),
            inventoryTransactions = await context.InventoryTransactions.CountAsync(),
            reservations = await context.InventoryReservations.CountAsync(),
            reservationAllocations = await context.InventoryReservationAllocations.CountAsync(),
            salesOrders = await context.SalesOrders.CountAsync(),
            warehouseWorks = await context.WarehouseWorks.CountAsync(),
            movementDocuments = await context.InternalMovements.CountAsync(),
            jobExecutions = await context.JobExecutions.CountAsync(),
            integrationOutbox = await context.IntegrationOutbox.CountAsync()
        };
    }

    private static async Task<string> ReadPostgreSqlVersionAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT version()", connection);
        return (string?)await command.ExecuteScalarAsync() ?? "unknown";
    }

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Could not warm the authenticated form '{path}': HTTP {(int)response.StatusCode}.");
        }
        var html = await response.Content.ReadAsStringAsync();
        var match = Regex.Match(
            html,
            "name=\\\"__RequestVerificationToken\\\"[^>]*value=\\\"([^\\\"]+)\\\"",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            throw new InvalidOperationException($"Could not find an antiforgery token on authenticated form '{path}'.");
        }
        return System.Net.WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static Task<HttpResponseMessage> ReceiveAsync(
        HttpClient client,
        string token,
        string itemSku,
        string locationCode,
        decimal quantity,
        string unit,
        string reference) => client.PostAsync(
        "/Receiving/Receive",
        new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["ItemSku"] = itemSku,
            ["LocationCode"] = locationCode,
            ["Quantity"] = quantity.ToString(CultureInfo.InvariantCulture),
            ["UnitOfMeasure"] = unit,
            ["ReferenceNumber"] = reference,
            ["__RequestVerificationToken"] = token
        }));

    private static Task<HttpResponseMessage> ImportItemsAsync(
        HttpClient client,
        string token,
        IReadOnlyList<string> skus)
    {
        var rows = new StringBuilder();
        rows.AppendLine("SKU,NAME,BASE_UNIT,CATEGORY,BRAND,TYPE,STATUS,PURCHASE_UNIT,SALES_UNIT,REQUIRES_LOT,REQUIRES_SERIAL,REQUIRES_EXPIRY,SHELF_LIFE_DAYS,USE_FEFO,STANDARD_COST,MINIMUM_STOCK,MAXIMUM_STOCK,SAFETY_STOCK,LEAD_TIME_DAYS,STORAGE_PROFILE,PUTAWAY_PROFILE,DEFAULT_SUPPLIER_CODE");
        foreach (var sku in skus)
        {
            rows.Append(sku)
                .Append(",Performance item,EA,Hardware,Qualification,Stock,Active,EA,EA,false,false,false,0,false,2.5,1,1000,2,3,Standard,Fast,")
                .AppendLine();
        }

        return client.PostAsync(
            "/Items/Import",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Csv"] = rows.ToString(),
                ["__RequestVerificationToken"] = token
            }));
    }

    private static async Task<PerformanceSample> MeasureHttpAsync(
        string workload,
        int concurrency,
        Func<Task<HttpResponseMessage>> operation)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var response = await operation();
            var responseBody = await response.Content.ReadAsStringAsync();
            stopwatch.Stop();
            var statusCode = (int)response.StatusCode;
            var successfulStatus = statusCode is >= 200 and < 400;
            var validatesHtmlOutcome = workload.Contains("receiving", StringComparison.OrdinalIgnoreCase) ||
                workload.Contains("item-import", StringComparison.OrdinalIgnoreCase);
            var applicationFailure = validatesHtmlOutcome &&
                (responseBody.Contains("class=\"alert alert-danger", StringComparison.OrdinalIgnoreCase) ||
                 responseBody.Contains("validation-summary-errors", StringComparison.OrdinalIgnoreCase));
            var applicationError = applicationFailure ? ExtractHtmlFailureMessage(responseBody) : null;
            var succeeded = successfulStatus && !applicationFailure;
            return new PerformanceSample(
                workload,
                string.Empty,
                concurrency,
                stopwatch.Elapsed.TotalMilliseconds,
                0,
                succeeded,
                statusCode is 409 or 412 or 423,
                statusCode,
                succeeded
                    ? null
                    : successfulStatus
                        ? "APPLICATION_RESPONSE_FAILURE"
                        : $"HTTP {statusCode}",
                succeeded
                    ? null
                    : successfulStatus
                        ? applicationError ?? "The authenticated operation rendered a validation or application error."
                        : $"HTTP {statusCode}");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            stopwatch.Stop();
            return new PerformanceSample(
                workload,
                string.Empty,
                concurrency,
                stopwatch.Elapsed.TotalMilliseconds,
                0,
                false,
                false,
                null,
                exception.GetType().Name,
                Redact(exception.Message));
        }
    }

    private static string? ExtractHtmlFailureMessage(string body)
    {
        var messages = new List<string>();
        var alertBlock = Regex.Match(
            body,
            "<div[^>]*class=\"[^\"]*alert alert-danger[^\"]*\"[^>]*>(?<content>.*?)</div>",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);
        if (alertBlock.Success)
        {
            var alertMessage = Regex.Match(
                alertBlock.Groups["content"].Value,
                "<strong[^>]*>.*?</strong>\\s*<div>(?<message>.*?)</div>",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);
            if (alertMessage.Success)
            {
                messages.Add(alertMessage.Groups["message"].Value);
            }
        }

        var summary = Regex.Match(
            body,
            "<div[^>]*class=\"[^\"]*validation-summary-errors[^\"]*\"[^>]*>(?<content>.*?)</div>",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);
        if (summary.Success)
        {
            foreach (Match validation in Regex.Matches(
                         summary.Groups["content"].Value,
                         "<li[^>]*>(?<message>.*?)</li>",
                         RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline))
            {
                messages.Add(validation.Groups["message"].Value);
                if (messages.Count >= 4)
                {
                    break;
                }
            }
        }

        if (messages.Count < 4)
        {
            foreach (Match validation in Regex.Matches(
                         body,
                         "<span[^>]*class=\"[^\"]*field-validation-error[^\"]*\"[^>]*>(?<message>.*?)</span>",
                         RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline))
            {
                messages.Add(validation.Groups["message"].Value);
                if (messages.Count >= 4)
                {
                    break;
                }
            }
        }

        var message = string.Join("; ", messages.Select(value =>
        {
            var plainText = Regex.Replace(value, "<[^>]+>", " ");
            var decoded = System.Net.WebUtility.HtmlDecode(plainText);
            decoded = Regex.Replace(decoded, @"PERF-[A-Z0-9-]+", "[test-value]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            return Regex.Replace(decoded, @"\s+", " ").Trim();
        }).Where(value => !string.IsNullOrWhiteSpace(value)));
        return string.IsNullOrWhiteSpace(message) ? null : Redact(message);
    }

    private static void EvaluateBatch(
        int repeat,
        MeasurementBatch batch,
        string datasetFingerprint,
        string revision,
        ICollection<EvaluationEvidence> evaluations,
        int reconciliationIssueCount = 0)
    {
        if (string.Equals(batch.Scenario, "mixed-http", StringComparison.Ordinal))
        {
            return;
        }

        var workload = batch.Samples.Count == 0 ? string.Empty : batch.Samples[0].Workload;
        var kind = workload switch
        {
            "dashboard" => PerformanceWorkloadKind.LoginDashboard,
            "inventory" => PerformanceWorkloadKind.InventoryLookup,
            "receiving" => PerformanceWorkloadKind.ReceivingScan,
            "allocation" => PerformanceWorkloadKind.Allocation,
            "report" => PerformanceWorkloadKind.ReportQuery,
            "item-import" => PerformanceWorkloadKind.BulkImport,
            "background-job" => PerformanceWorkloadKind.BackgroundJob,
            "reconciliation" => PerformanceWorkloadKind.Reconciliation,
            _ => (PerformanceWorkloadKind?)null
        };
        if (!kind.HasValue)
        {
            return;
        }

        var budget = PerformanceWorkloadCatalog.Default.Single(value => value.Workload == kind.Value);
        var samples = batch.Samples;
        var durations = samples.Select(value => (decimal)value.DurationMilliseconds).Order().ToArray();
        var succeeded = samples.Count(value => value.Succeeded);
        var conflicts = samples.Count(value => value.Conflict);
        var total = samples.Count;
        var elapsedSeconds = Math.Max((decimal)(batch.ElapsedMilliseconds / 1_000d), 0.001m);
        var metadata = new PerformanceRunMetadata(
            "LocalQualification",
            datasetFingerprint,
            $"{RuntimeInformation.OSDescription}; {Environment.ProcessorCount} logical processors; {GC.GetGCMemoryInfo().TotalAvailableMemoryBytes} available bytes",
            revision,
            batch.StartedAtUtc,
            batch.CompletedAtUtc);
        var run = new PerformanceRunResult(
            budget,
            metadata,
            succeeded / elapsedSeconds,
            Percentile(durations, 50),
            Percentile(durations, 95),
            Percentile(durations, 99),
            total == 0 ? 1m : (total - succeeded) / (decimal)total,
            total == 0 ? 0m : conflicts / (decimal)total,
            reconciliationIssueCount,
            BusinessOutcomeAssertionsPassed: true);
        var result = PerformanceBudgetEvaluator.Evaluate(run);
        if (result.IsFailure)
        {
            evaluations.Add(new EvaluationEvidence(
                repeat,
                batch.Scenario,
                workload,
                batch.Concurrency,
                run,
                new PerformanceEvaluation(
                    kind.Value,
                    false,
                    [$"invalid-run:{result.Error}"],
                    metadata,
                    run.ThroughputPerSecond,
                    run.P50Milliseconds,
                    run.P95Milliseconds,
                    run.P99Milliseconds)));
            return;
        }

        evaluations.Add(new EvaluationEvidence(
            repeat,
            batch.Scenario,
            workload,
            batch.Concurrency,
            run,
            result.Value));
    }

    private static decimal Percentile(decimal[] sorted, int percentile)
    {
        if (sorted.Length == 0)
        {
            return 0m;
        }
        var index = Math.Clamp((int)Math.Ceiling(percentile / 100d * sorted.Length) - 1, 0, sorted.Length - 1);
        return decimal.Round(sorted[index], 3);
    }

    private static object[] BuildRepeatVariance(IEnumerable<EvaluationEvidence> evaluations) =>
        evaluations
            .GroupBy(value => new { value.Scenario, value.Workload, value.Concurrency })
            .Select(group =>
            {
                var throughput = group.Select(value => value.Run.ThroughputPerSecond).Order().ToArray();
                var p95 = group.Select(value => value.Run.P95Milliseconds).Order().ToArray();
                return (object)new
                {
                    group.Key.Scenario,
                    group.Key.Workload,
                    group.Key.Concurrency,
                    repeatCount = group.Count(),
                    throughputMinimum = throughput.First(),
                    throughputMaximum = throughput.Last(),
                    throughputSpreadPercent = throughput.First() == 0m
                        ? (decimal?)null
                        : decimal.Round((throughput.Last() - throughput.First()) / throughput.First() * 100m, 2),
                    p95MinimumMilliseconds = p95.First(),
                    p95MaximumMilliseconds = p95.Last(),
                    p95SpreadPercent = p95.First() == 0m
                        ? (decimal?)null
                        : decimal.Round((p95.Last() - p95.First()) / p95.First() * 100m, 2)
                };
            })
            .ToArray();

    private static void WriteEvidence(object evidence)
    {
        var path = Environment.GetEnvironmentVariable("WARECOMMAND_PERFORMANCE_EVIDENCE_PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var json = JsonSerializer.Serialize(evidence, EvidenceJsonOptions);
        var bytes = Encoding.UTF8.GetByteCount(json);
        if (bytes > PerformanceQualificationPolicy.MaximumEvidenceBytes)
        {
            throw new InvalidOperationException(
                $"Performance evidence was {bytes} bytes, over the {PerformanceQualificationPolicy.MaximumEvidenceBytes}-byte limit.");
        }
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
        File.WriteAllText(path, json);
    }

    private static object SummarizeBatch(MeasurementBatch batch)
    {
        var samples = batch.Samples;
        var durations = samples.Select(value => value.DurationMilliseconds).Order().ToArray();
        return new
        {
            batch.Name,
            batch.Scenario,
            workload = samples.Count == 0 ? "unknown" : samples[0].Workload,
            batch.Concurrency,
            sampleCount = samples.Count,
            successCount = samples.Count(value => value.Succeeded),
            errorCount = samples.Count(value => !value.Succeeded),
            conflictCount = samples.Count(value => value.Conflict),
            statusCodes = samples.Where(value => value.StatusCode.HasValue)
                .GroupBy(value => value.StatusCode!.Value)
                .OrderBy(group => group.Key)
                .ToDictionary(group => group.Key.ToString(CultureInfo.InvariantCulture), group => group.Count()),
            errorTypes = samples.Where(value => !string.IsNullOrWhiteSpace(value.ErrorType))
                .GroupBy(value => value.ErrorType!, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal),
            batch.ElapsedMilliseconds,
            throughputPerSecond = batch.ElapsedMilliseconds <= 0
                ? 0m
                : decimal.Round(samples.Count(value => value.Succeeded) / (decimal)(batch.ElapsedMilliseconds / 1_000d), 3),
            p50Milliseconds = Percentile(durations, 50),
            p95Milliseconds = Percentile(durations, 95),
            p99Milliseconds = Percentile(durations, 99),
            batch.StartedAtUtc,
            batch.CompletedAtUtc
        };
    }

    private static int ReadBoundedInteger(string name, int fallback, int minimum, int maximum)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed < minimum || parsed > maximum)
        {
            throw new InvalidOperationException($"{name} must be between {minimum} and {maximum}.");
        }
        return parsed;
    }

    private static ResourceProfile GetResourceProfile()
    {
        var memory = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        var container = Environment.GetEnvironmentVariable("WARECOMMAND_TEST_POSTGRES_CONTAINER") ?? "unknown";
        var limits = ReadDockerResourceLimits(container);
        var connections = ReadPostgreSqlConnectionProfile(
            Environment.GetEnvironmentVariable("WARECOMMAND_TEST_POSTGRES_CONNECTION"));
        return new ResourceProfile(Environment.ProcessorCount, memory, limits with
        {
            ContainerName = container
        }, connections);
    }

    private static ExtendedContentionDecision EvaluateExtendedContentionSupport(ResourceProfile profile)
    {
        string? hostBlockReason = null;
        if (profile.ProcessorCount < 4)
        {
            hostBlockReason = "host exposes fewer than four logical processors";
        }
        else if (profile.AvailableMemoryBytes < 8L * 1024 * 1024 * 1024)
        {
            hostBlockReason = "host exposes less than 8 GiB of available memory";
        }
        else if (!profile.PostgreSqlContainer.Available)
        {
            hostBlockReason = "PostgreSQL container resource limits could not be read";
        }
        else if (profile.PostgreSqlContainer.MemoryBytes > 0 && profile.PostgreSqlContainer.MemoryBytes < 2L * 1024 * 1024 * 1024)
        {
            hostBlockReason = "PostgreSQL container is limited to less than 2 GiB";
        }
        else if (profile.PostgreSqlContainer.NanoCpus > 0 && profile.PostgreSqlContainer.NanoCpus < 2_000_000_000)
        {
            hostBlockReason = "PostgreSQL container is limited to fewer than two CPUs";
        }

        var levels = new List<ExtendedContentionLevelDecision>(capacity: 2);
        foreach (var concurrency in new[] { 50, 100 })
        {
            var poolLimit = Math.Clamp(Math.Max(16, concurrency + 8), 16, 128);
            var requiredConnections = poolLimit + 16;
            var reason = hostBlockReason;
            if (reason is null && !profile.PostgreSqlConnections.Available)
            {
                reason = "PostgreSQL connection capacity could not be read";
            }
            else if (reason is null && profile.PostgreSqlConnections.AvailableConnectionSlots < requiredConnections)
            {
                reason = $"PostgreSQL has {profile.PostgreSqlConnections.AvailableConnectionSlots} non-reserved connection slots; " +
                         $"the bounded pool needs {poolLimit} plus 16 fixture and observer connections";
            }

            levels.Add(new ExtendedContentionLevelDecision(
                concurrency,
                poolLimit,
                requiredConnections,
                profile.PostgreSqlConnections.AvailableConnectionSlots,
                reason is null,
                reason ?? "host and PostgreSQL connection capacity pass the bounded resource gate"));
        }

        return new ExtendedContentionDecision(true, levels);
    }

    private static PostgreSqlConnectionProfile ReadPostgreSqlConnectionProfile(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return new PostgreSqlConnectionProfile(false, 0, 0, 0, 0, "PostgreSQL test connection is unavailable");
        }

        try
        {
            var connectionBuilder = new NpgsqlConnectionStringBuilder(connectionString)
            {
                ApplicationName = "WareCommand.PerformanceResourceGate",
                Pooling = false
            };
            using var connection = new NpgsqlConnection(connectionBuilder.ConnectionString);
            connection.Open();
            using var command = new NpgsqlCommand(
                "SELECT current_setting('max_connections')::integer, " +
                "current_setting('superuser_reserved_connections')::integer + " +
                "COALESCE(NULLIF(current_setting('reserved_connections', true), '')::integer, 0), " +
                "(SELECT COUNT(*)::integer FROM pg_stat_activity)",
                connection);
            using var reader = command.ExecuteReader();
            if (!reader.Read())
            {
                return new PostgreSqlConnectionProfile(false, 0, 0, 0, 0, "PostgreSQL returned no connection-capacity row");
            }

            var maximumConnections = reader.GetInt32(0);
            var reservedConnections = reader.GetInt32(1);
            var currentConnections = reader.GetInt32(2);
            var availableConnectionSlots = Math.Max(
                0,
                maximumConnections - reservedConnections - currentConnections + 1);
            return new PostgreSqlConnectionProfile(
                true,
                maximumConnections,
                reservedConnections,
                currentConnections,
                availableConnectionSlots,
                null);
        }
        catch (Exception exception)
        {
            return new PostgreSqlConnectionProfile(
                false,
                0,
                0,
                0,
                0,
                $"{exception.GetType().Name}: {Redact(exception.Message)}");
        }
    }

    private static DockerResourceLimits ReadDockerResourceLimits(string containerName)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "docker",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.StartInfo.ArgumentList.Add("inspect");
            process.StartInfo.ArgumentList.Add("--format");
            process.StartInfo.ArgumentList.Add("{{.HostConfig.Memory}}|{{.HostConfig.NanoCpus}}|{{.Config.Image}}");
            process.StartInfo.ArgumentList.Add(containerName);
            if (!process.Start())
            {
                return new DockerResourceLimits(containerName, 0, 0, "unknown", false, "docker process did not start");
            }
            var output = process.StandardOutput.ReadToEnd().Trim();
            var error = process.StandardError.ReadToEnd().Trim();
            process.WaitForExit(10_000);
            if (process.ExitCode != 0)
            {
                return new DockerResourceLimits(containerName, 0, 0, "unknown", false, Redact(error));
            }
            var parts = output.Split('|');
            if (parts.Length != 3 || !long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var memory) ||
                !long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var nanoCpus))
            {
                return new DockerResourceLimits(containerName, 0, 0, "unknown", false, "docker inspect returned an unrecognized resource profile");
            }
            return new DockerResourceLimits(containerName, memory, nanoCpus, parts[2], true, null);
        }
        catch (Exception exception)
        {
            return new DockerResourceLimits(containerName, 0, 0, "unknown", false, exception.GetType().Name);
        }
    }

    private static string QuoteIdentifier(string value) =>
        $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private static string Redact(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }
        var password = Environment.GetEnvironmentVariable("WARECOMMAND_TEST_POSTGRES_PASSWORD");
        var redacted = string.IsNullOrWhiteSpace(password)
            ? value
            : value.Replace(password, "[redacted]", StringComparison.Ordinal);
        return redacted.Length <= 500 ? redacted : redacted[..500];
    }

    private sealed class PerformanceMeasurements : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly ConcurrentDictionary<string, ConcurrentQueue<double>> _histograms = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, double> _counters = new(StringComparer.Ordinal);

        public PerformanceMeasurements()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name.StartsWith("WareCommand", StringComparison.Ordinal) ||
                    instrument.Meter.Name.StartsWith("Npgsql", StringComparison.Ordinal))
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<double>((instrument, measurement, _, _) =>
                _histograms.GetOrAdd(instrument.Name, _ => new ConcurrentQueue<double>()).Enqueue(measurement));
            _listener.SetMeasurementEventCallback<long>((instrument, measurement, _, _) =>
                _counters.AddOrUpdate(instrument.Name, measurement, (_, total) => total + measurement));
            _listener.SetMeasurementEventCallback<int>((instrument, measurement, _, _) =>
                _counters.AddOrUpdate(instrument.Name, measurement, (_, total) => total + measurement));
            _listener.Start();
        }

        public object Snapshot() => new
        {
            counters = _counters.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            histograms = _histograms.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(
                    pair => pair.Key,
                    pair =>
                    {
                        var values = pair.Value.Order().ToArray();
                        return new
                        {
                            sampleCount = values.Length,
                            p50 = Percentile(values, 50),
                            p95 = Percentile(values, 95),
                            p99 = Percentile(values, 99),
                            maximum = values.Length == 0 ? 0d : values[^1]
                        };
                    },
                    StringComparer.Ordinal)
        };

        public void Dispose() => _listener.Dispose();

        internal static double Percentile(double[] sorted, int percentile)
        {
            if (sorted.Length == 0)
            {
                return 0d;
            }
            var index = Math.Clamp((int)Math.Ceiling(percentile / 100d * sorted.Length) - 1, 0, sorted.Length - 1);
            return Math.Round(sorted[index], 3);
        }
    }

    private sealed class PostgreSqlActivityMonitor(
        string connectionString,
        string applicationName) : IAsyncDisposable
    {
        private readonly CancellationTokenSource _stop = new();
        private readonly ConcurrentQueue<PostgreSqlActivitySample> _samples = new();
        private Task? _monitorTask;
        private string? _lastError;

        public void Start() => _monitorTask = MonitorAsync(_stop.Token);

        public async Task<PostgreSqlActivityEvidence> StopAsync()
        {
            _stop.Cancel();
            if (_monitorTask is not null)
            {
                await _monitorTask;
            }
            var samples = _samples.ToArray();
            return new PostgreSqlActivityEvidence(
                samples.Length,
                samples.Select(value => value.Active).DefaultIfEmpty().Max(),
                samples.Select(value => value.LockWaiters).DefaultIfEmpty().Max(),
                samples.Select(value => value.Idle).DefaultIfEmpty().Max(),
                samples.Length == 0 ? 0d : Math.Round(samples.Average(value => value.Active), 3),
                samples.Length == 0 ? 0d : Math.Round(samples.Average(value => value.Idle), 3),
                samples.Length == 0 ? 0d : Math.Round(samples.Average(value => value.LockWaiters), 3),
                samples.Length == 0 ? null : samples[0].CapturedAtUtc,
                samples.Length == 0 ? null : samples[^1].CapturedAtUtc,
                _lastError);
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync();
            _stop.Dispose();
        }

        private async Task MonitorAsync(CancellationToken cancellationToken)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
            try
            {
                while (await timer.WaitForNextTickAsync(cancellationToken))
                {
                    await using var connection = new NpgsqlConnection(connectionString);
                    await connection.OpenAsync(cancellationToken);
                    await using var command = new NpgsqlCommand(
                        "SELECT COUNT(*) FILTER (WHERE state = 'active'), COUNT(*) FILTER (WHERE state = 'idle'), COUNT(*) FILTER (WHERE wait_event_type = 'Lock') FROM pg_stat_activity WHERE datname = current_database() AND application_name = @applicationName",
                        connection);
                    command.Parameters.AddWithValue("applicationName", applicationName);
                    await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                    if (await reader.ReadAsync(cancellationToken))
                    {
                        _samples.Enqueue(new PostgreSqlActivitySample(
                            DateTimeOffset.UtcNow,
                            reader.GetInt64(0),
                            reader.GetInt64(1),
                            reader.GetInt64(2)));
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                _lastError = exception.GetType().Name;
            }
        }
    }

    private static decimal Percentile(double[] sorted, int percentile) =>
        (decimal)PerformanceMeasurements.Percentile(sorted, percentile);

    private sealed record PerformanceSample(
        string Workload,
        string Scenario,
        int Concurrency,
        double DurationMilliseconds,
        double BatchDurationMilliseconds,
        bool Succeeded,
        bool Conflict,
        int? StatusCode,
        string? ErrorType,
        string? ErrorMessage);

    private sealed record MeasurementBatch(
        string Name,
        string Scenario,
        int Concurrency,
        double ElapsedMilliseconds,
        IReadOnlyList<PerformanceSample> Samples,
        DateTimeOffset StartedAtUtc,
        DateTimeOffset CompletedAtUtc);

    private sealed record RepeatBatchEvidence(int Repeat, MeasurementBatch Batch);

    private sealed record RequestLimiterEvidence(
        int Repeat,
        int GlobalPermitLimit,
        int ReportPermitLimit,
        int ApiPermitLimit,
        int ScanningPermitLimit,
        int ImportPermitLimit);

    private sealed record EvaluationEvidence(
        int Repeat,
        string Scenario,
        string Workload,
        int Concurrency,
        PerformanceRunResult Run,
        PerformanceEvaluation Evaluation);

    private sealed record RepeatEvidence(
        int Repeat,
        DataGenerationRunReport Dataset,
        object Diagnostics,
        InventoryReconciliationReportDto Reconciliation,
        IReadOnlyList<MeasurementBatch> Batches);

    private sealed record OrderWorkRecord(int SalesOrderId, string SalesOrderNumber, string AllocationKey);

    private sealed record AllocationWorkRecord(
        int SalesOrderId,
        string SalesOrderNumber,
        SalesOrderAllocationCommand Command,
        decimal AllocatedQuantity,
        decimal BackorderQuantity,
        int[] WorkIds);

    private sealed record LimitedStockEvidence(
        IReadOnlyList<MeasurementBatch> MovementBatches,
        IReadOnlyList<MeasurementBatch> AllocationBatches,
        IReadOnlyList<MeasurementBatch> PickBatches,
        object Assertions);

    private sealed record WorkerEvidence(MeasurementBatch JobBatch, object Diagnostics);

    private sealed record PostgreSqlActivitySample(
        DateTimeOffset CapturedAtUtc,
        long Active,
        long Idle,
        long LockWaiters);

    private sealed record PostgreSqlActivityEvidence(
        int SampleCount,
        long MaximumActiveConnections,
        long MaximumWaitingForLock,
        long MaximumIdleConnections,
        double AverageActiveConnections,
        double AverageIdleConnections,
        double AverageLockWaiters,
        DateTimeOffset? FirstSampleAtUtc,
        DateTimeOffset? LastSampleAtUtc,
        string? Error);

    private sealed record ResourceProfile(
        int ProcessorCount,
        long AvailableMemoryBytes,
        DockerResourceLimits PostgreSqlContainer,
        PostgreSqlConnectionProfile PostgreSqlConnections);

    private sealed record PostgreSqlConnectionProfile(
        bool Available,
        int MaximumConnections,
        int ReservedConnections,
        int CurrentConnections,
        int AvailableConnectionSlots,
        string? Error);

    private sealed record ExtendedContentionLevelDecision(
        int Concurrency,
        int PoolLimit,
        int RequiredConnections,
        int AvailableConnectionSlots,
        bool Supported,
        string Reason);

    private sealed record DockerResourceLimits(
        string ContainerName,
        long MemoryBytes,
        long NanoCpus,
        string Image,
        bool Available,
        string? Error);

    private sealed record ExtendedContentionDecision(
        bool Requested,
        IReadOnlyList<ExtendedContentionLevelDecision> Levels);
}
