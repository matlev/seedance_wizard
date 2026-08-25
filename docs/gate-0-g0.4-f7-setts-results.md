# Gate 0 G0.4 F7 `setts` experiment results

Status: bounded experiment executed; blocked at the first direct MP4 case

Date: 2026-08-25

Authority: [G0.4 common-input follow-up decisions](gate-0-g0.4-input-follow-up-decisions.md)

## Outcome

The exact `P2.BtbnLgplShared.WindowsX64.20260820` profile contains the FFmpeg `setts` bitstream filter. The approved proof-only experiment applied it through stream copy to the first direct F7 MP4 case and then stopped at the first approved block condition.

The command completed successfully and preserved every encoded video and audio packet payload. It did not preserve the approved video presentation timestamps: the output MP4 replaced each video PTS with its DTS. The unique input packet at PTS 1200 therefore no longer existed after the filter/mux path, its duration remained 43 ticks instead of 800, and the required video presentation end at tick 2000 was not produced.

This is a truthful blocked result, not a process-exit failure, payload rewrite, decoder failure, or harness comparison defect. No WebM case, remaining MP4 case, direct-Matroska pilot, sentinel frame, alternate expression, new component, or weakened oracle was attempted.

## Run identity

| Field | Value |
| --- | --- |
| Experiment | `Gate0.G04.F7.Setts.20260825` |
| Profile | `P2.BtbnLgplShared.WindowsX64.20260820` |
| Contract SHA-256 | `C55506EA14DD697C6EAAAA11AA9ABE2E6BB0BD148C0B8AC05DCFA253C627CF4C` |
| Corrected evidence SHA-256 | `1835D0D38993141539197AD34B2ACF138E63B61EE3F109837C36A5D11C43A13E` |
| Corrected evidence size | 15,180 bytes |
| Output MP4 SHA-256 | `D043E9DB256E6F832F61B5195A8B7CAE9A08CF5ED45FD6CEE28D70D9EAABF54D` |
| Result counts | 0 passed; 1 blocked; 5 not run |
| First blocked case | `V-MP4-H264-MAIN-AAC-MONO-44100-VFR_OFFSET` |
| Block reason | Video packet PTS changed during the `setts` stream-copy/mux path. |

An earlier pre-execution run is retained as superseded evidence with SHA-256 `503BF3DDC5C6A18135AC28596B9E9913A700079949C39770B4A6A3AE12ABB37B`. FFprobe 8.1 emitted a combined `packets_and_frames` collection where the first harness revision expected separate collections. That run stopped during source inspection, before any `setts` remux command executed. The normalization defect was corrected and covered by focused tests before the canonical run above.

## Exact component mapping

`setts` was observed in exact P2 and mapped to FFmpeg source file `libavcodec/bsf/setts.c` at source commit `7c533d0f86f13a06ec93968f6194349665b3536a`. The component source is `LGPL-2.1-or-later`; its binary use here follows P2's explicit LGPLv3 path because the pinned build uses `--enable-version3`. It is part of FFmpeg's `libavcodec`, not a newly acquired external dependency.

The bounded expression was:

```text
setts=duration=if(eq(PTS\,1200)\,800\,DURATION):time_base=1/1000:prescale=1
```

The target was selected by the unique input presentation timestamp, never packet ordinal. Presence and a zero exit code were treated only as prerequisites; the post-mux packet and semantic oracles determined the result.

## Packet evidence

The source uses B-frame reordering, so encoded packet order is DTS order rather than presentation order. The terminal presentation packet is the second encoded packet, which confirms that ordinal selection would have been invalid.

| Encoded packet order | Source PTS | Source DTS | Source duration | Target PTS | Target DTS | Target duration | Video payload |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| 1 | 1000 | 870 | 44 | 870 | 870 | 44 | unchanged |
| 2 | 1200 | 914 | 43 | 914 | 914 | 43 | unchanged |
| 3 | 1120 | 957 | 43 | 957 | 957 | 43 | unchanged |
| 4 | 1040 | 1000 | 40 | 1000 | 1000 | 40 | unchanged |
| 5 | 1130 | 1040 | 40 | 1040 | 1040 | 40 | unchanged |

The source presentation order remains the approved `1000,1040,1120,1130,1200`. In the output, every video PTS equals its retained DTS, the video stream start changes from tick 1000 to tick 870, and PTS 1200 disappears. All audio packet payloads, timestamps, and durations remain unchanged. The two-second container duration does not satisfy the rejected packet-level video semantics.

## Disposition

- The six direct F7 rows remain outside the guaranteed-common matrix.
- The six F7-dependent Matroska rows remain failed or capability-qualified.
- The approved direct-Matroska pilot remains blocked because its required corrected F7 case does not exist.
- The existing 171 passed common-input rows remain valid evidence and are not weakened.
- `setts` does not enter ReelForge product behavior, the portable project contract, shipping-runtime selection, export capability, or public-distribution policy.

The experiment established that this duration-only expression is insufficient on the exact P2 MP4 path. A possible narrower investigation would have to be separately approved and prove explicit PTS/DTS preservation rather than relying on omitted options. Gate 0 must not assume that such a variant will work, and no further experiment is authorized by this result.
