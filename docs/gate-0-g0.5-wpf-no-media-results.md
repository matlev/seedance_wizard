# Gate 0 G0.5 WPF no-media control results

## Outcome

The visible, unminimized 30-second proof-only WPF no-media control passed on the owner's reference Windows system. It started zero FFmpeg or other media children and used the same window, dispatcher heartbeat, and closed evidence format reserved for later media scenarios.

| Measure | Observed | Gate |
|---|---:|---:|
| Expected dispatcher cadences | 1,871 | every cadence classified |
| Executed callbacks | 1,871 | at least 1,800 |
| Explicitly missed while one callback was outstanding | 0 | recorded, never silently dropped |
| Overdue callbacks | 0 | recorded |
| p95 enqueue-to-execute latency | 0.3625 ms | at most 50 ms |
| p99 enqueue-to-execute latency | 0.6935 ms | at most 100 ms |
| Maximum latency | 1.0581 ms | at most 250 ms |

The exact clean-built adapter assembly SHA-256 was `A768BFEA522C93F8ADD184F2579991AD22304FF4CEB12EAA87852ED877101D0D`; it loaded the copied frozen contract only after verifying SHA-256 `C13AA5236AD025415807F843D5861E707CB9DD82BC386CC79FF2AB580B954836`. The execution revision was `f4d39ac`.

## Closed host evidence

The evidence gate recorded Windows display version 25H2, build 26200.9168; the NVIDIA GeForce RTX 3070 Ti with driver 32.0.15.9186; the installed Meta virtual-display driver; Balanced power mode; two active 1920x1080 60 Hz displays with distinct bounds/work areas and primary identity; a 96-DPI native-visible WPF window in `Normal` state; and an interactive desktop. Required host fields were complete.

The authoritative `evidence.json` is 8,312 bytes with SHA-256 `237C38B7EA8A99DC3BE865F4EB25BE0D6BE1C24CCEB1CA45D6236C7DD549000D`. Its full dispatcher and window samples are retained with it in immutable local group `Gate0.G05.WpfMeasurementAdapter.NoMedia.20260826.Final`. The tracked machine-independent summary is `eng/gate0/g0.5-wpf-no-media-result-summary.json`. R2 byte verification of this newly expanded corpus remains pending.

An earlier semantically passing attempt is retained as `Gate0.G05.WpfMeasurementAdapter.NoMedia.20260826` but superseded for authoritative use: its binary was built immediately before commit `f4d39ac` existed, so its source provenance could not be closed as strongly as the clean rebuild and rerun from committed source.

## Disposition

This closes the no-media control and establishes the normal adapter/WPF/system latency reference. It does not prove behavior under media load. The representative media scenario must still include explicit human pointer/desktop observation, and a visible stall must still fail regardless of dispatcher metrics and trigger the approved private-WPR-or-owner-disposition path. Exact-P2 cancellation, preview, cache, full Stage 2, and long-form evidence also remain unexecuted.

The remaining prerequisites before pre-matrix smoke are complete R2 verification of the current corpus and resource/retention/free-space preflight. This result makes no current ReelForge product rendering, preview, cache, cancellation, project, UI, shipping-runtime, distribution, patent, or legal claim.
