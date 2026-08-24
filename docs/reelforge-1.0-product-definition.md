# ReelForge 1.0 product definition

Status: owner-approved product direction; planning only; evidence gates remain

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

The desktop feature program should therefore finish the editing and reliability foundation around this loop. It must not absorb commerce, managed compute, release engineering, installers, store publication, or macOS work. Those are separate workstreams even when a public release depends on more than one of them.

## Scope rules

- **Free is the complete core product.** It includes the useful generate-to-finish workflow, BYOK generation, Saved Frames, Saved Clips, ordinary local editing, and ordinary export.
- **Pro is depth and leverage.** Its candidates add automation, advanced control, analysis, comparison, repair, and throughput. Pro does not remove project custody, basic editing, safe export, or BYOK from Free.
- **Ingots are orthogonal.** Managed compute, accounts, billing, entitlements, security, and payment processing are outside this product-definition milestone and outside the desktop 1.0 implementation slices.
- **Tier names are metadata.** The project and recipe formats remain tier-neutral. This roadmap does not authorize entitlement code.
- **Current plans are evidence, not promises.** A previously mentioned feature survives only when it supports the smallest coherent product.
- **Competitors are expectation evidence, not specifications.** ReelForge needs credible editing fundamentals, not their catalog breadth.

## Approved release shape

The product and release labels are deliberately separated:

1. **Free Desktop Feature Complete** is the implementation target defined by the Free contract and Slices A-J below.
2. **External Free Beta** follows Feature Complete plus a separate reproducible beta-package gate, diagnostic/feedback readiness, and friends-and-family acceptance. It does not require Pro, Ingots, final signing, a production installer, marketplace publication, or a production update pipeline. A beta either requires an approved user-configured media tool and bundles none, or separately reviews any included beta runtime for redistribution and license compliance; Gate 0 capability evidence alone never authorizes shipping a binary.
3. **Full 1.0.0** is the target for adding a separately implemented first Pro continuity/repair offer and managed Ingots soon after Free beta. Accounts, billing, entitlements, security, managed compute, and payment processing remain a distinct backend/commercial program; no desktop feature slice below owns them.
4. **Production Release Ready** remains a separate release-engineering outcome: the exact bundled media runtime, license audit, legal review where required, signing, production installers, CI/CD release pipeline, updates, distribution, and store work.

This sequencing prevents the Free editor from waiting on commerce while still making the desired 1.0.0 launch composition explicit. If the independent Pro or Ingots workstream slips, the owner chooses whether to move the 1.0.0 label; the Free desktop contract does not silently expand or regress.

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
- Milestone 1-3 sequential development files may be rejected at the final pre-beta format break; no converter or reopen guarantee is required. Compatibility obligations begin only with the deliberately declared external-beta format baseline.

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

- Named composition, re-encode, and export profiles covering media type, container, codec, canvas, resolution, frame rate, quality, and audio behavior. Free includes ordinary single-item and composition conversion to the common delivery formats approved at Gate 0; it is not restricted to one video and one audio format merely to create a Pro gate. The likely defaults remain MP4 video and M4A audio, but Gate 0 must approve the actual free-to-use-and-distribute baseline rather than treating today's `libx264`/AAC commands as a contract.
- Export or re-encode a selected physical/virtual video, a selected audio asset/range, and the Working Composition without modifying the source. Batch conversion, custom profile builders, and specialty/professional delivery formats may be Pro; ordinary broadly useful formats supported by the approved baseline remain Free.
- Preview-quality policy that stays responsive, clearly identifies draft versus final fidelity, and never exports from a stale revision. Basic automatic reduced-quality preview or disposable proxy use is Free when required to meet the performance contract; Pro may add manual proxy, cache, and queue controls.
- Stage-aware progress, cancellation, retry, deterministic cache identity, and atomic final output.
- A documented supported input/output matrix and clear unsupported-media errors.
- Export remains available without Pro and never consumes Ingots for local work.

An explicitly configured local FFmpeg/ffprobe installation may expose additional capability-qualified formats or filters that are absent from ReelForge's baseline profile. Those choices must be labeled as local-tool capabilities, checked before work starts, fail clearly when unavailable, and never change project meaning or make an ordinary Free project impossible to open. Availability alone does not decide Free versus Pro placement.

Why Free: “export” is not complete if the user cannot choose a useful target or trust that preview and final output represent the same edit.

### 7. Workspace and viewer ergonomics

- The Timeline and Edit Tools are dockable within the Edit workspace and detachable into floating windows. Floating or docking a surface never creates a second project, editor session, selection, history, or playback owner.
- Layout is machine-local rather than `.rfp` project truth, survives ordinary restart, and safely restores into the visible desktop when monitor topology or DPI changes.
- The media viewer presents three clear targets:
  - **Source** shows the selected Project Media object or the unedited source of a selected timeline occurrence.
  - **Edit** shows the current Working Composition at the shared playhead. Selecting a timeline occurrence adds contextual on-canvas handles or assistance for that occurrence without creating a second authoritative editor.
  - **Rendered** shows the newest explicit durable bake/materialization and identifies the exact composition revision it represents. It labels an older bake as out of date and offers a render action when no bake exists for the current revision. Disposable Edit previews never masquerade as Rendered output.
- Project Media selection may move the viewer to Source and timeline selection may move it to Edit; Rendered is an explicit user choice. Transport, playhead, selection, and draft state have one coordinator even when surfaces float.

This adopts the intent behind **Audition | Focused Editor | Rendered** but avoids making “Audition” mean both source preview and composition playback, or making Focused Editor a parallel editing session. The user receives the same three contexts—raw/source inspection, contextual composition editing, and materialized output—with unambiguous state.

Why Free: flexible workspace arrangement and clear media context are foundational usability for large and multi-context projects, not professional-only creative depth.

### 8. AI-native edit-to-generation bridge

- Capture an exact immutable composition frame or range from a pinned composition revision as a logical generation reference.
- Start a generation draft from a selected clip/range while preserving source, prompt/provenance context where available.
- Validate each logical occurrence against provider capabilities before any submission.
- Ingest outputs as new durable Project Media, then invoke the same generic timeline replace command defined by structural editing; do not create a generation-specific replacement path.
- Preserve Free BYOK, Saved Frame, and Saved Clip behavior and the existing human authorization boundary.

Why Free: this closes ReelForge's differentiating loop. Advanced automated continuity analysis, generative repair, and variant ranking are not required for Free 1.0.

### 9. 1.0 usability and reliability hardening

- First-run and missing-tool guidance that explains what works without FFmpeg/provider credentials and how to configure optional capabilities.
- Focused keyboard/accessibility review of the primary create/import/generate/edit/export path.
- Automated smoke coverage around presentation coordinators where practical and a repeatable human end-to-end acceptance project.
- Measured responsiveness and memory/disk acceptance against the approved reference profile: Windows on 32 GB RAM, Ryzen 7 3700X, and RTX 3070 Ti 8 GB; primarily 30-second to 10-minute projects at 720p/1080p interactive resolution; higher-resolution inputs accepted where feasible through reduced-quality preview or disposable proxies; and 4K import/export permitted without guaranteeing full-resolution real-time 4K editing. Longer 1-2 hour projects must remain openable and operable, but are not the primary interactive-optimization case.
- Media processing is bounded and cancellable. FFmpeg concurrency/thread use leaves capacity for the application and operating system, and rendering, exporting, baking, waveform/proxy analysis, and diagnostics never freeze the editor UI.
- Clear cancellation, restart, missing-file, full-disk, permission, corrupt-cache, and unsupported-codec behavior.
- An explicit **Export Diagnostic Bundle** action containing sanitized application logs, app/OS/hardware information, media-runtime path/version/configuration/capabilities, and render/job/performance timing. Credentials, authorization material, signed URLs, prompts, and user media are excluded by default; no bundle is uploaded automatically.
- A structured external-acceptance checklist and standard feedback/bug-report template for friends/family builds followed by a small target group of AI-video creators. No silent telemetry is introduced.

## Pro feature contract and staged roadmap

This is product-planning metadata, not an entitlement design. Free beta does not wait for Pro. The intended first Pro value proposition for full 1.0.0 is **continuity, repair, and deeper control for AI-video finishing**, subject to an exact engine/license/quality gate:

| Pro candidate | Value | Relative risk | Prerequisites |
| --- | --- | --- | --- |
| Generic keyframe automation | Animate Free transform, opacity, basic color, gain, and pan parameters with stable keys, easing, and reusable curves. | High | Stable tracks, typed effects/parameters, history, render graph. |
| Advanced trim and timing | Roll, slip, slide, J/L-oriented controls, variable speed ramps, and higher-quality retiming where an approved engine exists. | High to very high | Exact time domains, structural editing, preview/render policy. |
| Variant compare and continuity workspace | Side-by-side or A/B compare generated variants and the preceding shot, reuse provenance, and rapidly replace selected timeline usage. | High | Edit-to-generation bridge, replace semantics, synchronized playback. |
| Productivity tools | Project Media multi-select/bulk operations, batch export, reusable delivery presets, and composition/version conveniences. | Medium to high | Stable commands, progress/cancellation, batch failure policy. |
| Performance controls | Explicit proxy generation/attachment, preview quality controls, cache diagnostics, and render queue controls when measured projects need them. | High | Stable render graph, capability discovery, cache identity. |
| Assisted audio | Loudness analysis/normalization, auto-ducking, and selected speech cleanup when engine and license review pass. | High | Waveforms, analysis artifacts, audio time model, engine discovery. |
| Automatic transcription/captions | Optional local or approved remote transcription feeding the Free caption model, with editable text and timing. | High | Caption model, analysis jobs, language/model packaging and privacy review. |
| Continuity and repair toolkit | Assisted **Match to Previous Clip**, stabilization, deflicker, denoise, sharpen, and format/loudness matching. Each operation is independently capability-gated and creates typed settings/analysis artifacts rather than assuming one FFmpeg filter family. | High to very high | Stable effect/analysis model, Gate 0 engine candidates, performance budgets, code/model/data license review, preview/export agreement. |
| Deeper finishing | Reverse playback, a broader audited transition/effect catalog, LUTs, reusable effect presets, adjustment layers, and nested compositions when demand is demonstrated. | High to very high | Stable effects/render graph, automation, nesting/cycle rules, capability discovery. |
| Media intelligence and provenance | Scene detection, semantic media search, generation-graph visualization, and stronger provenance browsing. | High to very high | Analysis jobs/indexes, privacy/storage policy, stable provenance graph. |
| Advanced audio and captions | Voiceover recording, richer meters, dialogue cleanup, caption translation, and deeper audio matching. | High | Audio timing/mix model, device abstraction, engine/language review. |
| Provider-assisted repair | Provider-neutral regenerate/extend/object/background operations that always ingest new durable media before an explicit replace. | Very high | Separate media-edit provider contract, immutable requests, cost authorization, jobs, provenance, provider/legal review. |
| Direct delivery integrations | Social/publishing delivery only where evidence shows material value beyond ordinary Free export. | High | External APIs/accounts, credential/security/support policy; separate from core export. |

The continuity and repair toolkit is the target first Pro bundle alongside an owner-approved subset of keyframes, comparison, or productivity. It is not permission to depend on GPL-only filters or ship an unverified engine: `libvidstab` stabilization and several FFmpeg denoisers are known GPL-path blockers for an LGPL-only runtime. Gate 0 must evaluate each operation separately and the owner must approve its actual implementation contract. Provider/cloud repair is not required for Free beta or the first local Pro bundle.

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
| Common re-encode/export profiles | A fixed opaque export or one-format tax is not credible delivery; competitors expose format/preset choices as ordinary workflow. | High after render graph; capability-reported codecs/containers, audio-only output, patent/distribution review. | Free 1.0 common baseline |
| Advanced delivery | Batch conversion, queues, reusable/custom profiles, and specialty/professional formats create productivity and support cost without blocking ordinary output. | Medium to high; batch failure policy and exact runtime support. | Pro |
| Dockable workspace and clear viewer contexts | Fixed panels constrain real editing work; conflating source, live edit, and rendered output risks state mistakes. | Medium to high presentation work; shared coordinators, reparenting, multi-monitor/DPI restore, stale-revision identity. | Free 1.0 |
| Exact edit-to-generation/replace | Directly expresses ReelForge's differentiated loop. | High; immutable snapshots, provider preparation, jobs/provenance, replace semantics. | Free 1.0 |
| Keyframes and curves | Advanced control and upgrade value; omission does not block ordinary finishing. | High; generic automation model, UI, cache/render determinism. | Pro candidate |
| Variant/continuity comparison | Highly relevant to iterative AI generation and productivity, but manual selection still works. | High; synchronized playback, provenance, replace workflow; no ML required for first version. | Pro candidate |
| Bulk media and batch export | Valuable at project scale; one-at-a-time operation remains functionally complete. | Medium to high; batch progress, cancellation, partial-failure/retry policy. | Pro candidate |
| Responsive high-resolution preview | Higher-resolution sources must not freeze ordinary 720p/1080p editing. | High; automatic reduced preview/proxy identity, storage/eviction, render budgets. | Basic automatic behavior Free; manual controls Pro |
| Transcription/auto-captions | Strong AI productivity expectation, but manual captions preserve completeness. | High; analysis jobs, privacy, model/code/data licenses, language packs, hardware/disk. | Conditional Pro/post-1.0 |
| Match/stabilize/deflicker/denoise/sharpen/format/loudness | High-value continuity and repair for generated clips; intended first Pro differentiation. | High to very high; distinct analysis/engine needs, UI responsiveness, license quality gates; `libvidstab` and several denoisers are GPL-path blockers. | Pro 1.0 target, evidence-gated |
| Generative repair/object/background editing | Differentiated but not needed while generate-reference-replace solves the basic loop. | Very high; new media-edit provider family, billable authorization, provenance, masks, provider rights. | Later Pro research |
| Interpolation/upscale/tracking/local model packs | Advanced quality and repair, hardware and support heavy. | Very high; optional downloads, model/data licenses, GPU/VRAM, engine/version identity. | Horizon |
| Multicam, node VFX, DAW, broadcast, marketplace | Irrelevant to the smallest AI-native finishing contract despite competitor presence. | Very high product and architecture breadth plus support/licensing ecosystem. | Rejected near term |

## Pro horizon, research horizon, and rejected scope

### Staged Pro roadmap after the first local bundle

- Provider-neutral media repair/regenerate/extend jobs with new durable outputs and explicit replace-after-success.
- Scene detection, semantic media search, generation graph visualization, and stronger provenance browsing.
- Reverse playback, advanced transition/effect depth, reusable presets, adjustment layers, and nested compositions when demand is demonstrated.
- Voiceover recording, richer audio meters, dialogue cleanup, and caption translation.
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
pre-beta format boundary      LGPL-first capability/license gate
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
       workspace/viewer state + exact composition references + generic replace
                          |
               +----------+----------+
               v                     v
        Pro automation       Pro comparison/continuity/repair

recovery + relink -----------------------> 1.0 hardening
```

Optional external engines depend on capability discovery, engine/version identity, license review, artifact integrity, hardware checks, and fallback behavior. They are never prerequisites for opening a Free project.

## Recommended implementation slices

These slices are dependency units, not preassigned milestone numbers. The high-risk model and rendering slices should not be hidden inside one oversized “editing features” milestone.

### Gate 0 — distribution-realistic media capability and licensing feasibility

Scope: a bounded decision spike before render, visual, text, or Pro-repair implementation. Define and pin an LGPL-compatible development/CI profile that is realistic for eventual free distribution, replacing today's accidental reliance on arbitrary developer installations and `libx264`. Evaluate candidate video and standalone-audio import, encode, re-encode, and export formats; required Free filters; text renderer and font fallback/embedding policy; captions and basic color; thread/concurrency behavior; and each proposed Pro continuity/repair operation independently. Also define an explicit enhanced local-tool profile whose discovered extras cannot become Free baseline dependencies. Record code/library/font license obligations and known patent/redistribution blockers while keeping selection, audit, build, signing, and packaging of the exact public binary in release engineering.

Acceptance:

- the approved development capability matrix names required encoders, decoders, containers, pixel/audio formats, filters, font/text behavior, thread policy, and failure messages without claiming that a public distribution binary is approved;
- the matrix proposes a common Free video/audio conversion and delivery baseline, identifies optional enhanced local-tool profiles, and reports capability source and portability/support limits in data rather than scattered engine checks;
- small fixtures prove transform, transition, basic color, title/caption burn-in, audio mixing, video re-encode, audio export, and delivery against the pinned LGPL-compatible development profile; CI detects profile drift or forbidden GPL/nonfree dependencies;
- Match to Previous Clip, stabilization, deflicker, denoise, sharpen, format matching, and loudness matching each name a candidate engine/path, quality measure, resource model, fallback, and license state. Known blockers include GPL `libvidstab` and several GPL denoise filters; no bundle-level promise hides those distinctions;
- any GPL, nonfree, patent, font, code/model/data, or external-library implication is visible to the later release-engineering/legal gate;
- user-local enhanced capabilities are checked before work starts, labeled clearly, and never silently change creative meaning or become necessary to open a baseline Free project;
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

Scope: persisted tracks/items, video and audio time domains, the target immutable revision/history-cursor contract, the final pre-beta development-format correction, invariants, DTOs, and a straightforward traditional multitrack projection. Define history against the new stable identities before implementing undo UI.

Acceptance:

- multiple ordered video/audio tracks persist stable identities and reopen exactly;
- lock, visibility, and mute policy is deterministic and enforced by commands and rendering;
- the implementation may make one explicit clean break from Milestone 1-3 development files and rejects obsolete internal formats clearly without a migration ladder or silent reinterpretation;
- DTO/version boundaries remain migration-capable, and the deliberately declared external-beta format marker becomes the first compatibility baseline for future changes;
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

### Slice D — render/playback/conversion/export graph

Scope: track-aware render planning, effect-aware preview, Free conversion/export profiles, progress, cancellation, stale-result rejection, automatic reduced-quality preview/proxy behavior, resource constraints, and the capability policy established by Gate 0.

Acceptance:

- audition/preview/export resolve the same committed semantics at their declared quality levels;
- a change during render cannot present or export a stale result as current; every materialized result is tied to and visibly reports its exact composition revision;
- cancellation leaves no authoritative partial output and retry succeeds;
- profiles deterministically define media type, container, codec, pixel/audio format, canvas, resolution, frame rate, quality, and audio;
- a selected physical/virtual video, selected audio asset/range, and the Working Composition can be converted/exported through the approved common Free profile without modifying source media;
- missing encoders/filters/fonts or unsupported configured tools fail capability checks before render with actionable guidance;
- high-resolution sources use declared draft quality or disposable proxies when required and return to final semantics for export;
- FFmpeg concurrency and thread budgets leave the editor responsive; render, export, bake, analysis, cancellation, and rapid editing never block the UI thread;
- representative mixed-format projects meet the owner-approved responsiveness and final-output criteria on the reference machine.

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

### Slice H — workspace and viewer contexts

Scope: dock/floating hosts for Timeline and Edit Tools; machine-local layout persistence and recovery; a single viewer coordinator presenting Source, Edit, and Rendered targets; contextual on-canvas focused editing; and explicit current/out-of-date/not-rendered durable-bake identity.

Acceptance:

- docking, floating, closing, reopening, project switching, and application restart preserve one authoritative composition, selection, history, playhead, playback, and draft state;
- no floating surface creates another editor coordinator, media owner, playback transport, save path, or event subscription that can mutate independently;
- Project Media and timeline selection choose the documented Source/Edit context without losing the user's explicit Rendered choice unexpectedly;
- Rendered presents only the latest explicit durable bake/materialization, identifies its exact recipe revision as Current or Out of date, and offers a render action when no current bake exists; disposable Edit previews remain in Edit;
- layout remains machine-local, restores safely after monitor/DPI changes, and has a visible reset action; no WPF/window/monitor concept enters Core, Application, or `.rfp` persistence;
- Windows-specific window placement remains inside the App/platform presentation boundary so a future macOS host can implement its own placement behavior over the same editing contracts.

Risk: Medium to high. The feature is presentation-scoped, but control reparenting, window lifetime, DPI/monitor restore, and shared event routing are easy sources of duplicate state.

### Slice I — edit-to-generation bridge

Scope: immutable composition frame/range references, draft creation from a timeline selection, provenance reuse, output comparison entry point, and invocation of the generic Slice C replace command after ingestion.

Acceptance:

- a reference pins composition revision and exact range; later edits cannot change submitted meaning;
- cache deletion and reopen preserve the logical reference and allow reconstruction;
- provider preparation validates media type, duration, role, order, and limits before authorization;
- automated tests remain physically incapable of paid submission;
- output ingestion creates new durable media; the generation workflow hands that asset to the generic replace command, which changes only the selected usage while preserving the original source and prior recipe revision.

Risk: High. This is the first new 1.0 slice that materially extends ReelForge's differentiation rather than conventional completeness.

### Slice J — Free desktop hardening gate

Scope: onboarding/error UX, supported-media matrix, performance acceptance, diagnostic/feedback readiness, desktop smoke coverage, accessibility/keyboard review, and full manual regression.

Acceptance:

- one representative user can create/open a project, import or generate, save an exact reference, assemble and finish, preview, export, restart, relink, undo/redo, and recover without developer intervention;
- representative 30-second to 10-minute 720p/1080p projects pass responsiveness, memory, disk, cache-rebuild, cancellation, and output-quality checks on the reference machine; a longer-project fixture remains operable, and measured track-count limits are documented rather than guessed;
- higher-resolution source acceptance, reduced-quality preview/proxy behavior, 4K import/export claims, FFmpeg thread limits, and background job concurrency match the approved performance matrix without freezing editor interaction;
- every Free capability has deterministic domain/service coverage where practical and a named manual path for actual WPF behavior;
- all six suites, portable CI, Windows CI, architecture checks, and the full manual matrix pass;
- no live paid-provider request is part of automated acceptance;
- Export Diagnostic Bundle requires an explicit local action and passes redaction tests proving that credentials, authorization material, signed URLs, prompts, and media are excluded by default while useful logs, system/runtime capability data, and render/job/performance timings remain;
- no diagnostic, acceptance, or feedback path silently uploads telemetry;
- friends/family and target-creator builds use a versioned acceptance-task checklist and standard feedback/bug-report template.

Risk: High because it exposes integration and performance debt, even though it should add little new product scope.

## Architecture, persistence, performance, and engine implications

- **Architecture:** Core owns track/effect/time/history invariants; Application owns commands, references, recovery, and capability contracts; Infrastructure owns FFmpeg, analysis artifacts, cache, and persistence; App owns WPF interaction and draft state. No slice may create a parallel renderer, persistence path, or provider workflow.
- **Persistence:** tracks, effects, text, captions, markers, history cursor, and exact composition references require explicit typed DTO/mapping changes. ReelForge may make one final clean format break before declaring the external-beta baseline; internal development formats are rejected rather than migrated. DTO/mapping/version boundaries remain migration-capable, but backward-compatibility obligations start only at that deliberate supported marker.
- **Rendering:** multiple video tracks, overlays, transitions, text, effects, and retiming require a richer deterministic render graph rather than isolated flags appended to `FfmpegCommandBuilder`. Preview/final profiles may differ in quality, never in creative meaning.
- **Playback:** the existing fast source-by-source audition cannot display every future overlay/effect. Slice D must define when live composition is possible, when partial preview rendering is required, and how stale results are rejected.
- **Audio:** audio boundaries require an audio-appropriate exact time representation. Waveforms and loudness scans are reconstructable analysis artifacts, not project truth.
- **Text:** titles and captions need a typed overlay model plus an audited font shaping/render path. Font files, Unicode behavior, fallback, and any FFmpeg/font-library dependency are part of the capability contract.
- **Cache/performance:** effects, waveforms, proxies, and analysis keys include exact source/revision, engine/version, settings, and purpose. Automatic reduced-quality previews/proxies are Free disposable artifacts when the performance policy needs them; Pro may add manual/cache/queue controls. The reference profile is 32 GB RAM, Ryzen 7 3700X, RTX 3070 Ti 8 GB, primarily 30-second to 10-minute 720p/1080p editing. Track-count and lower-hardware floors remain measured outcomes. Higher-resolution source and 4K import/export do not imply full-resolution real-time 4K playback.
- **FFmpeg:** pivot development and CI to the Gate 0 pinned LGPL-compatible capability profile before dependent feature work. The current command builder requests `libx264`, which cannot be assumed there. Most FFmpeg code is LGPL, while optional GPL components and external libraries can change the resulting obligations; `--enable-nonfree` builds are unredistributable, and codec/patent review is separate. The known GPL list includes `libx264`, `libx265`, `libvidstab`, `librubberband`, and several denoise filters. Enhanced user-local tools may expose optional capabilities, but no baseline feature depends on them. Selecting, producing, auditing, signing, and packaging the exact public binary and final encoder contract remains release-engineering work.
- **Optional engines:** stabilization, transcription, interpolation, upscale, segmentation, tracking, and repair require separate code/model/data license, hardware, integrity, privacy, update, and fallback gates. No engine name belongs in project-domain concepts.
- **Providers and cost:** ordinary local edits never require Ingots. A future media-edit provider is a separate semantic capability family from video generation and retains explicit human authorization for billable execution.
- **Workspace presentation:** docking, floating windows, monitor/DPI recovery, viewer mode, and layout persistence belong to the App/platform presentation boundary. They project one shared application/editor state and remain outside Core, Application, and `.rfp` project truth.
- **Diagnostics:** local logs and timings remain platform-owned operational evidence. Diagnostic export is explicit and sanitized by construction; it does not introduce telemetry, project-media capture, provider payload capture, or another persistence authority.
- **Portability:** shared behavior stays in portable Core/Application/Infrastructure. Windows presentation and platform integration remain replaceable host layers; no 1.0 feature may move Windows APIs into portable projects.

## Definition of Free Desktop Feature Complete

ReelForge Free desktop is Feature Complete when:

1. every existing implemented capability in the audited inventory remains supported;
2. every item in the 1.0 Free contract is implemented or the owner explicitly amends this contract with documented evidence;
3. the generate/import → exact reference → edit/finish → preview/export → edit-to-generation/replace loop works in one project without another editor for routine output;
4. project state, history, media identity, and durable outputs survive save/reopen, cache deletion, cancellation, expected failure, and approved recovery scenarios;
5. preview and export share creative semantics and meet the approved Gate 0 capability, supported-media, and performance matrix;
6. Free remains useful offline for local project/edit/export work and supports BYOK without Pro or Ingots;
7. no account, billing, entitlement, installer, store, updater, managed compute, or macOS requirement is hidden inside desktop feature completion;
8. automated and manual acceptance pass without automated paid-provider calls; and
9. remaining Pro, horizon, and rejected items are not required to call the Free desktop feature set complete.

Free Desktop Feature Complete is not External Beta Ready or Production Release Ready.

External Beta Ready additionally requires an explicit diagnostic bundle, acceptance checklist, feedback template, completed friends/family readiness pass, and a reproducible beta package or preliminary installer from a separate beta release-engineering slice. It does not require silent telemetry, a final signed production installer, production CI/CD, the final production-runtime audit, marketplace packaging, Pro, or Ingots. The beta package either configures an approved external media tool without bundling it or ships only a beta runtime that has passed a specific redistribution/license review; an arbitrary developer FFmpeg build is never included.

Full 1.0.0 is targeted to combine the already-validated Free editor with separately delivered Ingots and an approved first Pro continuity/repair subset. Production Release Ready additionally requires the exact bundled media runtime and license audit, signing, production packaging/installers, CI/CD release pipeline, updates/distribution, support policy, and legal review before public commerce. Those outcomes remain outside Slices A-J.

Gate 0 does not itself certify an H.264 encoder or public FFmpeg distribution. It proves desktop behavior against an approved distribution-realistic LGPL-compatible development profile; release engineering chooses and verifies the exact binaries ReelForge may ship.

## Evidence and approval gates remaining

The owner's eight product-direction questions are resolved. These narrower decisions require research or measured evidence rather than another statement of principle:

1. **Gate 0 media contract:** approve the exact pinned LGPL-compatible development/CI capability matrix after candidate formats, encoders, filters, fonts, repair engines, patent issues, and performance behavior are demonstrated. The exact bundled runtime still waits for release engineering.
2. **Measured performance floor:** benchmark representative sources and 1, 2, 4, and 8-track stress cases on the reference machine, then set interaction, preview, export, memory/disk, concurrency, and practical track-count thresholds. Later hardware expands this into a customer minimum.
3. **First Pro implementation subset:** approve the actual engine/license/quality route and final launch subset for Match to Previous Clip, stabilization, deflicker, denoise, sharpen, and format/loudness matching. A blocked operation is re-scoped explicitly rather than implemented through an incompatible bundled dependency.
4. **Release naming gate:** confirm whether both the first Pro subset and Ingots are hard requirements for the `1.0.0` label after Free beta, or whether either may follow without renaming the already-complete Free product.
5. **Beta package mechanism:** the release-engineering roadmap must choose the reproducible friends/family and creator-test package/preliminary installer. This choice does not pull the final signing, update, store, or production-distribution system into desktop feature slices.

## Research basis

Repository evidence: [README](../README.md), [milestone history](milestones.md), [architecture](architecture.md), [editor capability direction](editor-capability-direction.md), [business and packaging direction](business-and-packaging.md), [provider research](provider-research.md), and [manual acceptance](manual-acceptance.md).

Current official product evidence was used only to identify expectations:

- [CapCut Desktop](https://www.capcut.com/tools/desktop-video-editor)
- [CapCut format conversion](https://www.capcut.com/resource/how-to-change-video-format)
- [DaVinci Resolve Edit](https://www.blackmagicdesign.com/products/davinciresolve/edit)
- [DaVinci Resolve 19 supported codec list](https://documents.blackmagicdesign.com/SupportNotes/DaVinci_Resolve_19_Supported_Codec_List.pdf)
- [Adobe Premiere features](https://www.adobe.com/products/premiere/features.html)
- [Adobe Premiere desktop help](https://helpx.adobe.com/ca/premiere/desktop/edit-projects/edit-video-using-text-based-editing/overview-of-text-based-editing.html)
- [Adobe Premiere export guide](https://helpx.adobe.com/premiere/desktop/render-and-export/export-files/export-video.html)
- [FFmpeg legal guidance](https://ffmpeg.org/legal.html)
- [FFmpeg license and external-library details](https://ffmpeg.org/doxygen/trunk/md_LICENSE.html)
- [FFmpeg codec documentation](https://ffmpeg.org/ffmpeg-codecs.html)
- [FFmpeg filter documentation](https://ffmpeg.org/ffmpeg-filters.html)

These products demonstrate that trim/ripple, tracks, transforms, audio placement, text/captions, format choice, and delivery controls are ordinary editor expectations. Common conversion/export should therefore remain Free; Pro can sell batch work, custom presets, professional/specialty delivery, and other depth. Professional compositing, multicam, deep color/audio, large effect catalogs, and AI engine breadth remain differentiators rather than ReelForge requirements. Every external capability and license must be re-verified at its implementation gate.
