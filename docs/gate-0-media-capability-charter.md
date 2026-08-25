# Gate 0 media capability charter

Status: owner and Project Manager approved; G0.1/G0.2 complete; Checkpoint A approved with amendments; automated G0.3 P2 matrix complete at 13 passed; G0.4 bounded output proof complete at 11 portable plus two optional W1 passes; independent playback partial; exact common-input proposal awaiting owner decisions

Approved: 2026-08-24

## Purpose

Gate 0 answers one question before ReelForge commits its Free 1.0 editor to new rendering behavior:

> What media-runtime capability can ReelForge safely build against without discovering during release engineering that required codecs, filters, formats, or performance assumptions are unavailable?

Gate 0 is a bounded research-and-proof phase. A technically or legally questionable workaround is not success. `Blocked`, `Conditional`, and `Deferred` are valid evidence-based outcomes.

## Binding scope

Gate 0 may add:

- generated or clearly licensed fixtures;
- probe scripts and tests;
- semantic capability manifests and concrete runtime-profile mappings;
- CI checks that compare an observed runtime with a reviewed profile;
- the smallest reusable runtime capability reader and validator needed to prove and enforce the contract; and
- lean documentation and architecture decision records.

Gate 0 must not add:

- new editing operations, editing UI, or user-facing media features;
- speculative plugin or engine frameworks;
- broad production refactors;
- changes to existing production behavior without separate owner approval;
- a final packaged runtime, installer, signing, update, or distribution system;
- entitlement, account, billing, Ingot, pricing, or commerce behavior;
- macOS implementation or executable validation;
- silent telemetry; or
- live or paid provider calls.

The current development-machine FFmpeg installation is an audit input and possible enhanced-local-runtime candidate only. Its success is not evidence that ReelForge may distribute it or depend on its complete capability set.

## Portability boundary

Gate 0 defines a **platform-neutral media capability contract** whose mandatory capabilities do not depend on Windows-only acceleration. Gate 0 technically proves that contract on Windows using the owner's reference machine. macOS runtime implementation and executable verification remain future platform work.

Windows-specific acceleration may implement an optional profile. It cannot determine project meaning or be required for mandatory Free functionality. No FFmpeg executable path, library name, filter name, hardware device, WPF type, or Windows API becomes a Core domain concept.

## Runtime profiles under consideration

| Profile | Purpose | Contract status |
| --- | --- | --- |
| Distribution-realistic baseline | Platform-neutral mandatory Free capabilities using an LGPL-compatible candidate configuration and dependencies assessed for redistribution concerns. | The only profile on which mandatory Free behavior may depend. Proven on Windows during Gate 0. |
| Optional platform acceleration | Windows hardware/media paths that may improve performance when present. | Optional implementation of semantic capabilities; never project truth or the only supported route. |
| Enhanced user-configured runtime | Explicitly selected local FFmpeg/ffprobe with additional discovered codecs, filters, or libraries. | Capability-qualified and clearly labeled; never silently treated as portable or required to open a baseline Free project. |

Candidate dependency review includes code licenses, transitive dependencies, binary redistribution, patents, maintenance state, platform availability, reproducible-build availability, model weights, bundled fonts, and test assets where applicable. Gate 0 records issues; it does not provide a legal opinion or certify a public binary.

Every concrete profile treats `ffmpeg` and `ffprobe` as a paired toolchain. Observed evidence records each executable's identity, version, configuration/build report, source/provenance, and hash; verifies that the pair is compatible; and identifies output/parser assumptions on which ReelForge depends. Independently configured paths are not presumed compatible merely because both executables start successfully.

## Semantic capability model

The machine-readable contract describes ReelForge needs independently of FFmpeg implementation names. Representative requirements include:

```text
Video.Decode.CommonInput
Video.Encode.DefaultDelivery
Video.Compose.Overlay
Audio.Mix.Stereo
Audio.Export.Lossless
Text.Render.Unicode
Caption.BurnIn
Preview.GenerateDraftProxy
```

Gate 0 maintains separate concepts:

```text
RequiredCapabilities
    semantic capabilities required by a ReelForge profile

ObservedRuntimeCapabilities
    capabilities and build evidence reported for one concrete runtime

RuntimeProfileMapping
    encoders, decoders, muxers, demuxers, filters, libraries, and
    build options proposed to implement the semantic requirements
```

Validation compares required semantics with observed runtime evidence. Project files preserve creative intent and typed settings; they do not persist an engine name, executable path, or FFmpeg-shaped capability list as creative meaning.

## Work packages

### G0.1 — current dependency audit

Inventory every current FFmpeg/ffprobe use and assumption, including:

- executable discovery, saved paths, PATH fallback, version fingerprinting, and process execution;
- independent `ffmpeg`/`ffprobe` path selection, version/configuration pairing, output parsing, and mismatch behavior;
- exact frame indexing and extraction;
- Saved Clip trim materialization;
- composition concat, normalization, audition, preview, bake, and export;
- audio extraction, detachment, normalization, mixing, and encoding;
- hard-coded codec, container, extension, pixel format, sample rate, channel layout, and filter choices;
- runtime/build capabilities currently assumed without validation;
- cache identity and invalidation inputs; and
- test coverage and fixture gaps.

Every existing workflow receives one initial classification:

- `Baseline-supported`;
- `Enhanced-local-runtime-only`;
- `Conditional`; or
- `Blocked`.

Gate 0 reports options and consequences. It does not silently remove, replace, or reinterpret an existing workflow.

### G0.2 — candidate profiles and dependency research

Propose candidate implementations for:

- common input decoding and inspection;
- default and alternative Free video delivery;
- standalone lossy and lossless audio export;
- transforms, scaling, crop, padding, overlays, alpha, and basic color;
- the small Free transition set;
- Unicode title and caption rendering, font fallback, and caption burn-in;
- waveform, stereo mix, loudness analysis, and draft proxy generation;
- thread/concurrency control; and
- optional Windows acceleration and enhanced-local-runtime behavior.

Each candidate records concrete build components, external dependencies, known license/patent/redistribution concerns, expected platform support, maintenance risk, and the evidence needed to prove it.

Each candidate also proposes a reproducible, provenance-recorded way for CI to obtain or build the paired `ffmpeg`/`ffprobe` proof toolchain, including pinned source or artifact identity, hashes, build/configuration evidence, and cache strategy. This is proof-toolchain acquisition, not selection or packaging of the final public runtime. If no acceptable acquisition path exists, CI execution of that profile is reported as blocked rather than silently using an arbitrary binary.

### Checkpoint A — owner profile selection

Gate 0 pauses after G0.1 and G0.2. Before executable proof expands, Codex reports:

- the current dependency inventory;
- every hard-coded codec, filter, container, and build assumption;
- one or two recommended baseline candidates plus rejected alternatives;
- candidate external dependencies and transitive concerns;
- the proposed fixture and proof matrix;
- the semantic capability-manifest design;
- the proposed paired-toolchain compatibility rules and reproducible CI acquisition/build procedure;
- expected blockers and roadmap consequences; and
- the exact scope of any proposed reusable capability reader/validator.

The owner approves which one or two profiles proceed to executable proof. No broad proof matrix, production behavior change, or candidate-profile commitment proceeds without that approval.

### G0.3 — executable capability proof

After Checkpoint A, small deterministic fixtures exercise the approved candidates for:

- common decoding and metadata inspection;
- exact frame extraction;
- trim, concat, timestamp continuity, and normalization;
- video and audio mixing;
- transform, crop, scale, alpha, overlays, and basic color;
- cross-dissolve and dip-to/from-black;
- titles, captions, Unicode, wrapping, and font fallback;
- waveform and proxy generation;
- selected-media and composition video conversion/export; and
- standalone audio conversion/export.

A process exit code is insufficient. Outputs are inspected with ffprobe, decoded again, and checked for expected duration, streams, frame/audio properties, timing, and metadata.

Shortlisted delivery formats also receive representative independent validation:

- Windows-native playback where applicable;
- browser playback for web-oriented formats;
- at least one independent common player;
- metadata inspection for color, audio, and timing; and
- visual confirmation or deterministic golden-frame comparison for titles, captions, Unicode, color, opacity/overlays, and transitions.

Gate 0 may research likely macOS compatibility, but it labels that evidence as researched rather than technically proven.

### G0.4 — Free media and delivery proposal

Propose the common Free contract for:

- input families;
- default video delivery;
- alternative broadly useful video conversion/export;
- standalone audio delivery, including a lossless option;
- image/frame export where relevant;
- container/codec combinations;
- quality and size controls; and
- unsupported-media diagnostics.

Free is not restricted to one video and one audio format merely to create Pro value. Pro candidates remain batch conversion, queues, reusable/custom profiles, professional or specialty delivery, and deeper workflow controls.

If no acceptable H.264 route exists, Gate 0 presents the product, compatibility, patent, packaging, and beta consequences rather than hiding the issue behind the current `libx264` command.

### G0.5 — performance and resource proof

Testing uses the owner's reference machine as the initial benchmark, not the public minimum:

```text
Ryzen 7 3700X
32 GB RAM
RTX 3070 Ti 8 GB
```

Workloads distinguish simultaneous video layers from audio tracks:

| Shape | Representative workload |
| --- | --- |
| Baseline | 1 video layer plus 1 audio track. |
| Typical | 2 simultaneous video layers plus 4 audio tracks. |
| Stress | 4 simultaneous video layers plus 8 audio tracks. |
| Long-form integrity | 1-2 mostly sequential video layers, multiple audio tracks, and a 60-120 minute project. |

The repeatable methodology records at least:

- render/export duration;
- preview startup delay;
- maximum observed UI-dispatch delay;
- cancellation-request-to-process-exit latency;
- peak CPU and memory;
- GPU use where applicable;
- disk throughput or latency;
- concurrent media-process count; and
- cache/proxy disk consumption.

Gate 0 compares candidate FFmpeg thread caps and concurrent-job limits, exercises 720p/1080p primary editing, and probes higher-resolution sources with draft-quality/proxy behavior. It proposes numeric acceptance thresholds for owner approval. Subjective smoothness remains useful manual evidence but cannot replace repeatable measurements.

### G0.6 — non-blocking Pro continuity/repair feasibility

Provide a preliminary disposition for:

- Match to Previous Clip;
- stabilization;
- deflicker;
- denoise;
- sharpen;
- format matching; and
- loudness matching.

Each candidate records a plausible engine/algorithm, architecture implications, license state, resource behavior, analysis/intermediate artifacts, expected quality/failure modes, and whether it can plausibly join the first Pro release.

Each disposition is `Proven`, `Conditional`, `Blocked`, or `Deferred`. Exhaustive implementation spikes and final quality validation are not required. Gate 0 may complete with any number of Pro candidates deferred, provided their known architecture and licensing implications are documented. Pro research never holds the Free baseline decision hostage.

### G0.7 — contract, CI, and decision packet

Consolidate the evidence into:

- the approved semantic Free capability contract;
- reviewed runtime-profile mappings;
- generated/licensed fixtures and reproducible proof instructions;
- the smallest approved reusable runtime capability discovery/validation implementation, if Checkpoint A authorizes it;
- CI checks for runtime/profile drift;
- the approved provenance and acquisition/build procedure for the paired CI proof toolchain, or an explicit blocked disposition;
- performance methodology and proposed thresholds;
- the Free delivery-format proposal;
- preliminary Pro dispositions;
- architecture and roadmap updates; and
- a concise owner decision packet with recommendations and consequences.

## CI boundary

CI may detect:

- runtime-version or binary/hash drift;
- incompatible or unexpectedly different `ffmpeg`/`ffprobe` versions, configurations, provenance, hashes, or parser-output assumptions;
- changed configuration flags;
- missing required components;
- newly present forbidden or unexpected flags/components;
- changed external dependencies; and
- changed license/build-report text.

CI obtains or builds its candidate `ffmpeg`/`ffprobe` pair only through the Checkpoint-A-approved, pinned, provenance-recorded procedure. It must not fall back to an arbitrary runner or developer-machine installation. If that procedure cannot be established within Gate 0, the corresponding profile check remains visibly blocked.

Its success statement is:

> Runtime matches the reviewed Gate 0 profile.

CI does not certify patent safety, jurisdictional legality, commercial redistribution rights, or adequacy of license compliance. Those remain release-engineering and qualified-legal-review responsibilities.

## Gate 0 exit acceptance

Gate 0 is complete when:

1. every current and planned Free media operation maps to a named semantic capability;
2. every mandatory baseline capability is proven against an owner-approved candidate on Windows or explicitly reported as blocked;
3. existing workflows have explicit baseline/enhanced/conditional/blocked dispositions and none was silently removed;
4. observed runtime evidence is separate from required semantic capabilities and concrete profile mappings;
5. common Free video/audio candidates have been produced, inspected, independently played where applicable, and visually validated;
6. missing capabilities are detectable before work starts and have actionable failure behavior;
7. Windows acceleration is optional and no Windows implementation detail enters portable media or project contracts;
8. performance evidence uses named workload shapes, repeatable metrics, and proposed numeric thresholds;
9. every first-Pro repair candidate has a preliminary, non-blocking disposition;
10. the paired `ffmpeg`/`ffprobe` identity, compatibility, parser assumptions, provenance, and CI acquisition/build procedure are approved and reproducible, or the affected profile is explicitly blocked;
11. CI detects deviation from the reviewed profile without claiming legal approval;
12. automated work remains physically incapable of billable provider submission;
13. public-runtime packaging, signing, distribution, final audit, and legal conclusions remain outside Gate 0; and
14. the owner approves the exit decisions or explicitly amends the roadmap before dependent implementation begins.

A blocked mandatory Free capability is a valid Gate 0 outcome. It requires an explicit owner decision among changing the candidate runtime, changing the technical approach, narrowing the feature contract, or delaying the affected feature. Gate 0 must not force a weak, non-portable, or legally questionable implementation merely to preserve the proposed roadmap.

## Owner decisions

The owner has approved the Gate 0 scope assumptions and clarifications in this charter. Two decision points remain part of execution.

### Checkpoint A

The owner approved `P2.BtbnLgplShared.WindowsX64.20260820` as third-party LGPLv3-path proof infrastructure for the full Free proof matrix, authorized the scoped W1 probe and minimal paired-runtime observation/validation seam, approved fixtures F1-F6 plus exact-timing and explicit multi-stream-selection fixtures, and kept P1 blocked. P2 is not a selected shipping runtime, public-distribution approval, final default-delivery contract, or approval to use every compiled component.

Current G0.3 results are recorded in [Gate 0 G0.3 executable-proof results](gate-0-g0.3-executable-proof.md). All 13 automated P2 semantic capabilities passed, including the owner-approved basic-color and Unicode text mappings. The [independent-playback checkpoint](gate-0-independent-playback-checkpoint.md) now retains partial executable evidence, while playback completion and long-form integrity remain G0.3/G0.5 completion gates. G0.4 may not finalize the default Free delivery contract before the remaining playback rows are completed or explicitly dispositioned. No fixture result is treated as a capability verdict.

The [G0.4 Free media and delivery proposal](gate-0-g0.4-delivery-proposal.md) recommends H.264/AAC MP4 as the compatibility/default target if an acceptable route passes and retains proven VP9/Opus WebM as the open alternative. The owner approved its four decisions in the [G0.4 owner-decision record](gate-0-g0.4-owner-decisions.md), authorizing the bounded component proofs while retaining independent playback and all shipping/legal boundaries.

The [G0.4 executable delivery-proof results](gate-0-g0.4-executable-proof.md) record eleven passed portable output routes and two passed optional W1 variants. The [independent-playback checkpoint](gate-0-independent-playback-checkpoint.md) adds two native-Chromium WebM control passes, explicit MP4 long-corpus blocks, and optional WMP Legacy WebM blocks without finalizing the default or shipping route. Remaining playback rows, the common-input decode matrix, durable P2 retention, G0.5 quality/performance/long-form evidence, and release-engineering/legal work stay open. OpenH264's no-frame-skip bitrate warning remains an explicit G0.5 quality-policy finding.

The [G0.4 common-input proof proposal](gate-0-g0.4-input-proof-proposal.md) now bounds candidate guarantees by exact container/codec/profile/timing cases, negative diagnostics, and deterministic video/audio stream-selection policy. It requests owner decisions on the exact row dispositions, VP8/Vorbis fixture components, a narrow NVENC-only H.264 Main/High fixture producer, and the future selection/persistence policy. No input row or additional component is promoted before those decisions and executable proof.

Authoritative decision and amendments: [Gate 0 Checkpoint A](gate-0-checkpoint-a.md).

### Gate 0 exit

The owner approves or amends:

1. the development/CI baseline profile;
2. the paired `ffmpeg`/`ffprobe` proof-toolchain identity, compatibility rules, provenance, and CI acquisition/build procedure;
3. the common Free input, video, and audio format matrix;
4. whether an acceptable H.264 route is mandatory for external beta;
5. the support status and product labeling of enhanced local-tool capabilities;
6. the text/font/caption rendering route;
7. the initial FFmpeg thread and media-job concurrency policy;
8. the proposed performance thresholds and any claimed 4K behavior;
9. the repair operations still targeted for the first Pro release;
10. any roadmap change caused by a blocked baseline capability;
11. whether beta uses user-configured tools only or may contain a separately reviewed beta runtime; and
12. which unresolved licensing or patent questions require qualified review before beta versus before public commerce.

## What Gate 0 unlocks

An approved Gate 0 contract directly de-risks the render/playback/conversion/export graph, visual finishing, audio finishing, titles/captions, automatic draft preview/proxies, and the first Pro continuity/repair program.

Recovery/relink, track/time persistence, and structural editing do not need exhaustive Pro feasibility results. They may proceed according to their own dependencies once the roadmap is approved, but they must not embed media-runtime assumptions that Gate 0 has not settled.
