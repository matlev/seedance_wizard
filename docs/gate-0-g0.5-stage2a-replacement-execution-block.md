# Gate 0 G0.5 Stage 2A replacement-execution block

Date: 2026-08-27

Status: fail-closed after six local physical attempts; owner disposition required

## Result

The effective replacement run `g05-stage2a-20260827T162509165Z-9dc617af` passed the clean-tree full preflight and executed the first scheduled cell, `baseline-720p-mp4-one`, from global ordinal 1. Its warm-up and five measured attempts each completed with a local `passed` semantic disposition. The first immutable evidence append then stopped before retention with:

> Evidence shard attempt binding does not reference one exact retained artifact.

These six attempts are local diagnostic observations, not authoritative Stage 2A matrix evidence. No approved shard or root entry exists, and they may not enter route dispositions or matrix statistics without a new owner decision.

## Root cause

This is a proof-harness integration defect, not a media, runtime, capability, resource, or R2 result.

`New-G05Stage2AAttemptBinding` correctly creates source-root-relative paths such as `attempt-1/summary.json` for local compact-record validation. The runner serialized those paths unchanged. The containment writer correctly names retained artifacts under `future/stage2/<proofRunId>/...`, while the immutable shard validator correctly requires each attempt binding to match one exact retained artifact path and hash. The missing runner bridge therefore produced zero exact path matches.

Existing tests proved the source-relative semantic helper and destination-prefixed writer independently, but did not integrate the actual runner-produced binding through the retained destination namespace.

## Preserved closure and containment state

The blocked run remains local under the approved staging sibling:

- run ID: `g05-stage2a-20260827T162509165Z-9dc617af`;
- total closure: 26 files and 27,370,400 bytes;
- first-cell closure: 23 files and 10,090,400 bytes;
- `attempt-bindings.json` SHA-256: `1E341D9BB9D72F50742D3F6F0EA6533BE0FEBD5D63888B6E3EBA5CFC18D6FB13`;
- `cell-summary.json` SHA-256: `8558C0400FCBD664F41967DF824466180623418BC0F712915EC0DED53032F847`;
- complete attempt-2 MP4 SHA-256: `C876FE2865D6543B6FD8853E5D68968AAEEA76C627D2DC785C3E5BC836A7B5F7`.

The error occurred during the writer's first local candidate-shard validation, before append-journal creation, credential access, R2 operations, payload movement, shard creation, or root commit. The future root index remains unchanged at two infrastructure runs, four logical artifacts, 2,416 logical bytes, and SHA-256 `98DB696B5A57341B41CBE18A030555B62952CDF80242D61E4FC767FCA8065500`. No matching future payload, shard, or append journal exists. No FFmpeg or ffprobe process remains active, and the repository worktree remained clean through the block.

Stage 2A has therefore produced zero authoritative attempts and seven physical media attempts so far: one in the earlier ordered-dictionary defect run and six in this retained-namespace defect run.

## Recommended narrow repair

If approved, preserve the semantic helper's source-relative behavior and the validator's exact one-artifact invariant. Derive the proof-run and destination namespace before serializing the runner's evidence-facing bindings, then prefix each already-validated local `recordPath` with the exact `future/stage2/<proofRunId>/` destination. Do not make the validator accept either namespace or perform ambiguous matching.

Add a no-media isolated integration regression that:

1. creates six source-relative bindings through the real semantic helper;
2. applies the runner's retained-namespace projection;
3. invokes the real containment writer with those bindings;
4. proves that every binding resolves to exactly one immutable shard artifact with the same hash; and
5. proves that the unprojected runner-shaped input fails locally before any remote operation.

## Owner decisions required

1. Classify this run and its six attempts as a non-authoritative pre-matrix proof-harness defect, retained locally and excluded from all matrix dispositions and statistics.
2. Approve the narrow retained-namespace bridge and integration regression above without weakening the semantic helper, immutable shard validator, containment boundary, or schedule.
3. Decide whether to authorize another fresh restart from global ordinal 1 after the repair, focused no-media suite, independent review, clean-tree full preflight, and complete local/R2 verification pass. A successful fresh 108-attempt run would bring the Stage 2A physical-attempt total to 115 while retaining exactly 108 authoritative attempts.
4. Keep both defect closures local through the eventual Stage 2A owner packet. No recovery, reconstruction, backfilled retention, R2 promotion, reuse, splicing, or deletion is proposed.

No Stage 2B, concurrency, long-form work, new media investigation, product integration, shipping-runtime selection, distribution, licensing, patent, or legal work is authorized by this report.
