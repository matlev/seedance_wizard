# Gate 0 G0.5 Stage 2A execution approval

Date: 2026-08-27

Status: owner-approved exact Stage 2A runtime-route execution; no later Stage 2 work authorized

This record preserves the owner's execution approval after review of the canonical Gate 0 current-status reconciliation, the exact G0.5 Stage 2A matrix, and the future-only evidence-containment design. Earlier decision and result records remain immutable descriptions of their respective moments in time.

## Authorized matrix

The authorization is limited to the frozen 18-cell Stage 2A matrix:

- workloads: Baseline 1V1A, Typical 2V4A, and Stress 4V8A;
- resolutions: 720p and 1080p;
- route/thread candidates: MP4/OpenH264/AAC at one requested thread, WebM/VP9/Opus at one requested thread, and WebM/VP9/Opus at half-logical (eight requested threads on the 16-logical-processor reference host);
- one warm-up and five measured attempts per cell;
- 18 warm-ups, 90 measured attempts, and 108 total attempts.

No other route, codec, resolution, workload, thread policy, concurrency level, fixture family, effect, or performance dimension is authorized.

The committed schedule must remain counterbalanced and hash-bound before execution. Attempts may not be reordered after performance results are observed.

## Validation and retention rules

Every attempt, including warm-ups, receives the complete approved semantic validation. Warm-ups are excluded from measured statistics. Each measured attempt retains its individual observations; five-observation summaries report median, range, and appropriate variability without unsupported percentile precision.

For every cell, the first successfully completed measured attempt supplies the ordinary complete closure. If that attempt is exceptional, its complete closure is retained and the next successful measured attempt may supply the ordinary closure. Every failed, blocked, cleanup-failed, orphan-producing, byte-divergent, semantically divergent, structurally divergent, or otherwise exceptional attempt retains its complete closure.

Repeated passing attempts may use compact records only after complete independent validation and only with the exact contract-required SHA-256 relationship to their referenced complete closure. Compact records retain the approved command, timing, process, resource, oracle, output, decoded/probe identity, disposition, local-retention, and R2 receipt identities.

Future evidence uses the approved immutable shard/root-index design under `eng/gate0/evidence/`. The existing legacy manifests are sealed at:

- source/local inventory SHA-256 `AE088727059D3686930C4422237A02E6691580D93C85E3862489C8F65FCDD0A0`;
- durable-ledger SHA-256 `AF9B368D44FDE3EFD2C45E2D847CB989D38E52066607A0D3E61384588D23C113`;
- 4,101 logical artifacts and 1,121,540,509 logical bytes.

Before media execution, the containment writer, root-index validator, structural and negative tests, no-media local/R2 dry run, sealed-hash checks, immutable-order checks, and path/credential sanitization checks must all pass. A failed append may not partially mutate the root index.

The Stage 2A ceiling remains 805,306,368 bytes (768 MiB), enforced incrementally. Planning limits remain 18 cell shards, no more than 64 KiB and 300 lines per shard, no more than 128 KiB and 400 lines for the root index, 90 compact passing-repetition records no larger than 256 KiB each, one ordinary complete closure per cell, complete closure for every exceptional attempt, and one bounded run/result overhead reserve. Evidence may not be discarded or truncated to stay under the ceiling.

The full resource/corpus preflight runs before the matrix. Each cell also records the approved lightweight cleanliness checks. A deterministic structural, semantic, or byte-integrity failure suspends the affected route rather than spending the matrix on redundant reproductions. Slowness alone remains evidence.

## Evidence boundary

Stage 2A is exact P2 runtime-route evidence only. It does not prove current ReelForge composition or rendering behavior, WPF responsiveness, preview, caching, product cancellation, public hardware requirements, a selected shipping runtime, redistribution suitability, or legal or patent clearance.

## Completion and stop condition

After Stage 2A completes or blocks, return one owner packet with the executed cells and attempts, dispositions and exceptions, individual and per-cell measurements, route/thread and resource comparisons, quality and output-size evidence, retained-byte and R2 growth, shard/index growth, headroom, exact legacy/root identities, architecture-boundary review, build/test status, and a recommendation for route/thread policies entering Stage 2B. Then stop for owner approval.

This approval does not authorize Stage 2B WPF media scenarios, concurrency comparison, long-form execution or sizing, VLC or another player installation, new playback dependencies, new codec/container/filter investigations, new fixture families, Pro feature spikes, product runtime/import/render/cache changes, shipping-runtime selection, release engineering, distribution decisions, or legal conclusions.
