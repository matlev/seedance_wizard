# Gate 0 G0.5 retained-audio results

## Outcome

The frozen lossy-audio oracle was applied to all 48 already-retained G0.5 Stage 1 media artifacts without re-encoding. All 48 rows passed. Both approved routes advance to Stage 2 marker-survivability qualification:

- P2 OpenH264 video with native AAC-LC audio in MP4: 24 of 24 passed.
- P2 VP9 video with Opus audio in WebM: 24 of 24 passed.

This result supersedes the Stage 1 route disposition produced by the old maximum-absolute-delta audio check. It does not rewrite the immutable Stage 1 evidence: those AAC attempts remain historical `completed-with-failures` rows, and this follow-up proves that their retained bytes pass the owner-approved frozen oracle.

The authoritative report is `g0.5-retained-audio-oracle-report.json`, 681,304 bytes, SHA-256 `387BEF57A359C43479ECBF1B85C20DAB927378F7B8AFD1C58FF7CDEC87F56A0A`. The tracked machine-independent result is `eng/gate0/g0.5-retained-audio-result-summary.json`.

## Independence and execution boundaries

The threshold and metric were frozen only after all 12 predeclared synthetic controls retained their intended four accepted and eight rejected dispositions. Route outputs were not inspected to select the formula or thresholds. The evaluator then read the retained media from the exact Stage 1 group, verified manifest sizes and hashes, explicitly selected the demuxer, decoder, and audio stream, decoded to retained PCM, and ran the frozen oracle. It did not encode or modify a route output.

The exact presentation endpoint is based on the sum of decoder-emitted audio-frame sample counts. Every row produced exactly 384,000 samples per channel with zero endpoint error. Container timestamp spans remain recorded as diagnostics; a WebM 1/1000 time base is too coarse to serve as the exact sample endpoint.

## Aggregate regression-floor evidence

| Route | Minimum correlation | Maximum NRMSE | Minimum SNR | RMS-ratio range | Expected-tone amplitude-ratio range | Priming / discard maximum |
|---|---:|---:|---:|---:|---:|---:|
| MP4 H.264/AAC-LC | 0.999764 | 0.021780 | 33.239 dB | 0.998178–0.998232 | 1.000385–1.027993 | 1024 / 0 samples |
| WebM VP9/Opus | 0.999739 | 0.023261 | 32.667 dB | 0.997590–0.997955 | 0.993456–0.993596 | 312 / 648 samples |

These figures are a deterministic technical regression floor for the approved synthetic two-tone source. They are not a perceptual-quality conclusion for speech or music.

## Superseded harness evidence

Five earlier immutable attempts are retained rather than erased:

1. Two pre-execution infrastructure failures exposed an uninitialized PowerShell exit-code state.
2. One run exposed an automatic-variable collision that removed process arguments.
3. One run exposed incorrect parsing of combined ffprobe packet-and-frame output.
4. One run passed all quality criteria but exposed use of a coarse WebM timestamp span as an exact sample endpoint.

The tracked summary binds every attempt to its execution commit and report hash. Only the `final3` attempt is authoritative for route admission.

## Disposition and remaining gate

Both routes are admitted only to the next bounded proof stage. Marker-atlas generation, durable retention, route-and-quality-profile survivability, the proof-only WPF control contract and adapter, complete private-copy verification, and resource/free-space preflight remain prerequisites before the pre-matrix smoke. No full Stage 2 measured matrix is authorized before the smoke results return for review.

This work makes no shipping-runtime, public-distribution, patent, legal, perceptual-transparency, or ReelForge product-composition claim.
