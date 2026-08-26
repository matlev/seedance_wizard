# Gate 0 G0.5 Stage 1 owner decisions

Status: approved with guardrails; bounded correction and Stage 2 design authorized; Stage 2 execution blocked

Approved: 2026-08-25

Authority: [G0.5 Stage 1 calibration results](gate-0-g0.5-calibration-results.md) and [Gate 0 media capability charter](gate-0-media-capability-charter.md)

## Authorized preparation work

The owner approves all six G0.5 Stage 1 decisions. This authorizes three bounded preparation units:

1. correct and consistently apply the lossy-audio oracle;
2. produce the versioned Stage 2 workload/oracle contract for owner review; and
3. document the proof-only P2 Windows WPF measurement-adapter boundary.

It does not currently authorize media execution. The pre-matrix smoke cases become conditionally authorized only after their preparation prerequisites are satisfied. The full Stage 2 2A–2C matrix, concurrent proof, application-host measurement, and long-form run remain blocked behind their additional gates. Production runtime-profile mapping and production behavior changes remain outside this authorization. The preparation unit returns three linked review packets: the [lossy-audio oracle proposal](gate-0-g0.5-lossy-audio-oracle-proposal.md), [Stage 2 workload proposal](gate-0-g0.5-stage2-workload-proposal.md), and [P2 Windows WPF measurement-adapter boundary](gate-0-g0.5-wpf-measurement-adapter-boundary.md).

## Lossy-audio oracle

The bounded correction unit is authorized. It retains exact checks for:

- sample rate and channel count/layout;
- decoded duration and the approved codec-delay/sample-count envelope;
- start and end timing;
- channel identity and no channel swapping;
- expected tone dominance; and
- absence of unintended clipping or silence.

The unit researches a reproducible lossy-quality oracle using an evidence-supported combination of normalized per-channel cross-correlation, RMS or normalized RMS error, SNR, and expected-frequency dominance/leakage. Any bounded alignment used only for quality measurement must stay within an independently approved codec priming/delay envelope. It cannot conceal timing drift, which remains a separate exact/tolerance-bounded verdict.

Thresholds must be proposed from approved source and codec evidence before either route is rerun. A threshold may not be chosen merely to admit the previously observed AAC output.

The corrected oracle applies to AAC and Opus. Existing WebM evidence remains valid for structure, timing, video, complete decode, and Stage 1 performance, but its audio-quality verdict must be revalidated consistently.

## Stage 2 thread candidates

Approved candidates are:

- MP4 `one`, only if the corrected audio oracle admits the route;
- WebM `one`; and
- WebM `half-logical`.

`auto` and `full-logical` are removed from Stage 2. This does not ban other settings in capability-qualified enhanced local runtimes.

The Stage 2 contract must resolve every label to exact decoder, ordinary-filter, complex-filter, and encoder thread controls. It may not imply that FFmpeg's codec thread request is a process-wide CPU cap.

## Versioned workload/oracle contract

The bounded contract-design unit is authorized and returns for owner review before execution. Every workload defines exact:

- retained source identities;
- timing and overlap;
- canvas;
- crop, scale, position, and opacity;
- audio placement, gain, and mixing;
- filter graph;
- cache and proxy state;
- output timing;
- frame and audio oracle; and
- artifact and retention expectations.

The typical case uses a real two-layer composition, such as split-screen or picture-in-picture, with four distinguishable audio sources. The stress case uses four simultaneous video layers and eight audio tracks with bounded approved transforms and mixing.

Every row identifies itself as runtime-route evidence or product-composition evidence. Neither classification can be silently promoted into the other.

## P2 Windows WPF measurement boundary

The owner approves the narrower proof-only P2 Windows WPF measurement adapter. It must:

- use the real WPF dispatcher;
- remain proof/test-specific and absent from shipped product functionality;
- avoid Core and project-persistence changes;
- avoid current-production-render-path claims;
- never use the current `libx264` product command path as P2 evidence; and
- report the exact host and runtime boundary exercised.

Gate 0 does not introduce a production runtime-profile mapping or ADR merely to obtain measurements. After that architecture is deliberately implemented, an appropriate smaller proof must be repeated through the real production mapping.

## Stage 2 design target

The owner approves 2A–2C as a design target with these bounds:

- one heavyweight media job by default;
- a bounded concurrency-two comparison;
- no concurrency four without separate approval;
- a 60-minute primary long-form proof;
- deferred 120-minute proof;
- the proposed 2A attempt counts;
- a 0.75 GiB 2A retention ceiling; and
- a mandatory long-form sizing dry run.

Lightweight inspection, metadata, and cache operations may use a separately bounded resource policy; they are not counted as heavyweight media-job concurrency merely because they are asynchronous.

Before the full 2A matrix, one 1080p typical smoke case runs for each admitted route/thread candidate. Each smoke case must pass the complete oracle and retention pipeline. A deterministic route-level integrity failure stops repeated matrix rows rather than multiplying a known failure.

These smoke cases are a separate, bounded pre-matrix execution stage. They are not part of the full repeated 2A matrix and have their own prerequisites below.

The 60-minute run may execute only after its non-media sizing dry run returns an approved wall-time range, output and temporary-storage estimate, and free-space floor with safety margin.

## Provisional reference-machine thresholds

The owner approves these provisional thresholds:

| Metric | Approved threshold |
| --- | --- |
| UI command return | At most 100 ms. |
| WPF dispatcher delay | p95 at most 50 ms; p99 at most 100 ms; no observation above 250 ms. |
| Reduced-quality preview | First usable frame within 2 seconds. |
| Cancellation acknowledgement | At most 100 ms. |
| Media process-tree exit | At most 2 seconds. |
| Stress-shape working set | Combined ReelForge and child-process working set below 4 GiB. |
| Temporary/cache storage | Bounded, measured, and cleaned according to the workload contract. |
| Integrity | Zero unexpected frame, timestamp, stream, marker, or cleanup deviations. |

Initial 1.0 cancellation requests graceful shutdown first, waits for a short bounded grace period, then force-terminates the complete process tree when necessary. Total process exit remains within two seconds; unvalidated partial output is removed; no orphan remains; and application, cache, and project state return to consistency. Evidence records whether termination was graceful or forced.

Forced termination may count as successful initial 1.0 user cancellation only when every responsiveness, cleanup, and integrity requirement passes. It remains a reported fallback and does not prove graceful encoder shutdown.

Passing dispatcher measurements do not overrule human evidence of mouse or whole-desktop freezing. That observation is a separate system-responsiveness failure and requires appropriate Windows performance evidence.

## Execution gates

No media execution is authorized in the current repository state.

### Pre-matrix smoke gate

The bounded 1080p typical smoke cases become authorized without expanding into the repeated matrix only after all of the following are complete:

1. the corrected audio oracle and proposed thresholds are approved and applied consistently to AAC and Opus;
2. the versioned workload/oracle contract returns for owner review and approval;
3. the proof-only P2 Windows WPF adapter boundary is documented;
4. a verified second private copy of the current Gate 0 artifact corpus is complete; and
5. the applicable smoke resource, retention-capacity, and free-space preflight passes.

Every attempted smoke run is retained. A failed or blocked smoke is a valid result and does not authorize repeated route rows.

### Full Stage 2 gate

The full 2A matrix remains blocked until every admitted route/thread candidate also passes its pre-matrix smoke case and complete oracle/retention pipeline. The 2B application-host and concurrency work remains blocked until the proof adapter and its exact workload/claim boundary pass contract review. The 2C long-form run remains blocked until the full long-form sizing dry run produces and receives approval for its wall-time range, output and temporary-storage estimate, and free-space floor with safety margin.

A blocked or failed gate returns as evidence; the implementation may not weaken portability, quality, timing, reproducibility, cleanup, licensing, or architecture to force progress.

No Stage 1 result selects a shipping runtime, establishes public legal approval, changes public hardware support, or alters production application behavior.
