# Media test fixtures

`test_video.mp4` is a user-supplied, repository-local fixture used only by network-isolated automated tests.

- SHA-256: `3F2F8F6AF7B559441724BF1F3F9532F9D79017049E174E173679811F30CB9FC8`
- Size: 1,642,405 bytes
- Duration: 10.125 seconds
- Video: H.264, 1344x768, 24 fps
- Audio: AAC-LC stereo, 32 kHz

Tests serve these bytes through custom `HttpMessageHandler` instances. They must never fetch this fixture or any generated output from the public network.

## `degraded_timing_gap.webm`

`degraded_timing_gap.webm` is a synthetic acceptance fixture for Phase 4B timing readiness. It is a small, sequentially decodable VP9/WebM video with a finite positive span and an intentional presentation-timestamp gap after its tenth frame. ReelForge must therefore classify its video stream as **Estimated** with a `DiscontinuousTimestamps` issue rather than Exact or Unusable.

- SHA-256: `F005A77C048912A6964DF6C492A9D66E11FBD473B45ABFC691E536D854339FC7`
- Size: 27,343 bytes
- Timeline span used by the assessment: 1.333 seconds
- Video: VP9, 160x90, 30 decoded frames
- Audio: none

The committed bytes are authoritative and automated tests neither regenerate nor download them. The fixture was generated once with the owner's configured FFmpeg 8.1.2 using the approved LGPL-first VP9/WebM route:

```text
ffmpeg -hide_banner -nostdin -f lavfi -i testsrc2=size=160x90:rate=30 -frames:v 30 -vf "setpts=(N+if(gte(N\,10)\,10\,0))/(30*TB)" -fps_mode vfr -an -c:v libvpx-vp9 -deadline realtime -cpu-used 8 -b:v 160k -pix_fmt yuv420p -threads 1 -y degraded_timing_gap.webm
```

The adjacent `degraded_timing_gap.ffprobe.json` is the compact transcript emitted for these exact bytes by the production timing-assessment invocation against that configured ffprobe. Infrastructure tests replay it through the normal assessment service so CI verifies the expected readiness, span, stream identity, and issue classification without launching an external media tool.

This fixture proves the specific degraded-timing path above. It is not a general promise that every variable-frame-rate or damaged file has the same classification.
