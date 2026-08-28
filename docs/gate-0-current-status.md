# Gate 0 current status

Status: Stage 2A continuation blocked at first-cell retention; V2 shard serialization and replacement-execution decisions required

Updated: 2026-08-27

This is the small canonical current-status entry point for Gate 0. Historical decision and result records remain immutable even when their original status lines describe an earlier point in the evidence sequence.

## Current conclusion

Gate 0 remains inside its approved research-and-proof boundary. It has not added editing UI, project persistence, product render-command changes, runtime enforcement, or Windows-specific project meaning. The narrow product-source runtime observation/validation seam remains unintegrated with production behavior.

The accepted Stage 2A run remains unchanged at 108 authoritative schedule records: 38 physical media executions—36 passed and two semantically divergent—and 70 blocked records with no media execution. Seven earlier non-authoritative harness-defect executions and 12 newer non-authoritative continuation executions bring the cumulative physical-media-execution count to 57. None of the 12 newer executions advances the 72-attempt authoritative continuation schedule.

The approved V2/V5 continuation prerequisites and a fresh full local/R2, resource, corpus, and headroom preflight passed. Execution nevertheless remains blocked at retention of the first continuation cell, `stress-720p-webm-eight` (global ordinals 109 through 114). The latest warm-up and five measured attempts passed their semantic checks, but their staged evidence is non-authoritative because review found malformed compact closure references and the valid V2 shard shape exceeds the exact 300-line pretty-printed limit. The append stopped before journal, destination, shard, root-index, transaction-staging, or R2 mutation.

The closure-reference defect is fixed and independently enforced by the V2 writer; the focused continuation and containment set passes 71 of 71 tests. The remaining policy decision is whether V2 shard manifests may use deterministic compact JSON while retaining the exact schema, evidence, hashes, validation, and 65,536-byte ceiling. The current 23-file staged cell must not be repaired or appended. It awaits owner approval for atomic quarantine and one replacement execution. See the [V2 shard block and owner decisions](gate-0-g0.5-stage2a-v2-shard-block.md).

The platform-neutral semantic proof remains strong, but Gate 0 is not ready to exit. The Stage 2A continuation, independent playback, measured Stage 2 performance, WPF media-load evidence, cancellation/preview/resource evidence, long-form integrity, final Pro dispositions, and the G0.7 decision packet remain open.

## Authoritative current evidence

| Area | Current disposition | Authority |
| --- | --- | --- |
| Charter and exit contract | Owner/PM approved | [Gate 0 media capability charter](gate-0-media-capability-charter.md) |
| G0.1/G0.2 and Checkpoint A | Complete and approved | [Checkpoint A](gate-0-checkpoint-a.md) |
| G0.3 semantic proof | 13 automated capabilities passed; playback and long-form gates open | [G0.3 executable proof](gate-0-g0.3-executable-proof.md) |
| Free delivery proof | 11 portable and two optional W1 routes passed; default remains conditional | [G0.4 delivery proof](gate-0-g0.4-executable-proof.md) |
| Guaranteed-common candidate input subset | 173 passed rows; remaining 83 excluded from the candidate baseline pending final classification | [G0.4 input proof](gate-0-g0.4-input-proof-results.md) |
| Independent playback | Two WebM Chromium controls passed; named browser/player, perceptual-sync, and suitable MP4 evidence remain open | [Independent-playback checkpoint](gate-0-independent-playback-checkpoint.md) |
| G0.5 retained audio | Both routes passed all 48 retained Stage 1 rows under frozen V3 | [Retained-audio results](gate-0-g0.5-retained-audio-results.md) |
| Marker survivability | 1,500 of 1,500 frames passed | [Marker results](gate-0-g0.5-marker-survivability-results.md) |
| WPF no-media control | Passed; media-load behavior remains unexecuted | [WPF no-media results](gate-0-g0.5-wpf-no-media-results.md) |
| Replacement pre-matrix smoke | MP4 one-thread, WebM one-thread, and WebM half-logical each passed once under the complete frozen contract | [Replacement-smoke result](gate-0-g0.5-stage2-replacement-smoke-results.md) |
| Durable artifact retention | Accepted corpus independently byte-verified in private R2; continuation failures made no R2 evidence mutation | [Containment result](gate-0-g0.5-stage2-containment-results.md) |
| Stage 2A measured matrix | Completed all 18 cells and 108 authoritative records: 36 passed, two semantically divergent, 70 blocked; all 289 indexed artifacts independently verified locally and in R2 | [Stage 2A owner packet](gate-0-g0.5-stage2a-results.md) |
| Stage 2A stress-audio diagnostic | Reference fails its own frozen active model with the same 25 findings; no route defect inferred | [Audio diagnostic owner packet](gate-0-g0.5-stage2a-audio-diagnostic-results.md) |
| Stage 2A continuation contract | V2, stress-only V5, a fixed 72-attempt schedule, and exact activation gates approved | [Continuation approval](gate-0-g0.5-stage2a-continuation-approval.md) |
| Continuation activation | Approved no-media prerequisites and fresh full preflight passed; no authoritative continuation cell retained | [V2 shard block](gate-0-g0.5-stage2a-v2-shard-block.md) |
| First activation quarantine | Two-file, 5,763,062-byte pre-media partial root atomically preserved; reuse and automatic deletion prohibited | [First quarantine receipt](../eng/gate0/g0.5-stage2a-continuation-quarantine-receipt.json) |
| Second activation quarantine | 81-file, 59,800,276-byte media-bearing partial root atomically preserved and reverified; reuse and automatic deletion prohibited | [Second quarantine receipt](../eng/gate0/g0.5-stage2a-continuation-quarantine-receipt-2.json) |
| Current owner decision | Compact V2 shard serialization, third-root quarantine, one replacement cell, and final non-authoritative accounting await approval | [V2 shard block](gate-0-g0.5-stage2a-v2-shard-block.md) |

## Active bounded unit

Continuation activation reached the first approved cell through three fail-closed attempts:

1. The first stopped before media with a two-file partial root. The owner approved atomic quarantine, and the exact bytes were moved and verified under a committed receipt.
2. The second executed six physical media attempts, then stopped on a duplicate immutable evidence write. Its 81-file partial root was atomically quarantined and verified. The writer defect was removed and regression-covered.
3. The third executed the same six physical attempts after a fresh full preflight. All six passed semantic checks. V2 retention then stopped before journal or remote work because the projected pretty-printed shard was 353 lines against the 300-line cap, despite measuring only 19,828 bytes against the 65,536-byte cap. Review also found the doubled continuation prefix in compact closure references, making this 23-file root non-authoritative regardless of serialization.

The closure-reference correction preserves the binding helper's canonical attempt identity and requires every compact reference to resolve to exactly one complete, passed attempt in the same six-attempt document. The current staged root remains untouched pending an owner-approved quarantine. No failed activation produced a continuation shard, root-index entry, accepted continuation disposition, or R2 evidence mutation.

The recommended next bounded unit is to authorize deterministic compact JSON for V2 shard manifests only, preserving all schema and byte controls; quarantine the current malformed-reference root with a complete hash receipt; implement and review the serialization change without media; refresh the exact authorization hashes; pass a fresh full preflight; and execute one replacement six-attempt cell. The alternative is a broader V2 line-contract amendment and associated root/V5 revalidation.

## Containment and remaining sequence

No new codec/container/filter matrix, dependency, installation, fixture family, performance dimension, or platform target may be added without a required exit condition first returning a genuine block and a new bounded owner approval.

F7/Matroska expansion has stopped. The final packet will classify the 83 non-passing rows rather than attempt further repairs unless a required Free 1.0 workflow is shown to depend on an exact row.

V1, the accepted 18-cell/108-record matrix, the original warm-up dispositions, and previously passed cells remain immutable. The V2/V5 activation evidence remains valid; the current block concerns the first continuation cell's retained shard representation and staged binding correctness, not the approved media semantics or product architecture.

The immediate sequence requires the four decisions in the [V2 shard block](gate-0-g0.5-stage2a-v2-shard-block.md). Stage 2B, concurrency comparison, long-form work, new playback installations, product integration, shipping-runtime selection, and distribution/legal conclusions remain unauthorized. After separately authorized measured work, Gate 0 still requires long-form sizing and authorization, suitable independent playback, documentation-only Pro repair dispositions, and the G0.7 exit packet.
