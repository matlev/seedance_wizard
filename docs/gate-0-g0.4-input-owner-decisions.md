# Gate 0 G0.4 common-input owner decisions

Status: approved; bounded common-input proof executed with partial pass; follow-up dispositions approved with guardrails

Approved: 2026-08-25

Authority: [Gate 0 media capability charter](gate-0-media-capability-charter.md) and [G0.4 common-input proof proposal](gate-0-g0.4-input-proof-proposal.md)

The corrected executable result is recorded in [Gate 0 G0.4 common-input proof results](gate-0-g0.4-input-proof-results.md). The original candidate authorization remains authoritative, but failed or blocked rows did not enter guaranteed-common merely because they were listed here.

The subsequent F7, direct-Matroska, deterministic-JPEG, and durable-retention guardrails are authoritative in the [G0.4 common-input follow-up decision record](gate-0-g0.4-input-follow-up-decisions.md).

## Exact guaranteed-common matrix

The complete bounded matrix is approved for executable proof:

- video: the enumerated MP4 H.264/AAC-LC and video-only envelopes; MOV H.264/AAC-LC, H.264/PCM, and video-only envelopes; WebM VP9/Opus and VP8/Vorbis paired and video-only envelopes; and Matroska with only the explicit allowlisted pairs and corresponding video-only variants;
- audio: WAV PCM, FLAC, MP3, M4A AAC-LC, ADTS AAC-LC, Ogg Opus, and Ogg Vorbis; and
- images: PNG and JPEG.

The guarantee applies only to the enumerated profiles, levels, pixel formats, dimensions, frame rates, timing cases, sample rates, channel layouts, image coding variants, and boundary cases. It does not generalize to arbitrary content sharing a container, extension, or codec name.

Anything outside the exact matrix remains capability-qualified, blocked, rejected, or runtime-unavailable under the approved classification contract. HEVC/H.265, HEIC/HEIF, HDR, greater-than-8-bit media, uncommon chroma, multichannel audio, and other deferred families remain capability-qualified unless separately proven and approved.

## VP8/Vorbis fixture production

P2 `libvpx` in VP8 encoder mode and `libvorbis` are authorized solely to author the approved deterministic VP8/Vorbis and Ogg Vorbis input fixtures. Native P2 `vp8` and `vorbis` remain the decoders under test.

This does not approve those encoders as shipping dependencies, export capabilities, public-distribution components, or portable project requirements.

## H.264 Main/High fixture production

The narrow NVENC fixture-production lane is authorized on the owner's reference RTX 3070 Ti. NVENC may produce only retained, hash-pinned H.264 Main and High Profile test inputs; native P2 `h264` remains the decoder under test.

The producer evidence records and preserves:

- exact P2 runtime identity;
- OS identity;
- GPU and driver identity;
- exact commands;
- raw-source hashes;
- output hashes;
- profile, level, and pixel format; and
- timing and stream metadata.

The bytes require durable project-controlled artifact storage before unattended CI depends on them. NVENC does not become a portable capability, production encoder, shipping route, project dependency, or licensing/distribution conclusion. If the producer fails or is unavailable, the result is blocked for owner review; the common-input contract is not weakened.

## Deterministic stream selection

The default-disposition, lowest-index, and fail-on-unusable-default policy is approved together with proof cases S1-S7.

Later implementation must:

- inspect all relevant streams and exclude attached pictures from ordinary timeline-video selection;
- resolve video and audio independently;
- persist each resolved selection against the source content hash;
- retain stream index, codec identity, disposition, language/title, timing, and the descriptor required for revalidation;
- report ignored or ambiguous alternatives;
- revalidate the same selected streams before every dependent operation; and
- never silently reselect after source-byte or runtime-capability changes.

When a default stream is unusable but an alternate is usable, preflight blocks and clearly reports the alternate. It does not call the source corrupt and does not substitute silently.

A user-facing alternate-stream picker remains deferred unless external testing shows that multilingual, commentary, alternate-angle, or other multi-stream selection is common enough for the Free 1.0 contract.

## Retained boundaries

This authorization remains proof and product-contract work only. It does not authorize changes to import behavior, persistence, render commands, UI, shipping-runtime selection, or public-distribution policy.

Guaranteed-common establishes bounded inspection and complete-decode correctness only. It does not establish real-time editing, proxy performance, long-form integrity, or customer hardware minimums; G0.5 owns those conclusions.

Blocked and failed rows remain first-class results. No verdict may be inferred from extension, component presence, same-runtime output success, concealed error recovery, or fixture-producer availability.
