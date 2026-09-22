# Putaway rules and location suggestions

Issue #49 adds the first deterministic putaway-rule boundary. `PutawayRule`
records are warehouse-scoped, effective-dated, priority ordered, auditable,
and can be marked simulation-only. Rules may match item/category, supplier,
package and LPN type, inventory status, lot status, temperature requirements,
hazard/storage profile, and source process. They can target a fixed location or
rank valid storage, pick-face, bulk, consolidation, empty, nearest-sequence,
and capacity-aware candidates.

## Evaluation contract

`POST /api/putaway-rules/suggest` evaluates the same input deterministically:

1. authorize the warehouse and load the item/lot context;
2. select active rules whose effective window and matching dimensions apply;
3. reject inactive/non-storage, target-profile, mixed-item/mixed-lot,
   temperature, hazard, and capacity violations;
4. rank candidates by rule priority, strategy score, location priority, code,
   and ID; and
5. return ranked explanations and bounded rejection reasons.

Simulation callers with rule-management permission can evaluate active
simulation rules without making them operational. Rule edits do not mutate
already-created work. New receipt putaway generation asks the suggestion
service for the top destination and snapshots it onto the work line when one
is available.

## Current boundary

The persisted CRUD/evaluation API and SQLite qualification cover precedence,
fixed-location selection, simulation exclusion, and capacity/no-match
explanations. A no-match result explicitly tells the caller to route to the
exception/staging process; #50 owns the durable staging assignment and
exception-resolution workflow. PostgreSQL verification now proves the
warehouse-scoped normalized rule-code index: a duplicate code is rejected in
one warehouse while the same code is valid in another; the disposable suite
passed `23/23` with container cleanup. The current slice still needs query/
volume qualification, concurrent capacity revalidation at execution,
handheld/browser UI, localized RTL/LTR screens, import/export, and richer
packaging-dimensional capacity calculations before #49 can be closed.
