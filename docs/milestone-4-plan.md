# Milestone 4 plan — Project Trust and Multitrack Foundation

## Status and purpose

This is the approved implementation plan for Milestone 4 and the tentative dependency shape for Milestones 5 and 6. It refines Slices A-D from the [ReelForge 1.0 product definition](reelforge-1.0-product-definition.md) without authorizing feature implementation in this planning work unit.

Milestone 4 contains only project recovery/relink and the persisted multitrack foundation. It is the current high-risk persistence milestone, not a general editor-feature milestone.

Milestone 4 outcome:

> A ReelForge project can be safely recovered and relinked, and its candidate multitrack composition state reopens with identical identities, timing, ordering, provenance, and history position.

Implementation order is contract-first rather than two isolated persistence rewrites:

1. define the Phase 4A lifecycle, recovery-envelope, and relink contracts plus their fault-injection harness;
2. implement bounded 4A behavior without duplicating project DTO mapping inside the recovery envelope;
3. establish the Phase 4B track/time/history model and candidate project format;
4. complete final combined 4A/4B recovery, relink, persistence, and restart acceptance against that candidate format.

## Non-negotiable boundaries

- Core owns portable track, item, time, link, revision, and history invariants.
- Application owns lifecycle state, recovery/relink use cases, composition commands, and render-plan contracts.
- Infrastructure owns project serialization, atomic file operations, hashing, recovery artifacts, and concrete media integration.
- Platform.Windows and App own Windows integration and WPF presentation; neither may become project truth.
- Project meaning remains project-relative, cache-independent, engine-neutral, and portable to a future non-Windows host.
- Recovery data is not a second project authority, a cache entry, or composition undo history.
- No startup, autosave, recovery, relink, or automated-test path may authorize or submit provider work.
- No commerce, accounts, Ingots, entitlements, installers, signing, packaged-runtime selection, macOS implementation, or store publication enters Milestones 4-6.
- Each substantial implementation unit follows the architecture preflight and completion process in [architecture governance](architecture-governance.md).

## Phase 4A — Recovery and verified relinking

### 4A.1 Lifecycle contract

Define explicit, testable workspace states for clean, dirty, saving, saved, recovery available, recovered, degraded, and failed save/recovery conditions. State transitions belong to the Application lifecycle rather than WPF event handlers or mutable persistence DTOs.

The last successfully committed project remains authoritative. One project-local recovery candidate may exist outside disposable cache. It is version-bound to the serialized project representation but must not duplicate field-by-field project mapping or become an independently editable project format.

### 4A.2 Crash-safe recovery

- Extend the existing atomic project-file replacement with a bounded recovery protocol.
- Preserve the last committed project when a save, process, or host is interrupted.
- After abnormal termination, clearly offer the recovery candidate and allow the user to open or discard it.
- Opening a candidate creates recovered working state; it does not silently rewrite the committed project.
- Normal clean shutdown removes or invalidates obsolete recovery state.
- A successful explicit save makes the recovered working state authoritative and retires the obsolete candidate.
- Failure to persist or validate recovery evidence must fail closed and leave the committed project untouched.

### 4A.3 Degraded-source diagnosis

For every project-owned physical asset, distinguish at least:

- expected contained path present with verified content;
- expected path missing;
- candidate selected but inaccessible;
- candidate bytes do not match the expected SHA-256;
- candidate verified and relinked.

Diagnostics identify affected assets and dependent recipes, anchors, generation snapshots, and derived objects without mutating them. A degraded project remains inspectable and savable where doing so cannot corrupt meaning.

### 4A.4 Verified relink transaction

- Relink is initiated by the user for an existing physical asset record.
- The selected candidate is hashed before acceptance and must match the asset's existing verified SHA-256 identity.
- A successful relink preserves `AssetId`, recipes, anchors, generation snapshots, provenance, provider-reference history, and exact logical references.
- The accepted bytes return to project-controlled, project-relative storage; no absolute external path enters the project file.
- A content mismatch cannot replace the existing identity. The user may deliberately import those bytes as new media through the ordinary import workflow.
- Partial copies, failed saves, or cancellation roll back without claiming a successful relink.

### 4A.5 Explicit project relocation and derived-media cleanup

- The active project folder can be moved without changing project identity, portable meaning, or project-relative references. The move preserves the complete folder; it is distinct from Clone Project, which creates a new identity and omits cache/recovery artifacts.
- Relocation is allowed only from a stable clean, saved, or degraded workspace and while no active or unreconciled generation job targets the old path. Unsafe lifecycle states must be resolved explicitly.
- Same-volume relocation uses a directory rename. Cross-volume relocation stages and validates a complete copy before publication, rebinds the exact active workspace, updates recent/last-project paths, and retires the source after the destination is authoritative.
- On stable project open and immediately before cleanup, active physical-media availability is reconciled transactionally before Project Media derives a red degraded state for Saved Frames, Saved Clips, and compositions whose pinned dependencies reach deleted or unavailable physical media; missing physical media retains its distinct yellow state.
- **Cleanup Project** is an explicit irreversible action. It removes degraded items from active Project Media in one recovery-aware save while retaining only the hidden tombstone/archive records required for exact history and invariant validity. Unsafe lifecycle states and active or unreconciled project jobs must be resolved first.
- Cache availability is advisory and machine-local. A surviving indexed representation may be exported before cleanup, but cleanup never treats cache as project truth, prunes cache, renders media, or authorizes provider activity.

### Phase 4A acceptance

- A killed process leaves either the last committed project or one valid, clearly offered recovery candidate.
- Dirty, saving, saved, recovery-available, recovered, degraded, and failed states follow the documented transition contract.
- Recovery is never silently activated and never silently overwrites the committed project.
- Clean shutdown and successful explicit save retire obsolete recovery data deterministically.
- Missing, inaccessible, mismatched, and verified media states produce distinct actionable results.
- Relink accepts only matching SHA-256 bytes and preserves all logical identities and dependencies.
- Relocation preserves the complete project and stable identity, rejects unsafe paths/lifecycle/job state, and reopens from the new location.
- Degraded derived media is visibly distinct from missing physical media; cleanup is explicit, transactional, history-safe, and cache-independent.
- Recovery and relink preserve immutable recipes, anchors, generation snapshots, provenance, and cache independence.
- Fault-injection coverage exercises interrupted serialization, interrupted replacement, invalid recovery bytes, relink copy failure, save failure, cancellation, and restart.
- Startup, recovery, autosave, and tests cannot reach provider submission authorization.

Phase 4A risk: **Medium to high**.

## Phase 4B — Track/time model and candidate format correction

### 4B.1 Authoritative multitrack model

Define a scalable persisted Working Composition with:

- ordered video and audio tracks with stable identities;
- timeline items with independent stable identities;
- persisted empty tracks until explicitly deleted;
- lock on both track types, visibility on video tracks, and mute on audio tracks;
- explicit source asset/revision, selected stream, source range, and composition placement;
- one explicit link-group relationship for synchronized timeline items.

Track list positions and view indexes are not identities. Track controls and link relationships are Core/Application concepts rather than WPF state.

Importing video into Project Media does not create timeline items or modify the Working Composition. Placing or inserting video with a usable selected audio stream creates one video item and one source-backed audio item with independent identities and one shared link-group relationship. Both occurrences reference the exact source asset/revision, selected stream, and source range. Video without usable audio creates only the video item.

While items remain linked, later structural commands may treat them as one synchronized unit according to their command contract. The independent identities allow that relationship to be removed without replacing either item.

The following operations remain distinct:

- **Unlink audio** removes only the timeline link relationship and creates no media file. Its user-facing command may wait for Milestone 5.
- **Detach audio…** preserves the existing exact-occurrence behavior: materialize the selected occurrence's exact audio range into a durable physical Audio asset, rebind or replace the source-backed audio occurrence, remove the link relationship, and prevent the original source-audio route from doubling in the mix.
- Project Media **Extract audio…** remains whole-source durable extraction, independent of timeline occurrences.

### 4B.2 Exact time domains

- Persist exact rational/presentation timing for video.
- Persist an explicit sample-aware or equivalently exact audio time representation.
- Define deterministic checked conversions at UI, media-engine, and display boundaries.
- Do not treat floating-point seconds as authoritative stored timing.
- Do not force audio timing through video frame anchors.
- Preserve source time, composition time, and selected-stream identity distinctly.

**Pre-DTO decision gate:** the primary agent must document and review the exact persisted audio-time representation, conversion/rounding rules, invariants, and media-engine boundary before track/item DTO implementation begins. This is an architectural decision, not a mapper detail to be invented during implementation.

Timing readiness is separately classified as Exact, Estimated, or Unusable according to [ADR-0007](adr/0007-degraded-timing-placement.md). A durable import may remain timeline-usable with acknowledged Estimated timing only when its versioned assessment pins verified source identity, deterministic stream identity, dependable sequential decode, a finite positive frozen span, and specific unresolved issues. Video and audio are assessed independently. Historical occurrences never silently adopt later analysis; unusable audio requires an explicit video-only choice, and unusable video blocks placement. Repair execution is not part of this contract unit.

### 4B.3 Track policy and contribution contract

- Locked tracks reject any command that would mutate their items, timing, or track-owned properties.
- Cross-track commands fail before mutation when an affected track is locked; they never silently skip it.
- Invisible video tracks contribute no video.
- Muted audio tracks contribute no audio.
- Solo is deferred.

Milestone 4 makes this policy authoritative and proves model, command-rejection, and structural contribution-plan behavior without claiming final media-output correctness. Milestone 6 consumes the same contribution contract and proves it in executable preview and export media. There must be one contribution policy, not UI-only flags or parallel renderer interpretations.

### 4B.4 Immutable composition revisions and history cursor

The history cursor governs committed Working Composition/edit state, including track and item identities and later structural/property edits. It defines:

- the exact active immutable composition revision;
- previous/next relationships or an equivalent unambiguous history topology;
- deterministic divergent-edit behavior;
- identity rules used by future cache, preview, export, and dependency analysis.

It does not govern Project Media import/delete/transfer, Saved Frame or Saved Clip creation, generation drafts/jobs, application settings, external exports, or recovery state. Milestone 4 establishes the revision and cursor contract but does not implement Milestone 5's interactive undo/redo stack or UI.

### 4B.5 Candidate project-format correction

Milestone 4 establishes the current candidate development project format. It may clearly reject obsolete development formats and does not need an internal migration ladder.

Milestone 4 does **not** permanently end clean format breaks or create a customer compatibility obligation. Clean breaks remain permitted until the owner explicitly declares the first externally supported beta project-format baseline. Persistence and version-reader boundaries must nevertheless remain migration-capable.

The future compatibility boundary must be recorded by an explicit supported-format declaration and marker. It is not inferred from a milestone, build number, or the name `1.0.0`. Once declared, later breaking changes require an approved migration or compatibility policy.

### 4B.6 Traditional multitrack projection

Milestone 4 includes:

- track creation and explicit deletion;
- track reordering;
- track and item selection;
- video visibility, audio mute, and both-track lock controls;
- adaptation of existing basic composition behavior to the new model;
- unambiguous migration of the current Detach audio behavior.

It excludes new edge trims, ripple operations, generic replacement, snapping, markers, and session undo/redo UI. Presentation consumes the authoritative model and must not own persistence, history, link, or timing policy.

### Phase 4B acceptance

- Multiple ordered video/audio tracks, including intentionally empty tracks, reopen with identical stable identities and order.
- Timeline items, link groups, exact selected streams/ranges, and track controls round-trip exactly.
- Placing video with usable audio creates linked source-backed occurrences without creating a durable extracted Project Media asset.
- Video without usable audio creates only a video item.
- Unlink, Detach, and whole-source Extract semantics are distinct in contracts and tests; existing Detach behavior remains exact and non-doubling.
- Locked-track commands and affected cross-track operations reject before mutation; visibility/mute contribution policy is deterministic.
- Video and audio time-domain boundary cases round-trip without floating-point or frame-anchor reinterpretation.
- Exact, acknowledged Estimated, and Unusable timing assessments produce deterministic independent video/audio placement decisions; Estimated occurrences reopen with identical frozen geometry and warnings.
- Immutable composition revision identity, cursor movement, and divergent-history behavior are deterministic.
- History excludes the explicitly out-of-scope project, generation, settings, export, and recovery operations.
- Obsolete development files are rejected clearly and are not rewritten.
- Version boundaries can host future migrations, while no external support marker is declared implicitly.
- `RecipeRevision.Id` is the canonical aggregate identity of an immutable Working Composition: its `CompositionRecipe` payload transitively freezes exact track, item, link, timing/source-revision pins. Cache lookup for an explicit earlier composition revision remains distinct from current/default revision lookup. Render-derived dependency hashes and stale-result rejection remain Milestone 6 work.
- Core/Application project truth contains no WPF types, absolute machine paths, FFmpeg component names, or Windows-only concepts.
- Every Phase 4A recovery/relink acceptance case passes against the new candidate format.

Phase 4B risk: **Very high**. It changes the composition graph consumed by persistence, editing, timeline projection, dependency analysis, and materialization.

## Milestone 4 definition of done

Milestone 4 is complete only when both phase acceptance sets pass together; the architecture preflight/completion record and any required ADRs are complete; all six test suites and architecture checks pass; and the manual acceptance matrix covers recovery, degraded media, relink, restart, track management, linked media, Detach behavior, and exact reopening.

Milestone 4 does not declare the external-beta format baseline. That remains an explicit owner action.

## Tentative Milestone 5 — Structural Editing Grammar

Outcome:

> Users can make deterministic multitrack structural edits, with every completed gesture producing one immutable composition revision and participating in session undo/redo.

Scope:

- insert and generic selected-occurrence replace;
- edge trim, ripple trim/delete, ordinary delete, gaps, and close-gap;
- snapping, markers, selection, track targeting, and keyboard commands;
- linked-item synchronization and user-facing Unlink audio;
- session composition undo/redo and redo invalidation after divergent edits.

Acceptance retains the exact-boundary, locked-track, overlap, empty-space, attachment, linked-media, no-op, restart, failure-rollback, mouse, keyboard, and rapid-edit cases from Slice C. One completed gesture commits once; cancellation and no-op gestures commit nothing. Save/reopen restores the committed result without promising persistence of the session undo stack.

Milestone 5 excludes finishing effects, a render/proxy overhaul, persisted session undo stacks, and generation-specific replacement commands.

Risk: **High**.

## Tentative Milestone 6 — Track-Aware Rendering and Free Delivery

Outcome:

> Audition, preview, proxy, conversion, and export consume the same committed composition semantics and produce identifiable, cancellable, capability-checked results without freezing the editor.

Phases 6A-6C are tentative work units inside Milestone 6, not separately approved milestones.

### Phase 6A — Render-plan integration

- immutable, track-aware semantic render plan;
- exact composition/source/profile identity;
- authoritative visibility, mute, timing, and linked-occurrence interpretation;
- stale-result rejection and dependency/cache identity;
- extension boundaries for later visual, audio, and text finishing.

After 6A, stop for a scope and architecture checkpoint if evidence shows that preview/proxy behavior and final conversion/export cannot safely remain one delivery milestone. Any split must retain one semantic render graph and creative agreement between preview and export.

### Phase 6B — Preview, proxy, and execution control

- audition/preview agreement at declared quality levels;
- deterministic automatic reduced-quality preview and disposable proxies;
- progress, bounded concurrency/thread use, cancellation, orphan cleanup, partial-output cleanup, and retry;
- UI responsiveness on the reference profile.

### Phase 6C — Free conversion and export

- capability-preflighted ordinary Free output profiles from the approved media contract;
- selected physical/virtual video, audio asset/range, and Working Composition outputs;
- deterministic container/codec/media/canvas/resolution/frame-rate/quality/audio profile definitions;
- conditional routes unavailable or explicitly unsupported until their active-runtime gates pass;
- no selection or packaging of the final redistributable runtime.

Milestone 6 supplies the render graph and extension seams used by later effects, audio finishing, titles, and captions; it does not claim those later feature semantics.

Risk: **High to very high**.

## Later desktop sequence

The later dependency order remains:

1. visual finishing;
2. audio finishing;
3. text, captions, and Free project-owned custom font import;
4. workspace/viewer contexts, including Free floating and dockable timeline/Edit Tools surfaces;
5. edit-to-generation bridge;
6. Free hardening and external-beta readiness.

Numbers and release assignments after Milestone 6 remain provisional. The first Pro continuity/repair bundle, Ingots/backend, release engineering, distribution, and macOS are separate workstreams.
