# Gate 0 G0.5 Stage 1 calibration results

Status: Stage 1 completed with failures; retained and revalidated; owner checkpoint required before Stage 2.

## Decision summary

The bounded Stage 1 run produced usable calibration evidence without changing product code or installing software.

- The VP9/Opus WebM route passed all 24 warm-up and measured attempts, including strict inspection, complete decode, every-frame identity, exact timestamp, and audio checks.
- The OpenH264/AAC MP4 route encoded successfully in all 24 attempts but failed the committed audio waveform oracle in every attempt. It remains blocked; no threshold was relaxed and no output was substituted.
- The cancellation probes produced bounded forced-process-tree-termination evidence for both routes. They do not prove graceful cancellation or application UX.
- The results narrow useful Stage 2 thread candidates, but two repetitions and a headless single-process workload are not acceptance statistics.

The MP4 failure exposed a contract-definition error. G0.5 reused G0.4's value `3072` as a maximum per-sample amplitude delta. In G0.4, `3072` was defined and executed as a lossy decoded-sample-count tolerance for codec delay. It was never approved as a lossy waveform-quality threshold. Raising it to the observed result would fit the evidence after the fact and is not acceptable. A bounded owner-approved audio-oracle correction is required before MP4 can re-enter performance proof.

## Retained evidence

| Field | Value |
| --- | --- |
| Contract | `Gate0.G05.Calibration.V1` |
| Proof profile | `P2.BtbnLgplShared.WindowsX64.20260820` |
| Proof run | `g05-calibration-20260826T002913397Z-189786ac` |
| Run interval | 2026-08-25 17:29:13 to 17:41:52 America/Vancouver |
| Result | `completed-with-failures` |
| Retained group | `Gate0.G05.Calibration.20260826T004152697Z.FDABDAA2` |
| Evidence path | `proofs/g05-calibration-20260826T002913397Z-189786ac/g0.5-calibration-evidence.json` |
| Evidence size | 497,033 bytes |
| Evidence SHA-256 | `A95979C4211BD2CB844C44A2F1A1F55B9A7341EAC8C81A5B356AF7121468AC15` |
| Retained group closure | 254 files; 52,137,064 bytes |
| Complete interim corpus | 12 groups; 3,007 files; 517,763,820 bytes |
| Second private copy | Incomplete |

The immutable retained evidence is authoritative for run observations. Its embedded retention record intentionally remains `pending-append`, because it was hashed before append and is never rewritten. The tracked manifest group together with `eng/gate0/g0.5-calibration-result-summary.json` establishes the later `retained-and-revalidated` outcome. The manifest contains no machine-specific absolute paths and the complete corpus passed post-append validation.

Four earlier failed or partial harness attempts remain retained under their immutable group identities. They preserve, in order, a reparse-ancestor preflight rejection, an unset PowerShell exit-code defect, a command-argument construction defect, and an incomplete oracle/muxer-option run. None is reclassified as capability evidence.

## Executed matrix and route verdicts

Stage 1 executed 48 route attempts: two routes, two resolutions, four thread policies, one warm-up, and two measured repetitions. Concurrency remained one. All 48 FFmpeg processes exited with code zero.

| Route | Attempts | Complete-oracle passes | Failures | Bounded finding |
| --- | ---: | ---: | ---: | --- |
| OpenH264/AAC MP4 | 24 | 0 | 24 | Every output reached the audio waveform assertion after the preceding output assertions. Peak absolute sample delta was 9,015 at sample 553, channel 0; the unapproved amplitude threshold was 3,072. Route blocked pending oracle correction. |
| VP9/Opus WebM | 24 | 24 | 0 | Exact 384,000 decoded samples per channel; maximum audio sample delta 1,859; maximum observed per-frame visual MAE 4.72191; artifacts were 175,050 bytes at 720p and 189,109 bytes at 1080p. |

The MP4 failures do not establish that AAC quality is unacceptable. They establish that the current peak-sample comparator lacks an approved semantic basis for lossy audio. They also do not erase the successful process, container, stream, timestamp, frame-identity, and complete-decode work that preceded the throwing audio assertion; the route nevertheless has no complete pass.

## Exploratory measured results

Each cell below contains only two measured repetitions. Wall time is FFmpeg process time, excluding the independent oracle. CPU percentage is normalized across the 16 observed logical processors. `Unavailable` means the process completed inside the 500 ms sampling interval and produced only one resource sample; it must not be reported as zero.

### OpenH264/AAC MP4 — blocked route

| Resolution | Threads | Wall mean (range) | Mean / peak CPU | Peak working / private memory |
| --- | --- | ---: | ---: | ---: |
| 720p | auto | 456.5 ms (454–459) | Unavailable | Unavailable |
| 720p | one | 418.0 ms (400–436) | Unavailable | Unavailable |
| 720p | half-logical (8) | 425.0 ms (421–429) | Unavailable | Unavailable |
| 720p | full-logical (16) | 442.5 ms (428–457) | Unavailable | Unavailable |
| 1080p | auto | 909.0 ms (867–951) | 8.39% / 8.83% | 64.8 / 73.4 MiB |
| 1080p | one | 871.5 ms (827–916) | 9.60% / 9.78% | 59.1 / 66.8 MiB |
| 1080p | half-logical (8) | 873.0 ms (856–890) | 10.53% / 10.62% | 62.1 / 70.2 MiB |
| 1080p | full-logical (16) | 932.5 ms (918–947) | 13.06% / 16.59% | 64.9 / 73.5 MiB |

If the route is admitted by a corrected audio contract, `one` is the recommended Stage 2 policy. It was fastest at 720p, essentially tied for fastest at 1080p, and had the lowest observed 1080p memory. `full-logical` is rejected from the next matrix because it added resource use without a speed benefit. The 720p resource sampler must use a shorter interval or a longer representative workload.

### VP9/Opus WebM — passed route

| Resolution | Threads | Wall mean (range) | Mean / peak CPU | Peak working / private memory |
| --- | --- | ---: | ---: | ---: |
| 720p | auto | 6,892.5 ms (6,833–6,952) | 6.68% / 9.18% | 177.3 / 178.1 MiB |
| 720p | one | 6,845.5 ms (6,811–6,880) | 6.46% / 7.15% | 172.6 / 172.6 MiB |
| 720p | half-logical (8) | 7,139.5 ms (6,973–7,306) | 6.55% / 7.91% | 175.1 / 175.4 MiB |
| 720p | full-logical (16) | 7,345.0 ms (7,280–7,410) | 6.51% / 8.68% | 177.2 / 178.0 MiB |
| 1080p | auto | 11,781.0 ms (11,768–11,794) | 6.70% / 9.44% | 315.4 / 319.8 MiB |
| 1080p | one | 11,747.0 ms (11,506–11,988) | 6.44% / 7.47% | 309.7 / 313.3 MiB |
| 1080p | half-logical (8) | 11,005.0 ms (10,773–11,237) | 6.60% / 8.12% | 312.7 / 316.6 MiB |
| 1080p | full-logical (16) | 11,047.0 ms (10,825–11,269) | 6.67% / 10.72% | 315.4 / 319.8 MiB |

`one` is fastest and lowest-memory at 720p. `half-logical` is about 6% faster than `one` at 1080p, while `full-logical` offers no meaningful gain over `half-logical`. Stage 2 should compare only `one` and `half-logical` for this route, then choose by actual application responsiveness and workload rather than microbenchmark speed alone. `auto` and `full-logical` are rejected from the next matrix.

## Cancellation evidence

| Route | Active progress | Request | Request-to-exit | Partial output | Disposition |
| --- | ---: | ---: | ---: | ---: | --- |
| OpenH264/AAC MP4 | 764 ms | 1,266 ms | 17 ms | 524,336 bytes | Removed unvalidated |
| VP9/Opus WebM | 785 ms | 1,316 ms | 27 ms | 0 bytes | Removed unvalidated |

Both probes used forced process-tree termination after confirmed active progress and recorded exit code `-1`. This proves a fast bounded cleanup mechanism in the proof harness. It does not prove graceful encoder shutdown, a valid partial output, production cancellation behavior, dispatcher responsiveness, or acceptable user-facing semantics.

## Proposed Stage 2 scope

Stage 2 remains unauthorized. The following is the smallest useful measured program after the audio-oracle decision.

### 2A — representative route and thread proof

- Routes: VP9/Opus WebM; OpenH264/AAC MP4 only if an approved corrected audio oracle admits it.
- Resolutions: 720p and 1080p.
- Workload targets: baseline 1 video/1 audio, typical 2 simultaneous video/4 audio, and stress 4 simultaneous video/8 audio.
- Duration: 30 seconds per output.
- Threads: WebM `one` and `half-logical`; MP4 `one` only.
- Repetitions: one warm-up plus five measured per cell.
- Concurrency: one process only.

This is 72 WebM attempts and, conditionally, 36 MP4 attempts. The 24 eight-second Stage 1 WebM attempts consumed 222.694 seconds of FFmpeg time. Scaling only duration and attempt count gives a 41.8-minute baseline-cost floor for 2A. The 70–90-minute planning envelope applies an explicit, deliberately conservative 1.7–2.2 composition-cost multiplier; that multiplier is not measured, and oracle plus retention time remains additional. The MP4 portion should be well under ten minutes of FFmpeg time if admitted and if composition cost remains similar. Scaling the 52.1 MB Stage 1 group by attempt count and duration gives about 0.27 GiB before composition overhead; a 0.75 GiB local retention ceiling is proposed. The harness must enforce the ceiling and return a blocked result instead of exceeding it.

Layer counts alone are not executable workloads. Before 2A can be authorized, a versioned Stage 2 contract must define the exact retained source identities, timeline placement and overlap, transforms, audio gain/mixing, filter graph, proxy/cache state, output timing, and independent oracle for every shape. It must also identify whether each row is an Infrastructure route proof or a product-composition proof; one may not be relabeled as the other.

### 2B — application host and concurrency

- Use the actual Windows WPF dispatcher; do not substitute a headless heartbeat for UI evidence.
- Exercise the 1080p typical workload at each selected route policy with concurrency one and two. Do not test four concurrent jobs in this gate unless two passes with material headroom and the owner separately authorizes expansion.
- Use one warm-up plus five measured scenarios per route/concurrency cell: 12 scenarios for one passing route or 24 for two.
- Record dispatcher delay, preview startup, command-return delay, process count, cancellation-to-exit, cleanup, cache bytes, and aggregate resource use.
- Keep portable project meaning outside the Windows measurement adapter. No WPF or Windows runtime identity enters Core or persisted creative intent.

The current product command builder still requests `libx264`, so it cannot stand in for the approved P2 OpenH264 route. Before 2B can be authorized, the owner must approve an architecture preflight choosing one boundary:

1. a P2-only Windows measurement adapter hosted beside the real WPF dispatcher, which may prove Windows scheduling/orchestration behavior but is not current product render-path evidence; or
2. a reviewed production runtime-profile mapping and ADR, which is a separate architecture/implementation unit and may then support product-path evidence.

The recommended Gate 0 choice is the first, narrower adapter. The second should occur only when the desktop implementation roadmap deliberately introduces runtime-profile mapping. Every result must state which boundary ran and which claims it supports.

### 2C — long-form integrity and playback handoff

- Execute one 60-minute, 25 fps, 1080p mostly sequential project per admitted delivery route; 120 minutes is deferred unless the 60-minute result exposes duration-dependent uncertainty or the owner asks for the upper-bound run.
- Author deterministic start, periodic, middle, and final visual identities and stereo audio markers from repository-owned raw primitives. The oracle must stream its checks; it must not retain a full raw decode.
- Require 90,000 video frames; tick-zero start; 40-tick cadence in the 1/1000 comparison time base; tick 3,599,960 final frame; tick 3,600,000 presentation end; complete decode; first/middle/final identity; periodic marker continuity; exact selected streams; and explicit lossy-audio timing/semantic checks approved through the audio decision.
- Record peak resources, cache and temporary-disk growth, UI responsiveness, cancellation methodology, first/final identity, and timestamp continuity together.
- Reuse the approved independent-playback procedure. Record exact OS/player/browser versions for new long outputs before any default delivery contract is finalized.

A pre-execution dry run must turn the long-form recipe into exact expected artifact bytes, wall-time range, and free-space floor. It may not execute the 60-minute workload during that sizing step.

## Proposed metric definitions for owner approval

These are candidate 1.0 reference-machine thresholds, not conclusions from Stage 1:

| Metric | Proposed threshold and method |
| --- | --- |
| UI command return | 100 ms maximum from an already-running app for render/export/cancel commands, measured at the WPF command boundary. |
| Dispatcher delay during one typical job | p95 at or below 50 ms, p99 at or below 100 ms, and no single observed delay above 250 ms, using a 16 ms dispatcher heartbeat. |
| Preview startup during background work | first usable reduced-quality 720p/1080p frame within 2 seconds for the approved typical fixture. |
| Cancellation | UI acknowledges within 100 ms; process tree exits within 2 seconds; partial output is either separately validated or removed; forced kill is a reported fallback, not a clean pass. |
| Process concurrency | one by default. Two is admitted only if every UI, integrity, memory, and cleanup threshold passes and it materially improves queue throughput. |
| Memory | all ReelForge and child media processes remain below 4 GiB peak working set on the 32 GiB reference system during the approved stress shape. |
| Temporary/cache disk | stay within the predeclared workload budget; report final, temporary peak, and retained cache separately; no unbounded growth after cancellation or completion. |
| Integrity | zero unexpected frame, timestamp, stream, marker, or cleanup deviations. Integrity is fail-closed and has no percentile allowance. |

The owner may amend these after product/PM review. They intentionally do not define a public hardware floor or promise full-resolution real-time 4K editing.

## Owner decisions required

1. **Lossy audio oracle:** authorize a bounded correction unit that retains exact sample rate, channel count, decoded length/timing, channel identity, and tone dominance, then researches a reproducible lossy-quality measure such as RMS error/SNR plus correlation. It must propose thresholds from approved source/codec evidence; it may not simply raise peak delta to 9,015. This is the recommended decision.
2. **Thread candidates:** approve MP4 `one` if the route is admitted, WebM `one` plus `half-logical`, and removal of `auto`/`full-logical` from Stage 2.
3. **Workload contract:** authorize a bounded design unit for the versioned, deterministic Stage 2 workload/oracle contract described under 2A. The contract returns for review before execution.
4. **Application-host boundary:** approve the recommended P2-only WPF measurement adapter, or instead require a separate production runtime-profile mapping/ADR before application-host evidence. The adapter would not be current product render-path proof.
5. **Measured matrix:** approve, amend, or reject 2A–2C as a design target, including the concurrency-one default, bounded concurrency-two comparison, 60-minute primary long-form duration, attempt counts, and disk ceiling. Execution still waits for the versioned contract and application-host boundary.
6. **Product thresholds:** approve or amend the proposed WPF responsiveness, preview startup, cancellation, memory, and integrity thresholds. In particular, decide whether forced termination is acceptable only as a fallback or may count as the initial 1.0 cancellation mechanism.

No installation was used or requested for Stage 1. No installation is proposed for the bounded audio-oracle correction or Stage 2 design work. If later independent playback requires a player installation, work stops for the separate purpose-and-lifespan approval checkpoint required by the owner.

Stage 2, application-host changes, concurrent proof, and long-form execution remain blocked until these decisions are recorded.
