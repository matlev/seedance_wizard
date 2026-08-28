# ADR-0004: Retire degraded derived media without rewriting history

Status: accepted
Date: 2026-08-28

## Context

ADR-0002 allows users to delete referenced physical media by retaining a hidden source tombstone. Saved Frames, Saved Clips, and compositions that pin that source consequently remain structurally valid but may no longer materialize. Users need one explicit way to remove these non-working items instead of accumulating permanent red entries, including when a source is temporarily missing and could otherwise still be relinked.

Deleting every backing asset, anchor, recipe, revision, provenance record, and generation reference would make submitted history invalid. Treating cache contents as authority would make cleanup depend on one machine's disposable state. Silently cascading physical deletion would also remove the opportunity to relink or rescue cached bytes.

## Decision

**Cleanup Project** is a separate, user-confirmed Application operation. It computes the exact transitive dependency graph of active Saved Frames, Saved Clips, and compositions. An item is a cleanup candidate when a required pinned dependency reaches a deleted, missing, inaccessible, or mismatched physical source, or when its exact recipe/anchor revision cannot be resolved.

On stable project open and immediately before cleanup, the existing physical-media availability seam reconciles active sources transactionally before dependency analysis runs. Cleanup then archives affected active frame anchors, tombstones affected virtual assets, and clears the active Working Composition selection when that composition is affected. It commits those changes in one recovery-aware project save. The items disappear from ordinary Project Media and cannot be restored through ordinary UI, but the minimum logical records, immutable recipes/revisions, provenance, and generation snapshots remain so historical references do not become unexplained dangling identifiers.

Physical extracted or detached audio that still has usable durable bytes is not degraded merely because its provenance records an unavailable source. Cleanup does not delete unrelated filesystem orphans, reconcile the project folder, prune cache, render media, or alter provider state. Cache availability is advisory presentation state only; an existing validated cached representation may be exported before cleanup through a separate read path.

## Consequences

- Cleanup is irreversible from the user's active-project perspective even though retained hidden history keeps the project structurally valid.
- Temporarily missing sources are eligible, so confirmation must say that cleanup removes work that relinking could otherwise recover.
- Restoring a physical source after cleanup does not silently reactivate previously cleaned derived items.
- The current unpublished format needs no new parallel authority or machine path. Existing deleted/archive fields represent the retained historical state.
- Automated cleanup remains local and cannot authorize provider, generation, or network work.

## Alternatives considered

- Physically removing every referenced logical record was rejected because it invalidates immutable generation and recipe history.
- Keeping degraded items forever was rejected because it denies users an explicit cleanup workflow.
- Automatically cascading when a source is deleted was rejected because it removes the user's chance to relink or export surviving cache bytes.
- Deleting healthy derived physical audio based only on provenance was rejected because provenance is historical; independently usable durable bytes are not degraded.

## Verification and follow-up

Focused tests cover transitive recipe and anchor degradation, healthy boundaries, missing/deleted/inaccessible/mismatched sources, one-save cleanup, save rollback, retained history, cache isolation, stale cache indexes, and cached export rescue. Manual acceptance verifies red/yellow presentation, confirmation, cached guidance/export, cleanup/reopen, and zero provider activity.
