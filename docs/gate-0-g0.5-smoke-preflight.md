# Gate 0 G0.5 smoke resource preflight

Status: owner decision requested on numeric execution floors; preflight and media execution blocked

Authority: [Stage 1 owner decisions](gate-0-g0.5-stage1-owner-decisions.md), [Stage 2 owner decisions](gate-0-g0.5-stage2-owner-decisions.md), and the [Stage 2 workload proposal](gate-0-g0.5-stage2-workload-proposal.md)

The machine-readable proposal is `eng/gate0/g0.5-stage2-smoke-preflight-contract.json`. It defines how the resource, retention-capacity, and free-space prerequisite could be closed for the already approved pre-matrix smoke without adding a product hardware promise or widening media authorization. The owner approved the need for this preflight, but not all numeric pass/fail floors below. Its runner is therefore fail-closed until those values are approved or amended.

## Exact boundary

The preflight covers only the three approved 1080p `typical-2v4a` candidates: MP4/OpenH264/AAC with one requested thread, and WebM/VP9/Opus with one and half-logical requested threads. It executes no media component. A pass means only that the owner reference host had the required bounded capacity at the recorded instant.

The proposed host checks recognize the established 32 GiB reference profile at 30 GiB or more reported physical memory, require the exact 16-logical-processor reference identity, and require at least 8 GiB currently available. The proposed 8 GiB execution floor is the approved 4 GiB combined-process ceiling plus a separate 4 GiB host reserve. Active `ffmpeg` or `ffprobe` processes would block the observation so unrelated media work cannot contaminate it. Current CPU utilization would be evidence, not a pass/fail threshold.

The storage proposal uses the approved 0.75 GiB Stage 2 retention ceiling for the smoke retained group, adds an equal 0.75 GiB peak scratch allowance, and preserves 2 GiB free. The exact non-reparse repository-sibling roots necessarily share one volume, so the proposed floor is exactly 3.5 GiB free. The current corpus is already allocated and is not double-counted. The later 60-minute sizing decision is explicitly excluded. The scratch allowance and fixed reserve are new owner decisions; they are not silently attributed to the earlier approval.

## Owner decision requested

Approve or amend these four bounded preflight rules:

1. recognize the reference host only with 16 logical processors and at least 30 GiB reported physical memory;
2. require at least 8 GiB currently available memory;
3. require the 3.5 GiB free-space floor on the shared artifact/staging volume; and
4. require zero active `ffmpeg`/`ffprobe` processes while recording—but not gating on—current CPU utilization.

These are reference-host execution safeguards only. They do not establish a customer hardware minimum or long-form storage contract.

## Evidence and stop conditions

After approval, the runner must independently validate every current local corpus byte and the local/durable manifest binding, prove the exact sibling roots contain no reparse points, observe memory/process/volume state, and hash-bind the workload and preflight contracts. Once a trusted new output directory has been established, passed and blocked attempts are atomically retained. An invalid invocation that cannot establish that trusted location is not a proof attempt. An evidence-persistence failure exits fail-closed and produces no reportable result; it cannot be relabeled as a retained block.

This evidence does not complete the separate R2 prerequisite, authorize the smoke by itself, execute FFmpeg, or prove current ReelForge product behavior. The pre-matrix smoke may begin only after this preflight and complete R2 byte verification both pass. Full 2A, 2B, 2C, and long-form execution remain outside this unit.
