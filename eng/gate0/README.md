# Gate 0 fixture primitives

This directory defines deterministic, authored input primitives for the Gate 0 media proof matrix. It is deliberately not a media-proof harness and does not contain generated media. The generator writes raw PPM, RGBA, PCM, recipe, and timestamp-truth files to a caller-supplied temporary directory; it never writes generated assets into the repository.

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
| F3 | Alpha and Unicode text | RGBA overlay plus Unicode text specification; blocked pending a pinned licensed test font |
| F4 | WAV/FLAC audio variants | 32/44.1/48 kHz PCM tone primitives, known peak/phase, and expected lossless facts |
| F5 | Silent/no-audio media | Opaque PPM source plus authored digital-silence PCM and explicit no-audio truths |
| F6 | Long-form integrity recipe | Compact generated 60-minute repeat recipe and source cadence facts |
| F7 | VFR, non-zero PTS, and presentation identity | Per-frame PPM IDs and non-zero 1/90000 presentation timestamps |
| F8 | Explicit video/audio stream selection | Distinguishable PPM and PCM primitives for two video and two audio streams |

F3 must remain blocked until the project supplies a separately licensed Unicode-capable test font, including its provenance, license identifier, file hash, and pinned relative path. Do not substitute a system font.

## Later proof requirements

When a later G0.3 proof runner packages or transforms these primitives, it must record the exact command and explicitly select every concrete decoder, encoder, muxer, demuxer, and filter named by the fixture plan. Auto-selection is not evidence. A successful process exit alone is not evidence either: the later runner must inspect, decode, and compare the output against `expected-truths.json`.

`P2.BtbnLgplShared.WindowsX64.20260820` is third-party LGPLv3-path proof infrastructure only. These fixture files do not select a shipping runtime, delivery contract, or legal conclusion.
