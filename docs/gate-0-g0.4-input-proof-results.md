# Gate 0 G0.4 common-input proof results

Status: bounded proof and P3 JPEG follow-up executed; 173 exact rows passed and 83 failed; F7 follow-up executed and blocked

Date: 2026-08-25

Authority: [Gate 0 G0.4 common-input owner decisions](gate-0-g0.4-input-owner-decisions.md) and [common-input proof proposal](gate-0-g0.4-input-proof-proposal.md)

The owner approved all three proposed dispositions and later added a narrow `setts` experiment, interim sibling-retention policy, and conditional P3-cleanup rules in the [G0.4 common-input follow-up decision record](gate-0-g0.4-input-follow-up-decisions.md). This result remains the baseline until fresh approved evidence changes an exact row.

## Outcome

The exact `P2.BtbnLgplShared.WindowsX64.20260820` profile executed the approved 256-row common-input matrix. The corrected base run retained 171 passes, 83 failures, and 2 blocked fixture-provenance rows. The later approved [P3 JPEG proof](gate-0-g0.4-p3-jpeg-results.md) executed those two exact blocked rows and passed both, producing the current aggregate of 173 passes and 83 failures. Neither run silently substituted a component, repaired timestamps, inferred support from an extension or component listing, or promoted a failed candidate without fresh approved evidence.

The strongest result is the direct bounded baseline: 103 of 109 non-Matroska video cases, all 30 audio cases, and all 8 image cases passed fresh inspection, explicit native-decoder selection, strict complete decode, and their bound semantic oracles. Thirty-two exact Matroska cases also passed. The remaining failures are the six direct F7 terminal-duration cases and 77 Matroska cases; their timing/provenance problems remain explicit rather than hidden.

This remains proof and product-contract evidence only. It changes no production import behavior, persistence, render command, UI, shipping-runtime selection, public-distribution policy, performance guarantee, or customer hardware floor.

## Corrected canonical evidence

Independent review found that the first full run checked F7 presentation timestamps, intervals, time base, frame identity, and container duration but did not separately enforce the declared 800-tick terminal video-frame duration. Paired audio could therefore make the container two seconds while the final video frame ended early. The oracle was corrected to require both the exact terminal frame duration and exact video presentation end with zero tick tolerance. The earlier 179-pass count is superseded and must not be used.

The corrected retained evidence is:

| Field | Value |
| --- | --- |
| Run | `2026-08-25T13:07:52.5737053-07:00` through `2026-08-25T13:24:03.0707443-07:00` |
| Status | `completed-with-failures` |
| Current aggregate | 256 total; 173 passed; 83 failed; 0 blocked; 0 runtime-unavailable |
| Corrected base run | 171 passed; 83 failed; 2 blocked |
| Contract SHA-256 | `FAD245D5664B49D52565834F01C0430E36CEFFEAB235A7E6BBA460AA5C599BD0` |
| Fixture-source inventory SHA-256 | `EF53040D51229F25FA5C965E415DD62AA93E98623E36BE7CC9942DA2F4DC1595` |
| Retained fixture report SHA-256 | `C10C84827C7D45567EFB92506F86AB0EC8176A20B94D0AFC5E134D64D657D16F` |
| Evidence SHA-256 | `F9D0A742F011BA19D1B7A30B547555D7DE7CC7A64B97F8294DD3CE828FFFD969` |
| Evidence size | 110,117,668 bytes |
| Recorded commands | 1,308 |
| Generated-artifact closure records | 2,228 |
| Local evidence path | `C:\Users\azure\AppData\Local\Temp\ReelForge-Gate0-G04-Input-20260825-corrected\g0.4-input-proof-evidence.json` |

The canonical evidence has since been copied and hash-verified in the approved project-controlled sibling artifact root. It remains one local copy only: the owner's OneDrive client is intentionally disconnected, and no separately backed-up private copy is configured. Hosted CI must not depend on it.

## Verdict summary

| Family | Passed | Failed | Blocked | Result |
| --- | ---: | ---: | ---: | --- |
| MP4, MOV, and WebM video | 103 | 6 | 0 | Every exact direct row passed except the six F7 terminal-duration variants. |
| Matroska video | 32 | 77 | 0 | Thirty-two exact rows passed; 71 executed remux rows failed timing preservation and 6 F7-dependent rows did not execute after their source rows failed. |
| Audio | 30 | 0 | 0 | Every exact WAV, FLAC, MP3, M4A AAC-LC, ADTS AAC-LC, Ogg Opus, and Ogg Vorbis row passed. |
| Images | 8 | 0 | 0 | All three PNG and five JPEG rows passed after the approved P3 follow-up executed the two provenance-blocked rows. |

### Passed Matroska rows

The exact passed Matroska subset is:

- twelve H.264 video-only cases derived from the approved MP4 Baseline, Main, and High cadence rows;
- one MOV H.264 High video-only case;
- three MOV H.264 High plus `pcm_s16le` cases: mono 32 kHz, mono 44.1 kHz, and stereo 48 kHz;
- eight VP9/Opus CFR cases;
- four VP9 video-only cases; and
- four VP8 video-only cases.

These 32 rows are evidence for only their exact contract envelopes. They do not imply arbitrary Matroska, H.264, PCM, VP9, Opus, or VP8 support.

## Material failures and blockers

### F7 terminal presentation

All six direct F7 rows preserved the exact PTS sequence `1000,1040,1120,1130,1200`, intervals `40,80,10,70`, `1/1000` time base, non-zero start, and ordered red/green/blue/white/black frame identity. They failed because the final video frame did not remain present through tick 2000:

- the four MP4 H.264/AAC rows reported terminal duration 43 and presentation end 1243 instead of 800 and 2000; and
- the two WebM VP9/Opus rows reported terminal duration 40 and presentation end 1240 instead of 800 and 2000.

The two-second container duration came from paired audio and is not accepted as video timing proof. The six corresponding Matroska rows were then failed without semantic execution because their source cases had not passed. This is a fixture-authoring/timing failure, not affirmative evidence that the native decoders cannot handle a correctly authored F7 source.

### Matroska stream-copy timing

Seventy-one Matroska rows executed strict complete decode and then failed the source-versus-target remux oracle:

| Affected rows | Count | Preserved | Failed |
| --- | ---: | --- | --- |
| H.264/AAC-derived Matroska | 52 | stream structure and ordered per-stream packet-payload SHA-256 identity | timeline shape, presentation timestamps, frame durations, and container duration |
| H.264/PCM-derived Matroska | 3 | stream structure, presentation timestamps, container duration, and packet payloads | frame-duration identity |
| VP8/Vorbis-derived Matroska | 16 | stream structure, per-stream timeline shape/presentation/durations, and packet payloads | container-duration identity |

The proof rejected these rows even though packet bytes survived stream copy. Four tested timestamp flag variants produced the same H.264/AAC shift; no repair or re-encode was accepted. These failures show that the approved MP4/MOV/WebM-to-Matroska stream-copy fixture route does not preserve the exact timing contract. They do not by themselves prove that directly authored Matroska inputs with the allowlisted codec pairs are undecodable.

### JPEG fixture provenance

The approved [P3 JPEG proof](gate-0-g0.4-p3-jpeg-results.md) resolved the two fixture-provenance blocks with fresh executable evidence. Exact retained `cjpeg` authored progressive 4:2:0 with an explicit C2/sampling oracle; the repository-owned APP1 writer authored orientation=6 without re-encoding. P2 native `mjpeg` passed explicit inspection, strict decode, visual identity, byte-preservation, and exact displayed-orientation oracles. The five exact JPEG rows are now passed; this does not make P3 a shipping dependency or broaden the JPEG envelope.

## Selection and classification policy evidence

All seven selection-policy cases produced the approved result. S1-S5 and S7 passed; S6 intentionally blocked an unusable default while reporting that a usable alternate existed. No alternate was silently selected.

The eight classification cases also produced the approved dispositions: misleading extension and multi-stream/capability-qualified cases passed their policy branches; corrupt/truncated, no-usable-media, and protected cases were rejected; missing decoder was blocked; and an invalid paired runtime was runtime-unavailable. These synthetic policy cases invoked no media command and do not count as decoder evidence.

## Approved follow-up dispositions

### 1. Preserve and re-author F7

**Approved with guardrails:** one bounded fixture-authoring correction must preserve the currently approved five frame identities, PTS sequence, intervals, non-zero start, `1/1000` time base, 800-tick terminal duration, and tick-2000 presentation end. It may adjust only the proof recipe using already approved components. If those semantics cannot be represented without adding a sentinel frame, changing the contract, or using a new component, execution stops and returns a narrower proposal for owner approval.

Without this authorization, the six direct F7 rows and all six dependent Matroska rows remain outside guaranteed-common.

**Executed and blocked:** the initial approved re-authoring components could not carry the terminal duration. The later approved proof-only `setts` experiment then preserved packet payloads but rewrote video PTS to DTS on its first direct MP4 case. The remaining direct cases and Matroska pilot did not run under the approved stop rule. See the [F7 `setts` experiment results](gate-0-g0.4-f7-setts-results.md).

### 2. Replace remux-derived Matroska provenance with direct fixture production

**Approved with a pilot gate:** the bounded direct-Matroska fixture lane may use only the exact P2 proof components already approved for the corresponding encoded sources: `libopenh264` for Baseline H.264, fixture-only `h264_nvenc` for Main/High H.264, native `aac`/`pcm_s16le`, `libvpx-vp9`/`libopus`, fixture-only `libvpx` VP8/`libvorbis`, and the explicit `matroska` muxer. Native `h264`, `aac`, `pcm_s16le`, `vp9`, `opus`, `vp8`, and `vorbis` remain the decoders under test. A representative six-family pilot must pass before expansion. Each direct fixture binds raw authored truth, producer identity, exact command, artifact hash, timing evidence, and a fresh strict complete-decode oracle.

This authorization would not make any producer a shipping dependency or export capability. If direct production still cannot meet timing truth, the affected rows remain failed or capability-qualified; the oracle is not weakened.

Without this authorization, the truthful smallest baseline is the 32 passed Matroska rows only.

### 3. Add a deterministic JPEG proof producer

**Executed and passed:** `P3.LibjpegTurboCjpeg.WindowsX64.3.2.0` acted only as third-party fixture-production proof infrastructure. The exact official VC x64 asset, installer/executable closure, upstream provenance, Authenticode identity, and IJG/Modified BSD materials were verified before execution. Both authorized rows passed the native P2 `mjpeg` semantic proof and are retained under manifest SHA-256 `8FC6FF0C427BF345EE54AD0198F85B6890356A12548C9D6A912C57EC9E937785`. See the [P3 JPEG results](gate-0-g0.4-p3-jpeg-results.md).

Official libjpeg-turbo documentation identifies progressive encoding and explicit sampling controls, and its license record describes the IJG and Modified BSD obligations for `cjpeg`: [usage](https://github.com/libjpeg-turbo/libjpeg-turbo/blob/3.2.0/doc/usage.txt), [release](https://github.com/libjpeg-turbo/libjpeg-turbo/releases/tag/3.2.0), and [license](https://github.com/libjpeg-turbo/libjpeg-turbo/blob/3.2.0/LICENSE.md). This is not a shipping, bundling, redistribution, or release-runtime approval.

The result applies only to the exact approved JPEG contract. It does not approve arbitrary JPEG variants or P3 for shipping, bundling, redistribution, or product encoding.

## Gate status

G0.4 common-input execution and the deterministic JPEG follow-up are complete. The bounded F7 follow-up is complete with a blocked result, and the direct-Matroska pilot remains blocked by its required corrected F7 case. The current aggregate is 173 passed and 83 failed. Independent playback, a separately backed-up second artifact copy, and G0.5 quality/performance/long-form work remain separate open Gate 0 exit conditions.
