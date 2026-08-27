# Gate 0 G0.5 Stage 2A audio diagnostic owner packet

Date: 2026-08-27

Status: no-media diagnostic complete; owner decisions required; no media execution authorized

## Outcome

The authorized diagnostic `g05-stage2a-audio-diagnostic-20260827T183504426Z` completed without invoking FFmpeg or ffprobe. It reproduced all 25 retained `active-rms` and `active-window-silence` findings when the independently authored stress reference PCM was evaluated against its own frozen descriptor and oracle.

The evidence classification is **A: oracle/descriptor self-inconsistency**. The stress descriptor marks every affected channel and 960-sample window active, while the frozen V3 oracle requires an absolute RMS of at least 0.05 full scale. The reference itself ranges from 0.0041074 to 0.0483346 in those affected checks and therefore cannot pass its own declared active model.

The exact diagnostic result is 111,771 bytes with SHA-256 `CA32B117FDB732F73EE28918501A59FD8901A884CBAAA94439CAE94ACD63ECE3`.

## Retained route comparison

Only after the reference self-check failed did the diagnostic read the two retained decoded PCM outputs. WebM VP9/Opus and MP4 OpenH264/AAC reproduced the same ordered set of 25 finding keys. The largest absolute difference between their per-finding output/reference RMS ratios was 0.0064484.

Both retained warm-ups had already passed descriptor validation, encoding, probing, exact video timing, visual identity, audio timing, onset timing, cleanup, and orphan checks. This diagnostic found no workload-truth/filter-graph mismatch and no evidence of route-specific damage. It therefore does **not** establish that either route failed the intended stress-audio semantics.

The original warm-ups remain immutable and `semantically-divergent` under the exact frozen contract that evaluated them. They are not promoted to passes by this diagnostic.

## Narrow proof-harness correction

The approved whitespace correction now emits `Audio timing or quality oracle failed.` as the intended friendly exception. Its no-media regression proves that the structured audio result is assigned and retained before the throw, the route is still suspended, the attempt remains semantically divergent, and pass/fail/block semantics did not change.

## Proposed V5 amendment

The recommended amendment is a stress-only V5 overlay for descriptor `stress-4v8a-30s`, using the already controlled `reference-relative-active-windows-v1` semantics:

- classify activity only from the independently authored reference PCM and descriptor;
- for every eligible 960-sample active window, require `RMS(output) / RMS(reference)` within 0.90–1.10;
- replace only the V3 absolute `minimumActiveChannelRmsFullScale` and `minimumActiveReferenceWindowOutputRmsFullScale` checks;
- retain every other V3 structure, timing, correlation, NRMSE, SNR, aggregate RMS ratio, DC, tone, clipping, and onset check; and
- leave the typical-only V4 overlay and every other descriptor unchanged.

Before retained route outputs may be reevaluated, the V5 contract must pass and freeze exact no-media controls: reference identity; 0.95 gain acceptance; a localized low-level 960-sample dropout rejection; 0.75 and 1.25 gain rejection; all 12 frozen V3 control dispositions and hashes; and all V4 control dispositions and hashes. The retained PCM bytes may then be reevaluated without re-encoding under a new proof identity. The two original warm-up records remain unchanged.

## Minimum continuation if V5 passes

No already passed baseline or typical attempt requires execution. If the V5 controls and retained-output reevaluation pass, the minimum completion set is the 70 attempts that were previously blocked:

- five measured attempts in stress 720p WebM/eight-thread;
- five measured attempts in stress 720p MP4/one-thread;
- all six attempts in stress 720p WebM/one-thread; and
- all 54 attempts across the nine 1080p cells.

The recommended count is therefore 70 new physical media attempts. Fresh replacement warm-ups are not technically required because the two retained warm-ups can be reevaluated byte-for-byte; if the owner requires fresh warm-ups, the count becomes 72. Every new attempt requires a new proof identity linked to the original run.

The conservative retained-growth projection is 240,613,360 bytes. After retaining this 111,771-byte diagnostic and that continuation, the existing 805,306,368-byte Stage 2A ceiling would retain approximately 486,042,394 bytes of headroom. A 60–120 minute operator window is the planning estimate for the 70-attempt continuation; it is not an acceptance threshold.

## Evidence-containment block

The diagnostic is hash-verified in project-controlled sibling staging, but it has not been copied into the authoritative retained root or R2. The completed append attempt stopped at the writer's former conservative root-shape estimate before journal creation, destination mutation, shard creation, root mutation, or R2 access. Existing 18 cell shards and 20 root entries remain byte-for-byte unchanged.

Independent validation shows that the compact diagnostic manifest and projected root metadata fit their byte/line caps and the artifact fits the global retention ceiling. However, V1 already uses all approved capacity by kind: two of two infrastructure shards and 18 of 18 Stage 2A cell shards. It must not be retried against V1. The recommended solution is a future-only chained V2 continuation segment anchored to V1 root SHA-256 `C6D3CD9E7B0FC62E199E6FAD0A7D0FBAB6AFE1BBA6C8EFD4EAE427BBB79E30EA`. It should retain the same 64 KiB/300-line shard caps, 128 KiB/400-line root cap, 256 KiB compact-attempt cap, and global 805,306,368-byte Stage 2A ceiling. Its initial bounded budget should be two infrastructure shards—diagnostic, then V5 controls/reevaluation—and 12 continuation cell shards. V1 remains immutable.

## Owner decisions requested

1. Accept classification A: the frozen stress oracle/descriptor is self-inconsistent, and neither retained route is shown defective by the 25 findings.
2. Approve the exact stress-only V5 amendment and authorize its no-media control/freeze and retained-output reevaluation. This does not authorize media execution or reclassification of the original warm-up records.
3. Approve the future-only chained V2 evidence segment described above so the diagnostic can be retained and independently byte-verified locally and in R2 without changing V1 limits or entries.
4. Only after the V2 segment is approved, implemented, and passes no-media validation—and the V5 controls are frozen and retained-output reevaluation passes—authorize the bounded 70-attempt continuation. Alternatively, keep the 70 rows blocked and direct another explicit Gate 0 disposition.

Thread-policy selection, Stage 2B, concurrency comparison, long-form proof, new playback installation, product integration, shipping-runtime selection, and distribution/licensing/patent/legal conclusions remain open or unauthorized.
