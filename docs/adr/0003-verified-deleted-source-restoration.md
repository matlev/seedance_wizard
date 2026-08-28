# ADR-0003: Restore deleted physical identities by verified content match

Status: accepted
Date: 2026-08-28

## Context

ADR-0002 retains a hidden logical tombstone when referenced physical Project Media is deleted. Re-importing the same bytes currently creates a new `AssetId`, so recipes, Saved Frames, generation history, provenance, and compositions that pin the deleted identity remain degraded even though hash-equivalent media is present.

## Decision

ReelForge may restore a deleted physical asset only after SHA-256 verification proves that user-selected bytes match the tombstone's recorded verified identity. Restoration is an explicit user choice, not automatic import deduplication. It reuses the tombstone's original `AssetId`, clears its deleted state, adopts project-relative physical storage, and preserves all existing logical metadata and references.

Import probes may offer **Restore deleted source**, **Import as new**, or **Cancel**. Multiple matching tombstones require explicit selection. An already-imported, unreferenced matching asset may be folded into the chosen tombstone in one project commit, but its bytes are first copied to a separate project-local destination and verified again; the old donor file is retired only after the commit and only while its captured signature still matches. A referenced matching asset remains distinct and supplies bytes through the verified-copy path.

## Consequences

- Existing dependents recover without rewriting their pinned logical references.
- Equal hashes do not generally merge active assets or silently reverse a deletion.
- Unverified identities, different media types, mismatched bytes, cancellation, and failed commits leave the tombstone unchanged.
- Restoration remains local, project-relative, provider-neutral, and unable to authorize network or billable work.
- Cleanup, cache pruning, and broad project reconciliation remain separate features.

## Alternatives considered

- Automatically merging every equal hash was rejected because duplicate logical assets can be intentional and multiple tombstones can share the same content.
- Rewriting every dependent reference to a newly imported `AssetId` was rejected because it would mutate history and exact pinned identities.
- Requiring users to recreate derived work was rejected because the tombstone already preserves the authoritative logical identity needed for safe recovery.

## Verification and follow-up

Tests cover exact matching, mismatch and ambiguity, external restoration, folding an unused imported copy, retaining a referenced copy, cancellation and save rollback, project reopen, and preservation of logical history. Manual acceptance verifies both file-dialog and drag-and-drop import choices without provider activity.
