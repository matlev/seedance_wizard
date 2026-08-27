# Gate 0 G0.5 Stage 2 replacement pre-matrix smoke results

Status: three authoritative candidate passes; locally and durably retained; full Stage 2 awaits owner authorization

## Outcome

The one authorized replacement attempt for each candidate passed the complete frozen contract:

| Candidate | Attempt wall time | Encode wall time | Peak working set | Mean normalized CPU | Output |
| --- | ---: | ---: | ---: | ---: | ---: |
| MP4 OpenH264/AAC, one thread | 31.592 s | 6.826 s | 108.3 MiB | 10.472% | 1,733,392 bytes |
| WebM VP9/Opus, one thread | 64.789 s | 42.690 s | 363.6 MiB | 7.319% | 708,459 bytes |
| WebM VP9/Opus, eight threads | 64.092 s | 41.967 s | 372.1 MiB | 8.308% | 708,459 bytes |

Each candidate was attempted exactly once. There were no blocked rows, reruns, route substitutions, harness-definition failures, or active-process contamination.

All three outputs matched their exact approved descriptors, decoded 750 frames, placed every frame at ticks `0` through `29,960`, ended presentation at tick `30,000`, passed immediate terminal EOF, and passed every-frame visual identity. Maximum frame mean absolute error was `1.559903` for MP4 and `1.394567` for both WebM candidates against the maximum `18` threshold.

## Frozen structured-audio result

V4 is a descriptor-scoped amendment; it does not reinterpret the V3 F1 contract or its 12 historical controls.

| Binding | Identity |
| --- | --- |
| V3 contract | `Gate0.G05.LossyAudioOracle.V3.Frozen.20260826`; SHA-256 `119A4C179BFA010F3202DBF6AA368E42EDE5FD0FC23EF2781AA9C7F63540CBE4` |
| V4 amendment | `Gate0.G05.LossyAudioOracle.V4.ReferenceRelativeTypical.20260827`; SHA-256 `21ECAFCD94F71E58AA43955079EF9959C135DB12530D015E8380CFD09B5E9FBC` |
| Frozen closure | `Gate0.G05.LossyAudioOracle.V4.ReferenceRelativeTypical.Frozen.20260827`; SHA-256 `E2EFFD683FFE21BE902D77D7564F81C550F555C0989871C5D98B2DBE580D4CB2` |
| Controls | Five structured controls passed; all 12 V3 hashes and dispositions remained unchanged; report SHA-256 `2CAEE1C652F292BBF7E9DB6E1DAA0DD7C5E68788C3E5D74F63997DC3775F2AF6` |

The amendment and controls were frozen, retained locally, and independently retrieved and byte-verified in R2 before any replacement route output was evaluated.

All candidates passed exact structure, content-normalized timing, correlation, NRMSE, SNR, full-channel RMS ratio, reference-relative active windows, DC, tone identity, clipping, channel identity, and onset timing:

| Candidate | Minimum correlation | Maximum NRMSE | Minimum SNR | Full-channel RMS ratio | Relative-window RMS ratio |
| --- | ---: | ---: | ---: | ---: | ---: |
| MP4 OpenH264/AAC, one thread | 0.999855 | 0.017123 | 35.328 dB | 0.997219–1.000642 | 0.986564–1.017365 |
| WebM VP9/Opus, one thread | 0.999925 | 0.012309 | 38.196 dB | 0.997799–1.002504 | 0.990707–1.013576 |
| WebM VP9/Opus, eight threads | 0.999925 | 0.012309 | 38.196 dB | 0.997799–1.002504 | 0.990707–1.013576 |

| Candidate | Maximum absolute DC | Expected-tone amplitude ratio | Lowest active output-window RMS | Near-clipped samples | Raw decoder tail |
| --- | ---: | ---: | ---: | ---: | ---: |
| MP4 OpenH264/AAC, one thread | 0.00000915 | 0.992084–1.001422 | 0.009552 | 0 | 768 samples |
| WebM VP9/Opus, one thread | 0.00007680 | 0.997760–1.015375 | 0.009538 | 0 | 0 samples |
| WebM VP9/Opus, eight threads | 0.00007680 | 0.997760–1.015375 | 0.009538 | 0 | 0 samples |

The frozen limits are correlation at least `0.995`, NRMSE at most `0.10`, SNR at least `20 dB`, both aggregate and relative-window RMS ratios within `0.90–1.10`, absolute DC at most `0.005`, expected-tone amplitude ratio within `0.90–1.10`, and zero unexpected near-clipped samples. The independently authored reference's lowest active-window RMS was `0.009627`; output activity never classified a window. The typical descriptor declares no forbidden-tone set or silence window, so those two conditional metrics are explicitly not applicable rather than silently omitted. Exact structure, stereo identity, frequency identity, sample count, and onset checks passed.

The MP4 presentation was exactly 1,440,000 samples per channel; its 1,440,768-sample raw decode retained the already approved, metadata-bound 768-sample tail. Both WebM decodes contained exactly 1,440,000 samples per channel.

For every candidate the four declared onsets preserved both sides of the comparison:

| Track | Expected sample/time | Observed sample/time | Signed error |
| --- | ---: | ---: | ---: |
| a0 | 0 / 0.000 s | 0 / 0.000 s | 0 samples / 0.000 s |
| a1 | 12,000 / 0.250 s | 12,000 / 0.250 s | 0 samples / 0.000 s |
| a2 | 24,000 / 0.500 s | 24,000 / 0.500 s | 0 samples / 0.000 s |
| a3 | 36,000 / 0.750 s | 36,000 / 0.750 s | 0 samples / 0.000 s |

Every encode root exited, every observed descendant exited, no orphan remained, and no partial output required cleanup.

## Evidence and repository impact

Immutable group `Gate0.G05.Stage2PreMatrixSmoke.20260827T073620244Z.F2D80483` contains 59 files and 43,400,910 logical bytes under `proofs/g05-stage2-smoke-20260827T073321856Z-527fa736`. Its primary evidence JSON is 154,694 bytes, SHA-256 `ACF9E3DF3CD27051F7A88E0475AF9B0D72CEC8BE8ACE46E72357694162529F0A`.

The complete current corpus is:

- source-inventory SHA-256 `AE088727059D3686930C4422237A02E6691580D93C85E3862489C8F65FCDD0A0`;
- durable-ledger SHA-256 `AF9B368D44FDE3EFD2C45E2D847CB989D38E52066607A0D3E61384588D23C113`;
- 4,101 logical artifacts and 1,121,540,509 logical bytes;
- 2,032 distinct content-addressed R2 objects and 651,715,907 distinct bytes; and
- complete independent R2 retrieval and byte verification.

The local source manifest intentionally continues to classify itself as a local working copy with R2 state pending; the separate durable ledger is the authoritative second-copy record. This is the established two-manifest contract, not a retention inconsistency.

The live smoke and R2 unit changed only the two generated manifests: 1,802 additions and 13 deletions in their serialized diffs. It preserved all 33 earlier retention groups and appended one 59-file group. No historical group, result document, product source file, or test was reinterpreted or removed. The durable ledger added receipts and refreshed top-level source/status fields through the established atomic workflow.

Across the complete V4/controls/replacement-smoke unit, measured from parent commit `c4c3f98` before containment work and including this owner packet, the tracked impact is:

| Category | Additions | Deletions |
| --- | ---: | ---: |
| Human-readable docs/config | 370 | 1 |
| Proof harness and tests | 374 | 47 |
| Compact machine-readable contracts/summaries | 498 | 0 |
| Generated legacy manifests | 2,675 | 13 |

The application, Core, Application, platform, and product-persistence projects were unchanged.

## Full 2A projection and containment decision

The frozen contract contains 18 cells: three workloads, two resolutions, and three route/thread candidates. One warm-up plus five measured attempts per cell produces 18 warm-ups, 90 measured attempts, and 108 total attempts: 36 conditional MP4 and 72 WebM.

Applying the observed end-to-end candidate times to those exact route counts gives a central smoke-shape estimate of 96.3 minutes. The planning range is 75–125 minutes for media plus complete oracle work because 720p/baseline rows are cheaper while stress rows are not yet measured. Local retention validation and independent R2 receipt work are additional operational time. This is a bounded planning estimate, not a performance guarantee.

The existing flat retention shape cannot fit the approved 805,306,368-byte ceiling. The three observed per-candidate closures project to about 1,014,340,500 bytes (967 MiB) across 108 attempts before shared snapshots and summaries. Fixed decoded PCM and probe evidence alone make silent compliance implausible.

Recommended adjustment, requiring owner approval before 2A:

1. Seal the two current legacy manifests at the hashes above.
2. Adopt the already proposed future-only root index with one immutable shard per 2A cell; do not migrate or rewrite legacy groups.
3. Retain compact command, resource, timing, oracle-summary, hash, and disposition evidence for every warm-up and measured attempt.
4. Retain one complete media/PCM/probe closure per cell, plus complete closure for every failed, blocked, cleanup-failed, or byte/semantic-divergent attempt. A repeated attempt may reference the earlier exact SHA-256 only after its own full validation has completed.
5. Enforce the existing 768 MiB ceiling against actual retained bytes and stop before exceeding it. Do not raise the ceiling automatically.

The review budget for that design is explicit:

| Item | Count/cap | Projected bytes |
| --- | ---: | ---: |
| Shared contract/runtime/result closure | 20 files | 15,224,785 |
| One complete closure per cell | 18 × 13 files | 169,056,750 measured shape; 338,113,500 conservative 2× bound |
| Compact passing repetitions | 90 × 256 KiB maximum | 23,592,960 |
| Cell manifest shards | 18 × 64 KiB / 300 lines maximum | 1,179,648 / 5,400 lines |
| Root index | 20 entries; 128 KiB / 400 lines maximum | 131,072 |
| Result/run overhead reserve | one bounded reserve | 1,048,576 |

The recommended layout therefore projects 210,233,791–379,290,541 incremental local/logical-R2 bytes (200.5–361.7 MiB), 364 logical artifact receipts, and no more than the same byte range in new distinct R2 objects because the budget credits no deduplication. Added to the current corpus, local logical bytes project to 1,331,774,300–1,500,831,050 and distinct R2 bytes to at most 861,949,698–1,031,006,448. The conservative case leaves 426,015,827 bytes of the 2A ceiling for exceptional full closures.

Every failed, blocked, cleanup-failed, or byte/semantic-divergent attempt still retains a complete closure. If those closures consume the remaining headroom, execution stops and returns for owner review; it may not discard the exceptional evidence or raise the ceiling. This keeps every performance observation and exceptional result auditable while avoiding five redundant copies of large, already-validated bytes per cell. The full-matrix harness and shard writer remain unimplemented and unauthorized.

## Route/thread recommendation

Advance all three candidates into 2A if the owner authorizes it:

- MP4 OpenH264/AAC at one thread, the only approved MP4 policy;
- WebM VP9/Opus at one thread; and
- WebM VP9/Opus at eight threads on the 16-logical-processor reference host.

The single WebM observation showed eight threads only 1.7% faster while using slightly more memory. That is insufficient to select a policy, so the approved one-versus-half-logical comparison remains meaningful. The later 2B policy selection must continue using the frozen median/tie rule rather than this smoke sample.

## Boundaries and next owner decision

This is exact P2 runtime-route evidence, not current ReelForge product-composition or UI evidence. No P2 path, proof cache, artifact logic, WPF adapter, proof contract, or Windows-specific concept entered production behavior or portable project meaning. No editing feature, persistence contract, render command, runtime setting, distribution decision, or public hardware promise changed.

Owner authorization is now requested for:

1. the exact 18-cell / 108-attempt 2A route matrix above; and
2. the future-only shard/index plus compact-repeat retention adjustment.

Full 2A, WPF 2B media scenarios, concurrency comparison, long-form sizing/execution, new playback installations, and shipping/legal conclusions remain blocked until explicitly authorized.
