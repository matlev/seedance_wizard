# Editor capability direction

Status: accepted research synthesis; commercial lanes are reconciled, while exact feature tiering and release sequencing remain provisional

Research reviewed: 2026-08-19

## Product ambition

ReelForge should become an **AI-native finishing editor**, not a general-purpose professional NLE. It should ultimately let a solo creator generate shots, assemble them, repair common generation defects, improve continuity, add ordinary finishing elements, and export useful work without requiring another editor for routine projects. It should not attempt to reproduce Resolve, Premiere, Final Cut, Fusion, Fairlight, or a professional DAW feature-for-feature.

The intended audience includes AI short-film makers, social creators, and hobby filmmakers, with room to serve adjacent personas as the actual feature set matures. The initial workflow should stay understandable even though the underlying track model is designed to grow beyond a simplified first presentation.

The differentiating loop is:

```text
generate
   |
   v
inspect and save exact references
   |
   v
assemble and finish
   |
   v
isolate a continuity or quality problem
   |
   v
repair, regenerate, or create variants
   |
   v
compare and replace a timeline usage
   |
   +-------------------------------> continue editing or generating
```

Conventional editing depth exists to make that loop credible. It is not the primary differentiation by itself.

## How to interpret the market research

The researched “minimum editor feature set” is the capability target for ReelForge to feel like a credible finishing editor, not an automatically approved MVP or first-public-release checklist. Titles, captions, transcription, advanced HDR, variant comparison, proxies, and specialized repair engines retain different priority and effort gates described below. Business-model decisions may change tiering and order, but should not reverse the architectural boundaries settled here.

Likewise, the research's near-term list is dependency guidance rather than a replacement for the current roadmap. Milestone 2 Phase 2E remains the composition/timeline foundation. The already-approved post-Milestone 2 review and Milestone 3 structural refactor remain the next architecture program. Later editor features must be divided into reviewable milestones after that work rather than accumulated into one oversized “near-term editor” release.

Market comparisons and third-party engine/license observations are research leads. Current official capability, code license, model-weight license, redistribution terms, platform support, and commercial suitability must be re-verified at the implementation go/no-go gate for each integration.

## Settled product decisions

1. **Personas:** design for AI short-film makers, social creators, and hobby filmmakers rather than choosing only one; keep the product open to additional adjacent users.
2. **Multitrack depth:** the domain should not impose an arbitrary small track limit, while the first UI may reveal complexity progressively.
3. **Automation:** use one generic keyframe/parameter-automation foundation rather than bespoke animation models for transform, opacity, gain, color, masks, and later timing properties. Structural editing semantics come first.
4. **Titles and captions:** desirable finishing features, but not currently a release blocker unless a deliberately narrow implementation proves inexpensive.
5. **Transcription:** desirable and potentially feasible, but not currently a release blocker. Any local model is an optional, separately licensed component rather than assumed base-install content.
6. **Audio scope:** focus on arranging sound in a video—tracks, waveform-guided placement, trim/split, mute, gain, pan, fades/crossfades, normalization, mixing, and selected cleanup conveniences. Do not attempt sample/spectral editing or a DAW replacement.
7. **Durable destructive results:** a non-reconstructable or intentionally baked repair/enhancement is a normal durable physical Project Media asset, not a cache entry. It receives its own AssetId, SHA-256 identity, provenance, and source/revision relationship.
8. **Bake for performance:** retain an explicit future Bake operation, but do not optimize prematurely. Automatic disposable preview/render caching remains separate from a user-requested durable bake.
9. **Local ML:** advanced local engines are optional downloads/packs, subject to hardware discovery and separate code-versus-model-weight license review.
10. **Generative editing boundary:** media repair/editing receives a provider-neutral abstraction distinct from `IVideoGenerationProvider`. A provider adapter may implement both capability families, but an edit never assumes that the provider which generated a source must repair it.
11. **HDR:** preserve safe metadata-aware import/export as a future concern, but defer deep grading/mastering until product demand and feasibility justify it.
12. **Generation references:** every logical media object in Project Media should be eligible for reference selection, including physical assets, Saved Frames, Saved Clips, compositions, repaired outputs, and eventually exact composition frames/ranges and audio excerpts. Provider capability and limit validation still determines whether and how a particular occurrence can be submitted.
13. **Continuity matching:** “Match to Previous Clip” or an equivalent assisted continuity workflow is a prominent future ReelForge differentiator, especially for chains of continuation generations that accumulate drift.
14. **Variant comparison:** useful but not an MVP requirement. Add it only after core generation and timeline selection/replacement workflows are trustworthy.
15. **Repair output behavior:** always create new media by default. A later Replace action replaces selected timeline usage with the new AssetId; it does not silently overwrite the source file. A preference may streamline the post-create selection behavior, but physical source overwrite is not the ordinary repair workflow.

## Interaction taxonomy

The timeline owns structural operations:

- select, insert, replace, remove, and reorder;
- split/blade and edge trim;
- ripple trim/delete and gap management;
- roll, slip, and slide when the composition model supports their invariants;
- snapping, markers, track targeting, and keyboard commands.

Edit Tools owns properties of the current single selection:

- **video segment:** Source, Transform, Timing, Audio, Color, Repair and Enhance, and Effects;
- **audio clip:** Source, Timing, Audio, and selected Cleanup actions;
- **transition/junction:** transition type, duration, alignment, and easing;
- **title/caption item:** text, style, position, timing, and caption behavior;
- **nothing selected:** concise selection guidance rather than a permanent dashboard.

Inspector remains the read-only media/source/history surface. Discrete Edit Tools changes commit once when their value changes. Continuous controls remain mutable UI draft state during manipulation and commit one immutable recipe revision when applied or completed.

## Editing architecture to anticipate

### Typed effect stack

A composition segment will eventually need an ordered effect stack with stable effect identity, enable/bypass state, deterministic typed parameters, and explicit ordering. Do not add an unrelated permanent property to `CompositionSegment` for every filter. Also do not collapse effect behavior into an operation name plus an arbitrary string dictionary: the established typed-recipe and explicit-DTO policy still applies.

Conceptually:

```text
CompositionSegment
  structural source/boundaries/audio policy
  Effects[]
    EffectId
    Enabled
    typed effect definition
    typed constant-or-automated parameters
    optional exact mask/input references
```

Transform, color, repair, and other categories are user-facing organization. The render planner owns how compatible operations are fused and where a materialization boundary is actually required.

### Generic parameter automation

An animatable parameter conceptually has either a constant value or a typed curve. Curves need stable key identity, exact timeline time, interpolation/easing, deterministic ordering, and unit/range semantics. The implementation must not use UI pixels or floating-point display seconds as authoritative time.

Generic automation should be designed before several features each invent incompatible keyframes, but structural editing and track semantics should be stabilized before the keyframe UI multiplies their complexity.

### Tracks and time domains

The current `CompositionRecipe` is a sequential video list plus independently timed physical-audio clips. It is not yet the researched multitrack model. Future tracks require explicit identity, order/layering, media compatibility, mute/lock/visibility policy, overlap semantics, transitions, and exact time mapping.

Video exactness continues to use media-native presentation timestamp plus rational time base. Audio eventually needs sample-aware positions and ranges; the current `TimelineStartTicks` placement value is useful but is not a complete sample-accurate audio-editing model. Do not force audio boundaries into `FrameAnchor`.

### Analysis artifacts

Stabilization, loudness scans, transcription, scene detection, tracking, masking, and continuity measurement perform expensive analysis before rendering. Their artifacts are derived from exact source/recipe revisions plus algorithm, engine, model, version, and settings identity. They are normally disposable and reconstructable like cache/materialization evidence, may be retained for performance, and never become authoritative media merely because computation was expensive.

An analysis result that cannot actually be reproduced because its engine/model is unavailable must fail explicitly rather than silently resolve against a different engine. User-authored corrections or labels derived from analysis may be authoritative project state even when the raw analysis artifact is disposable.

### Durable derived media and Bake

Editing is non-destructive by default: authoritative source bytes are never silently modified. Operations fall into three categories:

| Category | Examples | Persistence |
| --- | --- | --- |
| Pure recipe | crop, transform, gain, LUT | typed recipe; rendered representation may be cached |
| Reconstructable expensive computation | stabilization analysis, optical-flow preview, proxy | disposable cache tied to exact inputs/engine identity |
| Durable derived output | generative repair, ML enhancement whose exact engine may not remain available, explicit Bake | new physical asset with hash and provenance |

An explicit Bake creates a durable physical asset even when the source recipe could theoretically be reconstructed. This differs from automatic render caching because the user has chosen a durable boundary. A repair/bake may optionally replace the selected timeline usage after successful ingestion, but the original media and prior immutable recipe revision remain recoverable.

The physical-asset schema will eventually need an explicit derived durability/origin classification and typed transformation provenance. The exact enum or DTO change waits for the implementation slice; pre-release schema policy permits the necessary breaking correction.

### Media-edit provider boundary

Generative repair, object/background replacement, range extension, and similar transformations are not ordinary full-video generation submissions. Introduce a provider-neutral `IMediaEditProvider`-style capability family when the first such workflow is scheduled. It should own edit-operation capabilities, cost/resource behavior, immutable edit request snapshots, prepared logical inputs, asynchronous job state, output acquisition, and sanitized execution evidence.

This is a semantic boundary, not necessarily a separate vendor client. One provider adapter or transport may implement both generation and media-edit capabilities. The selected edit provider/model is frozen per edit job and is independent of the provider/model recorded in the source asset's generation provenance.

Shared infrastructure may later generalize authorization, job monitoring, output ingestion, and diagnostics across generation, media edits, analysis, and renders. Domain requests remain operation-specific rather than being forced into one bag-of-parameters job contract.

### Universal logical references

Reference selection operates on logical media rather than only durable file paths. A selected object is pinned to the exact physical content identity, recipe revision, anchor revision, or immutable composition/range snapshot required to preserve meaning. Provider preparation materializes and transforms it for the selected route. The UI may show all eligible project media while provider capability validation explains unsupported media types, roles, durations, counts, or combinations before submission.

An exact Working Composition frame/range must snapshot the composition revision and exact boundaries; it must not reference a mutable live timeline. Generating from an edit never requires the user to export and re-import manually.

### Capability and optional-engine discovery

Advanced features may depend on CPU instruction sets, RAM, disk, GPU vendor/backend, VRAM, hardware encoders/decoders, Vulkan, ONNX, models, and engine versions. Use a provider-neutral engine/capability registry rather than embedding RIFE, Real-ESRGAN, Whisper, SAM, or another implementation name into project-domain concepts.

Optional engines are downloaded and consented to separately. Every integration gate audits the source-code license, model-weight/data license, redistribution conditions, supported territories, artifact integrity, hardware requirements, update policy, and fallback behavior.

## Candidate capability lanes

These lanes express dependencies, not final milestone numbers or commercial tiers.

### Trustworthy editing foundation

- split, trim, ripple behavior, gaps, snapping, markers, and undo/redo semantics;
- scalable video/audio track model and simple track controls;
- transform/crop/opacity and constant speed/reverse/freeze;
- audio placement, waveform display, gain/pan/mute/fades/normalization/mixing;
- basic transitions and color/LUT support;
- render progress/cancellation, export profiles, and preview-quality policy;
- proxy policy only when project complexity demonstrates the need.

### AI-native finishing differentiation

- stabilization, deflicker, denoise, sharpen, and format/loudness matching;
- assisted “Match to Previous Clip” continuity workflow;
- exact composition frames, ranges, and audio excerpts as generation references;
- regenerate/repair/replace a selected range through a provider-neutral edit route;
- source-aware prompt/provenance reuse;
- later variant comparison and rapid replacement of selected timeline usage.

### Optional advanced engines

- high-quality interpolation and upscale;
- segmentation, tracking, masking, face repair, and object/background editing;
- local transcription and premium speech cleanup;
- advanced scopes/HDR only when justified by real demand;
- all local ML distributed as optional, hardware-gated, separately audited packs.

### Explicit deferrals

- Resolve/Fusion/After Effects-class node compositing;
- Fairlight/Audacity-class sample or spectral audio editing;
- immersive/VR/Atmos production;
- broadcast/DCP/IMF mastering;
- multi-user collaborative editing;
- large transition/effect marketplaces;
- professional plugin hosting before the native effect and isolation model is mature.

## Commercial packaging implications and unresolved gates

The accepted [Business and packaging direction](business-and-packaging.md) resolves the broad lanes without setting feature gates:

- foundational local workflows that complete the generate-to-finish loop lean Free;
- professional depth, productivity, advanced finishing, repair, and workflow conveniences are Pro candidates;
- externally metered compute is a managed-credit candidate;
- BYOK remains first-class for Free and Pro users;
- optional local engines require separate packaging, hardware, license, support, and value decisions.

The exact first-public-release set, Free/Pro boundary, titles/captions/transcription priority, hosted repair route, optional-engine packaging, telemetry/account policy, licensing model, and support baseline remain unresolved. Premium packaging must not weaken project readability, recipe/provenance integrity, access to existing edits, safe export, or provider independence.

No implementation or public tier promise follows from either research synthesis alone.
