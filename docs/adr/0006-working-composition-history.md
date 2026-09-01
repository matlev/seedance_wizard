# ADR-0006: Use recipe revisions as Working Composition history

Status: accepted
Date: 2026-08-28

## Context

ReelForge already commits every Working Composition edit as an immutable `RecipeRevision`. The Working Composition's virtual asset stores the exact current revision ID, and each revision stores its predecessor. This is almost the Milestone 4B history foundation, but the current commit method assigns a revision number from the selected predecessor. If a future undo command moves the current pointer backward and the user makes a divergent edit, the new revision can reuse an existing number and make the project invalid.

Adding a separate composition-revision store or persisted undo stack would create a second authority alongside recipes. Persisting UI/session command objects would also extend project meaning beyond the approved requirement: reopen must restore the committed composition revision, but the interactive undo/redo path need not survive restart.

### Architecture preflight

```text
Feature/outcome: Deterministic immutable Working Composition history and divergent-edit behavior.
Existing owners touched: Core RecipeRevision, VideoProject recipe commit semantics, and existing project validation/tests.
Proposed responsibility and extension point: Keep the existing Working Composition virtual asset and RecipeRevision graph authoritative; treat its current-revision pointer as the persisted history cursor.
Dependency and public-contract impact: Core commit semantics become branch-safe; no dependency-direction change or new service.
Persistence/format/compatibility impact: No DTO shape change in this unit. Existing integer revision numbers remain persisted but become unique monotonic ordinals per virtual asset even across branches.
Parallel-workflow or boundary risk: A new composition history store, event log, or persisted command stack is prohibited.
Verification (tests and manual acceptance): Core tests prove linear commits, checkout-plus-divergence, unique ordinals, predecessor identity, retained abandoned branches, and project invariants. Later Application tests prove cursor movement/save rollback and session redo invalidation.
ADR or architecture-debt decision: ADR required because this fixes the durable history and persistence meaning. No architecture debt accepted.
```

## Decision

The existing Working Composition virtual asset remains the single composition identity. Its `CurrentRecipeRevisionId` is the persisted history cursor and identifies the exact immutable composition revision that is authoritative when a project is saved and reopened.

The existing `RecipeRevision` collection is the immutable historical graph. Every new revision:

- receives a revision number one greater than the highest number ever assigned to that virtual asset;
- records the revision selected by the cursor at commit time as its predecessor;
- never mutates or removes an older revision merely because it is no longer on the active edit path.

Moving the cursor does not create a revision. A later divergent edit creates a new child of the checked-out revision. Previously committed descendants remain available to pinned recipes, generation references, caches, diagnostics, and historical inspection, but they are no longer part of the active session redo path.

Application will own the transient undo/redo path and composition commands. Moving backward pushes the prior active revision onto that session path; moving forward selects only the explicitly recorded next revision. A divergent committed edit clears the transient redo path before exposing success. The session path is not project truth and need not reopen after restart. The persisted cursor does reopen exactly.

Project Media import/delete/transfer, Saved Frame or Saved Clip creation, generation work, settings, exports, recovery state, and other out-of-scope operations do not move the Working Composition cursor or create Working Composition revisions.

The candidate multitrack format will replace the contents of `CompositionRecipe` through the existing recipe DTO/mapping boundary. It will not introduce a parallel `CompositionRevision`, composition store, or renderer authority.

## Consequences

- Linear edit history retains the existing behavior and numbering.
- Divergent edits are valid and deterministic without deleting immutable historical evidence.
- Revision number is a unique per-asset commit ordinal, not the depth of a branch.
- `PreviousRevisionId` defines ancestry; callers must not infer ancestry from adjacent revision numbers.
- Reopen restores the active immutable revision but does not promise session redo.
- Later cursor movement must use the existing recovery-aware project save transaction and update the Working Composition draft coherently; this ADR does not authorize UI undo/redo in Milestone 4.

## Alternatives considered

- A separate persisted composition history store was rejected because it duplicates the recipe revision authority.
- Deleting abandoned descendants on divergence was rejected because exact revisions may be pinned by other durable project records.
- Reusing branch depth as revision number was rejected because branches then produce duplicate persisted identities and fail current invariants.
- Persisting a command/event undo log was rejected because commands are Application/session behavior, can change between versions, and are not required to reopen.

## Verification and follow-up

This unit changes only branch-safe revision numbering and focused Core coverage. The later multitrack units must add:

1. immutable track/item/link state inside `CompositionRecipe`;
2. candidate-format DTOs and exact reopen through the existing project store;
3. transactional Application cursor movement and transient redo invalidation;
4. traditional multitrack projection and manual acceptance without session-undo UI.
