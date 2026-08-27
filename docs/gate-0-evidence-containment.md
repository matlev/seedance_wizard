# Gate 0 generated-evidence containment

Status: owner-approved containment direction; future-only shard/index design proposed before full Stage 2 authorization

Authority: owner and Project Manager containment approval dated 2026-08-27

## Purpose and boundary

Gate 0 must preserve exact artifact identity and auditability without indefinitely rewriting two monolithic generated manifests. Human-readable result summaries remain the primary review surface; immutable evidence bytes remain private, content-addressed R2 objects.

This is a bounded repository-evidence design. It does not authorize a database, service, dashboard, backend, product UI, general artifact platform, hosted-CI credential, or migration of proof data into ReelForge production behavior.

## Legacy closure

The existing `eng/gate0/artifact-retention-manifest.json` and `eng/gate0/artifact-manifest.json` remain the authoritative legacy corpus inventories through the currently authorized oracle-control and replacement-smoke unit. The append machinery may extend them only through its existing verified append/receipt rules; it may not reinterpret, delete, reorder, or replace historical logical records.

After the replacement-smoke evidence and its R2 receipts are complete, the owner packet will report their final hashes and propose sealing both files as immutable legacy Gate 0 records. A future shard writer must reference those exact final hashes rather than copy or transform their historical entries.

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

## Adoption decision

The full Stage 2 owner packet must recommend one of:

- adopt this future-only shard/index layout before measured execution;
- amend it with a smaller bounded schema; or
- explicitly accept continued legacy-manifest growth for a named, finite matrix.

No implementation begins merely because replacement smoke succeeds.
