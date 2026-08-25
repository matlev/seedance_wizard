# Gate 0 G0.5 calibration plan

Status: authorized bounded calibration design; no performance contract has been approved.

This plan turns the approved G0.5 methodology into two deliberately separate proof stages. The first stage builds and runs a short calibration harness. Its results select a tractable measured matrix and expose bad assumptions. The second stage performs the representative, concurrent, application-host, and 60–120-minute evidence only after an owner checkpoint.

G0.5 remains proof and product-contract work. It does not select a shipping runtime, establish a public hardware minimum, approve redistribution, change project meaning, or make Windows-only measurement mechanisms portable requirements.

## Outcomes

G0.5 must leave behind:

1. reproducible evidence for the exact P2 delivery routes on the owner's reference machine;
2. an owner-approved thread and concurrent-job policy proposal based on observed results rather than FFmpeg option names;
3. separately identified route, composition, preview, cache, cancellation, and UI-responsiveness evidence;
4. a timestamp-clean long-form output suitable for the remaining independent-playback checks, or an explicit blocked result;
5. proposed numeric 1.0 performance thresholds and their measurement method; and
6. actionable implementation prerequisites without changing product architecture during the proof.

## Fixed boundaries

- The proof runtime is `P2.BtbnLgplShared.WindowsX64.20260820`, an LGPLv3-path third-party proof candidate only.
- Commands select every material encoder, decoder, muxer, demuxer, filter, and stream explicitly. Component presence is not semantic or performance proof.
- H.264/AAC MP4 uses the approved `libopenh264` plus native `aac` route. VP9/Opus WebM uses the approved `libvpx-vp9` plus `libopus` route.
- The current application command builder's `libx264` routes cannot stand in for P2 results. Product-path evidence and proof-route evidence remain distinct until a runtime-profile mapping is designed in a later implementation slice.
- FFmpeg `-threads` is a codec request, not a process-wide CPU ceiling. Codec, filter, and complex-filter thread settings are recorded separately and actual resource use is measured.
- No result establishes distribution legality, patent conclusions, public hardware support, macOS behavior, or a final bundled runtime.
- No new software installation is part of this plan. Any later installation requires a separate owner approval checkpoint stating purpose and intended lifespan.
- No 60–120-minute run, process concurrency above one, stress workload, or application-host/manual acceptance begins without the second-stage owner checkpoint.
- The only approved local proof corpus is the repository-sibling `ReelForge.Gate0Artifacts` root. It remains one local copy, is not synced or backed up, and cannot support unattended hosted CI.

## Stage 1: bounded calibration

Stage 1 is intentionally short and sequential. It may create the harness, run its structural tests, and execute the following calibration without a separate heavy-run approval:

| Dimension | Calibration scope |
| --- | --- |
| Runtime | Exact retained P2 archive after manifest and hash-closure validation. |
| Routes | H.264/AAC MP4 and VP9/Opus WebM, audio included. |
| Workload | One video layer plus one audio track. |
| Resolutions | 1280x720 and 1920x1080. |
| Cadence | 25 fps. |
| Authored duration | 8 seconds per output. |
| Source | Retained repository-owned F1 PPM and PCM primitives through explicit `image2`/`ppm` and `s16le`/`pcm_s16le` inputs. Both inputs use `-stream_loop -1`; filters reset the initial timestamps; output `-t` fixes the eight-second boundary. |
| Thread policies | `auto`, `one`, `half-logical`, and `full-logical`; resolved numeric values and logical-processor observations are recorded at run time. |
| Repetition | One warm-up plus two measured repetitions per route/resolution/thread policy. Calibration statistics are exploratory, not acceptance statistics. |
| Concurrency | One FFmpeg process. |
| Cancellation | One 1080p probe per route, targeting 120 seconds but canceled only after confirmed active progress. It has a 15-second hard wall limit and a live-monitored 512 MiB output ceiling; request-to-exit, termination path, and partial-output disposition are recorded. |

The calibration harness must use `-progress` for structured media progress and a monotonic host clock for elapsed time. Human-oriented FFmpeg stats are retained only as diagnostics. It samples the FFmpeg process at a modest interval and records:

- wall-clock duration and real-time factor;
- raw monotonic sample timestamps, process CPU-time deltas, and normalized observed CPU use;
- working-set, peak-working-set, and private-memory observations;
- process I/O operation and byte deltas where Windows exposes them;
- process count and child-process observations;
- structured progress records;
- output and evidence byte counts;
- cancellation request, exit, escalation, and cleanup timestamps; and
- GPU measurement as `not-applicable`, `unavailable`, or measured evidence—never an assumed zero.

CPU normalization uses `100 * process CPU-time delta / (sample wall-time delta * observed logical-processor count)`, producing a percentage of total host logical capacity. Raw samples are authoritative. Summaries report peak and mean normalized CPU, peak working set, peak private memory, total I/O operations and bytes, and mean/peak interval byte throughput with units. Root-process metrics are authoritative for these P2 routes; any discovered descendants are recorded separately, and process-tree aggregates must state their membership and sampling limitations.

Every output receives fresh independent inspection and strict complete-decode verification. For each eight-second output, the oracle requires 200 frames at 25 fps, timestamps from tick 0 through tick 7960 in a 1/1000 comparison time base, and the exact repeating F1 identity cycle across every decoded frame—not merely the endpoints. It also verifies 384,000 audio samples per channel, the left/right tone identities within the approved compressed-audio tolerances, container and stream identities, artifact size, and SHA-256.

Stage 1 also records machine, OS, CPU, memory, GPU/driver, storage target, runtime, command, component, and harness identities. Machine-specific absolute paths stay out of committed contracts and manifests.

Before execution, `Test-Gate0ArtifactRetention.ps1` must revalidate the complete retained corpus. Each attempt receives a collision-resistant proof-run ID and stages its closed, hashed evidence payload under the project-controlled repository sibling `ReelForge.Gate0Staging/<proof-run-id>`, outside both the repository and retained root. Staging is not temporary and survives a crash or failed append. `Add-Gate0RetainedProof.ps1` appends every attempted run under a unique immutable group and destination, then updates the tracked and retained manifests atomically. Results are not reported until the append and a fresh retention validation pass. The retained group includes exact commands, progress and sampling streams, stdout/stderr, environment/runtime/fixture identities, output media, independent oracle evidence, result summaries, and contract/harness snapshots. A staging payload becomes eligible for explicit cleanup only after its retained group passes revalidation; the harness does not delete it automatically.

### Calibration is not acceptance

Two repetitions, sequential execution, and a headless scheduling heartbeat cannot establish the final p50/p95, concurrent-job policy, long-form integrity, cache behavior, preview responsiveness, or actual UI-dispatch latency. Stage 1 may only:

- reject thread policies or route settings that are invalid, unstable, or clearly dominated;
- estimate the cost and disk footprint of the measured stage;
- reveal whether the approved OpenH264 route has a quality, rate-control, or timestamp blocker;
- validate measurement and cancellation mechanics; and
- propose the smallest defensible Stage 2 matrix.

## Evidence semantics

Metrics that answer different questions must not be collapsed:

| Evidence | What it can establish | What it cannot establish |
| --- | --- | --- |
| Route microbenchmark | P2 encoder-route cost and output integrity. | Current application render/preview behavior. |
| Host scheduling heartbeat | Whether the measurement coordinator was starved. | WPF dispatcher latency or UI usability. |
| Application-host probe | Dispatcher delay, preview startup, cancellation/publication behavior. | Portable runtime behavior by itself. |
| Cache probe | Cold/warm application behavior and retained bytes. | Encoder-only performance. |
| Long-form route proof | Timestamp, identity, resource, cancellation, and output integrity over duration. | Independent-player compatibility until those checks run. |

The existing `ExternalProcessRunner` cancellation behavior is relevant product evidence, but experimental metrics will be gathered outside that seam. G0.5 does not add telemetry or experimental performance contracts to production services.

## Stage 2: measured proof

After Stage 1, the primary thread returns an owner decision packet containing:

- all calibration results and failed/blocked rows;
- proposed numeric thresholds, each tied to a specific metric and workload;
- the recommended codec/filter thread policies and concurrent-job limits;
- the exact measured matrix, expected run count, estimated wall time, and estimated disk use;
- the proposed application-host/manual UI-responsiveness method;
- the proposed 60–120-minute authored truth and integrity oracles;
- whether the long MP4 can also complete independent playback; and
- any required installation, if one becomes necessary, with purpose and intended lifespan.

The owner must approve or amend these before Stage 2 execution. The measured stage is expected to cover:

- one warm-up and at least three measured repetitions where p50/p95 are claimed;
- baseline, typical, and stress shapes at the approved 720p/1080p boundaries;
- cold and warm application-cache behavior;
- concurrent-job candidates selected from 1, 2, and 4;
- actual WPF dispatcher-delay and preview-startup evidence on the reference system;
- higher-resolution source/proxy or draft-preview behavior without promising full-resolution real-time 4K editing;
- cancellation, process-tree exit, temporary-output cleanup, and cache behavior under load; and
- one 60–120-minute mostly sequential integrity project, including first/final identity, timestamp continuity, resource consumption, disk/cache behavior, responsiveness, and cancellation methodology.

The measured H.264/AAC artifact must be created directly through the exact reviewed route. A prior corrupt output may not be repaired or silently re-encoded to manufacture playback evidence.

## Acceptance of the calibration work unit

The bounded calibration unit is complete only when:

- the machine-readable contract and harness agree on every matrix dimension;
- runtime and fixture provenance fail closed before execution;
- the approved retained corpus passes preflight and the complete run closure is immutably appended and revalidated before reporting;
- every executed command records exact components and thread settings;
- measurement sampling does not parse human-oriented stats as an API;
- calibration output receives strict inspect/decode/timestamp checks;
- cancellation distinguishes clean exit, escalation, and forced termination;
- a cancellation probe begins only after active progress; an already-complete or never-active command is `not-exercised` or blocked, never a cancellation pass;
- partial outputs are validated or removed and never reported as passes;
- tests cover matrix expansion, path safety, evidence schema, statistics labels, and failure preservation;
- an independent reviewer finds no architectural-boundary or oracle weakening; and
- results are committed without machine-specific paths or large generated media.

## Known blockers and implementation prerequisites

- The current product render builder still requests `libx264`; P2 route measurements do not make that product path compatible.
- No global media-process budget or product thread policy exists yet.
- A headless proof cannot establish actual UI-dispatch latency. A bounded Windows application-host/manual probe is required while portable project meaning remains platform-neutral.
- The existing F6 construction proves duration only and is not sufficient by itself for the long-form integrity or independent-playback contract.
- OpenH264's no-frame-skip/rate-control warning remains an explicit quality-policy finding.
- The retained proof corpus still lacks its required second private copy. Hosted CI must not depend on machine-local artifacts.
- GPU counters are environment-dependent and do not gate the software P2 route when unavailable.

Blocked evidence is a valid Gate 0 outcome. G0.5 must not weaken timing, output integrity, portability, reproducibility, quality, or licensing boundaries merely to produce a passing matrix.
