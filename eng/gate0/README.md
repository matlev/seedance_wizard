# Gate 0 fixture primitives

The canonical current state and active authorization are maintained in `docs/gate-0-current-status.md`. Historical decision/result documents retain the status that was true when their evidence was recorded. Future measured-evidence growth is governed by `docs/gate-0-evidence-containment.md`; no shard/index writer or full Stage 2 execution is authorized yet.

This directory defines deterministic authored inputs and the opt-in executable Gate 0 media-proof harness. It does not contain generated media. The generator writes raw PPM, RGBA, PCM, recipe, and timestamp-truth files to a caller-supplied temporary directory; every proof runner likewise keeps generated media and evidence outside the repository.

The checked-in `fixture-source-inventory.json` is the independent byte oracle for generated primitives. Generation fails before reporting if a file is missing, additional, drifted, path-escaping, or reached through a reparse point. A caller-supplied inventory override is test-only and is never eligible as approved Gate 0 proof input.

## Inputs and truth

- `fixture-manifest.json` is the reviewed fixture identity, provenance, and concrete packaging-component plan. A component listed here is a required explicit selection for a later proof command, not evidence that the component works.
- `expected-truths.json` contains authored duration, timestamp, stream-identity, frame-identity, audio-tone, and source-property truths. It is independent of FFmpeg output.
- `Generate-Fixtures.ps1` creates the raw primitives, copies the authored truths, and emits `generated-fixture-report.json` with SHA-256 for every generated source/truth file. It does **not** execute FFmpeg, package media, inspect output, or prove a capability.

The raw source files are intentionally simple and reproducible: binary PPM for opaque images, raw RGBA for alpha, little-endian signed 16-bit PCM for audio, and JSON for timing/recipe facts. They make it possible to identify a packaging or decoder failure without treating a prior FFmpeg output as the oracle.

The generated report records the supplied paired-runtime paths only as generation context. Because this script never executes those tools, their presence is not runtime identity evidence or semantic proof; use `Acquire-P2Runtime.ps1` and the paired-runtime validator for that.

## Required invocation boundary

The generator requires all of the following explicit, rooted paths:

```powershell
pwsh ./eng/gate0/Generate-Fixtures.ps1 `
  -FfmpegPath 'D:\verified-p2\bin\ffmpeg.exe' `
  -FfprobePath 'D:\verified-p2\bin\ffprobe.exe' `
  -ApprovedRuntimeRoot 'D:\verified-p2' `
  -OutputDirectory "$env:TEMP\ReelForge-Gate0-Fixtures"
```

It refuses relative paths, `PATH` discovery, missing files, and tools outside `ApprovedRuntimeRoot`. The caller must first use the approved paired-runtime verification procedure. The generator validates only the path boundary; it does not claim that a supplied runtime is approved or license-compliant.

## Fixture identities

| ID | Purpose | Raw authored oracle |
| --- | --- | --- |
| F1 | Baseline color, frame IDs, and sync tones | PPM frame IDs; stereo 440/880 Hz PCM; fixed 25 fps timing |
| F2 | Mismatched dimensions, frame rates, pixels, and audio | Discrete PPM/PCM source property matrix |
| F3 | Alpha and Unicode text | RGBA overlay; inventory-bound Unicode logical/layout/ASS primitives; pinned OFL font manifest |
| F4 | WAV/FLAC audio variants | 32/44.1/48 kHz PCM tone primitives, known peak/phase, and expected lossless facts |
| F5 | Silent/no-audio media | Opaque PPM source plus authored digital-silence PCM and explicit no-audio truths |
| F6 | Long-form integrity recipe | Compact generated 60-minute repeat recipe and source cadence facts |
| F7 | VFR, non-zero PTS, and presentation identity | Per-frame PPM IDs and non-zero 1/90000 presentation timestamps |
| F8 | Explicit video/audio stream selection | Distinguishable PPM and PCM primitives for two video and two audio streams |

F3 uses the owner-approved, checked-in OFL-only proof-artifact stack in `artifacts/fonts`. Its exact release/archive provenance, retained bytes, license texts, roles, and locales are recorded in `font-proof-artifacts.json`. Run `Validate-FontProofArtifacts.ps1` before an F3 proof; it is offline and rejects missing, additional, path-escaping, reparse-point, resized, or hash-drifted files. System-font fallback and font/PATH discovery are prohibited. These retained files are project-controlled durable proof artifacts only, not shipping-runtime or public-distribution approval. Font presence remains insufficient evidence: F3 must render and inspect glyph selection, deterministic fallback, wrapping, captions/titles, and Arabic shaping. Color emoji remains optional and blocked.

## Later proof requirements

When a later G0.3 proof runner packages or transforms these primitives, it must record the exact command and explicitly select every concrete decoder, encoder, muxer, demuxer, and filter named by the fixture plan. Auto-selection is not evidence. A successful process exit alone is not evidence either: the later runner must inspect, decode, and compare the output against `expected-truths.json`.

## Executable proof

`Invoke-P2FullProof.ps1` is the aggregate G0.3 entry point. It validates the exact paired P2 runtime, generates inventory-bound fixtures, runs the fixture, edit/timing, visual, and delivery lanes, and emits exactly one verdict for each capability in `semantic-proof-contract.json`. The aggregate remains incomplete while any required capability is blocked or not run.

`Invoke-P2TextProof.ps1` is the F3 semantic lane. It first validates the checked-in font-artifact manifest and fixture inventory, then renders the generated ASS source with explicit Latin/CJK/Arabic family runs, `ass` complex shaping, explicit `fontsdir`, image2/PPM input selection, and rawvideo output. Positive proof rejects every `fontselect` target outside the three approved Noto target names. Its missing-CJK control must reject ambient fallback; that rejected output is negative evidence only. DirectWrite automatic fallback is therefore not used as capability evidence. The complex render is compared to a reviewed SHA-256 golden, repeated for determinism, and distinguished from simple Arabic shaping. Color emoji remains optional and is absent from the required render.

`Invoke-W1MediaFoundationProbe.ps1` is a separate optional Windows-only probe. Its result must never establish portable project meaning or a shipping/licensing conclusion.

`Invoke-P2G04DeliveryProof.ps1` is the separate owner-authorized G0.4 output matrix. It consumes generated F1/F4 fixtures and `g0.4-delivery-proof-contract.json`, records exact command-token components and runtime/dependency identity for eleven output routes, and proves paired/video-only MP4 and WebM, five audio outputs, PNG, and JPEG. It does not modify or join the completed G0.3 aggregate.

`Invoke-W1G04MediaFoundationProof.ps1` is the separate optional G0.4 comparison. It proves paired and video-only `h264_mf` MP4 on Windows while deliberately pairing W1 with the same native AAC route as P2. Hardware encoding is not forced, but the selected MFT implementation remains unobservable; W1 never establishes the portable baseline.

`Invoke-P2G04InputProof.ps1` executes the owner-approved 256-row common-input contract in `g0.4-input-proof-contract.json`. It validates the exact P2 pair and fixture closure, authors only approved fixture routes, explicitly selects demuxers/decoders/streams, performs strict complete decode and semantic oracles, exercises S1-S7 selection and N1-N7 classification policy, and retains pass/fail/block evidence plus complete command and generated-artifact closure. Generated media and the large evidence file remain outside Git. The corrected canonical result and rerun identity are recorded in `docs/gate-0-g0.4-input-proof-results.md`.

`Invoke-P2F7SettsExperiment.ps1` is the bounded proof-only F7 terminal-duration experiment. It requires the verified interim corpus, exact P2 identity, and `f7-setts-experiment-contract.json`; observes `setts` in the exact runtime; applies the approved duration-only mapping by input PTS rather than packet ordinal; and rejects any post-mux timestamp, duration, payload, frame, or audio drift. Its first direct MP4 case reached the approved timestamp-rewrite stop condition, so the remaining direct cases and Matroska pilot were not run. The result is recorded in `docs/gate-0-g0.4-f7-setts-results.md`.

`Invoke-P3JpegInputProof.ps1` is the bounded proof-only progressive-4:2:0 and EXIF-orientation follow-up. It binds the exact retained P3 installer, Authenticode/provenance record, `cjpeg.exe`/`jpeg62.dll` closure, and P2 runtime before running either row independently. It records exact SOF sampling, one-stream/frame/packet inspection, strict native `mjpeg` decode, visual error, byte preservation, and exact displayed rotation. `Write-ExifOrientation.ps1` inserts only the approved minimal APP1/TIFF orientation segment and performs no image encoding. Both rows passed; results are recorded in `docs/gate-0-g0.4-p3-jpeg-results.md`.

`Invoke-P2G05Calibration.ps1` executes the authorized short, sequential G0.5 Stage 1 matrix from `g0.5-calibration-contract.json`. It measures the exact P2 OpenH264/AAC MP4 and VP9/Opus WebM routes across 720p/1080p and four explicit thread policies, independently verifies each output, exercises bounded cancellation after active progress, and immutably retains every attempted run. Stage 1 completed with 24 WebM passes and 24 MP4 failures under the superseded maximum-delta audio check; the immutable historical evidence is recorded in `docs/gate-0-g0.5-calibration-results.md` and `g0.5-calibration-result-summary.json`. `Invoke-G05RetainedAudioOracle.ps1` verifies the exact retained Stage 1 matrix, explicitly decodes each audio stream without re-encoding, records timing and PCM evidence, and applies the hash-pinned frozen oracle. Its authoritative follow-up passed all 48 rows and admits both routes to marker-survivability qualification; see `docs/gate-0-g0.5-retained-audio-results.md` and `g0.5-retained-audio-result-summary.json`. No Stage 2 media matrix is authorized before the remaining prerequisites and bounded smoke pass.

`Invoke-G05RetainedAudioOracle.ps1` is the opt-in, no-re-encode evaluator for the exact 48 retained Stage 1 AAC/Opus outputs. It binds the complete original route/resolution/thread/repetition matrix, exact retained bytes, exact P2 decoders, the frozen V3 metric implementation, and decoder-applied timing/priming evidence. `G05RetainedAudioOracleHelpers.psm1` contains the fixture-free exact-matrix and rational-time-base timing gates. A blocked semantic route writes a closed report and exits nonzero; neither a previous Stage 1 verdict nor process success alone admits a route.

`Invoke-G05MarkerSurvivability.ps1` is the bounded 30-second marker qualifier for the two admitted Stage 2 route profiles. It explicitly selects every media component, re-encodes the approved 17-bit atlas through the exact MP4/OpenH264/AAC and WebM/VP9/Opus quality mappings, strictly decodes both streams, and rejects marker ambiguity, collision, identity, count, or PTS drift. Both routes passed 750 of 750 frames with zero marker deviations. Packet-bound audio evidence preserves raw decoder output without proof-side trimming; see `docs/gate-0-g0.5-marker-survivability-results.md` and `g0.5-marker-survivability-result-summary.json`. This closes only marker qualification; complete R2 verification and the bounded smoke remain open.

`g0.5-wpf-measurement-adapter-contract.json` is the frozen evidence and execution boundary for the proof-only P2 Windows WPF adapter. It imports the executed workload contract by hash and requires a real visible STA window, a 30-second zero-child control, closed host/GPU-driver/power/display/window evidence, non-dropping dispatcher accounting, privacy-sensitive manual WPR escalation, job-contained exact-P2 children, and packet-to-cleanup cancellation evidence. The contract does not implement or prove the adapter by itself.

`ReelForge.Gate0.WpfMeasurementAdapter` implements that isolated Windows proof boundary and is compiled by the Windows solution without referencing a ReelForge product assembly. Its authoritative visible no-media control passed with zero media children, complete host evidence, and all 1,871 dispatcher cadences classified; see `docs/gate-0-g0.5-wpf-no-media-results.md` and `g0.5-wpf-no-media-result-summary.json`. Media-load, human whole-system observation, preview, and cancellation evidence remain for the bounded smoke.

`g0.5-stage2-smoke-preflight-contract.json` defines the owner-approved non-media smoke resource gate. It binds the exact three 1080p typical candidates, reference-machine RAM/CPU identity, the 8 GiB available-memory floor, no active media process, non-reparse sibling roots, the 0.75 GiB retained and scratch ceilings, and the 2 GiB fixed disk reserve. `Test-G05Stage2SmokePreflight.ps1` remains fail-closed around those exact values. The authoritative reference-host run passed all criteria without invoking media; see `docs/gate-0-g0.5-smoke-preflight.md` and `g0.5-stage2-smoke-preflight-result-summary.json`. This result does not replace R2 verification, authorize the smoke alone, or size long-form work.

## Interim proof-artifact retention

`Preserve-Gate0Artifacts.ps1` creates the one approved local corpus root: the `ReelForge.Gate0Artifacts` sibling of this repository. It refuses any other destination, an existing destination, and source or retained reparse points. The first preservation run copies and independently verifies the exact P2 archive/runtime, F1-F8 fixture corpus, corrected G0.4 evidence closure, P3 producer closure, and immutable contract/provenance snapshots. It atomically replaces only `artifact-retention-manifest.json` in this directory, then places a hash-identical copy at the retained root.

`Test-Gate0ArtifactRetention.ps1` revalidates the retained root against that tracked relative-path manifest. It checks every size and SHA-256, group totals, scoped provenance/license/proof references, the exact physical root, reparse-point absence, and equality of the tracked and retained manifest copies. Run it before any proof uses the retained corpus and immediately before deleting temporary producer infrastructure.

`Add-Gate0RetainedProof.ps1` appends one new immutable proof group to an existing verified corpus. It stages and hashes the payload outside the retained root, requires a non-empty artifact-bound proof identity, preserves every existing manifest group, and uses an exclusive lock plus a recoverable transaction journal across the retained and tracked manifest copies. Canonical forward-slash paths, exact sibling-root containment, scoped reparse checks, and deterministic crash-boundary recovery are enforced. Its production root cannot be overridden; fault injection is available only in an isolated copied test repository under the system temporary root.

The dedicated private `reelforge-artifacts` R2 bucket now contains the complete current corpus: all 4,101 logical artifacts have independently retrieved size/SHA-256 receipts, represented by 2,032 distinct content-addressed objects. The tracked `artifact-manifest.json` reports the second private copy complete. `Upload-Gate0Artifact.ps1`, `Get-Gate0Artifact.ps1`, and `Test-Gate0ArtifactManifest.ps1` provide the credential-gated content-addressed transfer and verification surface; `Gate0ArtifactTools.psm1` reads only the three documented dedicated Windows Generic Credentials and bounds transient atomic receipt-replacement retries without weakening fail-closed behavior. Normal CI performs offline validation and receives no credentials. The temporary-provider R2 path remains prohibited for this corpus. See `docs/gate-0-current-status.md`, `docs/gate-0-g0.5-stage2-replacement-smoke-results.md`, and `artifact-manifest.json` for the current closure; the earlier `docs/gate-0-artifact-retention.md` and `g0.5-r2-retention-result-summary.json` remain immutable historical snapshots.

`P2.BtbnLgplShared.WindowsX64.20260820` is third-party LGPLv3-path proof infrastructure only. These fixture files do not select a shipping runtime, delivery contract, or legal conclusion.
