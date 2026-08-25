# Gate 0 G0.3 executable-proof results

Status: executable proof complete for the currently approved mappings; owner decisions required before blocked mappings are re-proved or G0.4 selects a Free delivery contract

Date: 2026-08-24

## Scope and evidence boundary

G0.3 executed against `P2.BtbnLgplShared.WindowsX64.20260820`. P2 is third-party **LGPLv3-path** proof infrastructure because the reviewed build uses `--enable-version3`. It is not the selected shipping runtime, public-distribution approval, a legal conclusion, or approval to use every component compiled into the archive. WebM VP9/Opus remains an open-delivery proof candidate, not the final ReelForge default.

The proof did not change product behavior, media discovery, renderers, project persistence, cache keys, Settings, or the portable platform boundary. P1 remains blocked and was not rebuilt. No paid provider call was made.

The proof separates four forms of evidence:

1. exact paired-runtime identity and component presence;
2. independently byte-pinned deterministic source primitives;
3. fixture transport/stream/timing proof; and
4. one semantic verdict for every reviewed capability.

Component presence never counts as semantic proof. Every media command records or explicitly selects its concrete demuxers, decoders, encoders, muxers, filters, and stream selectors.

## Reproducible entry points

| Entry point | Purpose |
| --- | --- |
| `Acquire-P2Runtime.ps1` | Acquire the exact content-pinned archive and verify archive/runtime-closure hashes. The daily URL is allowed only for explicit local proof until a durable artifact or monthly re-pin is approved. |
| `Validate-P2Runtime.ps1` | Validate the exact paired runtime, configuration, library rows, parser contract, and complete executable/DLL closure. |
| `Generate-Fixtures.ps1` | Generate F1-F8 raw primitives outside the repository and compare every output byte against `fixture-source-inventory.json`. |
| `Invoke-P2SemanticProof.ps1` | Prove fixture packaging, inspection, decode-again, F7 timing/frame identity, and F8 explicit stream selection without emitting feature-capability verdicts. |
| `Invoke-P2EditTimingProof.ps1` | Prove exact frame, trim, normalized concat/timestamp continuity, and deterministic audio mix semantics. |
| `Invoke-P2VisualProof.ps1` | Prove transitions and waveform semantics and report the composite/basic-color mapping blocker. |
| `Invoke-P2DeliveryProof.ps1` | Prove draft proxy, selected-media/composition WebM delivery, and FLAC/Ogg Opus standalone audio. |
| `Invoke-P2FullProof.ps1` | Run and aggregate the preceding lanes into exactly one verdict for each capability. |
| `Invoke-W1MediaFoundationProbe.ps1` | Run the separate optional Windows Media Foundation wrapper probe. |

All generated fixtures, media, logs, and evidence stay outside Git. Final evidence records the hashes of its child evidence files and rendered artifacts. Normal tests never discover or use a PATH FFmpeg; live checks require an explicit approved runtime root.

## P2 semantic verdicts

The integrated live run produced 15 unique verdicts:

| Capability | Verdict | Executed evidence |
| --- | --- | --- |
| `Media.Inspect.StructureAndTiming` | Passed | F1 structure/A-V timing, F7 VFR/non-zero PTS/presentation identity, and F8 distinguishable 2-video/2-audio selection. |
| `Video.Frame.ExtractExact` | Passed | Selected frame 1 decoded byte-exactly to the authored RGB oracle. |
| `Timeline.Trim.Exact` | Passed | Frames 1-2 and the exact interleaved PCM sample slice survived trim with zero-based monotonic output timing. |
| `Timeline.Concat.NormalizeAndContinueTimestamps` | Passed | F1 then F2 identities, six exact 25 fps timestamps, the 120 ms boundary, 640x360 normalization, and 48 kHz stereo normalization were proven. |
| `Audio.Mix.Deterministic` | Passed | Decoded PCM matched the independently summed and clamped authored samples within tolerance. |
| `Video.Composite.TransformAlphaAndColor` | **Blocked** | There is no owner-approved LGPLv3-path mapping that jointly proves brightness, contrast, and saturation. No unapproved filter or substitute was executed. |
| `Video.Transition.CrossDissolveAndBlack` | Passed | Timestamped FFV1 evidence proved exact cadence/frame counts, endpoint/intermediate cross-dissolve pixels, and ordered red-to-intermediate-to-black-to-intermediate-to-green behavior. |
| `Audio.Waveform.Generate` | Passed | Known-tone output was deterministic and source-sensitive; the digital-silence contrast produced no wave excursion. |
| `Preview.GenerateDraftProxy` | Passed | VP9/Opus WebM respected the 320x180 bound, portrait aspect/padding oracle, 15 fps, 48 kHz stereo, inspection, and explicit decode-again. |
| `Video.Export.OpenDelivery.SelectedMedia` | Passed | VP9/Opus WebM structure, explicit decode-again, ordered frame identity, and visual-error tolerance passed. |
| `Video.Export.OpenDelivery.Composition` | Passed | Normalized two-segment delivery retained six ordered F1-then-F2 identities across the boundary and stayed within duration tolerance. |
| `Audio.Export.Standalone` | Passed | FLAC decoded byte-exactly; Ogg Opus retained structure, duration/sample count within declared padding tolerance, channels, and a 1000 Hz tone stronger than the declared comparison frequencies. |
| `Text.Render.UnicodeTitlesAndCaptions` | **Blocked** | The approved F3 prerequisite does not yet identify an owner-approved, byte-pinned OFL font stack or settle color emoji scope. System-font fallback remained prohibited. |
| `Delivery.Validate.IndependentPlayback` | **Not run** | Same-runtime inspection/decode cannot satisfy browser, VLC, or Windows-native playback validation. The reference system has Chrome, Edge, and Firefox but no VLC installation; no result was inferred. |
| `Project.LongForm.Integrity` | **Not run** | The optional duration-only F6 artifact is not sufficient to prove 30,000 boundaries, final timestamps, and first/final identities. Resource evidence belongs to G0.5. |

Aggregate disposition: **incomplete with explicit blockers**. Eleven capabilities passed; two are blocked; two remain not run.

## Important findings

### Basic color

The earlier `eq` candidate was invalid for this profile: FFmpeg lists `vf_eq.c` among its GPL filters, and the approved P2 LGPLv3-path build correctly does not expose it. It must not enter the P2 mapping. Official FFmpeg documentation identifies native `colorlevels` controls for level/contrast correction and `hue` controls for saturation and brightness. The smallest complete candidate mapping is therefore the pair `colorlevels` + `hue`, with separate independent ramp/solid-color oracles. This is an equivalent semantic mapping proposal, not numerical equivalence to `eq`, and requires owner approval before execution. See the [FFmpeg license list](https://ffmpeg.org/doxygen/trunk/md_LICENSE.html), [`colorlevels`](https://www.ffmpeg.org/ffmpeg-filters.html#colorlevels), and [`hue`](https://www.ffmpeg.org/ffmpeg-filters.html#hue).

### Unicode text and font fallback

No single font honestly proves the current `ReelForge — 你好 — مرحبا — 🎬` sample, fallback, Arabic shaping, and color emoji. The smallest recommended OFL-only base stack is:

- Noto Sans Regular for Latin/punctuation/diacritics;
- Noto Sans Arabic Regular plus the runtime's shaping dependencies;
- one pinned Noto Sans CJK regional font for the approved fixture locale.

Color emoji should remain a separate optional/blocked capability until the exact renderer proves the CBDT/CBLC path reproducibly. Every chosen binary needs an exact release, byte size, SHA-256, license text, and preferably durable project-controlled retention. Font presence alone cannot prove shaping or fallback.

### Optional W1 Windows evidence

The separate W1 probe produced an H.264/AAC MP4 through explicit `h264_mf` and `aac_mf`, then verified 320x180 `yuv420p`, 25 fps, three frames, stereo 48 kHz audio, duration, paired inspection, and explicit H.264/AAC decode-again. Its narrow result is `basic-wrapper-supported` on the tested Windows environment.

W1 did not collect independent playback, hardware/driver selection, profiles, rate control, resource behavior, or determinism evidence. Full Windows compatibility therefore remains incomplete/manual. W1 establishes no portable baseline, shipping, patent, licensing, distribution, or macOS conclusion.

## Remaining evidence work

- Preserve the exact daily P2 archive in project-controlled private artifact storage or re-pin a monthly-retained BtbN build and regenerate the complete manifest before unattended CI depends on it.
- Execute the owner-approved basic-color mapping or retain the capability as blocked.
- Acquire, license-record, hash-pin, and preserve the owner-approved F3 font stack; then prove glyph selection, fallback, wrapping, captions/titles, and Arabic shaping. Keep color emoji separate unless explicitly required.
- Execute actual Chrome, Edge, Firefox, current VLC, and applicable Windows-native playback checks for local-file/HTTP open, seek, pause/resume, A/V sync, and EOF, recording exact environment state.
- Move the one-hour/30,000-boundary proof into the G0.5 long-form methodology so timestamp integrity and resource evidence are measured together.
- Keep CI proof opt-in/manual until the approved P2 bytes have durable retention. Hash/version/configuration/closure drift remains a hard failure; no substitution is allowed.

## Owner decisions required

1. Approve or reject `colorlevels` + `hue` as the P2 basic-color mapping for a new bounded composite proof. Approval retains brightness, contrast, and saturation semantics; it does not select a production UI or exact creative parameter model.
2. Approve the F3 contract as **Unicode glyph/fallback plus Arabic shaping**, using an exact OFL-only Noto Sans/Noto Sans Arabic/Noto Sans CJK stack, with color emoji separate and optional/blocked for Gate 0. The alternative is to require color emoji now and accept a larger four-font/runtime-compatibility proof.
3. Confirm independent playback and long-form integrity may remain explicit G0.3/G0.5 completion gates while G0.4 delivery-format analysis proceeds, rather than forcing premature local-player installation or an unmeasured one-hour render in this slice.

No Free contract or default-delivery decision should be finalized until these decisions are recorded.
