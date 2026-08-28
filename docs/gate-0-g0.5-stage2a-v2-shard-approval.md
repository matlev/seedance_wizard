# Gate 0 G0.5 Stage 2A V2 shard approval

Date: 2026-08-27

Status: owner approved compact V2 shard recovery and one replacement cell with exact prerequisites

## Approved serialization contract

The owner approves deterministic compact JSON for new V2 shard manifests only. Projected/preflight and final retained shard bytes must use one canonical serializer with:

- pinned PowerShell Core/runtime identity;
- ordered object and property construction;
- fixed `ConvertTo-Json` depth and options;
- UTF-8 without a byte-order mark;
- exactly one trailing LF; and
- no additional whitespace outside JSON string values.

The serializer must be regression-proven byte-deterministic for the same real shard object. The real 23-artifact/six-attempt shape must fit the unchanged 65,536-byte cap and parse through the normal V2 reader. Existing schema, artifact, attempt, capacity, chain, journal, remote-verification, and fail-closed checks remain unchanged. An oversized compact candidate must fail before journal or remote work, and preflight/final serialization must not diverge.

The 300-line rule remains exact but is only a serialization guard for compact V2 shards, not a human-reviewability control. A deterministic on-demand command must validate and pretty-render a shard for inspection without creating or retaining an authoritative second representation.

Root indexes, append journals, attempt summaries, evidence payloads, historical shards, and human-facing documents remain unchanged. Exact writer, containment, approval, and continuation-authorization identities must be hash-bound before media resumes.

## Approved quarantine

The owner approves atomic quarantine of the current malformed-reference staging root only after independently confirming:

- 23 files;
- 15,768,093 bytes;
- inventory SHA-256 `C0C88D263CDD4BFA70F608F881BBD0D2617C69E70AF387FE1E80357DF91EF75D`;
- cell-summary SHA-256 `591C2815EA9375B7DADE5635FF11C8A6E35D325BA6DB2BF90F37405EEF6C11DC`;
- attempt-binding SHA-256 `EA7AE21C7166CC41300C90D50FF31ADF3EC73113E0487A14E422D74837F4379E`; and
- consumed-preflight SHA-256 `07FA9C0ABF96820FFA990A1AA484C85BF72089D0FE5C85A5BEA4F31C3372D123`.

The move must be same-volume, atomic, unique, completely hash-receipted, and byte-reverified after the move. A move or verification failure stops work without destroying the original. The quarantine may not be repaired, reused, appended, indexed, promoted to authoritative R2 evidence, included in route/performance statistics, or automatically deleted. It remains through final Stage 2A owner review unless cleanup is separately approved.

## Authorized replacement cell

After serialization and closure-reference corrections are implemented, independently reviewed, committed, hash-bound, and regression-tested—and after quarantine and a fresh full preflight pass—the owner authorizes exactly one replacement execution of `stress-720p-webm-eight`:

- one fresh warm-up;
- five fresh measured attempts;
- one fresh proof-run identity and trusted staging root;
- the frozen continuation workload, route, thread, oracle, resource, and retention contracts;
- exact corrected closure-reference identity; and
- the approved compact V2 shard serializer.

No prior media, PCM, probe, measurement, summary, or binding may be reused, reconstructed, or promoted. After this cell completes or blocks, execution stops for owner review. No other continuation cell is authorized.

## Required completion packet

The replacement-cell packet must report:

- all six semantic dispositions and warm-up/measured classifications;
- shard size, line count, SHA-256, and schema result;
- root-index append and local-retention results;
- independent R2 retrieval and byte verification;
- exact artifact counts and bytes;
- performance measurements;
- cleanup and orphan disposition;
- remaining global retention headroom;
- proof that V1 and earlier V2 records remained immutable; and
- a recommendation on the remaining continuation.

Before requesting the next decision, `docs/gate-0-current-status.md` must be reconciled with the packet, including its headline, all counts, the active state, the next exact owner decision, and agreement between its opening summary and evidence table.

## Attempt accounting

The owner approves the verified pre-replacement accounting:

- accepted V1 matrix: 108 authoritative schedule records, 38 actual media executions, and 70 blocked-without-media records;
- earlier non-authoritative Stage 2A starts: seven actual media executions;
- non-authoritative continuation activations: 12 actual media executions across two six-attempt runs;
- cumulative actual media executions before replacement: 57; and
- authoritative continuation progress before replacement: zero of 72.

A successful replacement cell would produce six of 72 authoritative continuation executions and raise cumulative actual media executions to 63. A later 72-attempt continuation completed without further retries would project to 129 cumulative actual media executions. Records that block before FFmpeg starts are never called physical media attempts. Every non-authoritative execution remains separate from the 72 scheduled authoritative continuation executions.

## Boundaries

This approval does not authorize the remaining 66 continuation attempts, Stage 2B, concurrency comparison, long-form sizing or execution, playback installations, new media investigations or fixtures, product integration, production runtime-profile adoption, shipping-runtime selection, distribution, licensing, patent, or legal conclusions.
