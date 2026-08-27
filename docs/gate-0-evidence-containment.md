# Gate 0 generated-evidence containment

Status: owner-approved containment direction; measured future-only shard/index and compact-repeat adjustment proposed for owner approval

Authority: owner and Project Manager containment approval dated 2026-08-27

## Purpose and boundary

Gate 0 must preserve exact artifact identity and auditability without indefinitely rewriting two monolithic generated manifests. Human-readable result summaries remain the primary review surface; immutable evidence bytes remain private, content-addressed R2 objects.

This is a bounded repository-evidence design. It does not authorize a database, service, dashboard, backend, product UI, general artifact platform, hosted-CI credential, or migration of proof data into ReelForge production behavior.

## Legacy closure

The existing `eng/gate0/artifact-retention-manifest.json` and `eng/gate0/artifact-manifest.json` remain the authoritative legacy corpus inventories through the currently authorized oracle-control and replacement-smoke unit. The append machinery may extend them only through its existing verified append/receipt rules; it may not reinterpret, delete, reorder, or replace historical logical records.

The replacement-smoke evidence and R2 receipts are complete. The proposed legacy seal points are source manifest SHA-256 `AE088727059D3686930C4422237A02E6691580D93C85E3862489C8F65FCDD0A0` and durable-ledger SHA-256 `AF9B368D44FDE3EFD2C45E2D847CB989D38E52066607A0D3E61384588D23C113`. They cover 4,101 logical artifacts and 1,121,540,509 logical bytes. A future shard writer must reference those exact hashes rather than copy or transform historical entries. The seal is proposed, not effective, until the owner approves the full Stage 2 containment disposition.

The 256-row `eng/gate0/g0.4-input-proof-contract.json` likewise remains an immutable expanded proof contract. Future input decisions refer to its hash and the 173-row candidate guaranteed subset; they do not regenerate or broaden it.

## Proposed future layout

No root index or writer is implemented by this planning unit. If the owner authorizes full Stage 2 and this design, future measured evidence uses:

```text
eng/gate0/evidence/
  root-index.json
  stage2/
    <proof-run-id>.manifest.json
```

`root-index.json` is a small tracked index containing:

- schema and index identity;
- exact paths and SHA-256 values for the two sealed legacy manifests;
- one entry per future proof run;
- proof-run and evidence-group IDs;
- authoritative, superseded, failed, or blocked disposition;
- contract, runtime-profile, and shard SHA-256 identities;
- logical and distinct artifact counts/bytes;
- local and R2 retention state; and
- no per-artifact array.

Each immutable run shard contains only the complete records for one proof run or evidence group:

- logical artifact ID and canonical relative filename;
- byte size and SHA-256;
- purpose and provenance;
- producer/runtime and proof-contract identities;
- applicable license references;
- content-addressed R2 object key; and
- independent retrieval receipt and disposition.

A new run adds one shard and one root-index entry. It does not rewrite earlier shards or the sealed legacy manifests. Superseded and failed runs remain indexed and immutable.

## Validation and CI

The future root validator must fail closed on:

- changed legacy hashes;
- missing, duplicate, reordered, or mutated run identities;
- a shard whose bytes do not match its indexed hash;
- path traversal, rooted paths, backslashes, or reparse-point crossings;
- a logical artifact without an exact size/SHA-256/object-key binding;
- a claimed complete R2 disposition without an independent retrieval receipt; or
- credentials, endpoint URLs, signed queries, prompts, user media, or machine-local absolute paths in tracked metadata.

Normal CI may validate the tracked index and shards without credentials or network access. Generated manifests remain reviewable but are marked `linguist-generated`; diffs are not disabled.

## Growth projection and execution gate

The replacement-smoke owner packet must measure rather than guess:

1. current legacy corpus logical/distinct counts and bytes;
2. oracle-control increment;
3. replacement-smoke increment;
4. per-candidate retained file categories, output bytes, and wall time;
5. exact planned Stage 2 cells from the already approved workload contract;
6. projected local/R2 bytes and logical receipts for only those cells;
7. expected shard count, shard bytes/lines, and root-index delta; and
8. the resulting total compared with the existing Stage 2 retention ceiling.

The projection may use a range only where measured output size varies. It must keep raw outputs, scratch peaks, retained evidence, content-addressed distinct bytes, and tracked metadata growth separate. The 42-file discovery group is an observed reference, not an automatic multiplier for every future cell.

If the projection exceeds the approved retention ceiling or makes the proposed shard/index review surface materially larger than reported, execution returns for owner review. It does not silently increase the ceiling or add a new storage system.

The replacement smoke measured 59 files and 43,400,910 logical bytes for three candidates. Applying only the observed candidate-specific closure sizes to the exact 108-attempt 2A contract projects 1,014,340,500 bytes before shared snapshots and summaries. That exceeds the 805,306,368-byte ceiling.

The recommended bounded adjustment is one immutable shard per 2A cell and compact retention for repeated attempts. Every attempt retains commands, resource samples, timing/oracle summaries, output size/hash, and disposition. Each cell retains one complete media/PCM/probe closure; every failed, blocked, cleanup-failed, or byte/semantic-divergent attempt also retains its complete closure. A repeated passing attempt may reference a prior exact SHA-256 only after completing its own validation.

The bounded planning budget is 18 shards capped at 64 KiB/300 lines each, a root index capped at 128 KiB/400 lines, 90 compact passing-repetition records capped at 256 KiB each, the measured 15,224,785-byte shared closure, and one complete 13-file closure per cell. The measured complete-closure projection is 169,056,750 bytes; a conservative 2× workload/output allowance makes it 338,113,500 bytes. Including compact records, shards, index, and a 1 MiB run/result reserve produces a 210,233,791–379,290,541-byte increment and 364 logical receipts without crediting R2 deduplication. The conservative case leaves 426,015,827 bytes of the 2A ceiling for exceptional full closures.

Actual retained bytes remain fail-closed against the existing ceiling. If exceptional closures exhaust the headroom, execution returns for owner review rather than discarding evidence or raising the ceiling.

## Adoption decision requested

The replacement-smoke owner packet recommends:

- seal the two legacy manifests at the exact hashes above;
- adopt the future-only shard/index layout before measured execution; and
- use the compact-repeat retention rule above rather than raising the ceiling.

No implementation begins merely because replacement smoke succeeds.
