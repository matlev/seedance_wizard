# Gate 0 independent-playback checkpoint

Status: partial executable evidence retained; two WebM routes passed native Chromium control checks; MP4 long-corpus preparation and Windows Media Player Legacy playback are blocked; completion gate remains open

Date: 2026-08-25

Authority: [Gate 0 G0.3 owner decisions](gate-0-g0.3-owner-decisions.md) and [Gate 0 G0.4 owner decisions](gate-0-g0.4-owner-decisions.md)

## Outcome

The independent-playback lane now has a reproducible, provenance-bound local harness and partial executable results. The exact P2 G0.4 output evidence was used to prepare longer playback-only artifacts without silently selecting another encoder, decoder, filter, or delivery route.

- Both VP9/Opus WebM routes produced clean 40-segment stream-copy artifacts with strict per-stream packet-timestamp and duration checks. The paired route is 5.127 seconds and the video-only route is 4.800 seconds.
- Both H.264 MP4 routes are **blocked at playback-corpus preparation**. Repeating the already-proven 120 ms artifacts through the authorized stream-copy-only MP4-to-MPEG-TS-to-MP4 path exposed corrupt-packet, discontinuity, and non-monotonic-DTS repair behavior. The runner rejected those outputs instead of accepting repaired timestamps, re-encoding, or substituting another component.
- The two available WebM artifacts passed native `HTMLMediaElement` open, clock advance, pause stability, midpoint seek, resume, near-end completion, exactly one `ended` event, and replay checks in the Codex in-app Chromium environment.
- Windows Media Player Legacy could instantiate and was versioned, but it did not open either available WebM artifact before the declared timeout. That is optional Windows environment evidence, not a portable WebM failure. The two MP4 routes were inherited as corpus-blocked and were not presented as player failures.

This checkpoint does not complete independent playback. It does not prove audible or perceptual A/V synchronization, Chrome/Edge/Firefox product-browser coverage, VLC behavior, MP4 seek/control behavior, a default delivery format, a shipping runtime, redistribution, patents, or legal approval.

## Retained proof corpus

The fresh schema-v2 corpus is machine-local at:

`C:\Users\azure\AppData\Local\Temp\ReelForge-Gate0-G04-PlaybackCorpus-TimestampStrict-20260825-r7`

| Evidence | Length | SHA-256 |
| --- | ---: | --- |
| `manifest.json` | 1,958 | `4DA39919C66DD2E8641BA1A7E2B6991C6ACE3584D9CF6267ADE0D2EADA8DF86B` |
| `g0.4-playback-corpus-evidence.json` | 582,466 | `29B3E97665C45A668F3660DAF85671105E9240E10323ED6CF8073176CE1A2144` |
| `media/vp9-opus.webm` | 133,971 | `D23243129A4F643A6B382B7E8AAFA6C3F7BE8CE212F2943A943E72727981E273` |
| `media/vp9-video-only.webm` | 41,550 | `573B7855B5FA8D6411643530D6E1AA4E48DB0B16A36D992D0B2593436F8F2C59` |

The corpus evidence binds the exact P2 source proof, runtime identity, command logs, source and derived artifacts, packet oracles, copied harness, manifest, and each blocked-route transformation record. The paired WebM oracle also establishes exactly 280 derived audio packets from seven source packets repeated 40 times and a 5.120-second terminal audio timestamp within a 30 ms tolerance. A blocked route is a first-class manifest disposition and cannot be silently served or submitted as passed.

## Native Chromium result

Environment:

- player: Codex in-app browser native `HTMLMediaElement`;
- browser identity: Google Chrome/Chromium `151.0.7922.170`, x86-64, reported through high-entropy client hints; reduced user agent `Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Safari/537.36`;
- host identity: environment version `10.0.26200.0`, registry build `26200.9168`, display version `25H2`, edition `Core`;
- transport: loopback HTTP with validated byte-range support; and
- observation: explicit user-click start, muted technical controls, no perceptual-sync conclusion.

Retained result:

`C:\Users\azure\AppData\Local\Temp\ReelForge-Gate0-G04-BrowserObservation-20260825-r8\playback-observation-20260825T155234290Z-dd4c19fa871d4a5c9526f285855c515b.json`

Length 6,186; SHA-256 `A33FE9212E820C47480835E4A719D427201C6483CA94768B4BF3C7C8ED4FD1DF`.

The server-generated envelope records its receipt time, immutable corpus-manifest/evidence/harness hashes, exact server-script hash, host OS identity, and the validated client observation. Its top-level observation disposition is `completed-with-inherited-blocked-routes`, not `passed`.

| Route | Result | Observed controls |
| --- | --- | --- |
| H.264/AAC MP4 | Inherited blocked; not attempted | Long playback-only corpus could not satisfy the no-repair timestamp policy. |
| H.264 video-only MP4 | Inherited blocked; not attempted | Same corpus-construction block. |
| VP9/Opus WebM | Passed | 320x180; 5.127 s; clock advance; stable pause; seek to 2.5635 s; resume; EOF; one `ended`; replay. |
| VP9 video-only WebM | Passed | 320x180; 4.800 s; clock advance; stable pause; seek to 2.400 s; resume; EOF; one `ended`; replay. |

The player returned `canPlayType("video/webm") == "maybe"`; that advisory string was not used as the verdict. Actual native playback behavior produced the pass.

## Windows Media Player Legacy result

The fresh STA observation is machine-local at:

`C:\Users\azure\AppData\Local\Temp\ReelForge-Gate0-G04-WmpObservation-20260825-r7\g0.4-wmp-observation-evidence.json`

Length 13,163; SHA-256 `53E95AD56A9956C976FBF72973B45352F9AF28A7F4A4948B3AEC9220C655134A`.

Recorded environment:

- Windows environment version `10.0.26200.0`, registry build `26200.9168`, display version `25H2`, edition `Core`;
- Windows Media Player executable `12.0.26100.8457`;
- `WMPlayer.OCX` `12.0.26100.8972`;
- Windows PowerShell 5.1 STA; and
- COM control muted, stopped, closed, and released deterministically.

WMI identity access was denied, so the runner retained that failure and used `Environment.OSVersion` plus the Windows `CurrentVersion` registry values. Both available WebM routes remained at open state 21, play state 9, duration zero, and no reported WMP error through the 10-second open timeout. Their result is `blocked`, not failed. WMP is legacy, optional, Windows-only capability evidence and cannot establish portable project meaning.

## Harness and protocol boundaries

- The loopback server binds only `127.0.0.1`, supports an OS-assigned port, serves only the hash-validated manifest, harness, and available media allowlist, and does not expose the corpus directory.
- GET and HEAD support valid single byte ranges, including suffix ranges. Malformed, multiple, overflow, and unsatisfiable ranges return 416 with the required length context; unsupported methods return 405.
- Result POSTs require an exact bounded byte length, strict UTF-8, the schema-v2 four-route disposition contract, source blocked-route provenance, and atomic canonical JSON retention in an explicit external result directory. Missing/chunked/oversize/malformed submissions are rejected. Results never mutate the immutable playback corpus.
- The browser harness attaches native media elements before the explicit click and starts the first native `play()` synchronously within that activation. It records actual events and technical media state; it does not use FFmpeg or another application decoder as its playback oracle.

## Disposition and next dependency

The default Free delivery contract remains unfinalized.

1. G0.5 should create its approved measured H.264/AAC output directly through the exact reviewed route and reuse that long-form artifact for independent MP4 open/seek/pause/resume/EOF and perceptual-sync checks. Gate 0 must not repair or re-encode this failed short-artifact repetition merely to manufacture a playback pass.
2. The installed standalone Chrome, Edge, and Firefox environments still need actual retained playback runs. The in-app Chromium result is useful technical evidence but is not a substitute for the named product-browser matrix.
3. VLC remains absent. No installation was attempted. Its requirement must be completed later or explicitly dispositioned by the owner.
4. Audible/perceptual A/V synchronization and visual presentation-order checks remain manual acceptance work against an appropriate timed fixture.
5. Windows Media Player Legacy WebM behavior is recorded as capability-qualified and blocked on this host. It is not a required portable baseline.

The common-input decode matrix, durable P2 retention, and G0.5 quality/performance/resource/long-form work remain separate open Gate 0 lanes.
