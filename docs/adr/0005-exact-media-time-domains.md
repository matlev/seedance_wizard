# ADR-0005: Preserve exact media time in native domains

Status: accepted
Date: 2026-08-28

## Context

Milestone 4B replaces the sequential Working Composition with a persisted multitrack model. The existing project format mixes exact video frame positions (`PTS` plus rational time base), floating-point recipe-boundary seconds, 100-nanosecond `TimeSpan` ticks, and millisecond audio fades. The current audio path also rounds placement to milliseconds. Those representations cannot preserve every 44.1 kHz sample boundary, and a floating-point timestamp cannot be authoritative project state.

The multitrack DTOs cannot be designed safely until the project defines how video presentation time, audio sample time, and composition time relate; where rounding is allowed; and which layer owns media-engine conversion.

### Architecture preflight

```text
Feature/outcome: Exact, portable video/audio time contracts for the Milestone 4B multitrack model.
Existing owners touched: Core domain state and invariants now; Infrastructure persistence/media translation and Application commands in later bounded units.
Proposed responsibility and extension point: Add Core exact-time values; later compose them into the authoritative Working Composition and map them through the existing project store.
Dependency and public-contract impact: New portable Core value contracts; no dependency-direction change.
Persistence/format/compatibility impact: The later track DTO unit will replace approximate development-format fields in one explicit candidate-format break. This unit changes no DTO or reader.
Parallel-workflow or boundary risk: Do not add a second recipe, persistence, or render path. Existing media materialization remains the only engine boundary.
Verification (tests and manual acceptance): Core normalization, comparison, 44.1/48 kHz, video time-base, rounding, invalid-input, and overflow tests now; persistence/reopen and UI acceptance with the later candidate format.
ADR or architecture-debt decision: ADR required because this fixes a durable public and persistence contract. No architecture debt accepted.
```

## Decision

ReelForge uses three related but distinct portable time forms:

- `ExactTime` is a reduced rational number of seconds with a signed 64-bit numerator and positive 64-bit denominator. It represents composition placements and durations without selecting a video frame rate or audio sample rate as the universal timeline clock.
- `VideoPresentationTime` preserves a source-native signed presentation timestamp and positive rational time base. It does not normalize away the source tuple. The timeline item separately persists the selected video stream identity.
- `AudioSampleTime` preserves a signed sample-frame offset and positive sample rate. The timeline item separately persists the selected audio stream identity. Audio source ranges use these sample positions rather than video anchors or floating-point seconds.

Timeline and source ranges are half-open: `[start, end)`. Domain invariants require nonnegative composition placements, durations, and ordinary source-range offsets even though the primitive time values remain signed so negative source presentation timestamps can be represented faithfully. Range types and track/item validation enforce those contextual constraints.

`ExactTime` is canonical: zero is `0/1`, common factors are removed, and the denominator is positive. Cross-products, rescaling, and intermediate arithmetic use arbitrary-width integer calculations, then fail with a checked overflow if the persisted 64-bit result cannot represent the answer. There is no silent saturation or floating-point fallback.

Rounding is allowed only at an explicit boundary and must name one of these policies:

- floor (toward negative infinity),
- ceiling (toward positive infinity), or
- nearest with midpoint ties to even.

An exact source-native value converts without rounding when the target domain can represent it. For a half-open media-engine range that requires a coarser integer domain, the start rounds down and the end rounds up so conversion cannot silently discard requested content. Point placement and deliberate UI entry use nearest-ties-to-even in an explicitly selected target domain. Display seconds are an approximation only and never flow back into project truth as an inferred exact value.

Core owns these values and their arithmetic rules. Application owns the commands that select domains and reject invalid track/item mutations. Infrastructure owns serialization and translation to or from inspected media and the active media engine. FFmpeg names, command syntax, machine paths, and engine-specific clocks do not enter the Core contract.

The candidate Milestone 4B format will serialize integer components, not floating-point seconds:

- composition time: reduced numerator and denominator;
- video source time: presentation timestamp plus time-base numerator and denominator;
- audio source time: sample-frame offset plus sample rate;
- selected stream identity: an explicit field adjacent to, but not embedded in, the time value.

No external compatibility baseline is declared by this decision. The later candidate-format reader remains a versioned boundary capable of hosting migrations after an external supported-format marker is deliberately declared.

## Consequences

- 44.1 kHz and 48 kHz sample boundaries remain exact without forcing audio through a video frame clock.
- Variable-frame-rate and non-unit video time bases retain their inspected presentation meaning.
- Composition time can compare values from different source domains deterministically.
- Conversion callers must choose rounding deliberately; convenience conversion from `double` seconds is not part of the authoritative model.
- Current render and audition code may continue using approximate boundary values only until the bounded candidate-format integration replaces those fields. It must not become a parallel source of project truth.
- The upcoming DTO break must introduce track/item identities, selected streams, exact ranges, and exact placement coherently rather than incrementally serializing a hybrid model.

## Alternatives considered

- A fixed 100-nanosecond timeline was rejected because it cannot exactly represent every 44.1 kHz sample boundary.
- A universal audio-sample clock was rejected because projects can contain different sample rates and video presentation times.
- Floating-point seconds were rejected because equality, round-trip identity, and boundary selection would depend on approximation.
- Storing only normalized rational seconds for source video was rejected because the original PTS/time-base tuple is needed for exact source-frame identity.
- Arbitrary-width persisted integers were rejected as disproportionate for the candidate format. Arbitrary-width intermediates plus checked 64-bit storage make overflow explicit while keeping the format bounded and interoperable.

## Verification and follow-up

The first implementation unit adds only the Core values and focused boundary tests. It does not modify recipe DTOs, render plans, or UI.

The next bounded units must:

1. define immutable composition revisions, history cursor, tracks, timeline items, selected streams, exact ranges, link groups, and contribution policy in Core;
2. introduce the candidate project-format DTOs and one existing-store mapping path;
3. adapt Application commands and the traditional multitrack projection;
4. prove exact persistence/reopen, locked-track rejection, visibility/mute contribution, linked placement, Detach distinction, and all Phase 4A recovery/relink cases against the candidate format.
