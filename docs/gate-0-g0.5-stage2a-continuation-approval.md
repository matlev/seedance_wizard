# Gate 0 G0.5 Stage 2A continuation approval

Date: 2026-08-27

Status: owner approved with prerequisites; no media may execute until every activation gate passes

## Accepted conclusions

The owner accepts the no-media diagnostic classification **A: oracle/descriptor self-inconsistency**. The stress reference reproduces all 25 findings against its own frozen V3 contract. Neither retained MP4 nor WebM route is shown defective. The original two warm-ups remain immutable `semantically-divergent` V3 evidence.

The exact stress-only V5 overlay is approved for `stress-4v8a-30s`. It replaces only the two V3 absolute active-level checks with reference-derived 960-sample window ratios in the inclusive range 0.90–1.10. Every other V3 check, V4, other descriptor, and other threshold remains unchanged. V5 controls and implementation must be frozen before the retained outputs are read under V5.

The future-only V2 evidence segment is approved and must bind immutable V1 root SHA-256 `C6D3CD9E7B0FC62E199E6FAD0A7D0FBAB6AFE1BBA6C8EFD4EAE427BBB79E30EA`. V1 may not change. V2 is initially bounded to two infrastructure shards and 12 continuation-cell shards, the existing per-shard/root/compact-record limits, and the existing global 805,306,368-byte Stage 2A ceiling.

## Corrected pre-continuation accounting

The verified accounting is:

- 108 authoritative accepted-run schedule records;
- 38 accepted-run media executions: 36 passed and two semantically divergent;
- 70 blocked authoritative records with no media execution;
- seven earlier non-authoritative harness-defect media executions; and
- 45 cumulative actual media executions to date.

The previously reported 115 figure is 108 authoritative records plus seven earlier non-authoritative executions. It is not an actual-media-execution count.

## Activation gates

Before any continuation media process starts, all of the following must pass:

1. V2 implementation and no-media validation, including V1 immutability, predecessor-chain failure cases, atomic append behavior, local/R2 byte retrieval, global-ceiling enforcement, and metadata sanitation.
2. Diagnostic retention and independent local/R2 byte verification through V2.
3. V5 identity, 0.95 acceptance, localized low-level 960-sample dropout rejection, 0.75 and 1.25 rejection, unchanged V3/V4 control hashes and dispositions, and frozen V5 contract/implementation identities.
4. No-media reevaluation of the exact retained MP4 and WebM PCM under V5. Either failure returns to the owner before media.
5. A committed, hash-bound, counterbalanced 72-execution schedule that cannot be reordered after results are observed.
6. Fresh corpus, resource, free-space, retention-headroom, and zero-active-media-process preflights.

## Authorized continuation

After every activation gate passes, exactly 72 new media executions are authorized:

- stress 720p WebM/eight-thread: one fresh warm-up and five measured attempts;
- stress 720p MP4/one-thread: one fresh warm-up and five measured attempts;
- stress 720p WebM/one-thread: one warm-up and five measured attempts; and
- nine 1080p cells: one warm-up and five measured attempts per cell.

Previously passed baseline and typical cells must not rerun. Warm-ups are excluded from performance statistics but must pass the complete semantic, cleanup, and orphan contract. Every execution receives a new proof identity linked to the accepted Stage 2A run. Genuine deterministic semantic or integrity failures suspend the route fail-fast. Retention remains incremental and fail-closed; no evidence may be discarded and no ceiling may be raised.

## Architecture preflight

```text
Feature/outcome: proof-only V2 containment, stress-only V5 semantics, and bounded Stage 2A continuation
Existing owners touched: eng/gate0 proof harness, evidence containment, and Gate 0 documentation/tests
Proposed responsibility and extension point: version the existing proof-evidence owner; extend the existing structured-audio oracle by descriptor overlay
Dependency and public-contract impact: none in product assemblies or public product contracts
Persistence/format/compatibility impact: new internal proof-evidence V2 schema only; V1 stays immutable
Parallel-workflow or boundary risk: no product media path; V2 is the explicitly approved successor to the same Gate 0 evidence workflow
Verification: no-media containment/oracle controls, exact R2 retrieval, focused/full tests, resource preflight, measured continuation, independent review
ADR or architecture-debt decision: not required; bounded internal Gate 0 proof infrastructure, not a production architecture or distribution decision
```

Stage 2B, concurrency comparison, long-form work, new player installation, new media-family investigation, product integration, shipping-runtime selection, and distribution/licensing/patent/legal conclusions remain unauthorized.
