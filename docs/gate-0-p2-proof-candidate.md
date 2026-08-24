# Gate 0 P2 proof-toolchain candidate

Status: immutable candidate proposed for owner approval; identity inspection complete; no media proof executed

Observed: 2026-08-24

Authority: [Gate 0 Checkpoint A](gate-0-checkpoint-a.md)

## Candidate identity

P2 is a third-party, broad LGPL-path proof input. It is not a proposed shipping binary and its presence in Gate 0 does not approve every compiled component for product use or public distribution.

| Field | Pinned value |
| --- | --- |
| Profile | `P2.BtbnLgplShared.WindowsX64.20260820` |
| Release | BtbN `autobuild-2026-08-20-13-45` |
| Asset | `ffmpeg-n8.1.2-44-g7c533d0f86-win64-lgpl-shared-8.1.zip` |
| Immutable URL | `https://github.com/BtbN/FFmpeg-Builds/releases/download/autobuild-2026-08-20-13-45/ffmpeg-n8.1.2-44-g7c533d0f86-win64-lgpl-shared-8.1.zip` |
| Archive size | `70,835,392` bytes |
| Archive SHA-256 | `D311C8C7B86E06B54588E442652F963BAE165BD4D8393E73CC9EBB445B025547` |
| FFmpeg source identity | `n8.1.2-44-g7c533d0f86`, commit `7c533d0f86f13a06ec93968f6194349665b3536a` |
| Build-repository identity | BtbN commit `48576f197ad1c2afb2e0b8efe204919a1afbff54` |
| Target | Windows x86-64, MinGW, shared FFmpeg libraries |
| Compiler | GCC 15.2.0, crosstool-NG `1.28.0.23_185f348` |

The archive digest reported by the GitHub release API matched the independently downloaded archive. The archive was inspected from a dedicated temporary directory; only identity commands were executed and no media was generated.

## Runtime-closure manifest

CI must verify the archive hash before extraction, extract to an empty isolated directory, and verify every executable and DLL below before executing the pair. It must resolve `ffmpeg` and `ffprobe` from that directory, not from the runner or developer `PATH`. The Windows runner image/build remains part of observed evidence because operating-system DLLs are outside this archive.

| Relative runtime file | SHA-256 |
| --- | --- |
| `bin/avcodec-62.dll` | `C60553FB7CD910A8D67BB429EDD34C3EAC35ABBF2230EA8813BBED1949015BE9` |
| `bin/avdevice-62.dll` | `448ADD5A0AC4C0A28664BB3622EC082E59384DFF53C5CE6D1912814F41AA6733` |
| `bin/avfilter-11.dll` | `12BEF7EBC1742D95831DF04942ADEC2B380B0F14A438BDD26CD2DC515CB52D7F` |
| `bin/avformat-62.dll` | `E7CB09BFC411814F9A46473F0594FB24FA366812270B7BC8F9E48DF9EA1CA1C2` |
| `bin/avutil-60.dll` | `B81B619F150E0386662FDC647A17364734548C35D04F3BAB73ECA401F2DAAFB5` |
| `bin/ffmpeg.exe` | `E41DBF0563AF8B07D8CF1818187500ACDF36AAAFB00186BA12A18C6C661F7EB0` |
| `bin/ffplay.exe` | `7F363BE46A1034D07BCB4D2B336803893294832603CA92AE7501292916ADA081` |
| `bin/ffprobe.exe` | `08AE7637A339009A23BA469AAFC282A16A98E02B2999E14EC1A6C1198FD77221` |
| `bin/swresample-6.dll` | `E0F6C7D5DD2651DDDB959C8F48681F61B1466A44850AF36FE311A536B8600BD1` |
| `bin/swscale-9.dll` | `110AA5706F2F813336AD8942607DE1498186EEE683C89C7DDBC9B8074173A3D1` |

`ffplay.exe` is part of the downloaded runtime closure but is not authorized as a ReelForge dependency or an independent playback oracle.

## Observed build contract

Both `ffmpeg -version` and `ffprobe -version` reported the same version, compiler, configure line, and library versions:

- `libavutil 60.26.102`
- `libavcodec 62.28.102`
- `libavformat 62.12.102`
- `libavdevice 62.3.102`
- `libavfilter 11.14.102`
- `libswscale 9.5.102`
- `libswresample 6.3.102`

The complete observed configuration is:

```text
--prefix=/ffbuild/prefix --pkg-config-flags=--static --pkg-config=pkg-config --cross-prefix=x86_64-w64-mingw32- --arch=x86_64 --target-os=mingw32 --enable-version3 --disable-debug --enable-shared --disable-static --disable-w32threads --enable-pthreads --enable-iconv --enable-zlib --enable-libxml2 --enable-libvmaf --enable-fontconfig --enable-libharfbuzz --enable-libfreetype --enable-libfribidi --enable-vulkan --enable-libshaderc --enable-libvorbis --disable-libxcb --disable-xlib --disable-libpulse --enable-gmp --enable-lzma --enable-liblcevc-dec --enable-opencl --enable-amf --enable-libaom --enable-libaribb24 --disable-avisynth --enable-chromaprint --enable-libdav1d --disable-libdavs2 --disable-libdvdread --disable-libdvdnav --disable-libfdk-aac --enable-ffnvcodec --enable-cuda-llvm --disable-frei0r --enable-libgme --enable-libkvazaar --enable-libaribcaption --enable-libass --enable-libbluray --enable-libjxl --enable-libmp3lame --enable-libopus --enable-libplacebo --enable-librist --enable-libssh --enable-libtheora --enable-libvpx --enable-libwebp --enable-libzmq --enable-lv2 --enable-libvpl --enable-openal --enable-liboapv --enable-libopencore-amrnb --enable-libopencore-amrwb --enable-libopenh264 --enable-libopenjpeg --enable-libopenmpt --enable-librav1e --disable-librubberband --enable-schannel --enable-sdl2 --enable-libsnappy --enable-libsoxr --enable-libsrt --enable-libsvtav1 --enable-libtwolame --enable-libuavs3d --disable-libdrm --enable-vaapi --disable-libvidstab --enable-libvvenc --disable-whisper --disable-libx264 --disable-libx265 --disable-libxavs2 --disable-libxvid --enable-libzimg --enable-libzvbi --extra-cflags=-DLIBTWOLAME_STATIC --extra-cxxflags= --extra-libs=-lgomp --extra-ldflags=-pthread --extra-ldexeflags= --cc=x86_64-w64-mingw32-gcc --cxx=x86_64-w64-mingw32-g++ --ar=x86_64-w64-mingw32-gcc-ar --ranlib=x86_64-w64-mingw32-gcc-ranlib --nm=x86_64-w64-mingw32-gcc-nm --extra-version=20260820
```

The candidate has no `--enable-gpl` or `--enable-nonfree` flag and explicitly disables `libx264`, `libx265`, `libvidstab`, `librubberband`, and `libfdk-aac`. It is nevertheless intentionally broad: it enables AV1 implementations, OpenH264, MP3, acceleration, network, text, and other libraries beyond ReelForge's proposed P2 requirements. Those extra components are fixed observed inputs, not approved product dependencies. Gate 0 proof commands must use only components present in the owner-approved runtime-profile mapping. Any additional component proposed for use requires an explicit dependency disposition first.

## Pair-compatibility rule

A P2 pair is compatible only when all of these conditions hold:

1. The archive, every listed runtime file, the two executable hashes, source identity, compiler identity, full configure line, and library-version rows exactly match this reviewed manifest.
2. Both executables resolve from the same verified extraction directory. A matching human-readable version string is not sufficient.
3. No unlisted application-local DLL can be loaded ahead of the verified runtime closure; CI uses a clean extraction and controlled process environment.
4. Named ffprobe JSON parser-contract probes pass for program/library version reporting, stream/format inspection, rational time bases, and exact-frame timestamp fields.
5. Required semantic capabilities map only to explicitly reviewed encoders, decoders, muxers, demuxers, filters, and protocols observed from this pair.

Any deviation rejects the pair. It does not fall back to a runner-installed or developer-installed executable.

## CI acquisition procedure

After owner approval, CI may:

1. download only the immutable asset URL above;
2. verify the exact archive size and SHA-256 before extraction;
3. cache by the archive SHA-256, never by `latest` or a mutable version label;
4. extract into a clean job-local directory;
5. verify the complete runtime-closure manifest;
6. execute the identity/build/parser probes and compare their normalized observations with this candidate; and
7. execute only the separately approved G0.3 proof matrix.

CI success means only that the runtime matches this reviewed Gate 0 proof profile. This candidate is third-party evidence, not an FFmpeg-official Windows binary, a shipping selection, a legal conclusion, or a license/patent audit.

## Candidate limitation

P2 can prove that the proposed Free capabilities work in a concrete Windows LGPL-path build. It cannot prove that ReelForge's eventual public runtime should contain this build's entire dependency set. A narrower controlled build remains desirable, but it is not reproducibly specified at Checkpoint A and therefore cannot enter executable proof yet.
