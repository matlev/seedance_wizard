# Gate 0 G0.4 common-input proof proposal

Status: approved and executed; P3 follow-up complete; current aggregate 173 passed and 83 failed

Date: 2026-08-25

Authority: [Gate 0 media capability charter](gate-0-media-capability-charter.md), [Gate 0 Checkpoint A](gate-0-checkpoint-a.md), and [Gate 0 G0.4 owner decisions](gate-0-g0.4-owner-decisions.md)

Execution results and the three remaining owner dispositions are recorded in [Gate 0 G0.4 common-input proof results](gate-0-g0.4-input-proof-results.md). This proposal remains the approved candidate scope; only exact rows that passed executable proof may enter the guaranteed-common baseline.

## Desired outcome

Establish the smallest truthful Free input contract that lets ReelForge inspect a file by content, choose media streams deterministically, decode the selected content through the active approved runtime profile, and retain usable timing. The result classifies exact rows as guaranteed-common, capability-qualified, blocked, or rejected before processing.

This is proof and product-contract work only. It does not change import behavior, persistence, project meaning, render commands, UI, the selected shipping runtime, or public-distribution policy.

## Verified starting point

- P2 already passed bounded H.264/AAC MP4, VP9/Opus WebM, M4A/AAC, MP3, Ogg Opus, WAV PCM, FLAC, PNG, and JPEG decode-again checks. Those results prove the exact small output artifacts, not a general user-input contract.
- F7 already proves signed non-zero presentation timestamps, variable frame rate, and presentation-order identity. F8 already proves explicit selection of two distinguishable video and two distinguishable audio streams. These remain reusable semantic evidence rather than being rebranded as broad format support.
- Production import currently classifies by extension, then optionally inspects only the first video and first audio stream. Missing ffprobe does not block import. Production metadata does not represent all stream indices, default dispositions, language, start time, or a decode/preflight result.
- The proof-only paired-runtime observer can establish P2 identity and component presence, but broad P2 presence is not permission to execute an unapproved component and is not a capability verdict.

## Classification contract

### Guaranteed-common

An exact row may enter the guaranteed-common contract only when the approved profile:

1. matches the pinned paired-runtime identity and complete file closure;
2. reports the exact demuxer and decoder components required by the row;
3. identifies the content and all relevant streams without relying on the extension;
4. applies the declared deterministic stream-selection policy;
5. explicitly selects the required native decoder before input and the intended stream after input;
6. decodes the complete bounded artifact without concealed error recovery;
7. passes structure, timing, presentation-order, frame or audio identity, and absence/presence oracles; and
8. passes the negative preflight cases applicable to that family.

Technical decode support is not a real-time editing guarantee. G0.5 owns performance, proxy, long-form, resource, and responsiveness conclusions.

### Capability-qualified

A row outside the exact guarantee may remain useful and Free when the active runtime can inspect and decode it. It must be labeled with the active profile, preflight before processing, and never alter portable project meaning. Presence alone remains insufficient.

### Rejected, blocked, and runtime-unavailable

- **Rejected before processing** is reserved for corrupt, truncated, protected/encrypted, structurally unusable, or explicitly rejected source content.
- **Blocked** means the source may be sound but a required demuxer/decoder or another declared capability is absent from the active otherwise-valid profile.
- **Runtime unavailable** means the tool pair is absent, mixed, drifted, parser-incompatible, or otherwise invalid before content capability can be assessed.

All three stop dependent work before it starts. Diagnostics name the source/runtime distinction and active profile and preserve the source.

## Proposed guaranteed-common matrix

The matrix is deliberately not a Cartesian product. A Matroska demuxer does not make every codec combination guaranteed, and a decoder name does not prove every profile, bit depth, chroma format, or timing shape.

### Video

| Capability row | Exact container and stream variants | Bounded baseline envelope | Explicit P2 input components | Required evidence |
| --- | --- | --- | --- | --- |
| `Input.Video.Mp4.H264Aac` | ISO-BMFF MP4 with H.264 plus AAC-LC; H.264 video-only | progressive 8-bit `yuv420p`; H.264 Baseline, Main, or High at level 4.2 or below; at most 1920x1080 and 60 fps; AAC-LC mono/stereo at 44.1/48 kHz; enumerated CFR plus one VFR/non-zero-PTS case | `mov,mp4,m4a,3gp,3g2,mj2`; native `h264`; native `aac` | paired and no-audio structure; explicit decode and maps; profile/level/pixel format; frame identity/order; AAC tone/channel/sample timing; edit-list/start-time evidence |
| `Input.Video.Mov.H264Audio` | QuickTime MOV with H.264 plus AAC-LC or `pcm_s16le`; H.264 video-only | the exact enumerated 1920x1080 High level 4.2-or-below cases at `30000/1001`; AAC-LC mono/stereo at 44.1/48 kHz; PCM 16-bit mono/stereo at 32/44.1/48 kHz | same MOV-family demuxer; native `h264`, `aac`, and `pcm_s16le` | both audio variants; no-audio case; MOV timing/edit-list behavior; decoded video/audio oracles |
| `Input.Video.Webm.Vp9Opus` | WebM with VP9 plus Opus; VP9 video-only | VP9 profile 0 at level 4.1 or below, progressive 8-bit `yuv420p`, at most 1920x1080 and 60 fps; Opus mono/stereo; enumerated CFR plus one VFR/non-zero-PTS case | `matroska,webm`; native `vp9`; native `opus` | paired/no-audio structure; cues/timing; explicit decode/maps; frame identity/order; audio tone/channel/sample timing |
| `Input.Video.Webm.Vp8Vorbis` | WebM with VP8 plus Vorbis; VP8 video-only | progressive 8-bit `yuv420p` at most 1920x1080 and 60 fps; Vorbis mono/stereo at 44.1/48 kHz | `matroska,webm`; native `vp8`; native `vorbis` | same bounded structural, timing, selection, video, and audio oracles; fixture production requires the separately approved `libvpx` VP8 and `libvorbis` encoders |
| `Input.Video.Matroska.BaselinePairs` | Matroska with only H.264/AAC-LC, H.264/PCM, VP9/Opus, or VP8/Vorbis; corresponding video-only variants | the same codec envelopes above | `matroska`; the exact native decoders named above | stream-copy remux provenance; exact pair allowlist; explicit decode/maps; no inference that other Matroska combinations are guaranteed |

This interprets “MP4/MOV with H.264 plus AAC/PCM where present” by container convention: ordinary MP4 guarantees H.264/AAC, while MOV adds the PCM variant. It does not promise PCM in every MP4 brand.

#### Enumerated video proof cases

The machine-readable contract expands the grouped product rows into these discrete retained cases. Every paired `CFR-set` expands into the full cross-product of mono/stereo, the row's declared audio sample rates, and exactly `24000/1001`, `25/1`, `30000/1001`, and `60/1` fps. Every `VIDEOONLY-CFR-set` expands into four named cadence subcases. It is never one fixture generalized to multiple layouts, sample rates, or cadences.

| Case ID | Exact variant |
| --- | --- |
| `V-MP4-H264-BASELINE-AAC-CFR-set` | 1280x720, Constrained Baseline, level at or below 3.2, paired AAC-LC mono/stereo at 44.1/48 kHz |
| `V-MP4-H264-MAIN-AAC-CFR-set` | 1920x1080, Main, level at or below 4.2, paired AAC-LC mono/stereo at 44.1/48 kHz |
| `V-MP4-H264-HIGH-AAC-CFR-set` | 1920x1080, High, level at or below 4.2, paired AAC-LC mono/stereo at 44.1/48 kHz |
| `V-MP4-H264-{BASELINE,MAIN,HIGH}-VIDEOONLY-CFR-set` | the corresponding three video envelopes at all four enumerated cadences, no audio; expands to twelve exact cases |
| `V-MP4-H264-MAIN-AAC-{MONO,STEREO}-{44100,48000}-VFR-OFFSET` | five distinguishable frames with the approved F7 presentation intervals and a signed non-zero start; expands to four exact cases |
| `V-MOV-H264-HIGH-AAC-CFR-set` | 1920x1080 High at `30000/1001`; AAC-LC mono/stereo at 44.1/48 kHz; expands to four exact cases |
| `V-MOV-H264-HIGH-PCM-CFR-set` | same video; `pcm_s16le` mono/stereo at 32/44.1/48 kHz; expands to six exact cases |
| `V-MOV-H264-HIGH-VIDEOONLY-CFR` | same video, no audio |
| `V-WEBM-VP9-P0-OPUS-CFR-set` | 1280x720 at the four enumerated cadences, profile 0, Opus mono/stereo 48 kHz |
| `V-WEBM-VP9-P0-VIDEOONLY-CFR-set` | 1920x1080 profile 0 at all four enumerated cadences, no audio |
| `V-WEBM-VP9-P0-OPUS-{MONO,STEREO}-VFR-OFFSET` | approved F7 presentation intervals and signed non-zero start, Opus 48 kHz; expands to two exact cases |
| `V-WEBM-VP8-VORBIS-CFR-set` | 1280x720 at the four enumerated cadences, Vorbis mono/stereo at 44.1/48 kHz |
| `V-WEBM-VP8-VIDEOONLY-CFR-set` | 1920x1080 at all four enumerated cadences, no audio |
| `V-MKV-*` one-for-one remux cases | every approved MP4/MOV/WebM source case compatible with an allowlisted Matroska pair receives one distinct `V-MKV-...` case with the complete source suffix retained; no aggregate remux verdict is allowed |

The guaranteed video contract is exactly the fully expanded cases above, including their audio-layout/sample-rate and video-only variants. The bounded fixtures are 2-5 seconds. Their pass establishes the declared format/profile/cadence semantics only; it does not establish long-form integrity, arbitrary bitrate performance, or real-time editing. Streams whose declared raster, cadence, profile, level, bit depth, chroma, audio layout, or sample rate falls outside these cases remain capability-qualified. G0.5 sets measured bitrate/resource and long-form limits.

### Audio

| Capability row | Bounded baseline envelope | Explicit P2 input components | Required evidence |
| --- | --- | --- | --- |
| `Input.Audio.WavPcm` | RIFF WAV, `pcm_s16le`, mono/stereo, 32/44.1/48 kHz; ordinary non-RF64 size | `wav`; `pcm_s16le` | exact decoded PCM, declared header length, channels/layout/sample rate; malformed length is rejected without `ignore_length` |
| `Input.Audio.Flac` | native FLAC, 16-bit mono/stereo, 32/44.1/48 kHz | `flac`; `flac` | byte-exact decoded PCM and complete sample count |
| `Input.Audio.Mp3` | MPEG Layer III, mono/stereo, 44.1/48 kHz | `mp3`; native `mp3` | decoded duration/sample padding tolerance, frequency/channel semantics, complete decode |
| `Input.Audio.M4aAac` | M4A with AAC-LC, mono/stereo, 44.1/48 kHz | MOV-family demuxer; native `aac` | AAC profile, priming/padding-aware timing, frequency/channel semantics |
| `Input.Audio.AdtsAac` | ADTS AAC-LC, mono/stereo, 44.1/48 kHz | `aac`; native `aac` | content inspection independent of `.aac`; complete decode and timing/tone semantics |
| `Input.Audio.OggOpus` | Ogg Opus, mono/stereo, 48 kHz | `ogg`; native `opus` | codec-delay-aware sample/timing and frequency/channel semantics |
| `Input.Audio.OggVorbis` | Ogg Vorbis, mono/stereo, 44.1/48 kHz | `ogg`; native `vorbis` | complete decode, duration/sample tolerance, and frequency/channel semantics; fixture production requires separately approved `libvorbis` |

Twenty-four-bit PCM/FLAC, RF64, multichannel layouts, unusual sample formats/rates, and protected audio remain capability-qualified until separately proven.

The machine contract expands each audio family into discrete case IDs rather than inferring a range:

- `A-WAV-PCM16-{MONO,STEREO}-{32000,44100,48000}` and `A-FLAC-PCM16-{MONO,STEREO}-{32000,44100,48000}` each expand to six exact cases;
- `A-MP3-{MONO,STEREO}-{44100,48000}`, `A-M4A-AACLC-{MONO,STEREO}-{44100,48000}`, `A-ADTS-AACLC-{MONO,STEREO}-{44100,48000}`, and `A-OGG-VORBIS-{MONO,STEREO}-{44100,48000}` each expand to four exact cases; and
- `A-OGG-OPUS-{MONO,STEREO}-48000` expands to two exact cases.

Fixture production uses declared representative rates: 96 kbit/s mono and 192 kbit/s stereo for AAC/MP3, 64/128 kbit/s for Opus, and the pinned Vorbis quality mapping recorded by the proof contract. These are fixture identities, not arbitrary-bitrate performance guarantees; G0.5 owns bitrate/resource thresholds.

### Images

| Capability row | Bounded baseline envelope | Explicit P2 input components | Required evidence |
| --- | --- | --- | --- |
| `Input.Image.Png` | one non-interlaced 8-bit RGB or RGBA PNG; at most 8192 pixels on either axis and 40 megapixels | `image2` with explicit single-image handling; native `png` | exact RGB/RGBA pixels, geometry, alpha, boundary-size decode, and no accidental 25 fps sequence semantics |
| `Input.Image.Jpeg` | one 8-bit baseline or progressive DCT JPEG with 4:2:0 or 4:2:2 subsampling; at most 8192 pixels on either axis and 40 megapixels | `image2` with explicit single-image handling; native `mjpeg` | geometry and tolerance-based RGB identity for each coding/subsampling case, boundary-size decode, deterministic EXIF-orientation fixture, and displayed-orientation oracle |

The machine contract enumerates `I-PNG-RGB8`, `I-PNG-RGBA8`, `I-PNG-RGBA8-BOUNDARY`, `I-JPEG-BASELINE-420`, `I-JPEG-BASELINE-422`, `I-JPEG-PROGRESSIVE-420`, `I-JPEG-EXIF-ORIENTATION`, and `I-JPEG-BOUNDARY`. The boundary cases are exactly 8000x5000 (40 megapixels). Animated/interlaced or 16-bit PNG, image sequences, CMYK or unusual JPEG, and specialty metadata remain capability-qualified unless later product evidence makes them necessary.

## Shared selection and timing policy

For each requested media type:

1. inspect all streams and exclude attached pictures from ordinary video selection;
2. if one or more streams are marked `default`, evaluate the lowest-index default first: select it only when usable, otherwise fail preflight rather than silently switch creative meaning;
3. if no stream is marked default and exactly one usable stream exists, select it;
4. if no stream is marked default and several usable streams exist, select the lowest index and record every ignored alternative;
5. if no usable stream exists, reject the source for the requested operation with a distinct no-usable-stream result; and
6. retain separate resolved video and audio selections as internal project metadata so every later operation uses the same streams, without requiring a 1.0 user-facing stream override UI.

Each resolved selection is bound to the asset content hash and retains the media type, stream index, codec identity, default disposition, language/title when present, and the full observed descriptor needed to revalidate the same choice. The import/preparation flow commits the video and audio selections only after successful inspection and preflight. Every dependent operation revalidates the content hash, selected descriptors, and active-profile decode capability. Changed bytes, a missing selected stream, or a profile that cannot decode the already-selected stream blocks the operation; it never triggers silent reselection. A missing media type is represented explicitly, not by borrowing the other selection.

These selections are reproducibility metadata, not FFmpeg-shaped Core concepts. Their typed Core/Application/persistence contract belongs to the later feature implementation preflight. User-controlled alternate-stream selection remains post-1.0 unless external validation shows it is required.

The proof adds these exact selection-policy fixtures beyond F8:

| Case ID | Policy branch and oracle |
| --- | --- |
| `S1-OneUsable` | one usable stream is selected and its full descriptor retained |
| `S2-OneDefault` | several usable streams; the single default wins even when it is not the lowest index |
| `S3-NoDefault` | several usable streams and no default; the lowest index wins and all alternatives are reported |
| `S4-MultipleDefaults` | several defaults; the lowest default index wins and the ambiguous defaults are reported |
| `S5-AttachedPicture` | attached-picture video is excluded from timeline-video candidates but retained in inspection metadata |
| `S6-UndecodableDefault` | a marked-default stream lacks its required decoder while an alternate is decodable; preflight blocks without fallback |
| `S7-Descriptors` | language, title, disposition, stream index, codec, and time-base descriptors survive inspection and selection evidence |

The fixtures use distinguishable video frames and audio tones. Evidence binds the complete observed stream list, selected video/audio descriptors, ignored descriptors, and the reason for the decision; an asserted index alone cannot pass.

The proof records signed start PTS, time base, packet/frame PTS and DTS, presentation order, duration source, and edit-list behavior. It never normalizes timestamps merely to make inspection pass. Presentation/UI normalization remains a separate application behavior.

## Negative and diagnostic matrix

| Case | Required result |
| --- | --- |
| Valid content with a misleading or unknown extension | Content inspection identifies the actual family; extension is advisory only. |
| Recognized extension with corrupt or truncated bytes | Rejected before processing as corrupt/uninspectable; no decoder-success claim. |
| Structurally valid file with no stream usable for the requested operation | Rejected with “no usable video/audio/image stream,” distinct from corrupt input. |
| Approved container with a decoder removed from a synthetic observed-capability assessment | `Blocked`, not `Rejected`: profile-aware preflight stops before FFmpeg execution and names the missing decoder and profile. |
| Multiple distinguishable streams | Selection follows the declared policy, maps the exact chosen index, and reports ignored alternatives. F8 remains the base oracle. |
| Unsupported profile, bit depth, chroma format, channel layout, or protected/encrypted content | Classified capability-qualified or rejected before processing; never silently converted into a guarantee. |
| Active tool pair absent, mixed, drifted, or parser-incompatible | `Runtime unavailable`, distinct from both blocked capability and unsupported source content. |

## Fixture and evidence design

- Repository-generated raw F1/F2/F3/F4/F7/F8 primitives remain the independent authored truth.
- Existing passed G0.4 artifacts may seed no-new-component rows, but each input result gets a fresh inspect-and-complete-decode verdict; output success is not reused as the input verdict.
- Container variants are produced by explicit stream-copy remux where possible. Every transformation records source hashes and exact muxer; a remux does not claim broader codec interoperability.
- Main/High H.264 fixtures must come from an independently dispositioned encoder path because OpenH264 produced only Constrained Baseline in the retained P2 and W1 evidence. Baseline-only H.264 is not credible as the final common-input guarantee.
- The recommended narrow producer is P2's observed `h264_nvenc` wrapper on the owner's reference RTX 3070 Ti, used only to produce retained Main/High test bytes. The producer record pins P2, OS, GPU/driver identity, exact command, output hashes, profile/level/pixel format, and raw-source hashes. P2's native `h264` decoder remains the component under test. NVENC does not become a portable capability, project dependency, shipping route, or distribution conclusion.
- VP8/Vorbis fixtures require explicit execution of P2 `libvpx` in VP8 mode and `libvorbis`. Their encoders only author retained test input; native `vp8` and `vorbis` remain the decoders under test.
- JPEG EXIF orientation is authored deterministically at the byte/metadata level so proof does not depend on a system image library.
- Large or policy-sensitive derived fixtures remain outside Git with exact hashes and project-controlled retention. CI remains opt-in until all required bytes have durable retention.

The decode policy is executable rather than inferred from exit code alone. Each positive decode uses the exact version-supported fatal-error controls (`-xerror` plus the approved strict decoder error-detection setting), retains complete stderr, rejects undeclared corrupt-packet, concealment, discontinuity, invalid-data, or timestamp-repair diagnostics, and asserts the exact expected decoded frame count or codec-delay-aware audio sample envelope. Any codec-specific benign diagnostic must be predeclared by exact row and text pattern; a new warning blocks the row for review. Corrupt and truncated negative fixtures demonstrate that partial output cannot be promoted to a pass.

Every retained verdict binds the exact contract, P2 runtime evidence, source/derived fixture closure, concrete demuxer and decoders, stream selectors, command logs, artifact hashes, and structured oracles. Failed or blocked rows remain first-class results.

## Architecture and later implementation implications

- Gate 0 does not edit the current extension lists or import services.
- Feature implementation will need an Application-owned, engine-neutral inspection/preflight result and Infrastructure mapping to the active runtime profile.
- Inspection must represent all relevant streams, dispositions, language/labels, signed timing, and resolved selection. This requires typed DTO/mapping and persistence work; it is not folded into the proof harness.
- Core/project meaning stores semantic media identity and the stable resolved selection, not executable paths, FFmpeg decoder names, or platform codec details.
- A missing decoder and a corrupt source are separate errors. Capability-qualified rows remain Free when available but cannot make an ordinary guaranteed project non-portable.
- Threading, proxy requirements, 4K behavior, long-form resource use, and UI responsiveness remain G0.5 work.

## Acceptance criteria for the bounded proof

1. The contract enumerates every exact guaranteed row and every authorized fixture-production component; no listed P2 component is used accidentally.
2. Every row validates P2 identity and component presence before fixture transformation or decode.
3. Every command explicitly records the demuxer observed, decoder selected, stream map, fixture, and output sink.
4. Paired, video-only, mono/stereo, alpha, VFR/non-zero-PTS, and multi-stream cases are exercised where declared.
5. Lossless rows are byte-exact; lossy rows use predeclared frame/audio identity and timing tolerances.
6. Negative cases prove classification and preflight behavior without invoking an unapproved provider or product code path.
7. The runner writes truthful structured evidence on both pass and failure, rejects repository/stale output, and retains exact artifact closure.
8. Independent review finds no guarantee inferred from extension, component presence, same-runtime output success, or concealed timestamp/error repair.

## Owner decisions

The owner approved all four decisions as proposed in the [G0.4 common-input owner-decision record](gate-0-g0.4-input-owner-decisions.md). The recommendations below are retained as the approved rationale and scope.

### 1. Exact guaranteed-common matrix

**Approved:** the exact candidate rows and bounded envelopes above enter executable proof. The earlier owner decision approved the two-tier model; this decision approves the exact rows.

- Narrowing H.264/AAC or ordinary PNG/JPEG would block common phone, camera, browser, and AI-generator sources from the portable guarantee. Baseline-only H.264 is not recommended as credible ordinary support.
- Moving VP8/Vorbis or Ogg Vorbis to capability-qualified would mainly affect older WebM/Ogg sources while preserving the VP9/Opus open path.
- Moving MOV PCM to capability-qualified would narrow lossless camera/interchange input but leave ordinary MP4/AAC intact.
- Rejecting the narrow Matroska pairs would avoid an MKV guarantee but reduce low-friction import for otherwise approved codec pairs.

Approval prevents an unbounded “any MKV/MP4” promise; amendment is valid when the workflow consequence is accepted explicitly.

### 2. VP8/Vorbis proof-component expansion

**Approved:** P2 `libvpx` in VP8 encoder mode and `libvorbis` may solely author deterministic VP8/Vorbis input fixtures, with native P2 `vp8`/`vorbis` as the decoders under test. This is proof infrastructure only and does not approve a shipping dependency, export contract, public distribution, or every component compiled into P2.

If not approved, WebM VP8/Vorbis and Ogg Vorbis remain capability-qualified rather than guaranteed-common.

### 3. H.264 Main/High fixture producer

**Approved:** the narrow NVENC fixture-production lane described above may produce retained H.264 Main/High inputs. A producer failure or unavailable driver is a valid blocked result and returns for a different fixture-provenance decision; it does not weaken the H.264 input envelope or promote W1/NVENC into the portable baseline.

If not approved, Gate 0 can immediately prove only Constrained Baseline H.264 and must leave the ordinary H.264 common-input contract incomplete pending another approved, hash-pinned Main/High fixture source.

### 4. Deterministic stream-selection policy

**Approved:** the default-disposition/lowest-index/fail-on-unusable-default policy and S1-S7 proof cases above. Later implementation persists separate resolved video and audio selections bound to the asset hash, revalidates them before every dependent operation, and never silently reselects under a different runtime profile. No 1.0 alternate-stream selection UI is proposed.

If target-user validation shows multilingual, commentary, or alternate-angle selection is common, the UI/persistence override moves into the Free 1.0 contract rather than being silently deferred.

## Work after approval

The exact contract, structural tests, proof-only runner, bounded local corpus, corrected full run, and independent review are complete. The approved P3 follow-up passed the two fixture-provenance rows that were initially blocked, producing the current 173-pass, 83-failure aggregate without substitution. The F7 correction is blocked, so the required direct-Matroska pilot remains blocked. See the [result record](gate-0-g0.4-input-proof-results.md) and [P3 JPEG results](gate-0-g0.4-p3-jpeg-results.md). A separate private artifact copy remains required before unattended CI use.
