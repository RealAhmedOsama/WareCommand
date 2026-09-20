# Inventory command idempotency

WareCommand has two different idempotency boundaries:

- InventoryTransactions keeps immutable ledger entry keys. It prevents one
  ledger entry/sequence from being inserted twice.
- InventoryCommandIdempotencies keeps the command envelope. It protects a
  user, scanner, API client, or job from repeating a whole inventory command
  and stores the original result reference/payload for replay.

## Command contract

An inventory mutation may carry the conventional Idempotency-Key HTTP header.
The request context normalizes and carries that value into the application.
Desktop callers and future scanner/integration adapters can initialize the same
context directly.

The durable identity is (CallerScope, CommandKey). The current scope is the
source client, actor, and warehouse, so the same key can be used independently
by different callers or warehouses. The request hash is SHA-256 over the
operation name and serialized command payload. Reusing a key with a different
operation or payload returns idempotency.payload_mismatch.

Each record stores:

- operation type, caller scope, request hash, correlation ID, actor, and
  warehouse;
- InProgress, Succeeded, Failed, or Expired state;
- serialized result type/payload and a stable result reference;
- start, completion, expiry, and revision/concurrency metadata.

The unique database index is the final guard against concurrent claims. A
duplicate successful claim returns the persisted result. An active in-progress
claim returns a retryable idempotency.in_progress conflict. Failed or expired
records can be reclaimed with the same hash. A caller must complete and save
the idempotency record in the same explicit database transaction as its
quantity-changing work.

## Current wiring

The receipt, putaway, pick, and stock-adjustment application commands now:

1. validate the request and resolve its warehouse boundary;
2. start an explicit transaction when an idempotency key is present;
3. claim or replay the durable command record;
4. save the movement/ledger mutation and succeeded replay payload together;
5. roll back the claim and mutation together on cancellation or failure.

The recurring cleanup job expires stale in-progress records and prunes
non-active records after the retention boundary. The default retention is
30 days and callers may request up to 365 days.

## Remaining scope

This slice does not close the entire backlog issue. Reservation/allocation,
pack/ship/return/transfer/count approval, document-line completion, integration
imports, and PostgreSQL parallel contention qualification still need their
own command adapters and tests. Those paths must use this contract before
issue #36 can be closed. No production migration or deployment is implied by
the checked-in migration.
