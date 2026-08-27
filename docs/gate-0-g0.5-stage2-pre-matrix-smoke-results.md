# Gate 0 G0.5 Stage 2 pre-matrix smoke results

## Outcome

The bounded pre-matrix smoke returned an owner-decision-required harness result. It did **not** admit or reject either runtime route, and the full Stage 2 matrix did not begin.

Both executed one-thread routes successfully completed the encode, exact output descriptor, 750-frame timing, per-frame visual oracle, and process cleanup gates. Their audio verdicts are invalid because the selected frozen oracle rejects the independently generated workload reference with the same active-level failures reported for both codecs. The eight-thread WebM candidate was not executed because the approved route fail-fast rule responded to the invalid one-thread verdict.

The retained machine-readable summary is `eng/gate0/g0.5-stage2-pre-matrix-smoke-result-summary.json`.

## What passed before the invalid verdict

| Candidate | Encode | Descriptor | Exact video timing | Visual oracle | Cleanup | Wall clock |
|---|---|---|---|---|---|---:|
| MP4 OpenH264/AAC, one thread | Passed | Passed | 750 frames, ticks 0 through 30,000 | Passed; maximum frame MAE 1.559903 | Root exited; no observed orphan | 6.780 s |
| WebM VP9/Opus, one thread | Passed | Passed | 750 frames, ticks 0 through 30,000 | Passed; maximum frame MAE 1.394567 | Root exited; no observed orphan | 39.438 s |
| WebM VP9/Opus, eight threads | Not run | Not run | Not run | Not run | Not applicable | Not run |

The MP4 decode retained the approved 768-sample AAC tail. The WebM decode produced the exact 1,440,000 content samples. Both routes independently placed all four track onsets at offsets `0, 0, 0, 0`.

## Why the audio result is invalid

The approved `0.05` full-scale active-channel and every-active-window floors were calibrated against the louder F1 control source. The owner-approved `typical-2v4a` composition intentionally applies `0.15` track gains and `0.25` pan coefficients. Its independently authored reference is therefore below `0.05` in several valid active regions:

- the a0 left channel is about `0.0389` RMS and its panned right channel about `0.0097`;
- the a1 right-channel composite is about `0.0400` RMS; and
- the full composition contains valid early 960-sample windows below `0.05`.

Running the frozen oracle against the reference PCM itself reproduces every route failure. That proves the current failure cannot distinguish codec damage from the approved workload's intended signal level.

A non-verdict diagnostic disabled only those two impossible absolute active floors in memory. It did not re-encode media or change any approved result. Under that diagnostic, both retained decoded outputs passed every other approved structure, timing, correlation, NRMSE, SNR, RMS-ratio, frequency, DC, clipping, and onset gate:

| Candidate | Minimum correlation | Maximum NRMSE | Minimum SNR | RMS-ratio range | Onset offsets |
|---|---:|---:|---:|---:|---|
| MP4 OpenH264/AAC | 0.999855 | 0.017123 | 35.328 dB | 0.997219–1.000642 | 0, 0, 0, 0 |
| WebM VP9/Opus | 0.999925 | 0.012309 | 38.196 dB | 0.997799–1.002504 | 0, 0, 0, 0 |

These figures are diagnostic only. They do not admit either route.

## Evidence disposition and correction

The immutable local artifact group `Gate0.G05.Stage2PreMatrixSmoke.20260827T002126507Z.1BD6749C` retains 42 files and 28,232,898 bytes. It is classified as a non-authoritative harness-discovery attempt.

All 42 files also have independent retrieval and byte-verification receipts in the private `reelforge-artifacts` R2 corpus. Durable retention is complete for the current 4,013-artifact inventory; that protects the discovery evidence but does not promote it to authoritative proof.

Its evidence JSON also contains machine-local absolute command paths: input tokens combined `\` and `/`, while the original portable-token sanitizer recognized only one separator at the root boundary. No credentials, prompts, signed URLs, or user media were included, but the group is not suitable as final portable proof evidence. The harness now normalizes both separators, records complete audio metrics before a failure, and performs an identity self-check before any media process starts.

One earlier attempt, `g05-stage2-smoke-20260827T001533273Z-1b97ac87`, stopped before media because a simplified PowerShell snapshot-binding expression failed at runtime. The same defect prevented its automatic artifact append; its trusted staging directory remains available for final evidence housekeeping. Commit `ecbc2b4` corrected the lookup before the retained media attempt.

## Owner decision requested

Recommended amendment:

1. Preserve the existing absolute `0.05` floor for the F1 descriptors and the existing 12 synthetic controls.
2. Allow structured composite descriptors to declare a reference-relative active-window mode when their independently authored truth intentionally falls below `0.05`.
3. In that mode, require every 960-sample active output window to remain within the already approved `0.90–1.10` output/reference RMS ratio. Preserve every other approved threshold unchanged.
4. Before evaluating route bytes, prove identity, low-level and panned-channel preservation, a 960-sample low-level dropout rejection, under/over-gain rejection, and unchanged dispositions for all 12 existing controls.
5. Treat the current two route attempts as invalid harness-discovery evidence and authorize one replacement attempt for each approved smoke candidate after the amended oracle is frozen. The one-thread rows should be replaced rather than promoted from the diagnostic; the eight-thread WebM row remains unattempted.

Changing the workload gain is not recommended. It would distort the approved composition semantics and cannot cleanly solve the intentionally attenuated pan channels without creating clipping risk elsewhere.

## Boundaries

No full Stage 2 matrix, WPF media scenario, long-form run, shipping-runtime selection, distribution decision, patent/legal conclusion, product-composition claim, or perceptual-quality claim follows from this result.
