# WareCommand observability

WareCommand exposes vendor-neutral OpenTelemetry traces and metrics from the MVC
host. The SDK is enabled by default, but no exporter is enabled in the base
configuration. This keeps local startup deterministic and lets each environment
choose an OTLP collector or the development console exporter.

## Health endpoints

The endpoints are intentionally anonymous and return only `Healthy` or
`Unhealthy`; they never return connection strings, exception messages, migration
names, or other dependency details.

| Endpoint | Meaning | Probe use |
| --- | --- | --- |
| `GET /health/live` | The process and HTTP host are serving requests. | Container liveness |
| `GET /health/ready` | Startup completed and database, schema, storage, job state, and critical configuration checks are healthy. | Traffic readiness |

Readiness checks are:

- PostgreSQL reachability and pending-migration detection;
- configured application storage and volume free space;
- durable-job runner/storage state when `Wms:Jobs:Enabled=true`;
- backup freshness and post-create verification when `Wms:Backups:Enabled=true`;
- production Data Protection, database, bootstrap, seed, and telemetry configuration;
- completion of database and Identity startup initialization.

Background jobs remain disabled by default, but issue #21 now provides the
PostgreSQL-backed Hangfire runner. When `Wms:Jobs:Enabled=true`, the host
registers the five bounded queues, stable recurring definitions, the durable
execution ledger, and the admin-only dashboard at `/jobs`. The runner registers
with `WmsBackgroundJobHealthState`, updates `SetQueueBacklog`, and marks storage
unhealthy when monitoring or shutdown state fails. Jobs require PostgreSQL and
the `WmsJobExecutions`/`WmsJobNotifications` migration; the readiness check is
intentionally unhealthy until the runner is registered.

## Traces and metrics

The host instruments ASP.NET Core, outbound `HttpClient`, EF Core, runtime
health, and the shared `WareCommand.Wms` activity source. SQL text, parameters,
payloads, barcodes, serial numbers, user input, and exception messages are not
added by the custom instruments. EF statement capture remains disabled.

Custom activity boundaries include:

- `wms.inventory.receipt`, `wms.inventory.putaway`, `wms.inventory.pick`, and
  `wms.inventory.adjustment`;
- `wms.external.<operation>` for future integration adapters. Adapters must use
  `WmsTelemetry.StartExternalOperation` and keep payloads out of activity tags.

The bounded custom instruments are:

| Instrument | Labels | Use |
| --- | --- | --- |
| `warecommand.requests` | `status_class` | Request count/error ratio |
| `warecommand.request.duration` | `status_class` | Request latency |
| `warecommand.database.command.duration` | `command_kind`, `outcome` | DB latency/failure signal; no SQL text |
| `warecommand.inventory.operations` | fixed `operation`, `outcome` | Receipt/pick/putaway/pack/ship throughput |
| `warecommand.inventory.units` | fixed `operation` | Unit throughput |
| `warecommand.inventory.operation.duration` | fixed `operation`, `outcome` | Inventory operation latency |
| `warecommand.inventory.conflicts` | fixed `operation` | Concurrency/conflict alerting |
| `warecommand.jobs.failures` | bounded `job_kind` | Repeated job-failure alerting |
| `warecommand.jobs.backlog` | none | Queue/backlog alerting from Hangfire monitoring |
| `warecommand.storage.free` | none | Low-disk alerting |
| `warecommand.backups.failures` | bounded `backup_kind` | Failed PostgreSQL backup or replication |

Pack, ship, and external measurements remain extension contracts until their
owning issues add those workflows. The durable-job and backup measurements are
active when their respective features are enabled; integration retry,
cycle-count, and replenishment handlers remain explicit no-op adapters until
their owning domain issues provide pending-work models.

## Export configuration

Configuration keys are also available as environment variables in container
deployments:

```text
Wms__Telemetry__Enabled=true
Wms__Telemetry__ServiceName=WareCommand.Wms.Web
Wms__Telemetry__ConsoleExporterEnabled=false
Wms__Telemetry__SamplingRatio=0.25
Wms__Telemetry__Otlp__Enabled=true
Wms__Telemetry__Otlp__Endpoint=http://otel-collector:4317
Wms__Telemetry__Otlp__Protocol=grpc
```

Standard `OTEL_SERVICE_NAME`, `OTEL_EXPORTER_OTLP_ENDPOINT`,
`OTEL_EXPORTER_OTLP_PROTOCOL`, and `OTEL_TRACES_SAMPLER_ARG` values are also
accepted. `grpc` and `http/protobuf` are supported. The development profile
enables the console exporter for manual local qualification; production keeps
it disabled unless explicitly selected.

## Alert conditions

Alert thresholds are deployment policy, but these conditions must page or create
an operational ticket when sustained:

| Condition | Signal | Initial condition |
| --- | --- | --- |
| Database unhealthy | `/health/ready` database result or `warecommand.database.command.duration` failures | Two failed readiness checks or DB failures for 2 minutes |
| Migration mismatch | Readiness failure plus structured migration event `1202` | Any production occurrence |
| High request error rate | `warecommand.requests{status_class="5xx"}` divided by all requests | Above 2% for 5 minutes |
| Slow operations | `warecommand.request.duration` and `warecommand.inventory.operation.duration` percentiles | p95 above the configured slow-operation threshold for 10 minutes |
| Repeated job failures | `warecommand.jobs.failures` | Five failures in 5 minutes, or runner health is unhealthy |
| Queue backlog | `warecommand.jobs.backlog` | Above the runner-specific ceiling for 10 minutes |
| Low disk | `warecommand.storage.free` or readiness storage result | Below the configured `Wms:Health:MinimumFreeBytes` |
| Backup failure | `warecommand.backups.failures` or `/health/ready` backup result | Any failure, missing offsite copy, or artifact older than `Wms:Backups:MaximumAgeHours` |

Each alert should link to the [observability runbook](../operations/OBSERVABILITY_RUNBOOK.md),
the [deployment guide](../../DEPLOYMENT.md), and the [logging procedure](LOGGING.md).
