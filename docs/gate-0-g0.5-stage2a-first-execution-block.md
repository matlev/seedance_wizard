# Gate 0 G0.5 Stage 2A first-execution block

Date: 2026-08-27

Status: one local warm-up executed; matrix retry blocked on owner disposition

## What happened

The effective Stage 2A runner passed the full resource/corpus preflight and began the frozen schedule at global ordinal 1: the `baseline-720p-mp4-one` warm-up. The encode and complete semantic validation produced the expected local closure, but the runner then stopped before writing the attempt summary because its summary validator accepted `PSCustomObject` test inputs but not the ordered dictionaries used by the live runner.

This is proof-harness behavior, not a route failure. It does not change the MP4/OpenH264/AAC capability disposition.

The failed live-start identity is `g05-stage2a-20260827T113709087Z-9c98c3d3`. Its sibling-staging closure contains 18 files and 27,125,317 bytes. The encoded MP4 SHA-256 is `C876FE2865D6543B6FD8853E5D68968AAEEA76C627D2DC785C3E5BC836A7B5F7`. The output, strict frame and packet probes, decoded PCM, per-frame visual oracle records, process samples, command logs, three run-scoped audio truths, and both preflights remain preserved locally.

The failure occurred before the cell writer. Therefore:

- no immutable cell shard or root-index entry was added;
- no R2 upload or receipt was created;
- the root index remains at two no-media infrastructure runs with SHA-256 `98DB696B5A57341B41CBE18A030555B62952CDF80242D61E4FC767FCA8065500`;
- no complete attempt summary survived process exit, including its exact in-memory start/end timestamps and aggregate measurement object;
- no FFmpeg or ffprobe process remains active.

## Repair and validation

Execution authorization was returned to pending immediately. The summary validator now normalizes both the top-level live ordered dictionary and its nested validation, hash, and cleanup dictionaries. A regression exercises the runner's actual ordered shape. The focused no-media Stage 2A suite passes 28 of 28 tests. The repair remains fail-closed until committed with an owner-approved execution disposition.

## Owner decision required

The existing approval authorized exactly 108 matrix attempts. Silently restarting the complete schedule would produce 108 retained matrix attempts but 109 physical media attempts when the failed live-start warm-up is included.

Recommended disposition:

1. classify the preserved live-start attempt as a pre-matrix proof-harness defect, not as a retained matrix row or route result;
2. authorize one replacement warm-up when restarting the exact frozen 108-attempt schedule;
3. retain the local defect closure through the Stage 2A owner packet and report the total physical attempt count as 109;
4. run a fresh full preflight and preserve the committed counterbalanced matrix order without any other change.

The alternative is to add recovery/resume behavior and reconstruct a summary from the local files. That would require a new execution path and an evidence-contract amendment because the exact in-memory timestamps and measurement aggregate were not persisted. It is higher complexity and risk and is not recommended merely to avoid one replacement warm-up.

No Stage 2B, concurrency, long-form, product, shipping-runtime, distribution, or legal work is implicated or authorized.
