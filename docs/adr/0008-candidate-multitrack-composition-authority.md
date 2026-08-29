# ADR-0008: Replace the candidate composition payload with multitrack state

Status: accepted
Date: 2026-08-28

## Context

The current `CompositionRecipe` persists one sequential video list plus a separate audio-clip list. That shape cannot preserve stable track identity, intentionally empty tracks, track controls, exact independent video/audio occurrences, or frozen degraded-timing evidence. Keeping it beside the new multitrack model would create two composition authorities and make reopen, dependency analysis, history, and later rendering ambiguous.

### Architecture preflight

```text
Feature/outcome: Establish the unpublished candidate multitrack Working Composition format with stable identities, exact timing, linked source-backed audio, and immutable revision history.
Existing owners touched: Core recipe/track/item invariants; Application composition commands and contribution planning; Infrastructure project DTO mapping and legacy render adapters; App timeline projection.
Proposed responsibility and extension point: Replace only the contents of CompositionRecipe with WorkingCompositionState while retaining RecipeRevision and the Working Composition virtual asset cursor as the sole revision/history authority.
Dependency and public-contract impact: Existing dependency direction is unchanged; portable Core and persistence contracts change.
Persistence/format/compatibility impact: The internal development format is bumped and older unpublished formats are rejected. No migration ladder or external supported-format marker is introduced.
Parallel-workflow or boundary risk: Legacy segment/audio lists, implicit video-source audio, a second composition store, and UI-owned track state are prohibited.
Verification (tests and manual acceptance): Core invariant tests; exact DTO round-trip and obsolete-format rejection; command lock/identity/link tests; deterministic contribution projection; recovery/relink restart suites; traditional track-management manual acceptance.
ADR or architecture-debt decision: ADR required for the persistence and composition-authority replacement. No architecture debt accepted.
```

## Decision

`CompositionRecipe` contains one immutable `WorkingCompositionState`. The existing `RecipeRevision` graph remains the immutable composition-history authority, and the Working Composition virtual asset's current revision remains its persisted cursor. No parallel composition revision, store, segment list, audio-clip list, or command log is added.

The state persists ordered video and audio tracks. Each track has a stable identifier and lock state; video tracks additionally have visibility, and audio tracks additionally have mute. Empty tracks remain present until an explicit command deletes them. Track position and UI index are not identities, and selection remains transient presentation state.

Every video and audio occurrence has its own stable identity, exact source/revision reference, selected numeric stream identity, composition placement, timing-assessment pin, and applicable exact source range. Estimated occurrences retain their frozen rational span without inventing an exact source range. A link group contains exactly one video and one audio occurrence from the same exact source revision and content identity; their composition starts may differ to preserve source stream offsets.

Placing a video with usable selected audio creates separate source-backed video and audio occurrences joined by one link group. It does not create a durable extracted audio asset. The former implicit `AudioEnabled` flag is removed: video-only placement is explicit, Unlink removes only the link relationship, and Detach rebinds or replaces the exact audio occurrence with a durable derived audio asset without doubling the original source-audio route.

Existing item-level audio mute, gain, pan, and fades remain occurrence properties and are distinct from track mute. Fade durations use portable exact rational time rather than persisted UI milliseconds. Track controls determine structural contribution: hidden video tracks and muted audio tracks do not contribute; lock controls command mutation but never contribution.

Application exposes one deterministic contribution projection from the authoritative state. Milestone 4 does not silently flatten multiple visible video tracks into the legacy sequential renderer. Until Milestone 6 supplies the approved track-aware render graph, preview/export adapters may proceed only for shapes they can deterministically honor and otherwise stop with actionable guidance.

The project DTO mirrors the track/item graph and exact portable value objects. It stores no WPF state, absolute paths, machine/cache state, media-engine names, or floating-point timeline authority. Because no external beta format has been declared, the format reader rejects obsolete development versions rather than guessing a conversion.

## Consequences

- Stable track, item, link, stream, range, and timing-assessment identities reopen exactly, including explicitly empty tracks.
- The legacy sequential adapter may consume only an explicitly supported single-track shape. It must reject any additional contributing track or item it cannot honor; first-track placement is not a general executable preview/export projection and never permits silent omission.
- Audio from a video source is independently addressable without manufacturing an extracted Project Media asset.
- Existing audio mix behavior is preserved while track mute gains an independent meaning.
- Current rendering remains available only where its adapter can prove the persisted composition shape is representable; richer compositing remains Milestone 6.
- Candidate-format replacement requires coordinated updates across validators, DTO mapping, commands, dependency analysis, materialization adapters, and WPF projection.

## Alternatives considered

- Persisting both legacy lists and multitrack state was rejected because neither could be reliably authoritative.
- Retaining implicit source audio on video items was rejected because it conflicts with independent linked audio occurrence identity and Detach semantics.
- Flattening multiple video tracks for current rendering was rejected because it silently changes creative meaning.
- Creating a new composition-history store was rejected because `RecipeRevision` already owns immutable history and cursor ancestry.
- Adding a migration ladder was rejected because these formats have not been declared externally supported.

## Verification and follow-up

Implementation proceeds in bounded units through the existing project authority: current assessment/acknowledgement persistence, Core/DTO composition replacement, Application commands and contribution planning, then presentation projection. Phase 4A recovery/relink acceptance is rerun against the resulting candidate format. Track-aware executable preview/export remains Milestone 6 work.
