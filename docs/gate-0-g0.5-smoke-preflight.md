# Gate 0 G0.5 smoke resource preflight

Status: authoritative no-media resource preflight passed; complete R2 verification and media execution remain blocked

Authority: [Stage 1 owner decisions](gate-0-g0.5-stage1-owner-decisions.md), [Stage 2 owner decisions](gate-0-g0.5-stage2-owner-decisions.md), and the [Stage 2 workload proposal](gate-0-g0.5-stage2-workload-proposal.md)

The machine-readable contract is `eng/gate0/g0.5-stage2-smoke-preflight-contract.json`. It defines how the resource, retention-capacity, and free-space prerequisite is closed for the already approved pre-matrix smoke without adding a product hardware promise or widening media authorization. On 2026-08-26, the owner approved all four numeric floors as proposed and authorized implementation and execution of this no-media preflight only.

## Exact boundary

The preflight covers only the three approved 1080p `typical-2v4a` candidates: MP4/OpenH264/AAC with one requested thread, and WebM/VP9/Opus with one and half-logical requested threads. It executes no media component. A pass means only that the owner reference host had the required bounded capacity at the recorded instant.

The approved host checks recognize the established 32 GiB reference profile at 30 GiB or more reported physical memory, require the exact 16-logical-processor reference identity, and require at least 8 GiB currently available. The 8 GiB execution floor is the approved 4 GiB combined-process ceiling plus a separate 4 GiB host reserve. Active `ffmpeg` or `ffprobe` processes block the observation so unrelated media work cannot contaminate it. Current CPU utilization is evidence, not a pass/fail threshold.

The approved storage floor uses the 0.75 GiB Stage 2 retention ceiling for the smoke retained group, adds an equal 0.75 GiB peak scratch allowance, and preserves 2 GiB free. The exact non-reparse repository-sibling roots necessarily share one volume, so the floor is exactly 3.5 GiB free. The current corpus is already allocated and is not double-counted. The later 60-minute sizing decision is explicitly excluded.

## Owner decision

The owner approved these four bounded preflight rules as proposed:

1. recognize the reference host only with 16 logical processors and at least 30 GiB reported physical memory;
2. require at least 8 GiB currently available memory;
3. require the 3.5 GiB free-space floor on the shared artifact/staging volume; and
4. require zero active `ffmpeg`/`ffprobe` processes while recording—but not gating on—current CPU utilization.

These are reference-host execution safeguards only. They do not establish a customer hardware minimum or long-form storage contract. Failure blocks execution without weakening a floor.

## Evidence and stop conditions

The runner independently validates every current local corpus byte and the local/durable manifest binding, proves the exact sibling roots contain no reparse points, observes memory/process/volume state, and hash-binds the workload and preflight contracts. Once a trusted new output directory has been established, passed and blocked attempts are atomically retained. An invalid invocation that cannot establish that trusted location is not a proof attempt. An evidence-persistence failure exits fail-closed and produces no reportable result; it cannot be relabeled as a retained block.

This evidence does not complete the separate R2 prerequisite, authorize the smoke by itself, execute FFmpeg, or prove current ReelForge product behavior. The pre-matrix smoke may begin only after this preflight and complete R2 byte verification both pass. Full 2A, 2B, 2C, and long-form execution remain outside this unit.

## Result

The authoritative 2026-08-26 run from commit `7b3f7b2` passed all seven criteria and recorded `noMediaInvoked: true`. The point-in-time observation was:

- 16 logical processors;
- 34,261,430,272 bytes total physical memory and 17,961,807,872 bytes available;
- 15.667% CPU utilization from the fail-closed native Windows sample, recorded but not gated;
- no active `ffmpeg` or `ffprobe` process; and
- 139,466,764,288 bytes free on the common artifact/staging volume against the 3,758,096,384-byte floor.

The authoritative evidence is locally verified and immutably retained as `Gate0.G05.Stage2SmokePreflight.20260826.AuthoritativePass`, SHA-256 `2AD8399B808A2B673C002EBC8C4611F73265A8AC071365D50F0C49133FF3A119`. Three superseded attempts are retained as audit evidence: the native-memory compile block, the fail-closed stale durable-binding block, and an invalidated pass that exposed a nullable CPU observation. The latter caused the CPU probe to be replaced with a native `GetSystemTimes` sample that blocks instead of emitting a missing observation.

The machine-readable result is `eng/gate0/g0.5-stage2-smoke-preflight-result-summary.json`; it is the current result record, while the hash-bound Stage 2 preparation summary remains an immutable pre-execution record. The resource prerequisite is complete. The complete 3,971-file, 996,626,827-byte corpus subsequently passed independent R2 retrieval and hash verification. Together these gates satisfy the owner-approved prerequisites and authorize the bounded pre-matrix smoke, but not the full Stage 2 matrix or long-form proof.
