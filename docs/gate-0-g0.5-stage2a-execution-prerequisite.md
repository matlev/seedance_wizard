# Gate 0 G0.5 Stage 2A execution prerequisite

Status: passed as a contract-only prerequisite; semantic execution remains fail-closed

Recorded: 2026-08-27

## Completed contract

The exact owner-approved Stage 2A order is now materialized in `eng/gate0/g0.5-stage2a-schedule.json`. It contains six order-sensitive workload/resolution groups, 18 cells, 18 warm-ups, 90 measured attempts, and 108 globally ordered attempts. Each cell executes its warm-up followed by measured attempts 1–5. Candidate order rotates across groups as approved; the schedule reader rejects changes to group, workload, resolution, route, thread policy, phase, cell, or global order.

Five-observation summaries use individual values, minimum, maximum, range, median, and median absolute deviation. Warm-ups receive complete validation and retention treatment but remain excluded from measured statistics.

The preflight contract:

- validates the sealed legacy corpus and future evidence index;
- validates the exact retained P2 runtime bytes against the pinned manifest and emits a portable runtime identity;
- requires the 16-logical-processor/30-GiB reference-host identity and 8 GiB available memory;
- rejects active FFmpeg or ffprobe processes while recording CPU, OS, power, session, and display observations;
- preserves the approved 3.5 GiB full-matrix free-space floor on both sibling roots; and
- rejects zero per-cell reservations and requires room for the ordinary closure, five compact records, one reasonable exceptional closure, a second-copy peak, and the 2 GiB host reserve.

## Exact identities

| Item | SHA-256 |
| --- | --- |
| Schedule | `C16D4A65EDDEA2A6213C0A60D371BE605FD3295EE2695EF2716762EA2F85B90E` |
| Contract-only runner | `919108CC7D91BC26E90A661DD1CA35B024835229B8A50BE66471187187FDCBF8` |
| Preflight | `A5E0D8FCF4309B6CEDC3A043382DC08ACB77D6D8D4EEEC6D978996955855E4DC` |
| Schedule/helper module | `15D1DDC3DA0B94CA7A184376C3565B0A496F24C6676C61195749A596827DAA7A` |
| Pending execution authorization | `0F47DB61944AF26488D2F135313387574DB0B8100C15B7B1652ABE8731E89DA1` |

The pending authorization contains nine exact repository/hash bindings: owner decision, schedule, runner, preflight, helper, P2 runtime validator, P2 runtime manifest, workload contract, and containment contract.

## Fail-closed boundary

The execution authorization deliberately remains `owner-authorized-execution-implementation-pending`. The runner's live switch stops before preflight/media, and the evidence writer rejects `p2-runtime-route` appends. No media or network process was invoked by this unit.

The next bounded unit must implement the exact semantic cell executor, first-successful-measured closure selection, compact passing-repeat transformation, exceptional full closures, route-scoped fail-fast behavior, one-shard-per-cell append, and aggregate result production. That implementation must extend the authorization hash closure and update the writer's accepted role set before the status can become effective.

Stage 2B, concurrency comparison, long-form work, playback installations, product integration, shipping-runtime selection, distribution, and legal conclusions remain outside this unit.

Validation: 12 focused Stage 2A tests and the complete Infrastructure suite passed (241 passed, 14 intentionally skipped, zero failed). Independent review returned GO for this bounded prerequisite.
