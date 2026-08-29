# ADR-0007: Freeze degraded timing at acknowledged placement

Status: accepted
Date: 2026-08-28

## Context

Some media imports are durable and decodable even when ReelForge cannot prove every native timestamp, terminal frame boundary, audio sample boundary, or stream relationship required for precision editing. Blocking every such file from the timeline is unnecessarily restrictive. Treating approximate inspection as exact would be worse: reopening, later analysis, or a different runtime could change duration, synchronization, and downstream edit geometry.

ReelForge therefore needs a portable distinction between successful import, current timing evidence, and the frozen timing semantics accepted for one timeline occurrence. The distinction must remain engine-neutral and must not weaken exact Saved Frame, Saved Clip, anchor, or sample-boundary contracts.

### Architecture preflight

```text
Feature/outcome: Allow explicitly acknowledged degraded-timing placement while preserving frozen, reproducible timeline geometry.
Existing owners touched: Core timing/readiness state and timeline-item meaning; Application placement/acknowledgement/capability commands later; Infrastructure inspection and future repair implementations; App presentation later.
Proposed responsibility and extension point: Add immutable Core assessment and occurrence-pin contracts beside the existing exact-time and stream-descriptor models, then persist them through the one project-store path.
Dependency and public-contract impact: New portable Core/persistence contract; no dependency-direction change.
Persistence/format/compatibility impact: Candidate multitrack items will pin assessment schema/identity, source hash, selected stream, readiness, frozen span, and issue classifications. Later source reassessment never rewrites historical pins.
Parallel-workflow or boundary risk: Assessment cannot become a second asset identity or render authority. Repair must use existing import, provenance, media, and transactional file-operation seams.
Verification (tests and manual acceptance): Core eligibility/invariant tests now; later DTO reopen, acknowledgement, independent A/V placement, warning projection, operation gating, and render-preflight acceptance.
ADR or architecture-debt decision: ADR required because degraded timing changes durable project and editing meaning. No architecture debt accepted.
```

## Decision

Durable import and exact timeline readiness are separate concepts. Each relevant selected stream has one of three timing-readiness states:

- **Exact:** selected-stream timing and boundaries support ordinary timeline and precision-dependent operations.
- **Estimated:** a deterministic stream, dependable sequential decode path, and finite positive timeline span exist, but one or more exact timing properties remain unresolved.
- **Unusable:** ReelForge cannot establish a usable stream, finite positive span, or dependable sequential decode path.

Estimated placement is eligible only when all of these are frozen in a versioned assessment:

- verified source SHA-256 identity;
- deterministic selected stream index;
- usable sequential decode evidence;
- finite positive rational timeline duration;
- the representable source presentation start when timing is Exact, or its explicit absence/qualification when Estimated;
- one or more specific, engine-neutral timing issue classifications;
- stable assessment ID and assessment-schema identity.

A filename, extension, or container duration alone never establishes eligibility. Corrupt, protected, undecodable, durationless, no-usable-stream, and unreliable sequential-decode results remain Unusable.

Placement-fatal issue classifications cannot appear on an Exact or Estimated assessment. An Unusable assessment must carry at least one such specific classification; contradictory evidence cannot make corrupt, protected, undecodable, streamless, durationless, or unsupported media placeable. Known persisted assessment-schema identities remain explicitly supported by Core so historical pins can be represented, while unknown schemas are rejected for deliberate DTO-version handling rather than interpreted as current meaning.

Assessment is a cancellable, placement-time analysis rather than an import, startup, recovery, or autosave side effect. Infrastructure scans the already-resolved numeric stream identity through end of stream using the active capability-qualified media route. Successful inspection or component presence alone is not decode evidence. A heuristic timestamp is not promoted to exact source timing, and container duration alone never establishes a finite placement span. Engine-specific frame, packet, priming, and padding evidence remains in Infrastructure; only the portable readiness, source presentation start, rational span, issue classifications, source identity, and selected-stream identity cross into project meaning. Repeating the scan reuses an assessment ID only when that complete immutable evidence is unchanged; changed evidence receives a new ID and therefore a new acknowledgement decision.

Video and audio are assessed independently. Placement behavior is:

- Exact video plus Exact audio creates ordinary linked video/audio occurrences.
- Exact/Estimated combinations may create linked occurrences after acknowledgement, with the warning pinned only to the affected occurrence.
- Estimated video plus Estimated audio warns both occurrences.
- Usable video with unusable audio offers an explicit video-only action; audio is never silently omitted.
- Unusable video or video span blocks video placement.

An Exact or acknowledged Estimated occurrence pins an immutable snapshot of the assessment used at placement, including schema/assessment identity, source content identity, selected stream, readiness, frozen timeline span, decode-readiness evidence, and issue classifications. Estimated span is stored as exact rational project data even though its relationship to the original media is explicitly classified as estimated. This Phase 4 placement contract uses the assessed full span. A future explicit trim or other subrange command must separately preserve the assessment span and occurrence geometry, and prove the boundary evidence required by that operation.

The asset's current assessment may later change after new analysis. Existing occurrences never silently adopt it. Reopen reproduces the pinned geometry. Adopting newly established exact timing is an explicit edit that identifies affected occurrences and its structural consequences before commit.

Linked video/audio items preserve their frozen relative synchronization. A link group therefore identifies exactly one video occurrence and one audio occurrence from the same exact source revision, but does not require identical composition start values; source stream offsets may produce a deliberate relative offset. Linked commands preserve that delta unless an explicit command contract says otherwise.

Acknowledgement is project-specific state keyed by assessment ID. It suppresses repeated placement confirmation for the same unchanged assessment, but it never removes persistent warning presentation. A changed assessment receives a new ID and requires a new decision. Creating an Estimated occurrence proves acknowledgement for that placement, but does not convert the assessment to Exact.

Precision-dependent features check their own required evidence. Exact Saved Frames, frame stepping, split/trim boundaries, Saved Clips, exact composition generation references, sample-aware audio edits, and exact detach ranges remain unavailable or require explicit analysis/conformance when their required timing is unresolved. Other operations remain available when their inputs are exact. Messages identify the missing capability rather than labeling the entire asset unsupported.

Best-effort preview may preserve the pinned geometry and visible warning. Final render/export performs a fresh capability preflight and proceeds only when the renderer can deterministically honor the persisted semantics. It never silently changes duration, ripples items, retimes, pads/freezes video, trims audio, drops audio, or reinterprets an occurrence.

## Repair terminology and staged direction

**Attempt Repair** is the user-facing term for trying to establish reliable timing or produce timeline-safe derived media. It does not claim to recover unknowable original timing.

The future workflow may escalate explicitly:

1. **Re-inspect / re-index:** update the source assessment when exact timing can now be established; create no media.
2. **Lossless remux:** create a new durable derived asset when rewriting the container establishes dependable timing without re-encoding streams.
3. **Normalize / transcode:** create a new durable derived asset in a declared ReelForge editing profile.
4. **Unable to repair:** retain the original asset and current degraded/Unusable assessment.

Any repair that creates media creates a new durable Project Media asset with provenance to the original. A repair invoked for one occurrence may explicitly replace that occurrence after confirmation; other usages never change silently. This ADR defines future behavior only and does not authorize repair implementation in the current work unit.

## Consequences

- Imperfect but usable media can participate in a composition without being mislabeled exact.
- Historical edits remain reproducible even when later inspection improves.
- Project Media, timeline occurrences, Inspector, and composition warning UI can derive consistent yellow degraded-timing presentation from one contract.
- Exact operations remain exact rather than quietly degrading to approximate boundaries.
- Candidate-format DTOs must persist assessments, acknowledgements, and per-occurrence pins.
- Render integration must prove deterministic support for the frozen semantics instead of normalizing silently.

## Alternatives considered

- Blanket placement prohibition was rejected because sequentially usable media can support valuable non-precision editing.
- Provisional placement that silently updates after analysis was rejected because it changes project geometry and historical meaning.
- Treating approximate container duration as exact was rejected because it does not prove stream boundaries or decode behavior.
- Automatically dropping unusable audio was rejected because it changes creative meaning without consent.
- Implementing repair immediately was rejected because remux/transcode routes require their own capability, provenance, cancellation, quality, and licensing acceptance.

## Verification and follow-up

The first unit defines portable readiness, issue, assessment, acknowledgement, and occurrence-pin invariants. Subsequent Phase 4B units integrate them with candidate DTOs, placement commands, and warning projection. Repair execution and final render/export behavior remain separately gated work.
