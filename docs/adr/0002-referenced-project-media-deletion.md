# ADR-0002: Preserve deleted project-media identities when referenced

Status: accepted
Date: 2026-08-28

## Context

Users must be able to remove physical media from a ReelForge project even when Saved Frames, Saved Clips, extracted media, compositions, generation records, provenance, or other durable project state still refers to it. Deleting those dependent records would silently destroy history, while permitting arbitrary dangling identifiers would make accidental project corruption indistinguishable from an intentional deletion.

## Decision

Deleting unreferenced physical media removes its asset record and its contained file. Deleting referenced physical media instead marks the asset record as deleted, hides it from ordinary Project Media and new-reference pickers, and removes its contained file. The retained record is a logical tombstone: it preserves the original `AssetId`, content identity, project-relative source record, provenance, and provider-reference history so existing durable references remain structurally valid but degraded.

The deletion command remains user-driven and requires an explicit warning. It does not cascade, repair, reconcile, materialize, export, prune cache entries, or authorize any provider operation.

## Consequences

- A referenced item disappears from normal Project Media immediately, but dependent objects may no longer materialize or preview successfully.
- Project validation and persistence can distinguish an intentional deletion from an unexplained missing identifier without weakening reference invariants globally.
- A later Cleanup Project feature may offer reconciliation and affected-item guidance, but that work is outside this decision.
- The additive deleted-state field defaults to false, so the current unpublished development format does not require a migration step or format-version break.

## Alternatives considered

- Blocking deletion while dependencies exist was rejected because it prevents the user from controlling their project media.
- Cascading deletion or rewriting dependent state was rejected because it destroys history and belongs to an explicit cleanup workflow.
- Allowing unrestricted dangling asset identifiers was rejected because it would weaken validation for accidental corruption as well as intentional deletion.

## Verification and follow-up

Automated tests cover unreferenced removal, referenced tombstoning, save/reopen preservation, rollback after save failure, and exclusion from ordinary projections. Manual acceptance verifies the warning, list removal, physical-file removal, project reopen, and degraded dependent behavior without provider or network activity.
