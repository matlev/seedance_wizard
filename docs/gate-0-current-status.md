# Gate 0 current status

Status: Stage 2A replacement media passed; V2 retention blocked on an invalid producer identity; no-media recovery decisions required

Updated: 2026-08-27

This is the small canonical current-status entry point for Gate 0. Historical decision and result records remain immutable even when their original status lines describe an earlier point in the evidence sequence.

## Current conclusion

Gate 0 remains inside its approved research-and-proof boundary. It has not added editing UI, project persistence, product render-command changes, runtime enforcement, or Windows-specific project meaning. The narrow product-source runtime observation/validation seam remains unintegrated with production behavior.

The accepted Stage 2A run remains unchanged at 108 authoritative schedule records: 38 physical media executions—36 passed and two semantically divergent—and 70 blocked records with no media execution. Seven earlier non-authoritative harness-defect executions, 12 earlier non-authoritative continuation executions, and six physical attempts in the latest replacement bring the cumulative physical-media-execution count to 63. Authoritative continuation remains zero of 72 because no continuation shard has been accepted.

The approved compact-V2 recovery passed 75 focused tests and independent review, reverified and atomically quarantined the prior malformed 23-file activation, passed a fresh full local/R2, runtime, resource, corpus, and headroom preflight, and executed exactly one replacement `stress-720p-webm-eight` cell under a fresh proof identity. Its warm-up and five measured attempts all passed semantic checks.

Retention then failed closed because the runner supplied the unscoped profile label `P2.BtbnLgplShared.WindowsX64.20260820` where the V2 shard reader requires a portable `repository:` or `sha256:` identity. The writer had already verified the content-addressed R2 objects, but it cleaned its local transaction and retained no payload, shard, journal, or root entry. The unchanged V2 root still has two infrastructure runs and zero continuation runs. The current 23-file, 15,746,570-byte replacement root remains untouched and non-authoritative pending the four decisions in the [V2 producer-identity block](gate-0-g0.5-stage2a-v2-producer-identity-block.md).

The recommended recovery is no-media: correct the runner identity, make the writer semantically validate the compact projection before journal or R2 work, regression-test and independently review the fix, then append only the exact current replacement bytes through the V2 writer. Do not rerun media or advance to another cell under the current authorization.

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
| Durable artifact retention | Accepted corpus independently byte-verified in private R2; the latest failed append may have added unindexed content-addressed objects but retained no authoritative shard or root entry | [Producer-identity block](gate-0-g0.5-stage2a-v2-producer-identity-block.md) |
| Stage 2A measured matrix | Completed all 18 cells and 108 authoritative records: 36 passed, two semantically divergent, 70 blocked; all 289 indexed artifacts independently verified locally and in R2 | [Stage 2A owner packet](gate-0-g0.5-stage2a-results.md) |
| Stage 2A stress-audio diagnostic | Reference fails its own frozen active model with the same 25 findings; no route defect inferred | [Audio diagnostic owner packet](gate-0-g0.5-stage2a-audio-diagnostic-results.md) |
| Stage 2A continuation contract | V2, stress-only V5, a fixed 72-attempt schedule, and exact activation gates approved | [Continuation approval](gate-0-g0.5-stage2a-continuation-approval.md) |
| Continuation activation | Compact recovery and fresh full preflight passed; six replacement attempts passed; retention failed on unscoped producer metadata; no authoritative continuation cell retained | [Producer-identity block](gate-0-g0.5-stage2a-v2-producer-identity-block.md) |
| First activation quarantine | Two-file, 5,763,062-byte pre-media partial root atomically preserved; reuse and automatic deletion prohibited | [First quarantine receipt](../eng/gate0/g0.5-stage2a-continuation-quarantine-receipt.json) |
| Second activation quarantine | 81-file, 59,800,276-byte media-bearing partial root atomically preserved and reverified; reuse and automatic deletion prohibited | [Second quarantine receipt](../eng/gate0/g0.5-stage2a-continuation-quarantine-receipt-2.json) |
| Third activation quarantine | 23-file, 15,768,093-byte malformed-reference root atomically preserved and reverified; reuse and automatic deletion prohibited | [Third quarantine receipt](../eng/gate0/g0.5-stage2a-continuation-quarantine-receipt-3.json) |
| Current replacement root | 23 files and 15,746,570 bytes; six passed attempts; exact root untouched; no authoritative V2 entry | [Producer-identity block](gate-0-g0.5-stage2a-v2-producer-identity-block.md) |
| Current owner decision | Portable runtime identity, pre-journal semantic validation, one no-media retention-only append, and final accounting await approval | [Producer-identity block](gate-0-g0.5-stage2a-v2-producer-identity-block.md) |

## Active bounded unit

Continuation activation reached the first approved cell through four fail-closed attempts:

1. The first stopped before media with a two-file partial root. The owner approved atomic quarantine, and the exact bytes were moved and verified under a committed receipt.
2. The second executed six physical media attempts, then stopped on a duplicate immutable evidence write. Its 81-file partial root was atomically quarantined and verified. The writer defect was removed and regression-covered.
3. The third executed the same six physical attempts after a fresh full preflight. All six passed semantic checks. V2 retention then stopped before journal or remote work because the projected pretty-printed shard was 353 lines against the 300-line cap, despite measuring only 19,828 bytes against the 65,536-byte cap. Review also found the doubled continuation prefix in compact closure references, making this 23-file root non-authoritative regardless of serialization.
4. The fourth used the approved compact serializer, exact one-cell live authorization, corrected closure references, a fresh proof identity, and another fresh full preflight. All six attempts passed. Retention then rejected the runner's unscoped producer profile label after R2 object verification but before any authoritative local append.

The first three partial roots are quarantined and immutable. The current replacement root is not a malformed media result: its exact attempt bindings and six semantic dispositions pass. It remains non-authoritative only because the retention transaction failed. No failed activation produced a continuation shard, root-index entry, or accepted continuation disposition. The fourth transaction may have added unindexed content-addressed R2 objects, which must be reverified and bound by an accepted shard before they count as evidence.

The recommended next bounded unit is to authorize the exact scoped runtime-manifest identity, validate the projected compact shard through the ordinary reader before journal or R2 work, review and hash-bind those no-media corrections, reverify the exact current root, and perform one retention-only append followed by complete local/R2 validation. The consumed preflight and media evidence must remain byte-unchanged because they truthfully bind the implementation under which execution occurred.

## Containment and remaining sequence

No new codec/container/filter matrix, dependency, installation, fixture family, performance dimension, or platform target may be added without a required exit condition first returning a genuine block and a new bounded owner approval.

F7/Matroska expansion has stopped. The final packet will classify the 83 non-passing rows rather than attempt further repairs unless a required Free 1.0 workflow is shown to depend on an exact row.

V1, the accepted 18-cell/108-record matrix, the original warm-up dispositions, and previously passed cells remain immutable. The V2/V5 activation evidence remains valid; the current block concerns portable producer metadata and missing pre-journal semantic validation, not the approved media semantics or product architecture.

The immediate sequence requires the four decisions in the [V2 producer-identity block](gate-0-g0.5-stage2a-v2-producer-identity-block.md). If retention succeeds, the remaining 11 Stage 2A cells and 66 attempts still require separate authorization. Stage 2B, concurrency comparison, long-form work, new playback installations, product integration, shipping-runtime selection, and distribution/legal conclusions remain unauthorized. Gate 0 thereafter still requires measured-result analysis and threshold proposals, WPF media-load/cancellation/preview/resource evidence, long-form sizing and execution, suitable independent playback, documentation-only Pro repair dispositions, and the G0.7 exit packet.
