# Gate 0 G0.4 owner decisions

Status: approved; bounded delivery-format proof authorized; independent playback retained

Approved: 2026-08-24

Authority: [Gate 0 media capability charter](gate-0-media-capability-charter.md)

## External-beta compatibility target

H.264/AVC video with AAC-LC audio in MP4 is the mandatory ordinary compatibility/default target for external beta. VP9 video with Opus audio in WebM remains the guaranteed open-delivery alternative.

This establishes a product target only. It does not approve an encoder, shipping runtime, public redistribution, patent position, or completed playback contract. If no acceptable H.264/AAC route survives the approved technical, quality, portability, playback, licensing, and later legal gates, the result returns to the owner as blocked. `libx264` is not an implicit fallback.

## Free output breadth

The approved ordinary Free output families are:

- video: conditional H.264/AAC MP4 and the proven VP9/Opus WebM alternative;
- audio: conditional M4A AAC-LC, conditional MP3, Ogg Opus, WAV PCM, and FLAC; and
- images: PNG and JPEG.

Conditional formats remain unavailable or clearly unsupported until their executable, dependency, playback, and policy gates pass. Approval does not require every format in the first implementation slice and does not select a final shipping runtime.

## Input tiers and preflight

The owner approves guaranteed-common and capability-qualified input tiers. Support is established through content inspection, deterministic explicit stream selection, decode/timing capability, and active-runtime preflight—not by filename extension alone.

Capability-qualified inputs may remain useful and Free where supported, but cannot alter portable project meaning or be advertised as baseline guarantees. Missing capabilities must fail before processing with actionable, profile-aware diagnostics.

## Authorized bounded proof

G0.4 may execute technical proof with these exact component families:

- native AAC;
- `libopenh264`;
- `libmp3lame`;
- PCM/WAV;
- PNG;
- MJPEG/JPEG;
- `h264_mf` through the optional W1 Windows profile; and
- the exact approved AAC route paired with W1.

Video proof must cover:

- paired H.264/AAC MP4;
- H.264 video-only/omit-audio MP4;
- paired VP9/Opus WebM; and
- VP9 video-only/omit-audio WebM.

Audio-only formats remain separate delivery outputs. Every proof records the exact selected encoder, decoder, muxer, demuxer, filter, dependency identity, runtime profile, and fixture.

For the bounded comparison, the portable P2 H.264 candidate uses `libopenh264` with native FFmpeg AAC. W1 compares `h264_mf` with the same native AAC route so the video implementation changes without silently changing the audio dependency. The earlier `aac_mf` wrapper result remains optional historical evidence and is not promoted to the portable/default contract.

This authorization is technical evidence only. It does not authorize bundling, redistribution, public shipping, or commercial reliance on any tested component.

## Retained gate

Independent playback remains required before the default delivery contract is finalized. A blocked H.264 or AAC result is valid and returns for owner decision rather than compromising portability, reproducibility, quality, licensing boundaries, or architecture.
