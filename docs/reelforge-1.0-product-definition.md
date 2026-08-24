# ReelForge 1.0 product definition

Status: proposed product contract for owner approval; planning only

Research reviewed: 2026-08-24

## Product decision

ReelForge 1.0 should be the smallest desktop product that lets a solo creator move from AI-generated or imported shots to a trustworthy, ordinary finished video without requiring another editor for routine correction, assembly, sound, text, and delivery.

It is not a small clone of Premiere, Resolve, or CapCut. ReelForge differentiates through the loop it already owns:

```text
generate or import
        |
        v
inspect exact media and preserve references
        |
        v
assemble and finish non-destructively
        |
        v
use an exact edit selection to generate or compare a replacement
        |
        `------------------------------> continue editing
```

The 1.0 program should therefore finish the editing and reliability foundation around this loop. It should not absorb commerce, managed compute, release engineering, installers, store publication, or macOS work.

## Scope rules

- **Free is the complete core product.** It includes the useful generate-to-finish workflow, BYOK generation, Saved Frames, Saved Clips, ordinary local editing, and ordinary export.
- **Pro is depth and leverage.** Its candidates add automation, advanced control, analysis, comparison, repair, and throughput. Pro does not remove project custody, basic editing, safe export, or BYOK from Free.
- **Ingots are orthogonal.** Managed compute, accounts, billing, entitlements, security, and payment processing are outside this product-definition milestone and outside the desktop 1.0 implementation slices.
- **Tier names are metadata.** The project and recipe formats remain tier-neutral. This roadmap does not authorize entitlement code.
- **Current plans are evidence, not promises.** A previously mentioned feature survives only when it supports the smallest coherent product.
- **Competitors are expectation evidence, not specifications.** ReelForge needs credible editing fundamentals, not their catalog breadth.

## Complexity and risk scale

| Rating | Meaning |
| --- | --- |
| Low | Local extension of an established contract with bounded UI and tests. |
| Medium | Crosses a few established owners or adds a new durable concept with limited rendering impact. |
| High | Changes Core, persistence, timeline behavior, rendering, and presentation together, or has difficult correctness/performance work. |
| Very high | Introduces a new media model, external engine, provider/job family, or compatibility obligation with large unknowns. |

These are relative planning signals, not calendar estimates.

## Audited current-feature inventory

The inventory below distinguishes an end-to-end desktop capability from a domain or UI placeholder.

| Area | Current status | Evidence and boundary |
| --- | --- | --- |
| Project lifecycle | Implemented | Create, open, close, switch, recent/last-project recovery, project-relative `.rfp` storage, and atomic saves are implemented and manually accepted. |
| Project Media | Implemented core; incomplete recovery/productivity | Image, video, and audio import; drag/drop; grouped media; preview/Inspector; rename; export; copy/move; and dependency-aware delete exist. Multi-select/bulk operations and user-driven source relinking do not. |
| Media preview | Implemented | Image/video preview, play/pause, seek, mute/volume, exact previous/next decoded-frame navigation, and metadata inspection exist. |
| Saved Frames | Implemented | Exact immutable PTS/time-base anchors, labels/notes, update/archive, cache reconstruction, and generation-reference use exist. |
| Saved Clips | Implemented | Exact non-destructive trim recipes, naming, preview, reconstruction, export, generation-reference use, and timeline insertion exist. |
| BYOK generation | Implemented | Fake, BytePlus Seedance 2.5, AtlasCloud Seedance 2.5, and AtlasCloud MiniMax H3 routes; capability-driven forms; references; immutable snapshots; history; lineage; retry/variant/branch/continuation; async jobs; output ingestion; and human-only billable authorization exist. Live provider availability remains external. |
| Jobs and charge safety | Implemented | Persistent global jobs, restart recovery, cancellation, Undo Send, sanitized diagnostics, credential storage, and network-isolated tests exist. |
| Composition timeline | Useful foundation, not 1.0-complete | Duration-aware sequential video, timed independent audio, drag/drop insertion/reorder, remove, exact split, seeking, zoom, fast audition, immutable revisions, preview, and export exist. There are no persistent video tracks, edge trim, ripple/gap behavior, snapping, markers, replace edit, or editor undo/redo. |
| Audio finishing | Strong partial | Layered audio placement, source-audio mute, clip mute/gain/pan/fades, extraction, exact segment detachment, audition, and final mixing exist. Waveforms, source-range trim/split, crossfades, sample-aware boundaries, meters, and loudness analysis do not. |
| Visual finishing | Scaffold only | Edit Tools shows source/timing and explicitly defers Transform. Position/scale/rotation/crop/opacity, canvas controls, retiming, transitions, color, LUTs, effects, and keyframes are absent. |
| Titles and captions | Absent | No title item, caption track, SRT import/export, styling, transcription, or burn-in workflow exists. |
| Generation from an edit | Partial concept only | Physical media, Saved Frames, and Saved Clips can be references. Exact composition frames/ranges and a direct generate/compare/replace timeline workflow are not implemented. |
| Rendering and export | Implemented core; shallow delivery | Deterministic FFmpeg planning, compatibility normalization, caching/leasing, cancellation, no-partial-file behavior, baked representation recovery, and durable composition export exist. User-facing canvas/export profiles, quality controls, stage progress, codec validation matrix, and effect-aware preview policy do not. |
| Recovery and quality assurance | Partial | Atomic saves and deterministic service tests are strong. There is no editor undo/redo, crash-recovery journal/backup UX, missing-source relink workflow, automated desktop UI smoke harness, or broad codec/device/performance matrix. |
| Commercial/release systems | Absent by design | There are no accounts, billing, Ingots, entitlement gates, packaged FFmpeg, signing, installers, updates, store publication, or macOS host. These are not desktop feature gaps for this program. |

The six-suite automated topology and manual regression matrix credibly cover the current foundation. They do not turn unimplemented UI or domain concepts into features.

## 1.0 Free feature contract

Everything already implemented above remains Free. The following missing capabilities are the minimum additions recommended for a complete 1.0.

### 1. Trustworthy projects and editing history

- Session composition undo and redo for track/item structural commands, markers, and visual, audio, title, and caption property edits, with an explicit current-history cursor and clear redo invalidation after a divergent edit. Project Media import/delete/transfer, Saved Frame/Clip creation, provider jobs, and external file exports are excluded. Reopen restores the committed project; retaining an interactive undo stack across application restarts is not required.
- One revision per completed discrete action or continuous gesture; cancellation and no-ops create none.
- Crash-safe project recovery using bounded backups or a journal distinct from disposable cache.
- A user-driven missing-media relink flow that verifies expected content identity and never silently accepts a same-name different file.
- Clear dirty/saved/recovered/degraded state and actionable error messages.

Why Free: loss of work, inability to correct a mistake, or inability to reopen moved media makes the whole product untrustworthy.

### 2. Coherent track and structural editing model

- A scalable persisted track/time model with stable track and item identities. The domain must not impose an arbitrary small track limit, even if the first UI progressively reveals complexity.
- Basic video and audio tracks with ordering plus lock and visibility/mute controls appropriate to media type.
- Insert, move, remove, split, edge trim, ripple trim/delete, explicit gaps, close-gap, and snapping.
- One generic selected-occurrence replace command with two explicit modes: replace in place preserves the timeline span and compatible item properties and rejects a source range that is too short; replace-and-ripple keeps the selected start, adopts the chosen replacement range, and shifts by the duration delta every item on every unlocked track whose start is at or after the old selected end. Items spanning that boundary remain fixed. Attached title/caption items preserve their parent-relative timing. An affected locked track, ambiguous attachment, or invalid overlap rejects the operation before mutation. Replacement never silently retimes media, changes other usages, or overwrites the source asset.
- Timeline and clip markers with label/color and exact time.
- Keyboard commands for the ordinary operations above.
- Existing sequential compositions remain openable through the chosen pre-1.0 compatibility policy.

Why Free: split/reorder alone feels like an assembly demo. Tracks, trim, ripple, replace, undo, and snapping form the minimum credible editing grammar. Roll, slip, slide, multicam, nesting, and advanced track targeting are not required.

### 3. Ordinary visual finishing

- Composition canvas presets for common landscape, portrait, and square output, with an explicit background policy.
- Constant per-clip position, scale, rotation, crop, opacity, and fit/fill controls.
- Constant speed adjustment and freeze-frame creation. Variable speed ramps and optical-flow interpolation are excluded.
- A deliberately small transition set: cross-dissolve and dip-to/from-black, with duration and alignment.
- Basic color matching controls such as exposure/brightness, contrast, saturation, and temperature/tint. LUT import, advanced grading, and scopes are excluded.
- Exact preview/export agreement for every included property.

Why Free: AI shots commonly differ in framing, aspect ratio, pacing, and color. Requiring another editor for these corrections breaks the promised finish loop. A large effects/transition catalog would not improve the core contract.

### 4. Practical audio finishing

- Cached waveform generation and display for source and timeline audio.
- Sample-aware or equivalently exact audio source ranges; clip edge trim, split, move, snap, and short crossfades.
- Existing mute, gain, pan, fades, detach, extraction, layering, and 48 kHz stereo mix behavior remain available.
- Basic peak indication and clipping warning. Full metering, dialogue repair, spectral editing, and a mixer console are excluded.

Why Free: a waveform and editable ranges are basic placement tools, not professional luxury. Automated loudness matching and repair can remain Pro candidates.

### 5. Minimal text and captions

- Timed plain title and lower-third items with font, size, color, alignment, position, and background/shadow from a restrained model.
- Manual caption creation/editing plus SRT import.
- Caption styling, burned-in export, and SRT sidecar export.
- No bundled transcription model, motion-graphics template system, karaoke styling, or translation requirement.

Why Free: a creator should be able to add a title, credit, or accessible caption without leaving ReelForge. Current competitor evidence makes minimal text/captions a completeness expectation, while automatic transcription is depth.

### 6. Predictable preview and export

- Named composition/export profiles covering the selected canvas, resolution, frame rate, quality, and audio behavior. Desktop Feature Complete requires at least one broadly playable local delivery profile against an approved user-configured/developer media-tool capability; the exact public-release encoder/container contract waits for the codec, patent, and distribution decision.
- Preview-quality policy that stays responsive, clearly identifies draft versus final fidelity, and never exports from a stale revision.
- Stage-aware progress, cancellation, retry, deterministic cache identity, and atomic final output.
- A documented supported input/output matrix and clear unsupported-media errors.
- Export remains available without Pro and never consumes Ingots for local work.

Why Free: “export” is not complete if the user cannot choose a useful target or trust that preview and final output represent the same edit.

### 7. AI-native edit-to-generation bridge

- Capture an exact immutable composition frame or range from a pinned composition revision as a logical generation reference.
- Start a generation draft from a selected clip/range while preserving source, prompt/provenance context where available.
- Validate each logical occurrence against provider capabilities before any submission.
- Ingest outputs as new durable Project Media, then invoke the same generic timeline replace command defined by structural editing; do not create a generation-specific replacement path.
- Preserve Free BYOK, Saved Frame, and Saved Clip behavior and the existing human authorization boundary.

Why Free: this closes ReelForge's differentiating loop. Advanced automated continuity analysis, generative repair, and variant ranking are not required for Free 1.0.

### 8. 1.0 usability and reliability hardening

- First-run and missing-tool guidance that explains what works without FFmpeg/provider credentials and how to configure optional capabilities.
- Focused keyboard/accessibility review of the primary create/import/generate/edit/export path.
- Automated smoke coverage around presentation coordinators where practical and a repeatable human end-to-end acceptance project.
- Measured responsiveness and memory/disk acceptance against an owner-approved representative project, with no requirement for a proxy system unless measurements justify one.
- Clear cancellation, restart, missing-file, full-disk, permission, corrupt-cache, and unsupported-codec behavior.

## 1.0 Pro candidate feature contract

This is product-planning metadata, not an entitlement design or a promise that Pro must launch concurrently with Free 1.0. If a first Pro offer ships, the recommended coherent value proposition is **advanced control, comparison, and throughput**:

| Pro candidate | Value | Relative risk | Prerequisites |
| --- | --- | --- | --- |
| Generic keyframe automation | Animate Free transform, opacity, basic color, gain, and pan parameters with stable keys, easing, and reusable curves. | High | Stable tracks, typed effects/parameters, history, render graph. |
| Advanced trim and timing | Roll, slip, slide, J/L-oriented controls, variable speed ramps, and higher-quality retiming where an approved engine exists. | High to very high | Exact time domains, structural editing, preview/render policy. |
| Variant compare and continuity workspace | Side-by-side or A/B compare generated variants and the preceding shot, reuse provenance, and rapidly replace selected timeline usage. | High | Edit-to-generation bridge, replace semantics, synchronized playback. |
| Productivity tools | Project Media multi-select/bulk operations, batch export, reusable delivery presets, and composition/version conveniences. | Medium to high | Stable commands, progress/cancellation, batch failure policy. |
| Performance controls | Explicit proxy generation/attachment, preview quality controls, cache diagnostics, and render queue controls when measured projects need them. | High | Stable render graph, capability discovery, cache identity. |
| Assisted audio | Loudness analysis/normalization, auto-ducking, and selected speech cleanup when engine and license review pass. | High | Waveforms, analysis artifacts, audio time model, engine discovery. |
| Automatic transcription/captions | Optional local or approved remote transcription feeding the Free caption model, with editable text and timing. | High | Caption model, analysis jobs, language/model packaging and privacy review. |

The smallest credible first Pro bundle is generic keyframes, variant comparison, and productivity/batch tools. Proxy, transcription, and assisted audio should join only when performance evidence, licensing, and support capacity justify them. Advanced repair engines are intentionally not required to make the first Pro offer credible.

## Candidate evaluation summary

| Capability | Omission and product relevance | Complexity, dependencies, performance, and licensing | Decision |
| --- | --- | --- | --- |
| Undo/redo, recovery, relink | Without them the product cannot be trusted with real work; directly relevant to every workflow. | High; history semantics, atomic persistence, identity verification; no external engine. | Free 1.0 |
| Tracks, trim, ripple, replace, gaps, snapping | Split/reorder alone feels incomplete; ubiquitous editing grammar and prerequisite for later finishing. | Very high foundation; schema/time/render/UI migration; CPU cost depends on preview design. | Free 1.0 |
| Canvas and constant transform/crop/opacity | AI shots frequently mismatch frame and aspect; ordinary creator expectation. | High; typed effects and compositor/render graph; FFmpeg filter capability audit. | Free 1.0 |
| Constant speed and freeze | Useful for fitting generated shots; variable ramps are not necessary. | Medium to high after time model; audio/time mapping and render cost. | Free 1.0; ramps Pro |
| Waveforms and audio ranges | Placement without waveforms/trim is needlessly blind; ordinary video-audio workflow. | High; analysis cache plus audio-exact time and mixing updates; FFmpeg-capable. | Free 1.0 |
| Basic transitions and color | Hard cuts only and unmatchable AI shots undermine finishing; catalog breadth and LUT import are unnecessary. | High; effect graph and preview fidelity. Some FFmpeg filters/external libraries can alter license profile. | Small Free set; LUT later/Pro |
| Titles and manual captions | Omitting both forces another tool for credits/accessibility; current creator-editor expectation. | Medium to high; timed overlay/caption model, font shaping/rendering, Unicode, SRT; font/render dependencies need audit. | Narrow Free 1.0 |
| Export profiles | A fixed opaque export is not credible delivery. | High after render graph; codec/container validation and patent/distribution review. | Free 1.0 |
| Exact edit-to-generation/replace | Directly expresses ReelForge's differentiated loop. | High; immutable snapshots, provider preparation, jobs/provenance, replace semantics. | Free 1.0 |
| Keyframes and curves | Advanced control and upgrade value; omission does not block ordinary finishing. | High; generic automation model, UI, cache/render determinism. | Pro candidate |
| Variant/continuity comparison | Highly relevant to iterative AI generation and productivity, but manual selection still works. | High; synchronized playback, provenance, replace workflow; no ML required for first version. | Pro candidate |
| Bulk media and batch export | Valuable at project scale; one-at-a-time operation remains functionally complete. | Medium to high; batch progress, cancellation, partial-failure/retry policy. | Pro candidate |
| Proxies and render controls | Important only when measured projects exceed responsive preview budgets. | High; derivative identity, storage/eviction, relink, render queue; no new creative semantics. | Conditional Pro |
| Transcription/auto-captions | Strong AI productivity expectation, but manual captions preserve completeness. | High; analysis jobs, privacy, model/code/data licenses, language packs, hardware/disk. | Conditional Pro/post-1.0 |
| Stabilize/deflicker/denoise/continuity analysis | Strong AI-shot repair value and plausible Pro depth, but quality/engine uncertainty is large. | High to very high; analysis artifacts, GPU/CPU cost, engine/model licenses; `libvidstab` changes FFmpeg license profile. | Post-1.0 Pro research |
| Generative repair/object/background editing | Differentiated but not needed while generate-reference-replace solves the basic loop. | Very high; new media-edit provider family, billable authorization, provenance, masks, provider rights. | Post-1.0 |
| Interpolation/upscale/tracking/local model packs | Advanced quality and repair, hardware and support heavy. | Very high; optional downloads, model/data licenses, GPU/VRAM, engine/version identity. | Horizon |
| Multicam, node VFX, DAW, broadcast, marketplace | Irrelevant to the smallest AI-native finishing contract despite competitor presence. | Very high product and architecture breadth plus support/licensing ecosystem. | Rejected near term |

## Post-1.0, horizon, and rejected scope

### Post-1.0 candidates

- Provider-neutral media repair/regenerate/extend jobs with new durable outputs and explicit replace-after-success.
- Assisted **Match to Previous Clip**, stabilization, deflicker, denoise, sharpen, and format/loudness matching.
- Scene detection, semantic media search, generation graph visualization, and stronger provenance browsing.
- Reverse playback, advanced transition/effect depth, reusable presets, adjustment layers, and nested compositions when demand is demonstrated.
- Voiceover recording, richer audio meters, dialogue cleanup, and caption translation.
- Floating/dockable timeline and Edit Tools surfaces.
- Direct social delivery only if it proves materially more useful than ordinary export.

### Horizon research

- Optical-flow/neural interpolation, super-resolution, segmentation, tracking, masking, face repair, object/background editing, and premium restoration packs.
- Local generation through ComfyUI or large model packs. MiniMax H3 local execution remains unsuitable for scheduling without territory, hardware, disk, runtime, and distribution gates.
- Advanced color management, scopes, HDR mastering, plugin hosting, and interchange with professional editors.
- Collaboration, cloud projects, teams, review links, and shared asset systems.

### Rejected from the 1.0/near-term product direction

- Resolve/Fusion/After Effects-class node compositing.
- DAW/sample/spectral audio editing or Fairlight-class mixing.
- Multicam, live switching, immersive/VR/Atmos, broadcast/DCP/IMF mastering, and enterprise media management.
- A large effects/template marketplace or professional plugin ecosystem before ReelForge's native effect model is mature.
- Stock libraries, social-network publishing, or cloud collaboration merely for competitor parity.
- Any feature that requires an account, managed compute, or Pro entitlement to open existing projects or perform ordinary safe local export.

## Dependency relationships

```text
project format decision       media capability/license gate
          |                               |
          v                               |
track/time/item identities               |
 + revision/history contract              |
          |                               |
          v                               v
structural commands + undo ------> render/playback/export graph
          |                               |
          +---------------+---------------+
                          v
          visual effects + audio ranges + text items
                          |
                          v
       exact composition references + generic replace
                          |
               +----------+----------+
               v                     v
        Pro automation       Pro comparison/analysis

recovery + relink -----------------------> 1.0 hardening
```

Optional external engines depend on capability discovery, engine/version identity, license review, artifact integrity, hardware checks, and fallback behavior. They are never prerequisites for opening a Free project.

## Recommended implementation slices

These slices are dependency units, not preassigned milestone numbers. The high-risk model and rendering slices should not be hidden inside one oversized “editing features” milestone.

### Gate 0 — media capability and licensing feasibility

Scope: a bounded decision spike before render, visual, or text implementation. Choose the developer/user-configured tool capabilities used for desktop acceptance; candidate export encoders/containers; required FFmpeg filters; text renderer and font fallback/embedding policy; supported caption and color controls; and the behavior when a configured tool lacks a required capability. Record code/library/font license obligations and keep packaged binaries in the later release-engineering phase.

Acceptance:

- the supported desktop capability matrix names required encoders, decoders, filters, font/text behavior, and failure messages without claiming that a public distribution build is approved;
- small fixtures prove the proposed transform, transition, basic color, title/caption burn-in, audio, and delivery paths with a developer-approved tool;
- any GPL, nonfree, patent, font, or external-library implication is visible to the later release-engineering/legal gate;
- the Free contract is narrowed before implementation if a required path has no acceptable capability or license strategy.

Risk: Medium as a spike; it retires high-impact licensing and feasibility risk before high-cost implementation.

### Slice A — project recovery and relink

Scope: recovery backups/journal, degraded-source state, verified relink, and clear dirty/saved/recovered state. This slice can proceed independently of the track migration.

Acceptance:

- a killed process leaves either the last committed project or one clearly offered recovery candidate;
- relink accepts the expected content identity, rejects mismatches unless the user deliberately imports a replacement as new media, and repairs all logical references without changing AssetId;
- recovery and relink preserve immutable recipes, anchors, generation snapshots, provenance, and cache independence;
- startup, recovery, autosave, and tests cannot authorize provider spending.

Risk: Medium to high.

### Slice B — track/time model and format correction

Scope: persisted tracks/items, video and audio time domains, the target immutable revision/history-cursor contract, compatibility treatment for current development projects, invariants, DTOs, and timeline projection. Define history against the new stable identities before implementing undo UI.

Acceptance:

- multiple ordered video/audio tracks persist stable identities and reopen exactly;
- lock, visibility, and mute policy is deterministic and enforced by commands and rendering;
- existing composition fixtures follow the approved conversion/rejection policy with no silent reinterpretation;
- video remains PTS/rational-time exact and audio does not misuse frame anchors;
- the revision/cursor contract distinguishes immutable historical revisions from the active session undo/redo path and defines divergent-edit behavior;
- cache keys and dependency analysis include exact track/item/revision identity.

Risk: Very high. This is the last inexpensive opportunity to correct the pre-1.0 project format.

### Slice C — structural editing grammar

Scope: insert, generic selected-occurrence replace, edge trim, ripple trim/delete, gaps, close-gap, snapping, markers, keyboard commands, selection behavior, and session composition undo/redo over the command families listed in the Free contract.

Acceptance:

- commands are deterministic under locked tracks, overlaps, empty space, exact boundaries, and no-op gestures;
- adjoining edits neither duplicate nor lose a boundary frame;
- ripple operations preserve intended synchronization or clearly reject unsupported cases;
- replace-in-place and replace-and-ripple follow one Application-owned command contract, preserve compatible item properties, apply the Free contract's all-unlocked-tracks ripple domain, and reject ambiguous/too-short input, affected locked tracks, invalid overlap, or unpreservable attachments before mutation; neither mode silently retimes or overwrites source media;
- every completed gesture commits once and participates in undo/redo;
- a new edit after undo discards only the redo branch; save/reopen restores the committed result without promising that the session undo stack survives restart;
- manual acceptance covers mouse, keyboard, restart, and rapid repeated editing.

Risk: High.

### Slice D — render/playback/export graph

Scope: track-aware render planning, effect-aware preview, canvas/export profiles, progress, cancellation, stale-result rejection, and the configured-tool capability policy established by Gate 0.

Acceptance:

- audition/preview/export resolve the same committed semantics at their declared quality levels;
- a change during render cannot present or export a stale result as current;
- cancellation leaves no authoritative partial output and retry succeeds;
- profiles deterministically define canvas, resolution, frame rate, quality, and audio;
- missing encoders/filters/fonts or unsupported configured tools fail capability checks before render with actionable guidance;
- representative mixed-format projects meet owner-approved responsiveness and final-output criteria.

Risk: High to very high.

### Slice E — visual finishing fundamentals

Scope: typed constant transform/crop/opacity, canvas fit/fill, constant speed/freeze, restrained basic color, and two basic transitions. LUT import remains later/Pro.

Acceptance:

- properties are typed, ordered, persisted, hash-stable, and rendered by the existing materialization owner;
- UI continuous controls remain draft state and commit once;
- preview and export match for rotation, crop, alpha, aspect mismatch, retiming, color, and transition boundaries;
- unsupported combinations fail before FFmpeg execution with actionable guidance;
- no feature-specific parallel renderer or arbitrary string parameter bag is introduced.

Risk: High.

### Slice F — practical audio finishing

Scope: waveform artifacts/UI, exact source ranges, trim/split, snapping, short crossfades, and clipping indication.

Acceptance:

- waveforms are cacheable, cancellable, tied to exact source/engine identity, and reconstructable;
- source ranges and timeline positions remain synchronized after trim, split, ripple, undo, and reopen;
- fades/crossfades are anchored to the audible range and final mix remains deterministic 48 kHz stereo;
- preview and export agree within defined audio timing tolerance;
- no DAW/sample-editor scope is introduced.

Risk: High.

### Slice G — minimal text and captions

Scope: typed timed text items, basic title/lower-third UI, caption authoring, SRT import/export, and burn-in through the text/font capability chosen at Gate 0.

Acceptance:

- title and caption timing survives trim/ripple/undo/reopen according to explicit attachment rules;
- fonts, missing fonts, Unicode, line wrapping, safe areas, and aspect-ratio changes have deterministic behavior;
- SRT round-trip preserves supported timing/text and reports unsupported constructs;
- burned-in captions match preview and sidecar export remains available;
- transcription, translation, and template marketplaces remain out of scope.

Risk: Medium to high.

### Slice H — edit-to-generation bridge

Scope: immutable composition frame/range references, draft creation from a timeline selection, provenance reuse, output comparison entry point, and invocation of the generic Slice C replace command after ingestion.

Acceptance:

- a reference pins composition revision and exact range; later edits cannot change submitted meaning;
- cache deletion and reopen preserve the logical reference and allow reconstruction;
- provider preparation validates media type, duration, role, order, and limits before authorization;
- automated tests remain physically incapable of paid submission;
- output ingestion creates new durable media; the generation workflow hands that asset to the generic replace command, which changes only the selected usage while preserving the original source and prior recipe revision.

Risk: High. This is the first new 1.0 slice that materially extends ReelForge's differentiation rather than conventional completeness.

### Slice I — 1.0 hardening gate

Scope: onboarding/error UX, supported-media matrix, performance acceptance, desktop smoke coverage, accessibility/keyboard review, and full manual regression.

Acceptance:

- one representative user can create/open a project, import or generate, save an exact reference, assemble and finish, preview, export, restart, relink, undo/redo, and recover without developer intervention;
- the approved reference project passes responsiveness, memory, disk, cache-rebuild, cancellation, and output-quality checks;
- every Free capability has deterministic domain/service coverage where practical and a named manual path for actual WPF behavior;
- all six suites, portable CI, Windows CI, architecture checks, and the full manual matrix pass;
- no live paid-provider request is part of automated acceptance.

Risk: High because it exposes integration and performance debt, even though it should add little new product scope.

## Architecture, persistence, performance, and engine implications

- **Architecture:** Core owns track/effect/time/history invariants; Application owns commands, references, recovery, and capability contracts; Infrastructure owns FFmpeg, analysis artifacts, cache, and persistence; App owns WPF interaction and draft state. No slice may create a parallel renderer, persistence path, or provider workflow.
- **Persistence:** tracks, effects, text, captions, markers, history cursor, and exact composition references require explicit typed DTO/mapping changes. The current development format rejects obsolete versions, so the owner must choose the last pre-1.0 compatibility policy before Slice B.
- **Rendering:** multiple video tracks, overlays, transitions, text, effects, and retiming require a richer deterministic render graph rather than isolated flags appended to `FfmpegCommandBuilder`. Preview/final profiles may differ in quality, never in creative meaning.
- **Playback:** the existing fast source-by-source audition cannot display every future overlay/effect. Slice D must define when live composition is possible, when partial preview rendering is required, and how stale results are rejected.
- **Audio:** audio boundaries require an audio-appropriate exact time representation. Waveforms and loudness scans are reconstructable analysis artifacts, not project truth.
- **Text:** titles and captions need a typed overlay model plus an audited font shaping/render path. Font files, Unicode behavior, fallback, and any FFmpeg/font-library dependency are part of the capability contract.
- **Cache/performance:** effects and analysis keys include exact source/revision, engine/version, settings, and purpose. Proxies are deferred until measured need; if added, they remain disposable and replaceable.
- **FFmpeg:** the current command builder requests `libx264`, which cannot be assumed in the planned LGPL-only packaged build. Most FFmpeg code is LGPL, while optional GPL components and external libraries can change the resulting obligations; `--enable-nonfree` builds are unredistributable, and codec/patent review is separate. Desktop Feature Complete may be validated with an approved user-configured/developer tool profile; choosing, producing, auditing, and packaging the public binary and final encoder contract remains release-engineering work.
- **Optional engines:** stabilization, transcription, interpolation, upscale, segmentation, tracking, and repair require separate code/model/data license, hardware, integrity, privacy, update, and fallback gates. No engine name belongs in project-domain concepts.
- **Providers and cost:** ordinary local edits never require Ingots. A future media-edit provider is a separate semantic capability family from video generation and retains explicit human authorization for billable execution.
- **Portability:** shared behavior stays in portable Core/Application/Infrastructure. Windows presentation and platform integration remain replaceable host layers; no 1.0 feature may move Windows APIs into portable projects.

## Definition of 1.0 Feature Complete

ReelForge desktop 1.0 is Feature Complete when:

1. every existing implemented capability in the audited inventory remains supported;
2. every item in the 1.0 Free contract is implemented or the owner explicitly amends this contract with documented evidence;
3. the generate/import → exact reference → edit/finish → preview/export → edit-to-generation/replace loop works in one project without another editor for routine output;
4. project state, history, media identity, and durable outputs survive save/reopen, cache deletion, cancellation, expected failure, and approved recovery scenarios;
5. preview and export share creative semantics and meet the approved desktop configured-tool capability, supported-media, and performance matrix;
6. Free remains useful offline for local project/edit/export work and supports BYOK without Pro or Ingots;
7. no account, billing, entitlement, installer, store, updater, managed compute, or macOS requirement is hidden inside desktop feature completion;
8. automated and manual acceptance pass without automated paid-provider calls; and
9. remaining post-1.0, Pro, horizon, and rejected items are not required to call the Free desktop feature set complete.

Feature Complete is not Release Ready. Packaged license-audited FFmpeg/ffprobe, signing, installers, updates, distribution, store review, support policy, and any commerce launch are separate later gates.

In particular, Feature Complete does not certify an H.264 encoder or public FFmpeg distribution. It proves the desktop behavior against the approved development/user-configured capability profile; the release-engineering gate chooses and verifies what ReelForge may ship.

## Open owner decisions

These cannot be settled from current principles alone:

1. **Pre-1.0 project compatibility:** may the track/effect schema make one final clean break from Milestone 1–3 development files, or must ReelForge provide a one-time converter? Recommendation: allow one explicit pre-1.0 format correction; provide a converter only if real user projects justify it.
2. **Track presentation breadth:** approve the recommended scalable persisted model with a simple multitrack UI, or intentionally ship a primary-storyline UI over that model. The domain should not retain the current sequential-only limitation.
3. **Desktop media capability contract:** choose the development/user-configured encoder, container, audio, filter, text/font, caption, and basic-color matrix used for Feature Complete. Gate 0 must expose any GPL, patent, font, or external-library implications without pre-deciding the later packaged distribution.
4. **Public delivery contract:** choose the exact shipped canvas, resolution, frame-rate, codec/container, and audio matrix plus representative inputs during release engineering. The present `libx264` command is not approval to bundle it.
5. **Performance target:** define representative project duration, track count, source resolution/codecs, hardware floor, acceptable interaction latency, preview delay, export behavior, and disk budget. Proxy scope cannot be decided honestly without this.
6. **Pro launch timing:** decide whether Pro must ship with Free 1.0 or may follow it. Recommendation: do not delay Free Feature Complete for Pro; if simultaneous, use keyframes + variant comparison + productivity as the first coherent bundle.
7. **External repair at 1.0:** decide whether any provider-neutral repair operation must launch with Pro 1.0. Recommendation: no; validate the ordinary edit-to-generation/replace loop first.
8. **Product instrumentation:** define how feature value and performance will be evaluated without smuggling account or telemetry infrastructure into this milestone. Manual studies and opt-in local diagnostics may be sufficient initially.

## Research basis

Repository evidence: [README](../README.md), [milestone history](milestones.md), [architecture](architecture.md), [editor capability direction](editor-capability-direction.md), [business and packaging direction](business-and-packaging.md), [provider research](provider-research.md), and [manual acceptance](manual-acceptance.md).

Current official product evidence was used only to identify expectations:

- [CapCut Desktop](https://www.capcut.com/tools/desktop-video-editor)
- [DaVinci Resolve Edit](https://www.blackmagicdesign.com/products/davinciresolve/edit)
- [Adobe Premiere features](https://www.adobe.com/products/premiere/features.html)
- [Adobe Premiere desktop help](https://helpx.adobe.com/ca/premiere/desktop/edit-projects/edit-video-using-text-based-editing/overview-of-text-based-editing.html)
- [FFmpeg legal guidance](https://ffmpeg.org/legal.html)
- [FFmpeg license and external-library details](https://ffmpeg.org/doxygen/trunk/md_LICENSE.html)

These products demonstrate that trim/ripple, tracks, transforms, audio placement, text/captions, and delivery controls are ordinary editor expectations, while professional compositing, multicam, deep color/audio, large effect catalogs, and AI engine breadth are differentiators rather than ReelForge requirements. Every external capability and license must be re-verified at its implementation gate.
