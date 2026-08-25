# Gate 0 G0.4 Free media and delivery proposal

Status: [owner decisions approved](gate-0-g0.4-owner-decisions.md); [bounded delivery-format proof complete](gate-0-g0.4-executable-proof.md); no default delivery contract finalized

Date: 2026-08-24

Authority: [Gate 0 media capability charter](gate-0-media-capability-charter.md)

## Outcome

ReelForge should target a two-profile Free video contract:

1. **MP4 with H.264/AVC video and AAC-LC audio** as the ordinary compatibility/default candidate, conditional on an approved encoder, licensing, and playback route; and
2. **WebM with VP9 video and Opus audio** as the guaranteed open-delivery candidate already proven under P2.

WebM alone is not recommended as the general default. Modern browser support is strong, but native-player and older-environment behavior is less uniform than H.264/AAC MP4. Conversely, H.264/AAC must not be declared available merely because the current application requests `libx264`, because P2 deliberately excludes `libx264` and Gate 0 has not approved a portable H.264 shipping route.

This document compares and recommends candidates. It does not finalize the Free default, select a shipping runtime, approve public redistribution, make a patent conclusion, or satisfy independent playback. Those boundaries remain explicit.

## Current product audit

| Area | Current behavior | G0.4 consequence |
| --- | --- | --- |
| Import | Extensions classify image, video, and audio; video/audio are inspected when ffprobe is available. | An accepted extension is not evidence that the selected runtime can decode the content. Import needs probe/decode capability preflight and structured diagnostics. |
| Video materialization | Saved Clip, composition bake, and normalized video rendering use hard-coded MP4/`libx264`/`yuv420p` assumptions. The audio-overlay path stream-copies video and encodes AAC. | These commands are legacy implementation facts, not the approved contract. Video re-encode and audio-only mutation paths have different dependencies and must move behind delivery/runtime-profile mappings before Feature Complete. |
| Video export | Working Composition and virtual-video export are MP4-only. Physical selected-media re-encode is absent. | Free needs named selected-media and composition profiles covering both approved ordinary delivery families. |
| Audio | Extraction/mix output is fixed M4A/AAC; general selected-audio/range re-encode is absent. | Free needs ordinary compressed, open, and lossless audio profiles rather than a single hidden command. |
| Images | Saved Frame export is PNG-only; general image conversion is absent. | PNG and JPEG cover the minimum useful lossless and compact-photo/frame workflows. |
| Quality and size | No public resolution, cadence, quality, or audio-behavior profile exists. | Free needs understandable named controls. Raw encoder tuning, custom profiles, and queues can remain later Pro depth. |
| Diagnostics | Unsupported extensions may be skipped and missing ffprobe is explained, but there is no content/runtime/export preflight matrix. | “FFmpeg supports it” and extension-only claims must be replaced by capability-qualified results and actionable errors. |

The existing materialization, cancellation, atomic-output, caching, and metadata foundations are reusable. The missing work is a stable product/profile contract and engine-neutral planning seam, not a second rendering subsystem.

## Evidence available now

### Proven under P2

- VP9/Opus WebM draft proxy, selected-media delivery, and two-segment composition delivery;
- paired inspection and explicit VP9/Opus decode-again;
- frame identity/order, geometry, duration, and audio structure within declared tolerances;
- standalone FLAC with byte-exact PCM reconstruction; and
- standalone Ogg Opus with declared duration/sample-padding and frequency-preservation evidence;
- paired and video-only H.264/AAC MP4 through `libopenh264` plus native AAC, including AAC-LC, `faststart`, ordered frame identity, paired stereo-frequency semantics, and explicit decode-again;
- paired and video-only VP9/Opus WebM with bounded Segment-level Cues evidence;
- standalone M4A AAC-LC, MP3, Ogg Opus, WAV PCM, and FLAC with explicit demuxer/decoder and two-channel timing/frequency/phase or lossless oracles; and
- PNG exact-pixel and JPEG tolerance-based image delivery.

The complete eleven-route result and its retained limitations are recorded in [Gate 0 G0.4 executable delivery-proof results](gate-0-g0.4-executable-proof.md). Executable success does not remove the conditional status of H.264/AAC or MP3.

### Narrow optional Windows evidence

The original W1 probe created and decoded H.264/AAC MP4 through the exact P2 build's `h264_mf` and `aac_mf` wrappers. The approved G0.4 comparison instead passed paired and video-only MP4 through `h264_mf` plus the same native AAC route used by P2. Hardware encoding was not forced, but the concrete Media Foundation implementation remained unobservable. These results establish only working wrapper paths on the tested environment. They do not establish portability, exact hardware-versus-software behavior, final quality/rate control, independent playback, packaging, patents, or redistribution.

### Presence only, not semantic proof

WebP, AV1 encoders, and other broad-P2 components not named in the G0.4 contract remain presence-only. A listing does not authorize execution or establish ReelForge semantics. Checkpoint A still requires a dependency disposition before an additional P2 component enters proof commands.

## Candidate video delivery matrix

| Candidate | Interoperability | G0.3 evidence | Dependency/licensing position | G0.4 disposition |
| --- | --- | --- | --- | --- |
| MP4 H.264 + AAC-LC | Strongest ordinary browser, native-player, social, and upload compatibility. | Optional W1 Windows wrapper evidence only. | H.264/AAC software licenses do not settle codec patents. `libx264` is outside the LGPL-only P2 path. OpenH264 and OS encoders each need a narrow review. | **Recommended conditional Free default and external-beta requirement.** Not finalized until route proof and independent playback. |
| WebM VP9 + Opus | Strong current-browser compatibility; native-player behavior is less uniform. | Passed selected-media, composition, proxy, inspection, and decode-again proof. | `libvpx` and Opus have permissive/open licensing and patent grants with conditions; exact notices and legal review still apply. | **Recommended guaranteed Free open alternative.** |
| WebM AV1 + Opus | Increasing modern-browser support; hardware and native-player behavior vary. | Components observed only; no approved semantic proof. | Open ecosystem is promising, but implementation choice, notices, patent-license terms, and performance need review. | **Post-1.0 or capability-qualified local-tool candidate.** Not part of the minimum contract. |
| MP4 AV1 + AAC or Opus | Less consistently interoperable than the two candidates above. | Not proven. | Adds container/player variation without solving the immediate default-route problem. | **Reject as a 1.0 default.** |
| MP4/MOV HEVC | Common in some capture ecosystems but inconsistent without OS codecs and encumbered by unresolved patent/distribution questions. | Decode components observed only. | No Gate 0 clearance. | **Capability-qualified input only; no 1.0 Free delivery promise.** |
| MOV ProRes, DNxHR/MXF, image-sequence masters | Professional interchange value, high storage/implementation/testing cost. | Not proven or required for the minimum AI finishing workflow. | Exact codec and container dependencies vary. | **Post-1.0 Pro/horizon**, based on demand; not withheld from Free merely to create scarcity if later judged ordinary. |

Official compatibility references support H.264/AAC MP4 as the lowest-friction candidate: [Microsoft Media Foundation formats](https://learn.microsoft.com/en-us/windows/win32/medfound/supported-media-formats-in-media-foundation), [Apple Safari media formats](https://developer.apple.com/library/archive/documentation/AppleApplications/Reference/SafariWebContent/Introduction/Introduction.html), and [Mozilla's media-format guidance](https://developer.mozilla.org/en-US/docs/Web/Media/Guides/Formats/Video_codecs). WebM's intended VP8/VP9 plus Vorbis/Opus combinations are documented by the [WebM project](https://www.webmproject.org/about/faq/).

## Candidate audio delivery matrix

All ordinary formats below are proposed as Free. A format is not made Pro merely because it adds another local FFmpeg command.

| Candidate | User purpose | Evidence/status | Proposed disposition |
| --- | --- | --- | --- |
| M4A AAC-LC | Default compact audio and Apple/mobile compatibility. | Native AAC-LC passed the bounded P2 encode/inspect/decode/timing/stereo-semantic proof. Independent playback, shipping-runtime selection, and patent review remain open. | **Conditional Free default.** |
| MP3 | Broad legacy/device/publishing interchange. | `libmp3lame` plus the native MP3 decoder passed the bounded P2 timing/stereo-semantic proof. Independent playback, shipping-runtime selection, and patent review remain open. | **Conditional Free compatibility export.** |
| Ogg Opus | Open, efficient compressed audio. | Passed P2 semantic proof. | **Guaranteed Free open alternative.** |
| WAV PCM | Widely accepted uncompressed/lossless interchange; large files. | Native PCM/WAV passed bounded byte-exact decoded-PCM proof with ordinary RIFF (`rf64=never`). Independent playback and the greater-than-4-GB/RF64 policy remain open. | **Required Free lossless/interchange option.** |
| FLAC | Compact lossless archival/interchange. | Passed byte-exact P2 proof; native-player support is not universal. | **Free lossless option with compatibility labeling.** |

The minimum Free contract remains complete even if M4A or MP3 is temporarily blocked: Ogg Opus plus WAV/FLAC provides open compressed and lossless workflows. However, external-beta usability would be materially better if M4A and MP3 pass the approved dependency and playback gates.

## Image and frame delivery

The 1.0 Free minimum should guarantee:

- **PNG** for lossless frames, graphics, alpha, Saved Frames, and exact proof artifacts; and
- **JPEG** for compact photographic/video-frame delivery with an understandable quality setting.

WebP remains a capability-qualified import and later ordinary-export candidate. It is not needed to make the smallest 1.0 workflow complete. GIF animation, image sequences, HDR/AVIF/HEIF delivery, and professional alpha/master formats are post-1.0 unless a concrete AI workflow dependency emerges.

## Proposed input contract

“Supported input” means the active approved runtime profile can inspect the exact file, select the intended stream, decode the required content, and report usable timing. Extension recognition alone is insufficient.

### Proposed guaranteed common families

- video: MP4/MOV with H.264 plus AAC/PCM where present; WebM with VP8/VP9 plus Opus/Vorbis; Matroska with the same approved baseline codecs;
- audio: WAV PCM, FLAC, MP3, M4A/AAC, and Ogg Opus/Vorbis; and
- image: PNG and JPEG.

The final exact container/codec/profile/pixel-format rows require bounded decode fixtures. Proof commands must map distinguishable streams explicitly. The 1.0 product minimum may apply a documented deterministic default-stream policy and report ignored alternatives; per-asset stream-selection UI and persisted stream overrides remain post-1.0 unless a target workflow demonstrates that they are required.

### Capability-qualified, not guaranteed

- HEVC/H.265, AV1, AVI, WMV/WMA, M4V variants, TIFF, BMP, GIF, HEIC/HEIF, and unusual Matroska combinations;
- hardware- or OS-codec-dependent profiles;
- greater-than-8-bit, HDR, uncommon chroma/pixel formats, multichannel layouts, and encrypted/protected media; and
- any format exposed only by a user-configured local tool profile.

Capability-qualified media may be useful and Free when available. It must never change project meaning, make a common project platform-specific, or be described as guaranteed merely because a component is listed.

## Free profile and control contract

Free should expose named, semantic controls rather than raw encoder flags:

- target family/profile;
- source, 720p, 1080p, and capability-qualified 4K output sizing;
- preserve-source or ordinary target cadence choices;
- Draft, Balanced, and High Quality intent;
- include, omit, or audio-only behavior;
- stereo/mono and ordinary sample-rate normalization; and
- estimated consequence language such as faster/smaller versus slower/larger.

G0.5 should set measured defaults and thread/concurrency limits. Encoder-specific CRF, QP, GOP, tile, pass-count, and advanced rate-control settings are runtime-profile mappings, not project-domain meaning. Custom profiles, reusable presets, batch conversion, queues, and professional delivery controls remain credible Pro productivity/depth candidates.

## Unsupported-media diagnostics contract

Before import-dependent work or export begins, ReelForge must distinguish:

1. unrecognized extension or media family;
2. corrupt/truncated/uninspectable input;
3. recognized container with no usable media stream;
4. missing decoder, encoder, muxer, filter, or OS codec in the active runtime profile;
5. unsupported codec profile, pixel format, bit depth, channel layout, or protected/encrypted content;
6. multiple usable streams whose deterministic default selection and ignored-stream diagnostics must be recorded;
7. a profile available only through configured local tooling; and
8. an output destination, permission, disk-space, cancellation, or partial-file failure.

Errors should name the failed capability and active runtime profile, preserve the source, suggest a supported conversion or configuration when one exists, and remain eligible for the sanitized Export Diagnostic Bundle. Credentials, signed URLs, prompts, and media remain excluded by default.

## Architecture and sequencing implications

- Application owns engine-neutral delivery intent: media family, named profile, canvas/resolution, cadence, quality intent, and audio behavior.
- Infrastructure maps that intent to an approved runtime profile and records the exact selected components. FFmpeg names, Media Foundation details, executable paths, and platform encoders remain outside Core and persistence meaning.
- Cache identity includes the semantic profile/version, normalized parameters, source/composition identity, and concrete runtime mapping identity. A cached WebM result cannot satisfy an MP4 request or survive silent encoder drift.
- Production renderers must stop assuming `.mp4`, `.m4a`, `libx264`, or AAC before profile implementation expands. This is future Slice D implementation work, not authorized by G0.4 analysis itself.
- Platform adapters may provide equivalent H.264/AAC capability through different implementations, but no Windows-only route may define portable project meaning.
- Beta/runtime packaging, notices, exact binary audit, signing, and distribution remain release engineering. G0.4 defines the development capability target only.

Recommended sequence:

1. owner approves or amends the conditional Free matrix and whether H.264/AAC MP4 is mandatory for external beta;
2. authorize bounded proof of additional components rather than using P2 presence as proof;
3. compare a cross-platform OpenH264/native-AAC path with the optional Windows Media Foundation path, including quality/rate control and exact dependencies;
4. prove paired A/V, video-only/omit-audio, and audio-only variants where the approved Free controls require them;
5. prove M4A AAC, MP3, WAV PCM, PNG, JPEG, and common decode-input rows with explicit components and fixtures;
6. produce candidate artifacts for the retained independent-playback matrix, including MP4 `faststart` and WebM cue/seek behavior;
7. use G0.5 to set quality, performance, resource, thread, and concurrency defaults; and
8. implement the approved engine-neutral profile seam in the desktop 1.0 feature slice without selecting or packaging the public runtime.

## Owner decisions

The owner approved all four requested decisions in the [G0.4 owner-decision record](gate-0-g0.4-owner-decisions.md): H.264/AAC MP4 as the conditional external-beta compatibility/default target; VP9/Opus WebM as the open alternative; the proposed ordinary Free output breadth; the guaranteed-common versus capability-qualified input model; and the next bounded component proofs with paired and video-only delivery variants.

Independent playback remains a prerequisite to finalizing the default. A blocked H.264 or AAC route is a valid result and must return to the owner rather than weakening portability, reproducibility, quality, or licensing boundaries.

## Licensing and source boundary

FFmpeg's [legal guidance](https://ffmpeg.org/legal.html) and [component license list](https://ffmpeg.org/doxygen/trunk/md_LICENSE.html) distinguish the base LGPL path from GPL/nonfree components and warn separately about codec patents. OpenH264 source is BSD-licensed, while Cisco's distributed binary has additional [binary-license conditions](https://www.openh264.org/BINARY_LICENSE.txt). The [Opus license](https://github.com/xiph/opus/blob/main/COPYING), [libvpx license](https://chromium.googlesource.com/webm/libvpx/+/refs/heads/main/LICENSE), and [WebM patent grant](https://www.webmproject.org/license/bitstream/) make the open path promising but do not replace the project's exact dependency audit or qualified legal review.

No software-license observation in Gate 0 is a patent, territory, redistribution, or public-shipping conclusion.
