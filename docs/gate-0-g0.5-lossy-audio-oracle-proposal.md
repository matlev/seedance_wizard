# Gate 0 G0.5 lossy-audio oracle proposal

Status: proposed thresholds; owner approval required before retained AAC/Opus evaluation or route re-encode

Authority: [G0.5 Stage 1 owner decisions](gate-0-g0.5-stage1-owner-decisions.md)

## Outcome

The corrected oracle separates three questions that the Stage 1 peak-sample comparator collapsed:

1. **Structure and timing:** correct 48 kHz stereo layout, exact decoded length, presentation endpoints, and recorded codec priming/discard behavior.
2. **Channel semantics:** no channel swap, no silence or clipping, and the correct 440 Hz-left/880 Hz-right identities.
3. **Lossy signal preservation:** signed normalized correlation, normalized RMS error, SNR, RMS gain ratio, DC offset, and expected-versus-other-tone power.

The machine-readable proposal is `eng/gate0/g0.5-lossy-audio-oracle-contract.json`. It applies one quality contract to AAC and Opus. It does not make a perceptual-transparency claim.

## Proposed thresholds

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
| Absolute DC offset | At most 0.005 full scale per channel |
| Expected/other fixture-tone power | At least 100:1 |
| Unexpected clipped samples | Zero |
| Active-channel RMS | At least 0.05 full scale overall and in every 960-sample active window |

The current F1 source is a repeating pure-tone loop and cannot uniquely identify signal lag. The proposal therefore performs no signal alignment. Raw decoder output, packet/frame priming metadata, and content-normalized presentation timing are recorded separately; signal-derived trimming is prohibited.

## Threshold basis

Thresholds were selected before reading any AAC or Opus result under the new metrics. The executable synthetic controls admit deterministic identity, 95% gain, 24 dB SNR noise, and 1% crosstalk. They reject 75% gain, 15 dB SNR noise, polarity inversion, channel swap, clipping, full silence, a 960-sample midstream dropout, and frequency offset. Every vector has a locked SHA-256 in the contract and `eng/gate0/g0.5-lossy-audio-control-result-summary.json`; generation fixes xorshift32 seeds, RMS normalization, binary64 accumulation, rounding, and clamping.

The 20 dB SNR / 0.10 NRMSE pair is intentionally a technical regression floor. The accepted noise control measured at least 23.9999 dB SNR and 0.9980 correlation; the rejected noise control measured at most 15.0000 dB and 0.9846 correlation. Correlation, RMS ratio, and frequency identity prevent SNR alone from hiding polarity, gain, or channel errors. Identity measured exactly 1.0 output/reference expected-tone power; the accepted 95% gain control measured 0.90249–0.90250, inside the proposed 0.80–1.20 gate. The identity control's weakest expected-to-forbidden tone ratio was 4,567:1, establishing a deterministic leakage floor well above the proposed 100:1 threshold. Its lowest 960-sample active-window RMS was 0.2567 full scale, while the rejected midstream-dropout control measured zero.

No threshold was derived from the observed AAC maximum sample delta of 9,015 or the Opus delta of 1,859. Those old values do not enter the proposed contract.

## Execution sequence after approval

1. owner approves or amends the proposed thresholds and raw/content-normalized timing rules;
2. freeze and hash the oracle implementation and approved contract;
3. preflight the retained corpus;
4. decode every distinct retained Stage 1 AAC and Opus artifact with the exact selected P2 decoder, without re-encoding;
5. record raw timing/priming, normalized timing, and quality evidence separately;
6. retain and revalidate every attempted evaluation; and
7. return AAC and Opus dispositions before any route re-encode or pre-matrix smoke.

A retained output may pass, fail, or block. The oracle, timing envelope, or threshold may not be widened after seeing route results without another owner decision.

## Non-claims

The proposed oracle is suitable for deterministic synthetic-tone regression evidence. Traditional signal metrics do not establish perceived quality for arbitrary speech or music, and Gate 0 does not claim otherwise. Codec breadth, independent playback, shipping-runtime selection, and legal conclusions remain separate gates.
