# Gate 0 G0.5 lossy-audio oracle proposal

Status: owner approved; amplitude metric corrected; all 12 predeclared controls passed; frozen hash record required before retained AAC/Opus evaluation

Authority: [G0.5 Stage 1 owner decisions](gate-0-g0.5-stage1-owner-decisions.md) and [G0.5 Stage 2 owner decisions](gate-0-g0.5-stage2-owner-decisions.md)

## Outcome

The corrected oracle separates three questions that the Stage 1 peak-sample comparator collapsed:

1. **Structure and timing:** correct 48 kHz stereo layout, exact decoded length, presentation endpoints, and recorded codec priming/discard behavior.
2. **Channel semantics:** no channel swap, no silence or clipping, and the correct 440 Hz-left/880 Hz-right identities.
3. **Lossy signal preservation:** signed normalized correlation, normalized RMS error, SNR, RMS gain ratio, DC offset, expected-tone output/reference amplitude ratio, and expected-versus-other-tone power.

The machine-readable proposal is `eng/gate0/g0.5-lossy-audio-oracle-contract.json`. It applies one quality contract to AAC and Opus. It does not make a perceptual-transparency claim.

## Approved thresholds

| Measure | Proposed gate |
| --- | ---: |
| Content-normalized decoded samples per channel | Exactly the selected reference descriptor's sample count (384,000 for retained Stage 1 F1 outputs) |
| Presentation start/end | Samples 0 and the descriptor's exclusive end, within one sample |
| Recorded priming/discard envelope | At most 1,024 samples for these exact P2 fixture routes; recorded separately from raw decode |
| Quality alignment | None; zero-lag comparison only |
| Signed normalized correlation | At least 0.995 per channel |
| Normalized RMS error | At most 0.10 per channel |
| SNR | At least 20 dB per channel |
| Output/reference RMS ratio | 0.90–1.10 per channel |
| Expected-tone output/reference amplitude ratio | 0.90–1.10 per declared tone and channel; `sqrt(output tone power / reference tone power)` |
| Absolute DC offset | At most 0.005 full scale per channel |
| Expected/other fixture-tone power | At least 100:1 |
| Unexpected clipped samples | Zero |
| Active-channel RMS | At least 0.05 full scale overall and in every 960-sample active window |

The current F1 source is a repeating pure-tone loop and cannot uniquely identify signal lag. The proposal therefore performs no signal alignment. Raw decoder output, packet/frame priming metadata, and content-normalized presentation timing are recorded separately; signal-derived trimming is prohibited.

## Threshold basis

Thresholds were selected before reading any AAC or Opus result under the new metrics. After owner review identified the RMS-versus-power naming inconsistency, the expected-tone gate was corrected to the amplitude formula and the same 0.90–1.10 amplitude threshold already used by the RMS ratio. No retained codec output was inspected during correction. The executable synthetic controls admit deterministic identity, 95% gain, 24 dB SNR noise, and 1% crosstalk. They reject 75% gain, 15 dB SNR noise, polarity inversion, channel swap, clipping, full silence, a 960-sample midstream dropout, and frequency offset. All 12 retained their predeclared dispositions. Every vector has a locked SHA-256 in the contract and `eng/gate0/g0.5-lossy-audio-control-result-summary.json`; generation fixes xorshift32 seeds, RMS normalization, binary64 accumulation, rounding, and clamping.

The 20 dB SNR / 0.10 NRMSE pair is intentionally a technical regression floor. The accepted noise control measured at least 23.9999 dB SNR and 0.9980 correlation; the rejected noise control measured at most 15.0000 dB and 0.9846 correlation. Correlation, RMS ratio, and frequency identity prevent SNR alone from hiding polarity, gain, or channel errors. The expected-tone level metric is an amplitude ratio: `sqrt(output tone power / reference tone power)`. Identity measured exactly 1.0; the accepted 95% gain control measured 0.949997–0.949999, inside the same 0.90–1.10 amplitude gate used for RMS ratio. The distinct expected-to-forbidden metric remains a true power ratio: the identity control's weakest value was 4,567:1, establishing a deterministic leakage floor well above its 100:1 threshold. Identity's lowest 960-sample active-window RMS was 0.2567 full scale, while the rejected midstream-dropout control measured zero.

No threshold was derived from the observed AAC maximum sample delta of 9,015 or the Opus delta of 1,859. Those old values do not enter the proposed contract.

## Execution sequence after approval

1. freeze and hash the corrected oracle implementation, approved contract, and 12-control evidence;
2. preflight the retained corpus;
3. decode every distinct retained Stage 1 AAC and Opus artifact with the exact selected P2 decoder, without re-encoding;
4. record raw timing/priming, normalized timing, and quality evidence separately;
5. retain and revalidate every attempted evaluation; and
6. return AAC and Opus dispositions before any route re-encode or pre-matrix smoke.

A retained output may pass, fail, or block. The oracle, timing envelope, or threshold may not be widened after seeing route results without another owner decision.

## Non-claims

The proposed oracle is suitable for deterministic synthetic-tone regression evidence. Traditional signal metrics do not establish perceived quality for arbitrary speech or music, and Gate 0 does not claim otherwise. Codec breadth, independent playback, shipping-runtime selection, and legal conclusions remain separate gates.
