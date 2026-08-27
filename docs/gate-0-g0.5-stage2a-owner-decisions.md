# Gate 0 G0.5 Stage 2A owner decisions

Status: exact 2A matrix and future-only containment approved; media execution blocked on containment implementation and no-media proof

Authority: owner and Project Manager approval dated 2026-08-27

## Authorized matrix

Stage 2A is the existing frozen P2 runtime-route matrix only:

- workloads: Baseline 1V1A, Typical 2V4A, and Stress 4V8A;
- resolutions: 720p and 1080p;
- candidates: MP4/OpenH264/AAC at one requested thread, WebM/VP9/Opus at one requested thread, and WebM/VP9/Opus at half-logical/eight requested threads on the 16-logical-processor reference host; and
- repetitions: one fully validated warm-up plus five fully validated measured attempts per cell.

The resulting contract is 18 cells, 18 warm-ups, 90 measured attempts, and 108 total attempts: 36 MP4 and 72 WebM. No other route, codec, resolution, workload, thread policy, concurrency level, fixture family, effect, or performance dimension is authorized.

Stage 2A remains exact P2 runtime-route evidence. It makes no claim about current ReelForge rendering, project/composition behavior, WPF responsiveness, preview, product cache/cancellation, public hardware requirements, shipping-runtime selection, redistribution, licensing, patent, or legal suitability.

## Committed execution order

The complete schedule must be deterministic, committed, and hash-bound before media begins. Candidate order rotates by workload/resolution group:

1. MP4 one, WebM one, WebM eight;
2. WebM one, WebM eight, MP4 one;
3. WebM eight, MP4 one, WebM one;

and repeats that three-group rotation across the six workload/resolution groups. Attempts may not be reordered after observing results. Warm-ups receive the complete semantic oracle but are excluded from measured statistics. Every measured observation remains individually available; five-observation summaries report median, range, and bounded variability without unsupported percentile precision.

## Legacy seal and future evidence

The owner approves these legacy seal candidates:

- source/local inventory SHA-256 `AE088727059D3686930C4422237A02E6691580D93C85E3862489C8F65FCDD0A0`;
- durable-ledger SHA-256 `AF9B368D44FDE3EFD2C45E2D847CB989D38E52066607A0D3E61384588D23C113`;
- 4,101 logical artifacts; and
- 1,121,540,509 logical bytes.

The seal becomes effective only after both files/hashes, current R2 receipts/status, and a clean no-append working tree are independently verified; the root index is written and validated with those exact references; and the legacy append path is then disabled. After activation, legacy append, reorder, regeneration, rewrite, or reinterpretation fails closed. Future evidence references the sealed hashes and never copies or transforms the legacy artifact arrays.

The approved future-only layout is:

```text
eng/gate0/evidence/
  root-index.json
  stage2/
    <proof-run-id>.manifest.json
```

The root index is compact and has no per-artifact array. Each immutable shard owns one exact proof run/evidence group. A new run adds one shard and one root-index entry without changing prior shards or the sealed manifests. Authoritative, superseded, failed, and blocked evidence remains indexed.

## Required containment proof before media

Before Stage 2A media, implement and review the bounded writer and validator with structural and negative tests, then execute a no-media dry run that proves:

- shard creation and exact hash validation;
- atomic index append with no partial mutation on failure;
- local retention plus R2 upload and independent retrieval/byte verification;
- sealed legacy hash rejection;
- duplicate and reordered run-ID rejection;
- rooted path, traversal, backslash, reparse crossing, credential, signed-URL, endpoint, and machine-local path rejection; and
- the declared shard/index size and line caps.

If the writer, validator, atomicity, or caps fail, stop before media and return for owner review.

## Closure selection and repeat retention

Each cell retains one ordinary complete media/PCM/probe closure. The predeclared source is the first successfully completed measured attempt—not the warm-up, fastest, smallest, or otherwise favorable result. If that attempt is exceptional, retain its complete closure; the next successful measured attempt becomes the ordinary closure under the same rule.

A repeated passing attempt becomes compact only after independently completing the entire semantic validation. Its record retains cell/attempt/classification, contract/runtime/profile hashes, normalized command, execution order, timestamps, process/cleanup evidence, resource samples and summary, timing/oracle summaries, output size/hash, decoded/probe identity hashes, disposition, complete-closure reference, and local/R2 receipt identities.

Failed, blocked, cleanup-failed, orphan-producing, byte-divergent, semantically divergent, structurally divergent, or otherwise exceptional attempts retain complete closure. Exceptional evidence is never discarded to satisfy a budget.

## Retention and host gates

The 805,306,368-byte/768 MiB Stage 2A ceiling remains fixed. Planning caps are 18 shards at no more than 64 KiB/300 lines each, a root index at no more than 128 KiB/400 lines, 90 compact passing records at no more than 256 KiB each, one ordinary complete closure per cell, all exceptional closures, and one bounded result/run reserve.

Enforce caps and remaining headroom incrementally. Before every cell, reserve the expected ordinary closure, compact records, and reasonable exceptional closure. If actual retained bytes or metadata caps are exceeded, stop cleanly, preserve completed evidence, and return for owner review without truncation, deletion, or automatic ceiling growth.

Run the full approved resource/corpus preflight before the matrix. Before each cell, record zero unrelated FFmpeg/ffprobe processes, available memory, free staging/artifact space, current CPU as non-gating evidence, runtime identity, and applicable power/environment state. Unsatisfied resource conditions pause or block the matrix.

A deterministic integrity failure stops redundant repetitions for the affected route pending review. A merely slow result remains evidence unless an approved integrity/resource threshold fails.

## Completion and remaining block

After 2A completes or blocks, return one owner packet with exact execution/dispositions, every exception, individual and per-cell statistics, WebM thread comparison, route/resource/output/quality results, retention growth and headroom, shard/index identities, legacy seals, architecture boundaries, validation status, and a 2B route/thread recommendation.

This approval does not authorize WPF 2B media, concurrency comparison, long-form sizing/execution, playback installations, new codec/filter/fixture work, Pro spikes, product runtime/import/render/cache integration, release engineering, or legal conclusions.
