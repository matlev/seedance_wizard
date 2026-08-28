# Gate 0 final summary

Status: closed with explicit release/legal/playback conditions

Gate 0 asked which codecs, containers, filters, libraries, platform facilities, and runtime capabilities ReelForge can safely build its planned media features against. It is closed with a practical LGPL-first development contract, a small runtime profile/validator, and representative smoke coverage. It does not select or authorize a public redistributable binary.

## Work and evidence used

Gate 0 reviewed the product's current FFmpeg assumptions, selected an exact Windows x64 LGPLv3-path development candidate, added a platform-neutral paired-runtime observation/validation seam, and directly exercised representative inspection, editing, compositing, text, input, delivery, playback, audio-quality, marker, cancellation, and performance behaviors.

Useful direct results were distilled without preserving the former proof-retention hierarchy:

- thirteen semantic capability families passed against the exact P2 runtime, including inspection/selection, exact frame and trim behavior, normalized concat, deterministic audio mix, transform/alpha/basic color, transitions, waveform, proxy, open delivery, standalone audio, and Unicode titles/captions;
- the bounded input study produced 173 passing rows: 103 direct MP4/MOV/WebM video cases, 32 Matroska cases, all 30 audio cases, and all 8 image cases;
- all eleven ordinary portable output routes and two optional Windows H.264 variants produced valid representative outputs;
- VP9/Opus and OpenH264/native-AAC routes passed retained lossy-audio evaluation, every-frame marker survival, and the later representative replacement smoke;
- baseline and typical 720p runtime-route compositions passed for MP4 one-thread and WebM one/eight-thread candidates;
- the latest six `stress-720p-webm-eight` attempts passed descriptor, encode, probe, video timing/identity, audio timing/quality, cleanup, and orphan checks. Their later V2 retention rejection was a producer-label metadata defect and does not invalidate the media result;
- representative WebM paired and video-only outputs opened, played, paused, sought, resumed, ended, and replayed in Chromium;
- fresh representative MP4 and WebM outputs opened, decoded, played, paused, resumed, and reached end-of-media in the available Chromium engine; MP4 seeking succeeded, while the fresh WebM smoke output's HTTP seek remained inconclusive even though the earlier retained WebM playback corpus passed seeking; and
- the installed Windows Media Player automation interface did not load the fresh MP4 within the bounded check, so Gate 0 records that player route as unsupported on this host rather than claiming broad Windows-player compatibility; and
- the product-neutral WPF no-media control passed, while media-load responsiveness was intentionally not extrapolated to product behavior.

These automated controls plus the retained stream/timing/audio evidence are technical playback checks. They do not substitute for a human audible-sync observation. Perceptual A/V sync and a second ordinary Windows-player compatibility pass remain release-candidate/manual acceptance work; they keep MP4 Conditional rather than holding the development baseline open.

## Final decisions

- [`media-runtime-contract.md`](media-runtime-contract.md) is the semantic capability, input/output, and provisional resource contract for desktop 1.0 implementation.
- [`media-dependencies-and-licensing.md`](media-dependencies-and-licensing.md) is the concise dependency and license inventory.
- [`baseline-profile.json`](../eng/media-runtime/baseline-profile.json) is the machine-readable development profile. It is not the selected shipping runtime.
- The LGPL-first dependency policy is an accepted supply-chain decision in [ADR-0001](adr/0001-lgpl-first-media-runtime.md).
- Open VP9/Opus WebM is the guaranteed open video-delivery alternative.
- H.264/AAC MP4 remains the required ordinary compatibility target, but its final encoder, broader player compatibility, perceptual A/V sync, patent, redistribution, and binary-closure decisions are Conditional and belong to release engineering/legal/manual acceptance.
- Ordinary audio/image output remains Free. WAV, FLAC, Ogg Opus, PNG, and JPEG are baseline; M4A/AAC and MP3 are Conditional.
- Capability-qualified local formats remain useful and Free where available, but never become portable project requirements.
- Project persistence stores semantic editing intent and stable media identity, not runtime paths, encoder/filter names, cache paths, or platform capabilities.

## Explicitly blocked or deferred

- GPL/nonfree components are blocked from the redistributable baseline without the owner/architecture/legal gates in ADR-0001.
- The failed synthetic F7 terminal-duration recipe is not a guarantee for arbitrary VFR/non-zero-PTS edge cases. It does not justify more proof-authoring work; production VFR behavior receives focused tests when implemented.
- Arbitrary Matroska codec/timing combinations are not guaranteed. The directly passed bounded subset is documented in the runtime contract.
- Full-resolution real-time 4K, HDR mastering, color emoji, uncommon >8-bit/chroma media, multichannel audio, exhaustive codec/player/hardware matrices, and deep professional finishing are deferred.
- Stabilization, deflicker, denoise, sharpen, Match to Previous Clip, format matching, and loudness matching have preliminary Pro feasibility dispositions only.

## What feature development may assume

Upcoming editor slices may implement the baseline semantics in the media runtime contract while preserving the Milestone 3 boundaries:

- Core owns semantic track/effect/time/history state and never imports engine names.
- Application owns use cases and capability contracts.
- Infrastructure owns runtime observation, concrete FFmpeg mapping, materialization, analysis, cache, and process execution.
- Windows or future macOS facilities remain optional platform implementations behind Application contracts.
- App owns WPF presentation, draft interactions, docking, and the shared viewer/editor coordination boundary.

The existing paired-runtime observer/profile validator remains because it is platform-neutral at its contract boundary, independent of the removed proof system, normally unit-testable, and directly useful to upcoming runtime preflight and diagnostics work.

## Provisional performance conclusion

One heavyweight job is the default. Thread counts are explicit and conservative; heavy work remains off the UI thread; cancellation acknowledges immediately and removes unpromoted partial outputs; expensive previews use reduced quality or disposable proxies. The initial reference target is 720p/1080p work, usually 30 seconds to 10 minutes, on the owner's Ryzen 7 3700X / 32 GB / RTX 3070 Ti system. Full product-path performance, long-form behavior, lower hardware, track counts, and release-candidate qualification must be measured after the relevant editor/render mappings exist.

## Work deliberately moved out of Gate 0

- Feature slices own product-path preview/export agreement, cancellation, responsiveness, and focused tests for each implemented capability.
- Release engineering owns the exact bundled runtime, reproducible build/acquisition, SBOM/notices, license audit, signing, installers, updates, distribution, and release-candidate media/performance/playback qualification.
- Qualified legal review owns patent, territory, and final redistribution conclusions before public distribution or commerce.
- macOS implementation and validation remain a later platform workstream; the shared architecture remains portable.
- Accounts, billing, entitlements, Ingots, managed compute, and payment processing remain separate from desktop media implementation.

The former proof-attempt ledgers, evidence shards, retention roots, R2 receipts, quarantine histories, proof-only WPF adapter, expanded matrices, and validation-of-validation tooling were removed from the active repository. Git history and the local `archive/gate0-proof-lab-pre-cleanup` safety branch preserve the pre-cleanup state.
