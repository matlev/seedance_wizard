# Gate 0 G0.5 Stage 2 owner decisions

Status: preparation packet approved with clarifications; prerequisite correction and bounded smoke authorized; full measured matrix remains blocked

Authority: owner approval dated 2026-08-26. This record extends the [Stage 1 owner decisions](gate-0-g0.5-stage1-owner-decisions.md), [lossy-audio oracle proposal](gate-0-g0.5-lossy-audio-oracle-proposal.md), [Stage 2 workload proposal](gate-0-g0.5-stage2-workload-proposal.md), and [P2 Windows WPF adapter boundary](gate-0-g0.5-wpf-measurement-adapter-boundary.md).

## Lossy-audio oracle

The corrected oracle structure, timing, channel-semantic, and signal-preservation direction is approved subject to resolving the RMS-versus-power inconsistency before any retained AAC or Opus output is inspected. The approved correction is an expected-tone output/reference **amplitude ratio**:

```text
sqrt(output tone power / reference tone power)
```

Its gate is 0.90 through 1.10, consistent with the output/reference RMS-ratio gate. A 95% amplitude control therefore measures approximately 0.95. The separate expected-to-forbidden-tone metric remains a true power ratio with its existing minimum of 100:1.

All 12 predeclared controls must be rerun without reading retained codec outputs, and their four accepted/eight rejected dispositions must remain unchanged. After that result, the contract, implementation, and control evidence are frozen and hash-pinned. Only then may the frozen oracle evaluate retained AAC and Opus artifacts, without re-encoding. Any later threshold widening requires another owner decision. The oracle is a deterministic synthetic-tone regression floor, not a perceptual speech/music claim.

## Stage 2 workload and marker

The exact Baseline, Typical, Stress, and Long-form workload contract and proof-only 17-bit marker atlas are approved. Evidence boundaries remain unchanged: 2A is runtime-route evidence, and 2B is proof-only P2 Windows WPF measurement-adapter evidence; neither is current product-composition evidence.

Before a 60-minute run, every admitted route and quality profile must pass a bounded marker-survivability qualification. It records marker geometry, luma tolerance, decoder behavior, exercised marker IDs, unique recovery, collisions, and misidentification. Any ambiguous, collided, or incorrect marker stops execution and returns an amended proposal. The approved atlas must be generated, hash-verified, locally retained, and durably retained before smoke execution.

## P2 Windows WPF adapter

The proof-only WPF adapter remains under `eng/gate0`, references no ReelForge product assembly, and makes no product rendering, preview, cache, cancellation, or project-behavior claim.

Before media scenarios, a visible and unminimized no-media control uses the same window, dispatcher heartbeat, duration, and evidence format with no FFmpeg child. Evidence records the Windows build, GPU and driver, power mode, display state, and window visibility. At least one representative media scenario requires an explicit human whole-system responsiveness observation. A visible mouse-pointer or desktop stall is a failure even if dispatcher metrics pass and triggers a private Windows Performance Recorder trace or explicit owner disposition before an accepted rerun.

Cancellation remains graceful `q` plus newline followed by bounded process-tree termination. A forced termination passes only when acknowledgement, complete process exit, orphan checks, partial-file cleanup, and state consistency all pass.

## Artifact storage clarification

The private R2 backend choice, credentials, and artifact-storage availability are resolved by the [durable artifact-retention contract](gate-0-artifact-retention.md). The complete-corpus verification remains factual rather than assumed: every current logical artifact, including newly retained marker/oracle evidence, must have a retrieved size/SHA-256 receipt before the second-copy execution gate is reported complete. Storage setup is not returned as an owner decision.

## Authorized sequence and stop point

Proceed in this order:

1. correct, rerun, freeze, and hash the lossy-audio oracle;
2. evaluate retained AAC/Opus without re-encoding and disposition admitted routes;
3. generate, hash, and retain the marker atlas;
4. qualify marker survivability for every admitted route/quality profile;
5. complete the WPF no-media/control evidence contract;
6. complete the private R2 corpus verification and applicable resource/free-space preflight; and
7. run the bounded pre-matrix smoke.

Return the pre-matrix smoke results before beginning the full measured 2A, 2B, or 2C matrix. A blocked prerequisite or smoke row is a valid result and must not be weakened or silently substituted.
