# Gate 0 G0.4 executable delivery-proof results

Status: bounded P2 and optional W1 output proof complete; 11 portable routes passed; two optional Windows routes passed; independent playback partial; default delivery remains conditional

Date: 2026-08-24

Authority: [Gate 0 G0.4 owner decisions](gate-0-g0.4-owner-decisions.md)

## Outcome

The exact `P2.BtbnLgplShared.WindowsX64.20260820` proof profile executed every owner-authorized ordinary Free output route. All eleven portable proof capabilities passed their structural, explicit-component, decode-again, timing, explicit-map, and semantic oracles. The separate G0.3 F8 result remains the evidence for selection among distinguishable multiple video/audio streams. The W1 comparison passed paired and video-only H.264 MP4 using `h264_mf` with the same native AAC route used by P2.

This is executable technical evidence, not a selected shipping runtime, redistribution approval, patent or legal conclusion, performance contract, or final Free default. The separate [independent-playback checkpoint](gate-0-independent-playback-checkpoint.md) now retains partial player evidence without completing the playback matrix. The H.264/AAC and MP3 product rows remain conditional until their retained gates pass.

## Portable P2 verdicts

| Capability | Exact output route | Verdict | Executed evidence |
| --- | --- | --- | --- |
| `Video.Export.Compatibility.Mp4H264Aac.P2OpenH264` | `libopenh264` + native `aac` / MP4 | Passed, conditional candidate | Explicit F1 video/audio maps, `format=yuv420p`, Constrained Baseline request, AAC-LC, `+faststart`, top-level MP4 box parsing, three ordered frame identities, left-440/right-880 Hz audio, and explicit H.264/AAC decode-again. |
| `Video.Export.Compatibility.Mp4H264VideoOnly.P2OpenH264` | `libopenh264` / MP4, audio omitted | Passed, conditional candidate | Exactly one H.264 stream, explicit `-an`, `+faststart`, frame count/order/identity, and native H.264 decode-again. |
| `Video.Export.Open.WebmVp9Opus` | `libvpx-vp9` + `libopus` / WebM | Passed | Explicit F1 maps, pinned VP9/Opus settings, Segment-level Cues parsing, ordered frame identity, stereo-frequency semantics, and explicit VP9/Opus decode-again. |
| `Video.Export.Open.WebmVp9VideoOnly` | `libvpx-vp9` / WebM, audio omitted | Passed | Exactly one VP9 stream, explicit `-an`, Segment-level Cues evidence, ordered frame identity, and native VP9 decode-again. |
| `Audio.Export.M4aAac` | native `aac` / M4A | Passed, conditional candidate | AAC-LC, 48 kHz stereo, explicit MP4-family inspection/AAC decode, duration/sample tolerance, two-channel 1 kHz and opposed-phase semantics. |
| `Audio.Export.Mp3` | `libmp3lame` / MP3 | Passed, conditional candidate | Explicit native MP3 decode, duration/sample tolerance, both-channel frequency and phase semantics. |
| `Audio.Export.OggOpus` | `libopus` / Ogg | Passed | Explicit Opus decode, duration/sample tolerance, both-channel frequency and phase semantics. |
| `Audio.Export.WavPcm` | `pcm_s16le` / RIFF WAV with `rf64=never` | Passed | Exact decoded PCM equality to F4. The greater-than-4-GB/RF64 policy remains a separate long-output decision. |
| `Audio.Export.Flac` | native `flac` / FLAC | Passed | Exact decoded PCM equality to F4. |
| `Image.Export.Png` | native `png` / image2 | Passed | Exact decoded RGB pixel equality to the authored F1 frame. |
| `Image.Export.Jpeg` | native `mjpeg` / image2 | Passed | Explicit `yuvj420p`, quality and Huffman settings, decode-again, and declared RGB mean-absolute-error tolerance. |

Every retained capability record includes its fixture, exact runtime-profile identity and evidence hash, concrete command-token demuxers/decoders/filters/encoders/muxers, stream maps, artifact size and SHA-256, and structured oracle results. Component presence is recorded separately and never counts as semantic proof.

## Optional W1 comparison

The Windows-only runner passed:

- `W1.Video.Export.Mp4H264Aac.MediaFoundation`; and
- `W1.Video.Export.Mp4H264VideoOnly.MediaFoundation`.

Both used `h264_mf`, `format=yuv420p`, hardware encoding **not forced**, CBR 2 Mbit/s, GOP 25, and MP4 `+faststart`; the paired route used native FFmpeg AAC-LC at 192 kbit/s. The output probed as H.264 Constrained Baseline Level 2.0 at 25 fps, and the three-frame identity/order oracle passed with low visual error. The paired AAC sample-count/timing and left-440/right-880 Hz oracles passed.

The wrapper log identified `H264 Encoder MFT`, but the proof could not establish a concrete MFT implementation version or whether Windows selected a hardware or software implementation. `-hw_encoding false` means only that hardware encoding was not forced. Hardware inventory collection was unavailable in the proof environment. W1 therefore remains optional environment evidence and cannot define portable project meaning.

The historical `aac_mf` observation remains outside this approved comparison. W1 deliberately used the same native AAC route as P2.

## Material finding: OpenH264 quality policy

P2 produced and decoded Constrained Baseline Level 2.0 output while requesting `constrained_baseline`, bitrate mode at 2 Mbit/s, GOP 25, and disabled frame skipping. OpenH264 nevertheless logged that the requested layer profile was changed to unspecified and that bitrate cannot be controlled in the selected rate-control modes without enabling frame skipping.

Gate 0 does not enable frame skipping merely to silence that warning: exact frame count and identity are product requirements. The bounded route therefore passes technical delivery semantics but does **not** establish the final quality/rate-control mapping. G0.5 must compare quality, size, resource use, long-form behavior, and a no-frame-loss control policy before this mapping can become a production default.

## Product disposition after proof

- H.264/AAC MP4 remains the mandatory external-beta compatibility/default **target**, with an executable P2 OpenH264/native-AAC candidate and an optional W1 candidate now proven narrowly.
- VP9/Opus WebM remains the proven guaranteed open alternative.
- M4A AAC-LC and MP3 remain conditional Free outputs despite passing executable proof because software-license evidence does not settle codec patent, territory, redistribution, shipping-runtime, or independent-playback questions.
- Ogg Opus, WAV PCM, FLAC, PNG, and JPEG have passed the authorized bounded executable proof. Exact public runtime and notice/audit work still belongs to release engineering.
- No proof here authorizes feature implementation, entitlement infrastructure, bundling, or public distribution.

## Remaining gates and work

1. Complete the remaining rows in the [independent-playback checkpoint](gate-0-independent-playback-checkpoint.md): standalone Chrome/Edge/Firefox, VLC or owner disposition, perceptual sync, and a timestamp-clean long MP4 artifact produced by the approved measured G0.5 route. The default cannot be finalized before this evidence or an explicit owner disposition.
2. Preserve the corrected [G0.4 common-input proof result](gate-0-g0.4-input-proof-results.md): the approved P3 follow-up passed both previously blocked JPEG rows, producing 173 exact passes and 83 failures. F7 authoring is blocked, and the direct-Matroska pilot remains blocked by its required corrected F7 case.
3. Use G0.5 to measure quality, performance, thread/concurrency limits, UI responsiveness, cancellation, cache/disk behavior, and long-form integrity together. Resolve the OpenH264 rate-control warning there.
4. Preserve the exact daily P2 archive in durable project-controlled private artifact storage or re-pin a monthly-retained build before unattended CI depends on it.
5. Keep exact public runtime selection, dependency/SBOM audit, legal review, signing, packaging, and distribution in release engineering.

Gate 0 remains incomplete. Independent playback is partial and remains an explicit exit gate; common-input owner disposition, G0.5 long-form/resource evidence, and durable P2/evidence retention also remain open work.
