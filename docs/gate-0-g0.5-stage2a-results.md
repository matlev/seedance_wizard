# Gate 0 G0.5 Stage 2A owner packet

Date: 2026-08-27

Status: completed with failures; owner disposition required

## Outcome

The exact Stage 2A run `g05-stage2a-20260827T170732499Z-df30a192` completed all 18 scheduled cells and retained all 108 authoritative attempt records. It produced 36 passed attempts, two semantically divergent warm-ups, and 70 blocked attempts after both routes were suspended fail-fast. Including the two earlier non-authoritative harness-defect starts, Stage 2A has executed 115 physical media attempts.

All 18 immutable cell shards are committed to the future evidence index. Independent post-run retrieval verified every one of the 289 indexed local/R2 artifacts, totaling 78,538,843 bytes, against root SHA-256 `C6D3CD9E7B0FC62E199E6FAD0A7D0FBAB6AFE1BBA6C8EFD4EAE427BBB79E30EA`.

## Passed measured cells

Each passed cell contains one excluded warm-up and five measured passes. Median measurements are:

| Cell | Wall time | Peak working set | Normalized CPU | Output bytes | Max frame MAE |
| --- | ---: | ---: | ---: | ---: | ---: |
| baseline 720p MP4, one thread | 1,714 ms | 44,351,488 | 9.77% | 1,590,688 | 1.5042 |
| baseline 720p WebM, one thread | 26,526 ms | 181,026,816 | 6.44% | 653,859 | 1.1679 |
| baseline 720p WebM, eight threads | 24,758 ms | 183,324,672 | 6.55% | 653,859 | 1.1679 |
| typical 720p WebM, one thread | 25,286 ms | 210,341,888 | 7.10% | 659,944 | 1.6320 |
| typical 720p WebM, eight threads | 25,591 ms | 217,329,664 | 7.40% | 659,944 | 1.6320 |
| typical 720p MP4, one thread | 2,989 ms | 70,770,688 | 10.86% | 1,516,766 | 1.9912 |

These are exact P2 runtime-route measurements, not current ReelForge composition, UI, WPF, cache, preview, or customer-hardware evidence.

## Failure and suspension

The first stress WebM/eight-thread warm-up at global ordinal 37 and the first stress MP4/one-thread warm-up at ordinal 43 both passed descriptor, encode, probe, video timing, visual identity, audio timing, onset timing, process cleanup, and orphan checks. Both failed the frozen structured-audio quality gate with the same 25 `active-rms`/`active-window-silence` findings. Full-region correlation remained at least 0.99994 with approximately 39.6–51.2 dB SNR and RMS ratios near 1.0.

Because both routes encountered the same stress-audio gate, both were suspended. The remaining stress 720p cell and all nine 1080p cells were retained as blocked without media execution. Therefore:

- baseline and typical 720p evidence passed for MP4/one-thread and WebM/one- and eight-thread routes;
- the stress workload is unresolved rather than proven route-incompatible; and
- blocked 1080p rows are not evidence of a 1080p capability failure.

The retained exception text also exposed a narrow proof-harness diagnostic typo: `throw'Audio timing or quality oracle failed.'` was parsed as a command named `throwAudio...`. The structured audio record proves that the quality gate was already false, so the typo did not create that result or prevent suspension, but it obscured the intended friendly exception message.

## Retention accounting

- logical bytes added: 78,536,427;
- distinct R2 bytes added: 42,032,462;
- ending indexed logical bytes: 78,538,843;
- Stage 2A ceiling: 805,306,368;
- remaining headroom: 726,767,525;
- post-run independent local/R2 verification: 289 of 289 artifacts passed;
- both earlier local harness-defect closures remain untouched and excluded.

## Owner decisions requested

1. Accept this Stage 2A execution as a truthful `completed-with-failures` result: 108 authoritative records consisting of 36 passed, two semantically divergent, and 70 blocked attempts.
2. Authorize a bounded no-media diagnostic unit to compare the frozen structured-audio oracle, stress workload truth, and retained decoded outputs; correct only the `throw` tokenization defect and add its regression. This unit must not widen thresholds, re-encode media, reclassify a retained row, or infer a route defect without returning its evidence for owner review.
3. After that diagnostic, decide whether an owner-approved bounded stress/1080 rerun is required for G0.5 completion or whether the blocked rows receive another explicit Gate 0 disposition.

Stage 2B, concurrency comparison, long-form proof, product integration, shipping-runtime selection, distribution, licensing, patent, and legal work remain unauthorized. This packet stops for owner review.
