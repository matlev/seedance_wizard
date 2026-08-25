# Gate 0 G0.3 executable-proof results

Status: automated P2 semantic proof complete; 13 capabilities passed; independent playback remains a G0.3 completion gate; long-form integrity remains a G0.5 completion gate

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
| `Invoke-P2VisualProof.ps1` | Prove transitions, waveform, transform/alpha, and the owner-approved `colorlevels`/`hue` basic-color semantics. |
| `Invoke-P2DeliveryProof.ps1` | Prove draft proxy, selected-media/composition WebM delivery, and FLAC/Ogg Opus standalone audio. |
| `Invoke-P2TextProof.ps1` | Prove the approved Unicode title/caption, controlled font-run mapping, wrapping, placement, and Arabic-shaping contract. |
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
| `Video.Composite.TransformAlphaAndColor` | Passed | Explicit transform/alpha evidence plus separate deterministic `colorlevels` brightness/contrast and `hue` saturation oracles passed. This proves the bounded user semantics, not `eq` parity or a final UI model. |
| `Video.Transition.CrossDissolveAndBlack` | Passed | Timestamped FFV1 evidence proved exact cadence/frame counts, endpoint/intermediate cross-dissolve pixels, and ordered red-to-intermediate-to-black-to-intermediate-to-green behavior. |
| `Audio.Waveform.Generate` | Passed | Known-tone output was deterministic and source-sensitive; the digital-silence contrast produced no wave excursion. |
| `Preview.GenerateDraftProxy` | Passed | VP9/Opus WebM respected the 320x180 bound, portrait aspect/padding oracle, 15 fps, 48 kHz stereo, inspection, and explicit decode-again. |
| `Video.Export.OpenDelivery.SelectedMedia` | Passed | VP9/Opus WebM structure, explicit decode-again, ordered frame identity, and visual-error tolerance passed. |
| `Video.Export.OpenDelivery.Composition` | Passed | Normalized two-segment delivery retained six ordered F1-then-F2 identities across the boundary and stayed within duration tolerance. |
| `Audio.Export.Standalone` | Passed | FLAC decoded byte-exactly; Ogg Opus retained structure, duration/sample count within declared padding tolerance, channels, and a 1000 Hz tone stronger than the declared comparison frequencies. |
| `Text.Render.UnicodeTitlesAndCaptions` | Passed | Inventory-bound logical/layout/ASS inputs rendered Latin/diacritics, Simplified Chinese, and shaped Arabic in titles and wrapped captions using only the three pinned Noto faces. Same-provider absence and missing-CJK controls detected and rejected ambient DirectWrite fallback. Color emoji remained optional and unexecuted. |
| `Delivery.Validate.IndependentPlayback` | **Not run** | Same-runtime inspection/decode cannot satisfy browser, VLC, or Windows-native playback validation. The reference system has Chrome, Edge, and Firefox but no VLC installation; no result was inferred. |
| `Project.LongForm.Integrity` | **Not run** | The optional duration-only F6 artifact is not sufficient to prove 30,000 boundaries, final timestamps, and first/final identities. Resource evidence belongs to G0.5. |

Aggregate disposition: **incomplete with explicit gates**. Thirteen capabilities passed; independent playback and long-form integrity remain not run. There are no remaining automated P2 semantic blockers in the approved 15-capability matrix.

## Important findings

### Basic color

The earlier `eq` candidate was invalid for this profile: FFmpeg lists `vf_eq.c` among its GPL filters, and the approved P2 LGPLv3-path build correctly does not expose it. The owner-approved replacement uses `colorlevels` for brightness/contrast semantics and `hue` for saturation. Separate authored RGB oracles, tolerances, explicit stream selection, and repeat hashes passed. This is acceptable semantic coverage under P2, not numerical equivalence to `eq`, a final parameter scale, or a permanent domain/UI filter model. See the [FFmpeg license list](https://ffmpeg.org/doxygen/trunk/md_LICENSE.html), [`colorlevels`](https://www.ffmpeg.org/ffmpeg-filters.html#colorlevels), and [`hue`](https://www.ffmpeg.org/ffmpeg-filters.html#hue).

### Unicode text and font fallback

The retained, manifest-validated proof stack is:

- Noto Sans Regular for Latin/punctuation/diacritics;
- Noto Sans Arabic Regular plus the runtime's shaping dependencies;
- Noto Sans CJK SC Regular for the approved Simplified Chinese fixture locale.

The proof copies only those three hash-validated binaries into a clean directory and verifies the exact loaded paths and selected internal face names. A no-`fontsdir` run against the same DirectWrite provider establishes that the approved identities are absent from ambient fonts; a missing-CJK control then demonstrates that an ambient substitute is rejected. Inventory-bound JSON and ASS are cross-validated, the rendered output matches a reviewed golden hash, wrapping and safe regions are measured, and the simple/complex difference is localized to the Arabic oracle region. Automatic DirectWrite fallback is not accepted: an exploratory mixed-family run selected ambient `YuGothicUI-Semibold` and `ArialMT`.

This is controlled P2 proof mapping at the Infrastructure/runtime-profile boundary, not a final app-level text architecture. Color emoji remains separate and optional/blocked until an exact renderer path proves it reproducibly.

### Optional W1 Windows evidence

The separate W1 probe produced an H.264/AAC MP4 through explicit `h264_mf` and `aac_mf`, then verified 320x180 `yuv420p`, 25 fps, three frames, stereo 48 kHz audio, duration, paired inspection, and explicit H.264/AAC decode-again. Its narrow result is `basic-wrapper-supported` on the tested Windows environment.

W1 did not collect independent playback, hardware/driver selection, profiles, rate control, resource behavior, or determinism evidence. Full Windows compatibility therefore remains incomplete/manual. W1 establishes no portable baseline, shipping, patent, licensing, distribution, or macOS conclusion.

## Remaining evidence work

- Preserve the exact daily P2 archive in project-controlled private artifact storage or re-pin a monthly-retained BtbN build and regenerate the complete manifest before unattended CI depends on it.
- Execute actual Chrome, Edge, Firefox, current VLC, and applicable Windows-native playback checks for local-file/HTTP open, seek, pause/resume, A/V sync, and EOF, recording exact environment state.
- Move the one-hour/30,000-boundary proof into the G0.5 long-form methodology so timestamp integrity and resource evidence are measured together.
- Keep CI proof opt-in/manual until the approved P2 bytes have durable retention. Hash/version/configuration/closure drift remains a hard failure; no substitution is allowed.

## Approved next work

The owner approved all three decisions in the [G0.3 owner-decision record](gate-0-g0.3-owner-decisions.md): `colorlevels` plus `hue` for bounded basic-color proof; an exact OFL-only Noto font stack for Unicode fallback and Arabic-shaping proof with color emoji separate; and G0.4 analysis proceeding while independent playback and G0.5 long-form integrity remain explicit completion gates.

Both authorized mappings have now passed executable proof. G0.4 may compare and recommend delivery candidates, but the default Free delivery contract cannot be finalized until required independent-playback evidence is complete. Gate 0 cannot exit until playback and long-form gates are completed or explicitly dispositioned by the owner.
