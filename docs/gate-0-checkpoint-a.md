# Gate 0 Checkpoint A decision packet

Status: owner decision required; G0.1 and G0.2 complete; G0.3-G0.7 paused

Prepared: 2026-08-24

Authority: [Gate 0 media capability charter](gate-0-media-capability-charter.md)

## Decision requested

The audit confirms that ReelForge cannot treat the current developer FFmpeg installation or current `libx264` commands as its Free baseline. The recommended next step is to prove one immutable LGPL-path candidate after owner approval:

1. **P2 — Practical LGPL Free Candidate**, with the full Free proof matrix using open delivery and text dependencies. Its exact third-party archive, source and build identities, complete runtime-file hashes, observed configuration, pair rule, and CI acquisition procedure are recorded in the [P2 proof-toolchain candidate](gate-0-p2-proof-candidate.md).

P1 remains useful as a future narrow control, but it is **blocked from executable proof** because an immutable Windows build environment, exact dependency closure, and resulting paired-binary manifest have not yet been established. Gate 0 does not substitute a loosely pinned MSYS2 environment or arbitrary local build.

The recommended Free delivery candidate for P2 is **WebM with VP9/Opus**, alongside WAV/PCM, FLAC, and FFV1/FLAC in Matroska for lossless proof/intermediate roles. P2 is a deliberately broad proof build, not the final bundled runtime: proof commands may use only owner-approved mapped components. Native AAC/M4A remains a conditional compatibility candidate because software availability does not settle patent or public-distribution policy. No acceptable platform-neutral H.264 encoder is selected at Checkpoint A.

A separate, limited **W1 Windows compatibility probe** is recommended for Media Foundation H.264/AAC in MP4. W1 is optional acceleration/delivery evidence, not the platform-neutral baseline and not a redistribution or patent conclusion.

The owner is asked to approve, amend, or reject:

| Decision | Recommendation |
| --- | --- |
| Profiles entering executable proof | Approve the exact P2 candidate for the full Free proof matrix. Keep P1 blocked until a separately reviewable, reproducible build manifest exists. |
| P2 proof-toolchain acquisition | Approve the immutable BtbN 2026-08-20 Windows x64 LGPL-shared asset and validation procedure in the [candidate manifest](gate-0-p2-proof-candidate.md) as third-party proof input only. If third-party proof input is unacceptable, P2 is blocked until a controlled source build is specified and approved. |
| P1 proof-toolchain acquisition | Accept the blocked disposition for now, or direct Gate 0 to spend a separate research work unit producing an exact controlled-build manifest before G0.3. This decision does not authorize P1 proof without another owner review. |
| Optional Windows compatibility evidence | Authorize a narrow W1 H.264/AAC Media Foundation probe on the reference system after P2 identity validation. |
| Minimal reusable infrastructure | Approve the proposed observation/validation seam below for audit and CI reporting only. Do not migrate renderers or change user behavior in Gate 0. |
| Fixture/proof matrix | Approve the proposed generated/licensed fixtures and later G0.3 proof coverage below. |

Gate 0 stops here until these choices are approved. Approval of P2 cannot silently authorize a newer BtbN release, a `latest` URL, a different archive hash, or a later-selected source build.

## Executive findings

- All current Saved Clip trim and video concat/normalization re-encode paths explicitly request `libx264`. They are **blocked as implemented** under an LGPL-only baseline.
- Current audio extraction, audition, overlay, and normalized composition paths request native `aac` and hard-code M4A/MP4 containers. They are technically plausible under LGPL FFmpeg but remain **conditional** pending executable proof, delivery policy, and separate patent review.
- The configured developer pair is Gyan FFmpeg 8.1.2 `full_build`, compiled with `--enable-gpl`, `--enable-version3`, `--enable-static`, `libx264`, `libx265`, `libvidstab`, and many other optional libraries. It is enhanced-local evidence only.
- ReelForge resolves `ffmpeg.exe` and `ffprobe.exe` independently and declares them ready when both files exist. It does not validate versions, builds, hashes, provenance, pairing, or capabilities.
- Render cache fingerprints use only the first line of `ffmpeg -version`; they omit `ffprobe`, executable hashes, build configuration, parser contract, profile, and resource policy.
- There is no FFmpeg thread cap, media-process concurrency limit, executable runtime proof, independent playback test, visual/golden test, or performance harness.
- Existing tests strongly cover exact arguments, domain behavior, caching, cancellation, rollback, and network isolation, but almost all media execution uses doubles and fake bytes.
- The only committed real-media fixture is a user-supplied H.264/AAC MP4. Its hash is documented, but its provenance is insufficient for the Gate 0 generated/licensed proof corpus.
- FFmpeg's current license inventory explicitly classifies `libx264`, `libx265`, `libvidstab`, `librubberband`, the `eq` filter, and `hqdn3d` among GPL paths. They cannot be assumed in P1 or P2.
- LGPL-path alternatives exist for the planned basic-color and denoise needs, but feature quality and exact parameter mapping remain G0.3 proof work.
- H.264/AAC MP4 remains the strongest compatibility expectation, but no platform-neutral encoder and patent/distribution route is approved. WebM VP9/Opus is the strongest current open delivery candidate; Windows-native playback must be measured rather than assumed.

## G0.1 — current dependency inventory

### Configured developer pair

The local settings point to the paired Gyan WinGet FFmpeg 8.1.2 full build. Gate 0 executed only identity and component-list commands; it did not generate media.

| Evidence | FFmpeg | ffprobe |
| --- | --- | --- |
| Version | `8.1.2-full_build-www.gyan.dev` | `8.1.2-full_build-www.gyan.dev` |
| Compiler | GCC 16.1.0, MSYS2 | GCC 16.1.0, MSYS2 |
| SHA-256 | `AD8F211BC894755E0061C55AB280AE00E8D3D4F15A8CC4372B24CFA247B5942E` | `9DF3B0B5275E830961DF6D94E1F7A71121A7ABD5FF708E9FEC8A0B6084A55015` |
| Relevant configuration | `--enable-gpl`, `--enable-version3`, `--enable-static`, `--enable-libx264`, `--enable-libx265`, `--enable-libvpx`, `--enable-libaom`, `--enable-libsvtav1`, `--enable-libass`, `--enable-libfreetype`, `--enable-libfribidi`, `--enable-libharfbuzz`, `--enable-libvidstab`, `--enable-libmp3lame`, `--enable-libopus`, Media Foundation and GPU paths | Same reported configuration and library versions |

The pair exposes native and external encoders including `libx264`, `h264_mf`, `mpeg4`, `libvpx-vp9`, AV1 variants, native AAC, Media Foundation AAC, FLAC, libmp3lame, libopus, PCM, and FFV1. It also exposes the filters ReelForge is likely to research. Presence in this GPL full build does not establish availability in P1/P2.

### Current command and workflow inventory

All FFmpeg argument construction is centralized in [FfmpegCommandBuilder.cs](../src/ReelForge.Infrastructure/Media/Ffmpeg/Commands/FfmpegCommandBuilder.cs). `RecipeMediaMaterializer` remains the central recipe execution owner.

| Current workflow | Concrete assumptions | Provisional baseline disposition |
| --- | --- | --- |
| Approximate frame extraction | Timestamp seek; first/default video; image codec inferred from output path. | Conditional: native path expected, but explicit PNG/pixel/metadata proof is missing. |
| Exact decoded-frame extraction | ffprobe frame list/window, stream index, integer PTS, `select=eq(pts,...)`, PNG inferred from path, VFR mode. | Conditional: capability likely native; paired parser/extraction proof required. |
| Saved Clip trim | MP4 output, `libx264`, native `aac`; no explicit pixel format/profile/quality/sample-rate contract. | **Blocked as implemented** because `libx264` requires the GPL path. |
| Compatible video concat | `setpts`/`asetpts`/`concat`, `libx264`, optional AAC, MP4 `+faststart`. | **Blocked as implemented** because `libx264` is required. |
| Normalized video concat | scale/pad/setsar/fps/yuv420p, stereo resample/silence, `libx264`, AAC, MP4. | **Blocked as implemented** because `libx264` is required. Other filters require profile proof. |
| Remove source audio | Copy first video stream into MP4, discard audio, `+faststart`. | Conditional: only works when the copied source codec is legal in and compatible with MP4. |
| Overlay composition audio | Copy first video stream, normalize/mix 48 kHz float stereo, encode AAC into MP4. | Conditional: video-copy compatibility, native AAC, container, and patent policy need proof. |
| Audition audio mix | 48 kHz stereo filters, native AAC, mandatory `.m4a`. | Conditional: technically plausible; exact delivery/runtime policy unresolved. |
| Extract/detach audio | First audio stream, native AAC 192 kb/s, mandatory `.m4a`. | Conditional: same AAC/M4A issue; current durable asset contract is format-specific. |
| Media inspection | `ffprobe -show_format -show_streams` JSON; first video/audio streams; tolerant nullable parsing. | Conditional: no paired-version/schema validation or malformed/multi-stream proof. |
| Runtime fingerprint/cache | First stdout line of `ffmpeg -version` plus algorithm version. | Blocked for Gate 0 identity: build flags, binary hashes, ffprobe, parser contract, profile, and thread policy are absent. |
| Tool discovery | Configured path, app-local candidates, then PATH, independently for Windows executable names; readiness means files exist. | Blocked for semantic preflight and pairing; also embeds Windows filenames in reusable Infrastructure. |

### Hard-coded assumptions requiring later decisions

- `libx264` appears in frame-accurate trim, compatible concat, and normalized concat.
- AAC output and M4A/MP4 file types are embedded in extraction, audition, overlay, detach, and cache paths.
- Composition video caches always use `.mp4`; audition audio always uses `.m4a`; extracted frames use `.png` by convention.
- `+faststart` assumes the MOV/MP4 muxer family.
- First-video/first-audio stream selection is ordinary policy in inspection/extraction, while exact-frame indexing assumes a selected stream schema.
- Normalization pins even dimensions, square pixels, `yuv420p`, an inferred target frame rate, 48 kHz stereo, and AAC where audio exists.
- Input/output container support is inferred from extensions rather than a semantic profile.
- The exact frame parser depends on ffprobe JSON fields including stream `index`, rational `time_base`, `frames`, and `best_effort_timestamp`.
- No command supplies `-threads` or participates in a global media-process budget.
- Independent settings can combine unrelated FFmpeg and ffprobe builds.

### Current test and CI evidence

| Evidence class | Present | Missing |
| --- | --- | --- |
| Command tests | Exact assertions for frame, trim, concat, normalization, overlay, fade, pan, resample, silence, AAC, and file-type constraints. | Capability/profile-driven command selection. |
| Parser tests | Inline ffprobe JSON mapping for ordinary MP4/H.264/AAC metadata. | Executable ffprobe, malformed/schema drift, multiple streams, pair mismatch. |
| Orchestration tests | Strong fake-runner coverage for cache identity, coalescing, cancellation, cleanup, promotion, provenance, and rollback. | Media validation beyond fake bytes and stub metadata. |
| Acceptance | Human current-workflow checks against the configured development runtime. | Reproducible pinned runtime, independent playback, visual goldens, resource metrics. |
| Windows CI | Debug/Release build and all six suites. | No FFmpeg acquisition, hashes, capability validation, or executable media proof. |
| Portable CI | Four portable suites on Ubuntu. | No media runtime; no macOS CI. |

## G0.2 — candidate profile research

### P1 — Minimal Native LGPL Control (blocked)

Purpose: establish the smallest controlled FFmpeg/ffprobe behavior and expose accidental optional-library reliance. P1 is not expected to be the final consumer delivery profile and is not proposed for executable proof at this checkpoint.

Proposed constraints:

- pinned official FFmpeg release/tag and one paired build;
- no `--enable-gpl` and no `--enable-nonfree`;
- no GPL-listed filters or external libraries;
- disable autodetection and record the complete configure line;
- native decoding/inspection, PNG, PCM/WAV, FLAC, FFV1, Matroska, native AAC only as a separately flagged conditional capability, and native LGPL filters;
- exclude H.264 encode, libvpx/libopus, AV1 external encoders, text libraries, and platform acceleration from P1; and
- use native FFV1/FLAC Matroska for lossless video proof rather than claiming consumer delivery.

Value: P1 answers whether ReelForge's fundamental analysis, exactness, filter graph, audio, and container behavior survives in a genuinely narrow profile. It provides a clean control when P2 succeeds only because a broad external dependency is present.

Risk: P1 requires a controlled source build and cannot satisfy the complete Free delivery or text contract by itself. The source target can be pinned to FFmpeg commit `7c533d0f86f13a06ec93968f6194349665b3536a`, but the exact Windows build-environment identity, dependency inputs, and output hashes are not yet established. P1 therefore remains blocked rather than pretending that an arbitrary MSYS2 installation is reproducible.

### P2 — Practical LGPL Free Candidate (recommended)

Purpose: prove the likely platform-neutral Free 1.0 contract on Windows. The exact proposed proof pair is `P2.BtbnLgplShared.WindowsX64.20260820`; its immutable acquisition and runtime evidence are in the [candidate manifest](gate-0-p2-proof-candidate.md).

Required capability subset:

- P1 native capabilities;
- pinned libvpx for VP9 encode/decode and pinned libopus for Opus;
- WebM/Matroska muxing;
- FreeType, HarfBuzz, Fontconfig, FriBidi, and libass candidates for Unicode text, lookup/fallback, shaping, captions, and SRT/ASS burn-in;
- a separately licensed test font, with public font packaging deferred;
- native `nlmeans`/`atadenoise` rather than GPL `hqdn3d` for preliminary denoise evidence;
- native `colorbalance`, `colorchannelmixer`, `hue`, `curves`, and `colorspace` candidates rather than GPL `eq`;
- native `xfade`, overlay/alpha, scale/crop/pad/format, loudnorm, showwaves/waveform, deflicker, unsharp, and deshake candidates; and
- no GPL or nonfree configure path.

The pinned BtbN build is broader than this subset and includes additional LGPL-path libraries and patent-sensitive codec candidates. Those components are observed but not approved for ReelForge use merely because they exist. This tradeoff makes P2 fast and reproducible enough for feature feasibility while leaving selection of a narrower public runtime to release engineering. If the owner requires proof against a narrow dependency closure now, reject this candidate and report P2 blocked pending a controlled build.

Proposed delivery roles:

| Role | Candidate | Current judgment |
| --- | --- | --- |
| Default open video proof | WebM, VP9, Opus | Strongest platform-neutral candidate; browser and VLC support are strong, Windows-native/WMP behavior must be measured. |
| Lossless audio | WAV/PCM and FLAC | Strong candidates; Windows/browser/player behavior still tested empirically. |
| Lossless video proof/intermediate | Matroska, FFV1, FLAC | Good proof and possible intermediate; not an ordinary consumer-delivery promise. |
| Compatibility audio | Native AAC in M4A | Conditional on patent/distribution decision and independent playback proof. |
| Compatibility video | H.264/AAC MP4 | No P2 encoder selected; remains a major product decision. |
| AV1 | libaom/rav1e/SVT-AV1 | Deferred from P2 initially due build/performance/support complexity. |
| MP3 | libmp3lame | Deferred initially; useful compatibility candidate but adds dependency and requires separate review. |

The proposed P2 build records `--enable-version3`; the flag is not equivalent to GPL. `--enable-gpl` and `--enable-nonfree` are absent. The full configure line and every runtime-file hash are review inputs rather than an inferred legal conclusion.

### W1 — optional Windows compatibility probe

Purpose: determine whether the reference Windows system can provide acceptable H.264/AAC MP4 output without making that path the platform-neutral baseline.

- Probe FFmpeg's Media Foundation H.264/AAC wrappers against the reference system.
- Record software/hardware selection, driver/OS identity, profiles, pixel formats, rate control, quality, determinism, fallback, thread/resource behavior, and independent playback.
- Treat the result as optional Windows capability only.
- Do not infer public redistribution, patent safety, or future macOS support.
- Do not allow a W1 success to make a baseline Free project unrenderable under P2.

W1 is recommended because MP4/H.264/AAC is the broad compatibility expectation, but it must not distract from establishing P2.

### Candidates not recommended for initial proof

| Candidate | Disposition | Reason |
| --- | --- | --- |
| Current Gyan full build | Enhanced local only | GPL/static, very broad, includes current blocker and cannot establish the baseline. |
| libx264/libx265 | Rejected from P1/P2 | FFmpeg classifies these external libraries as GPL paths. |
| GPL `eq` and `hqdn3d` | Rejected from P1/P2 | Alternative LGPL-path color/denoise primitives should be tested instead. |
| libvidstab | Deferred/blocked pending clarification | Current FFmpeg license metadata classifies it as GPL even though newer upstream vid.stab licensing claims have changed; do not assume it is LGPL-compatible until the pinned FFmpeg integration proves otherwise. Native deshake is the initial candidate. |
| OpenH264 | Deferred | Source license and Cisco binary/patent terms are separate; encoding constraints and distribution route need a focused decision. |
| AV1 external encoders | Deferred | Technically viable, but larger builds, performance cost, and multiple version-sensitive patent/license artifacts add no immediate baseline advantage over VP9 proof. |
| MPEG-4 Part 2 | Not a default candidate | Native encoder exists, but web/user playback and patent history make it a poor primary delivery choice. |
| MOV | Not a general web target | Useful container support but not the broad browser-delivery answer. |

## Proposed semantic capability manifest

The proposal retains three separate artifacts.

### Required capabilities

Committed, human-reviewed product semantics. Representative shape:

```json
{
  "profileId": "Free.Baseline.1",
  "requirements": [
    {
      "id": "Video.Encode.DefaultDelivery",
      "required": true,
      "purpose": "ordinary Free composition and selected-media delivery"
    },
    {
      "id": "Text.Render.Unicode",
      "required": true,
      "purpose": "Free titles and captions"
    },
    {
      "id": "Preview.GenerateDraftProxy",
      "required": true,
      "purpose": "responsive high-resolution source handling"
    }
  ]
}
```

This file contains no encoder, filter, executable, path, or Windows device name.

### Runtime profile mapping

Committed reviewed mapping from semantics to concrete runtime predicates:

```json
{
  "profileId": "P2.PracticalLgpl.WindowsProof.1",
  "maps": [
    {
      "capabilityId": "Video.Encode.DefaultDelivery",
      "requires": {
        "encoders": ["libvpx-vp9", "libopus"],
        "muxers": ["webm"],
        "filters": ["scale", "format"]
      }
    }
  ],
  "forbids": {
    "configurationFlags": ["--enable-gpl", "--enable-nonfree"]
  }
}
```

Concrete names remain profile evidence, not domain/project meaning.

### Observed runtime capabilities

Generated CI/diagnostic evidence, not project truth:

- paired FFmpeg/ffprobe resolved paths for the local process only;
- SHA-256 hashes;
- version, compiler, configuration, and library versions;
- encoders, decoders, muxers, demuxers, filters, and relevant protocols;
- pair-compatibility result;
- parser-contract/version checks;
- source/artifact provenance and acquisition method; and
- warnings for missing, forbidden, or unexpected components.

Validation compares the required semantics with the selected mapping and observed pair. Its success wording is “Runtime matches the reviewed Gate 0 profile,” never “legally compliant.”

Pair compatibility is an exact manifest rule, not a version-string heuristic. Both executables must come from the same owner-approved source build or immutable archive; their hashes, complete build reports, library rows, and packaged runtime closure must match the reviewed manifest. The validator rejects mixed or drifted files even when the displayed FFmpeg versions match, then separately executes the named ffprobe JSON parser-contract probes. A shared build is extracted into an isolated directory and every packaged executable/DLL is hash-verified before use.

## Proposed minimal reusable discovery/validation seam

Checkpoint A approval is requested for only this scope:

- an immutable Application-level observation/assessment contract for a paired media toolchain;
- an Application port that requests observation and validation without naming FFmpeg domain semantics;
- an Infrastructure implementation beside `MediaToolDiscovery` that executes deterministic identity/component probes through `IExternalProcessRunner`, hashes both binaries, checks pairing/parser assumptions, and maps the evidence to the selected profile; and
- initial use only by Gate 0 probes/tests/CI reporting.

Explicitly excluded before separate approval:

- changing current discovery precedence or Settings UI;
- blocking existing renderers through new preflight;
- migrating renderers from paths to profiles;
- changing cache keys or invalidating current caches;
- adding an engine/plugin registry;
- placing runtime observations in Core or `.rfp`; and
- adding user-facing feature or capability behavior.

If the seam becomes a durable production contract, Gate 0 records it in an ADR before broader adoption.

## Proposed fixture and proof matrix

No Gate 0 proof fixture has been added yet. The existing user-supplied H.264/AAC file remains current-test evidence only.

| ID | Proposed deterministic fixture | Purpose |
| --- | --- | --- |
| F1 | Repository-generated color bars, safe-area markers, frame numbers/known PTS, and synchronized stereo tones. | Decode, exact frame, A/V timing, trim, seek, baseline export. |
| F2 | A second deterministic source with mismatched dimensions, frame rate, pixel format, sample rate, and channel layout. | Normalize, concat, resample, silence, transition boundaries. |
| F3 | Alpha image/sequence plus Unicode strings and one pinned test-only font with audited license/hash. | Overlay, opacity, title, captions, shaping, fallback, golden frames. |
| F4 | Mono/stereo PCM WAV and FLAC at 32/44.1/48 kHz plus known peaks and phase. | Mix, pan, fades, waveform, lossless export, clipping/loudness analysis. |
| F5 | Silent video and no-audio video. | Stream absence, generated silence, detach/extract errors, deterministic mix. |
| F6 | Generated/repeated long-form recipe rather than a large committed binary. | Performance and 60-120 minute integrity without repository bloat. |

Preferred provenance approach:

- generate raw PNG/PPM frames and PCM WAV deterministically from repository code so source truth does not depend on the runtime under test;
- pin and document every generator version/algorithm and output hash;
- use only separately reviewed, hash-pinned, test-only fonts/assets where generation is insufficient; and
- keep large performance media generated or cached outside Git.

Later G0.3 proof checks process success, atomic output, ffprobe structure, timing, decode-again, deterministic frame/audio expectations, independent playback, and visual/golden comparisons. Planned but unimplemented features are exercised through bounded proof commands, not product UI or production command builders.

### Independent validation targets

For shortlisted delivery formats:

- Chrome, Edge, and Firefox actual playback, not only `canPlayType`;
- Windows-native/Windows Media Player behavior with exact OS/player/codec-pack state;
- current VLC with version recorded;
- local file and HTTP-served web media where relevant;
- open, seek, non-zero start, pause/resume, A/V sync, and end-of-file behavior; and
- visual/golden confirmation for text, Unicode, color, alpha/overlays, and transitions.

Documented Mac compatibility remains research evidence only.

## Proposed later performance methodology

G0.5 remains after executable profile selection and proof. No numeric thresholds are invented at Checkpoint A.

| Shape | Workload |
| --- | --- |
| Baseline | 1 video layer plus 1 audio track. |
| Typical | 2 simultaneous video layers plus 4 audio tracks. |
| Stress | 4 simultaneous video layers plus 8 audio tracks. |
| Long-form | 1-2 mostly sequential video layers, multiple audio tracks, 60-120 minute integrity project. |

At 720p and 1080p, compare cold/warm cache, thread caps of auto/1/half-logical/full-logical, and process concurrency of 1/2/4. Run one warmup and at least three measured repetitions. Record p50/p95 where meaningful for render/export duration, preview startup, UI-dispatch delay, cancellation-to-process-exit, CPU, memory, GPU, disk I/O, process count, and cache/proxy bytes. The reference machine remains Ryzen 7 3700X, 32 GB RAM, RTX 3070 Ti 8 GB—not a public minimum.

## Preliminary non-blocking Pro dispositions

These are documentation/build-availability judgments, not quality proof:

| Capability | Preliminary disposition | Evidence and blocker |
| --- | --- | --- |
| Match to Previous Clip | Deferred | No single FFmpeg operation; needs defined analysis targets, typed outputs, comparison fixtures, and quality criteria. |
| Stabilization | Conditional | Native `deshake` is an LGPL-path candidate but requires quality proof. `libvidstab` remains blocked/deferred while FFmpeg classifies the integration as GPL. |
| Deflicker | Conditional | Native filter is available; temporal quality, parameter defaults, and preview cost need proof. |
| Denoise | Conditional | Native `nlmeans` and `atadenoise` candidates exist; GPL `hqdn3d` is excluded. Quality/performance must be measured. |
| Sharpen | Conditional | Native `unsharp` exists; artifact limits and safe defaults require proof. |
| Format matching | Deferred | Needs a product definition separating geometry, pixel/color metadata, cadence, and encoding normalization from creative color matching. |
| Loudness matching | Conditional | Native `loudnorm` supports analysis/apply workflows; target policy, intermediate evidence, and deterministic two-pass orchestration remain. |

These dispositions do not block P1/P2 or Free Gate 0 completion.

## Expected blockers and decision consequences

1. **Current video output is GPL-dependent as implemented.** Dependent feature slices must not copy the `libx264` assumption. Later command/profile work must change before P1/P2 can execute trim and composition render.
2. **Default consumer delivery is unresolved.** WebM VP9/Opus is the best open baseline candidate but may add friction in Windows-native workflows. H.264/AAC MP4 requires a separate encoder and patent/distribution decision.
3. **Current durable audio formats are embedded in product behavior.** If M4A/AAC is not approved, extraction/detach persistence and UX need an explicit replacement or multi-profile design.
4. **Basic color cannot use the obvious `eq` filter in an LGPL profile.** Planned controls must map to audited alternative filters and be visually proven.
5. **Stabilization cannot assume `libvidstab`.** Native deshake may be lower quality; an alternate library or later optional engine may be necessary.
6. **Text has a dependency and font-distribution chain.** Engine availability does not grant permission to bundle fonts or guarantee all Unicode shaping/fallback behavior.
7. **The existing runtime model cannot preflight semantics.** The proposed minimal seam is needed to prove and report profiles, but production enforcement waits for separate approval.
8. **CI has no media toolchain today.** P1 source build and P2 third-party proof acquisition both add CI/provenance work; neither is final runtime packaging.
9. **Test media provenance is insufficient.** A generated/licensed corpus is mandatory before claims about delivery or visual fidelity.
10. **Performance promises have no numeric evidence.** Thread limits, concurrency, 4K behavior, and track-count floors remain later Gate 0 measurements.

## Source basis

Repository evidence:

- [FfmpegCommandBuilder](../src/ReelForge.Infrastructure/Media/Ffmpeg/Commands/FfmpegCommandBuilder.cs)
- [MediaToolDiscovery](../src/ReelForge.Infrastructure/Media/Tools/MediaToolDiscovery.cs)
- [FfprobeMediaInspectionService](../src/ReelForge.Infrastructure/Media/Ffprobe/FfprobeMediaInspectionService.cs)
- [ExternalProcessRunner](../src/ReelForge.Infrastructure/Media/Processes/ExternalProcessRunner.cs)
- [RecipeMediaMaterializer](../src/ReelForge.Infrastructure/Media/Materialization/RecipeMediaMaterializer.cs)
- [ExactVideoFrameService](../src/ReelForge.Infrastructure/Media/Frames/ExactVideoFrameService.cs)
- [FFmpeg command tests](../tests/ReelForge.Infrastructure.Tests/FfmpegCommandBuilderTests.cs)
- [Fixture provenance](../tests/ReelForge.Tests/Fixtures/README.md)
- [Windows CI](../.github/workflows/windows-build-and-test.yml)
- [Portable CI](../.github/workflows/portable-tests.yml)

Primary and authoritative external evidence:

- [FFmpeg legal guidance](https://ffmpeg.org/legal.html)
- [FFmpeg license and GPL component inventory](https://github.com/FFmpeg/FFmpeg/blob/master/LICENSE.md)
- [FFmpeg codec documentation](https://ffmpeg.org/ffmpeg-codecs.html)
- [FFmpeg filter documentation](https://ffmpeg.org/ffmpeg-filters.html)
- [FFmpeg format documentation](https://ffmpeg.org/ffmpeg-formats.html)
- [ffprobe documentation](https://ffmpeg.org/ffprobe.html)
- [FFmpeg download and Windows third-party build links](https://ffmpeg.org/download.html)
- [BtbN build variants and reproducible build repository](https://github.com/BtbN/FFmpeg-Builds)
- [Pinned BtbN 2026-08-20 release](https://github.com/BtbN/FFmpeg-Builds/releases/tag/autobuild-2026-08-20-13-45)
- [Pinned FFmpeg source commit](https://github.com/FFmpeg/FFmpeg/commit/7c533d0f86f13a06ec93968f6194349665b3536a)
- [libvpx source and license](https://chromium.googlesource.com/webm/libvpx/)
- [Opus license and patent grant](https://opus-codec.org/license/)
- [AOMedia AV1 legal materials](https://aomedia.org/about/legal/)
- [libass source and license](https://github.com/libass/libass)
- [FreeType license](https://freetype.org/license.html)
- [HarfBuzz source and license](https://github.com/harfbuzz/harfbuzz)
- [Microsoft Media Foundation supported formats](https://learn.microsoft.com/en-us/windows/win32/medfound/supported-media-formats-in-media-foundation)
- [Microsoft Windows codec table](https://learn.microsoft.com/en-us/windows/apps/develop/media-authoring-processing/supported-codecs)
- [Microsoft H.264 encoder](https://learn.microsoft.com/en-us/windows/win32/medfound/h-264-video-encoder)
- [Chromium audio/video format documentation](https://chromium.googlesource.com/website/+/cb33846322e88ba1acc3188c1daa4b00b94be767/site/audio-video/index.md)
- [MDN media container guidance](https://developer.mozilla.org/en-US/docs/Web/Media/Guides/Formats/Containers)
- [VLC format support](https://images.videolan.org/vlc/features.html)

External documentation establishes candidates and expected support, not ReelForge proof. Only owner-approved G0.3 execution against pinned toolchains and fixtures can change these preliminary dispositions.
