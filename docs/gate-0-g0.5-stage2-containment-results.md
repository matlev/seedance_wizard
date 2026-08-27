# Gate 0 G0.5 Stage 2 evidence-containment result

Status: passed; no Stage 2A media executed

Executed: 2026-08-27

## Result

The owner-approved future evidence-containment prerequisite passed. The legacy corpus was freshly verified locally and through exact independent R2 retrieval before its tracked manifests were sealed. The supported legacy append and durable-ledger mutation paths then failed closed without changing either sealed hash.

The no-media adoption proof created two immutable infrastructure shards and an ordered two-entry root chain. It retained four logical artifacts totaling 2,416 bytes, independently retrieved and hash-verified the four exact R2 objects, and invoked zero FFmpeg or ffprobe processes.

| Identity | SHA-256 |
| --- | --- |
| Effective legacy seal | `91EA51E766448F35D832823E25A9DBF1A92523FF31790B1E0364BA9BC61C604C` |
| Initial root index | `146936D12F54D0DC6D324F51330445E1B9F07C2C0DF13575F4EA0EB7C8643126` |
| No-media payload shard | `95CB3207D8ABE1C67D8942A64F97C6CB441C41B0BBEC14BE76A139339390261A` |
| Retained result shard | `C4B679C27E60EC2A368C2C6A4C3ED9FA05F186090667FF60BB88225D25B4FD58` |
| Final root index | `98DB696B5A57341B41CBE18A030555B62952CDF80242D61E4FC767FCA8065500` |

Proof-run ID: `g05-stage2-containment-dry-run-20260827T092340819Z`.

## Verified controls

- exact legacy source/durable hashes, counts, bytes, and complete R2 status;
- immutable closed-schema shards with content-addressed object bindings;
- ordered, hash-chained root entries and fixed root/shard/record ceilings;
- local artifact inventory equality and recursive reparse-point rejection;
- exact tracked-shard-tree union, including rejection of non-manifest strays and unexpected directories;
- exact independent R2 byte retrieval for every future artifact;
- failure atomicity, concurrent-writer exclusion, cap enforcement, prohibited metadata rejection, and post-remote recovery-journal handling through focused tests;
- two reserved infrastructure entries consumed, leaving all 18 Stage 2A cell entries available;
- a fail-closed `p2-runtime-route` gate that remains unavailable until an exact hash-bound schedule/runner authorization exists; and
- 229 Infrastructure tests passed, 14 intentionally skipped, and zero failed after containment hardening.

## Boundary and next gate

This result proves evidence infrastructure only. It does not prove a media route, product composition, WPF responsiveness, a shipping runtime, distribution suitability, or a legal conclusion.

Stage 2A media remains blocked until the exact counterbalanced schedule, incremental resource/free-space checks, per-cell retention reservation, and exact 18-cell/108-attempt runner are committed, tested, and reviewed.
