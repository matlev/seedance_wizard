# ReelForge media runtime contract

Status: Gate 0 final development contract

This document defines the media semantics that Free desktop 1.0 implementation may build against. It is not approval to redistribute a particular FFmpeg build, a legal opinion, a public hardware minimum, or a promise that every file using a familiar extension is supported.

## Capability classes

- **Baseline:** required Free behavior may depend on this semantic capability. The current executable profile is a development candidate; release engineering must still select and audit the shipping runtime.
- **Optional Platform:** an operating-system implementation may accelerate or supply the behavior, but portable project meaning never depends on it.
- **Enhanced Local Runtime:** a user-configured compatible runtime may expose more formats or filters. ReelForge preflights and labels these capabilities; their absence never makes a baseline project unreadable.
- **Conditional:** technically viable, but shipping depends on an unresolved quality, redistribution, patent, playback, or legal gate.
- **Deferred/Blocked:** not part of the Free 1.0 baseline.

## Baseline semantics

| Capability family | Development route | Classification | Gate 0 conclusion |
| --- | --- | --- | --- |
| Paired runtime discovery | Explicit co-located `ffmpeg`/`ffprobe`; version, build flags, component inventory, and parser check | Baseline | Product-neutral observation and strict profile-validation seams exist in Application/Infrastructure. |
| Inspection and stream choice | `ffprobe` JSON plus explicit FFmpeg stream maps | Baseline | Inspect all relevant streams. Choose default disposition, then lowest index. Video and audio resolve independently; attached pictures are not ordinary timeline video. Persist the resolved descriptor against source identity and never silently reselect. |
| Exact frame and ranges | native decoders, explicit maps, `select`/`trim`/`atrim`, `setpts`/`asetpts` | Baseline | Exact frame extraction, Saved Frames, Saved Clips, trim, audio extraction, and normalized concat have representative direct passes. VFR sources are accepted only where inspected timestamps remain representable; the failed synthetic terminal-duration fixture is not a general VFR guarantee. |
| Layer composition | `scale`, `crop`, `overlay`, `format`, `rotate`, alpha-capable formats, `amix` | Baseline | Multiple simultaneous visual layers, position/scale/crop/rotation/opacity, split-screen/PiP, source-audio mute/include, and multiple audio-source mixing are valid implementation targets. |
| Basic visual finishing | `colorlevels` for brightness/contrast; `hue` for saturation; `xfade`; `fade`; `lut3d`/`colorspace` when preflighted | Baseline | Brightness, contrast, saturation, fades, and a restrained transition set are supported semantics. The product model must not persist FFmpeg filter names. `eq` is not a baseline route. |
| Audio finishing | `volume`, `pan`, `afade`, `acrossfade`, `amix`, `loudnorm`, resampling/layout normalization | Baseline | Gain, pan, fades, crossfade, deterministic mixing, analysis, and two-pass loudness matching are feasible. Audio uses an audio-appropriate exact time domain. |
| Text and captions | libass, FreeType, Fontconfig, FriBidi, HarfBuzz, pinned OFL Noto faces | Baseline | Latin/diacritics, Simplified Chinese, Arabic shaping, explicit fallback, wrapping, titles, and captions passed. System-font fallback is not a reproducible baseline. Color emoji is deferred. |
| Analysis and preview | `showwaves`/`waveform`, scale/fps/audio normalization, VP9/Opus draft output | Baseline | Waveforms and reduced-resolution draft/proxy generation are supported. These are reconstructable artifacts, never project truth. |
| Open video delivery | VP9 Profile 0 + Opus in WebM; video-only variant | Baseline | Direct encode, inspect, strict decode, timing, content checks, and Chromium playback passed. This is the guaranteed open delivery alternative. |
| Ordinary compatibility delivery | H.264 + AAC-LC in MP4; video-only variant | Conditional Baseline target | OpenH264/native AAC and optional Windows Media Foundation routes produced valid representative outputs. Final encoder, rate control, patent posture, independent playback, and shipping closure remain release-engineering/legal gates. No silent `libx264` fallback. |
| Audio/image delivery | WAV PCM, FLAC, Ogg Opus, PNG, JPEG | Baseline | Representative direct outputs passed inspection and decode. M4A AAC-LC and MP3 are Conditional because software success does not settle patent/territory/distribution questions. Ogg Vorbis is a supported open input and Enhanced Local output candidate, not a required first export profile. |

## Input contract

Support is determined from content inspection and active-runtime capability, never filename extension alone. Missing capabilities fail before processing with a profile-aware explanation.

### Guaranteed-common envelopes

- **MP4:** H.264 Constrained Baseline/Main/High, 8-bit `yuv420p`, progressive CFR at 23.976, 25, 29.97, or 60 fps, through 1920×1080; optional AAC-LC mono/stereo at 44.1 or 48 kHz. The 720p Baseline and 1080p Main/High rows are the tested anchors.
- **MOV:** H.264 High 8-bit `yuv420p` CFR around 29.97 fps through 1080p; video-only, AAC-LC, or PCM mono/stereo at the tested 32/44.1/48 kHz rates.
- **WebM:** VP9 Profile 0 with Opus mono/stereo 48 kHz, or VP8 with Vorbis mono/stereo 44.1/48 kHz; 8-bit `yuv420p`, progressive CFR at the common tested rates through 1080p. Video-only VP8/VP9 is included.
- **Matroska bounded subset:** CFR VP9/Opus, H.264 video-only, H.264/PCM for the directly passed mono 32/44.1 kHz and stereo 48 kHz cases, and VP8 video-only. Other Matroska combinations are capability-qualified.
- **Audio:** WAV PCM and FLAC mono/stereo at 32/44.1/48 kHz; MP3 and AAC-LC mono/stereo at 44.1/48 kHz; Ogg Opus mono/stereo at 48 kHz; Ogg Vorbis mono/stereo at 44.1/48 kHz.
- **Images:** ordinary 8-bit PNG and JPEG, including the tested progressive 4:2:0 and EXIF-orientation cases.

These are bounded family guarantees, not arbitrary-container guarantees. Production import implementation must add small fixtures for any narrower level, dimension, frame-rate, channel-layout, or boundary it advertises.

### Capability-qualified inputs

HEVC/H.265, HEIC/HEIF, AV1 delivery, HDR, greater-than-8-bit video, uncommon chroma, unusual profiles/levels, multichannel audio, arbitrary Matroska combinations, color emoji, protected media, and formats supplied only by optional hardware/platform/local runtimes are capability-qualified. They remain Free when the active runtime can handle them safely, but they cannot alter portable project meaning or baseline promises.

## Output profiles

Free exposes ordinary useful output breadth rather than using formats as an artificial Pro gate:

- WebM VP9/Opus and WebM VP9 video-only: Baseline.
- MP4 H.264/AAC-LC and MP4 H.264 video-only: Conditional until the final shipping route passes release/legal/playback gates.
- WAV PCM, FLAC, Ogg Opus, PNG, and JPEG: Baseline.
- M4A AAC-LC and MP3: Conditional until release/legal review.
- Audio-only extraction and explicit include-audio/omit-audio choices are ordinary Free behavior.
- Batch queues, custom profile builders, and specialty/professional delivery controls may be Pro, but ordinary re-encode/export remains Free.

## Resource and execution policy

- Default to one heavyweight media job. Admit more concurrency only after production-path measurements show useful throughput with UI, memory, cleanup, and integrity headroom.
- Use explicit bounded FFmpeg thread counts. Existing 720p evidence did not show a meaningful VP9 gain from eight threads over one for the tested workload; do not assume maximum threads are better.
- Run media work off the UI thread at lower/background priority where supported. Rendering, export, proxy, waveform, analysis, and cancellation must not block the editor dispatcher.
- A cancel command should acknowledge immediately; attempt graceful shutdown, then use bounded process-tree termination when required. Never promote a partial durable output.
- Prefer reduced-quality previews or disposable proxies for expensive compositions and high-resolution sources. Full-resolution real-time 4K is not a 1.0 guarantee on the reference Ryzen 7 3700X / 32 GB / RTX 3070 Ti system.
- Optimize primarily for 720p/1080p projects of roughly 30 seconds to 10 minutes. Longer projects must remain openable and operable, but final long-form and customer-floor qualification occurs on ReelForge's production render path.

## Pro feasibility dispositions

| Candidate | Likely route | Disposition | Main constraint |
| --- | --- | --- | --- |
| Match to Previous Clip | Analyze reference/current boundaries, then apply bounded color/format/audio transforms | Candidate | Needs a product-owned comparison model and clear confidence/manual override; no single filter defines the feature. |
| Stabilization | Native FFmpeg `deshake` or another permissive/LGPL engine; analysis plus apply | Conditional | Quality/crop behavior and performance need product-path evaluation. GPL `libvidstab` is excluded from baseline. |
| Deflicker | Native `deflicker`; analysis plus apply | Candidate | Temporal windowing, scene cuts, and preview cost need evaluation. |
| Denoise | Native `nlmeans`/`atadenoise` or another permissive engine | Conditional | CPU cost and detail loss; GPL-only filters are excluded. |
| Sharpen | Native `unsharp` with bounded presets | Candidate | Halo/noise amplification and preview agreement. |
| Format matching | `scale`, `crop`, `pad`, `fps`, `colorspace`, `aformat`, `aresample` | Candidate | Must distinguish objective format normalization from subjective visual matching. |
| Loudness matching | two-pass `loudnorm` analysis/apply | Candidate | Persist analysis identity and define target policy; do not confuse peak normalization with loudness. |

These are feasibility classifications only. They do not implement Pro, entitlements, or a shipping engine decision.

## Deferred and blocked

- Deep HDR grading/mastering, color emoji, arbitrary codec/container coverage, full-resolution real-time 4K, broad professional mastering, and optional AI/ML engines are deferred.
- GPL or nonfree dependencies are blocked from the redistributable baseline without explicit owner approval, product justification, distribution-architecture review, and qualified legal review.
- Enhanced user-configured GPL runtimes may expose optional local capabilities, clearly labeled and preflighted, without becoming project requirements.

Final performance qualification, the exact public runtime, macOS validation, signing, installers, updates, and legal review belong to production implementation or release engineering—not Gate 0.
