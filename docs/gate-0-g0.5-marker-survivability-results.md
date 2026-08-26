# Gate 0 G0.5 marker-survivability results

## Outcome

The approved 30-second marker smoke passed both admitted Stage 2 route profiles. Each route encoded and strictly decoded 750 frames at 1080p25. Marker IDs 0 through 749 were recovered in presentation order with exact 40-tick spacing from PTS 0 through 29,960. Across 1,500 exercised frames there were no ambiguous cells, misidentified IDs, duplicates, collisions, missing IDs, unexpected IDs, or timestamp failures.

| Route profile | Frames | Marker deviations | Strict A/V decode | Audio presentation disposition |
|---|---:|---:|---|---|
| OpenH264 constrained-baseline 2 Mbit/s + AAC-LC 192 kbit/s in MP4 | 750/750 | 0 | pass / pass | 1,440,000 expected samples; 1,440,768 raw; 768-sample decoder tail exactly bound by packet/frame metadata |
| VP9 CRF 32 cpu-used 2 + Opus 128 kbit/s CBR in WebM | 750/750 | 0 | pass / pass | 1,440,000 expected and raw samples; zero raw tail |

The authoritative report is `g0.5-marker-survivability-report.json`, 357,168 bytes, SHA-256 `48611C1D670AEA59CA7192537B36237FE769B7F52BC074A5F9387B666FDEBFA9`. The machine-independent result is `eng/gate0/g0.5-marker-survivability-result-summary.json`. Both route outputs, decoded marker strips, untrimmed decoded PCM, probes, packet evidence, commands, and logs are retained in immutable group `Gate0.G05.MarkerSurvivability.20260826.Final`.

## Exact proof boundary

This was a proof re-encode through the two exact P2 route and quality-profile mappings. Every demuxer, decoder, encoder, muxer, filter chain, stream map, and one-thread constraint was explicit. The MP4 descriptor proved constrained-baseline H.264, yuv420p, AAC-LC stereo at 48 kHz, and 1080p25. The WebM descriptor proved VP9 profile 0, yuv420p, Opus stereo at 48 kHz, and 1080p25. Both video and audio streams passed strict complete decode.

The MP4 decoder emitted 1,440,768 raw samples per channel for a 1,440,000-sample presentation. The 768-sample tail is accepted only because the retained packet/frame evidence binds it exactly: 1,024 samples of initial skip metadata and a final 256-sample packet decoded as a 1,024-sample frame. The WebM decode emitted exactly 1,440,000 samples and recorded 312 skip plus 648 discard-padding samples. No proof-side or signal trimming occurred in either route.

## Superseded evidence

Three earlier attempts remain immutable rather than being erased:

1. The first stopped before media execution because the retained-artifact expectation adapter did not accept the manifest value shape.
2. The second stopped before media execution because the free-space preflight read a drive object instead of its `Free` byte value.
3. The third proved the MP4 marker path but exposed an obsolete exact raw-decoded-audio count and an absent optional WebM profile field.

Their exact report sizes, hashes, execution commits, reasons, and retained group IDs are bound in the tracked result summary. They are infrastructure history, not failed route evidence.

## Disposition and remaining gates

Marker survivability is complete for the two admitted profiles and no longer blocks the bounded pre-matrix smoke. The smoke remains blocked by the proof-only WPF no-media/control and measurement-adapter contract, complete independent R2 byte verification of the current corpus, the verified private second copy, and resource/capacity preflight.

This result is not long-form integrity, current ReelForge composition, UI responsiveness, playback, shipping-runtime, distribution, patent, or legal evidence. It does not authorize the full Stage 2 measured matrix.
