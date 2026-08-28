# Media dependencies and licensing

Status: Gate 0 final engineering inventory; not legal advice

ReelForge uses an LGPL-first dependency policy. For each mandatory capability, evaluate: (1) the approved LGPL-path FFmpeg/native route, (2) another LGPL-compatible or permissive implementation, (3) an optional OS implementation that does not define portable project meaning, (4) an Enhanced Local Runtime, then (5) narrow or defer the feature.

GPL is never adopted because it is convenient or present on a developer machine. A proposed GPL shipping dependency requires explicit owner approval, documented product need, distribution-architecture review, and qualified legal review.

## Current development profile

The current executable candidate is BtbN `ffmpeg-n8.1.2-44-g7c533d0f86-win64-lgpl-shared-8.1`, source commit `7c533d0f86f13a06ec93968f6194349665b3536a`, built for Windows x64 with `--enable-version3`, shared libraries, no `--enable-gpl`, and no `--enable-nonfree`. The exact archive and tool identities live in [`baseline-profile.json`](../eng/media-runtime/baseline-profile.json).

This is a reviewed LGPLv3-path development candidate, not the selected shipping runtime. `--enable-version3` changes the exact LGPL path; it does not make the build GPL. The retained BtbN daily URL is not a durable distribution source. Release engineering must select, build or obtain, audit, retain, sign, and package the exact public runtime.

## Dependency inventory

| Component | ReelForge role | Source license/path | Classification | Redistribution and legal notes |
| --- | --- | --- | --- | --- |
| FFmpeg libraries/tools | inspect, decode, filter, encode, mux | LGPLv2.1+ by default; current candidate is LGPLv3-path because of enabled version-3 dependencies | Baseline candidate | `--enable-gpl` changes the combined license; `--enable-nonfree` produces an unredistributable build. Exact source/build notices and relinking obligations require release review. [FFmpeg legal](https://ffmpeg.org/legal.html), [license list](https://ffmpeg.org/doxygen/trunk/md_LICENSE.html). |
| libvpx | VP8/VP9 decode and VP9 WebM export | BSD-style with patent/IP grant | Baseline | Preserve license and patent/IP notices. [Upstream](https://github.com/webmproject/libvpx). |
| libopus | Opus decode/encode | BSD-style plus referenced IETF patent statements | Baseline | Preserve license and IPR materials. [License/IPR](https://github.com/xiph/opus/blob/main/LICENSE_PLEASE_READ.txt). |
| libvorbis | Vorbis decode; optional encoding | BSD-style implementation; public-domain specification | Baseline input / Enhanced Local output | Preserve notices. [Xiph Vorbis](https://xiph.org/vorbis/), [source](https://github.com/xiph/vorbis). |
| libopenh264 | H.264 encode candidate | BSD source; Cisco binary and patent terms are separate | Conditional | The BtbN closure does not itself settle patent royalties, territories, or whether Cisco's binary-license coverage applies. Final H.264 route requires qualified legal and release review. [Source license](https://github.com/cisco/openh264/blob/master/LICENSE), [binary releases](https://github.com/cisco/openh264/releases). |
| native FFmpeg AAC | AAC-LC encode/decode | FFmpeg LGPL-path implementation in candidate build | Conditional | Codec patents/territories are separate from code license. M4A/AAC and H.264/AAC distribution require legal review. |
| libmp3lame | MP3 encode | LGPL-2.0-or-later in LAME project | Conditional | Preserve exact LAME license/notices; MP3 patent/territory questions remain a separate legal check. |
| zlib | compressed-stream support | zlib license | Baseline auxiliary | Permissive; audit exact closure and separate contrib components. [Upstream](https://github.com/madler/zlib). |
| libass + FreeType + Fontconfig + FriBidi + HarfBuzz | titles, captions, glyph selection, shaping, fallback | permissive/LGPL-compatible mix in the reviewed candidate | Baseline candidate | Retain exact notices and verify the final binary closure. System fallback is not part of reproducible baseline behavior. |
| Noto Sans, Noto Sans Arabic, Noto Sans CJK SC | Latin/diacritics, Arabic shaping, Simplified Chinese fallback | SIL Open Font License 1.1 | Baseline font plan | Bundle exact pinned binaries with OFL text and reserved-font-name compliance. [Noto source](https://github.com/notofonts/noto-fonts), [usage guidance](https://github.com/notofonts/noto-docs/blob/main/docs/website/use.md). |
| Windows Media Foundation | optional H.264/AAC/MP4 route | platform-provided | Optional Platform | Useful Windows evidence, but not portable project meaning and not a patent/distribution conclusion. [Supported formats](https://learn.microsoft.com/en-us/windows/win32/medfound/supported-media-formats-in-media-foundation), [AAC encoder](https://learn.microsoft.com/en-us/windows/win32/medfound/aac-encoder). |

## Excluded baseline components

The redistributable baseline must not declare or silently use:

- `--enable-gpl` or `--enable-nonfree`;
- `libx264`, `libx265`, `libvidstab`, or `librubberband`;
- GPL-path filters such as `eq` or `hqdn3d`; or
- any GPL component discovered only through an Enhanced Local Runtime.

Approved semantic alternatives include `colorlevels` + `hue` for basic color, native `deshake` or a future permissive stabilizer instead of `libvidstab`, native `nlmeans`/`atadenoise` or a permissive engine for denoise, and product-level time/audio algorithms instead of `librubberband` where acceptable. If these alternatives are not good enough for a genuinely required feature, narrow or defer it before proposing GPL distribution.

## Patent and legal review flags

Before public distribution or commerce, qualified counsel must review at least H.264/AVC, AAC, MP3, applicable territories, the exact OpenH264 acquisition/build route, notices/source-offer/relinking duties for the final FFmpeg closure, and every bundled font/library license. Technical tests cannot resolve these questions.

Release engineering must produce the definitive software bill of materials, source/build provenance, notices, reproducible binary identity, vulnerability/update process, signing, and installer obligations. The development profile and Gate 0 result are inputs to that work, not substitutes for it.
