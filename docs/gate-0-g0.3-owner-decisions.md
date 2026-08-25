# Gate 0 G0.3 owner decisions

Status: approved; bounded color and text proof authorized; G0.4 analysis may proceed subject to retained completion gates

Approved: 2026-08-24

Authority: [Gate 0 media capability charter](gate-0-media-capability-charter.md)

## Basic-color mapping

The owner approves `colorlevels` plus `hue` as the P2 LGPLv3-path mapping for the bounded basic-color proof. The proof must cover brightness, contrast, and saturation with separate deterministic visual/oracle tests where appropriate.

This decision establishes an acceptable executable mapping for the required user semantics only. It does not require numerical parity with FFmpeg's GPL `eq` filter, select a final UI parameter model, make these filters permanent project-domain concepts, select a shipping runtime, or widen approval to other components in P2.

## Unicode text and font contract

The owner approves this required F3 baseline:

- Unicode glyph rendering;
- deterministic font fallback;
- Arabic shaping; and
- exact, hash-pinned OFL-only font binaries.

The proof stack must contain:

- Noto Sans Regular for Latin, punctuation, and diacritics;
- Noto Sans Arabic Regular; and
- one explicitly selected regional Noto Sans CJK font appropriate for the approved Simplified Chinese fixture text.

For every font, the proof manifest must record the exact release/version, authoritative source location, byte size, SHA-256, applicable license text, and durable project-controlled proof-artifact retention. System-font fallback is prohibited. Presence alone is not proof: executable rendered evidence must prove actual glyph selection, deterministic fallback, wrapping, title and caption presentation, and Arabic shaping.

Color emoji remains a separate optional/blocked Gate 0 capability. The required F3 baseline must not be widened merely to support it.

## Playback and long-form gates

G0.4 delivery-format analysis may proceed while independent playback remains an explicit G0.3 completion gate and long-form integrity/resource testing remains an explicit G0.5 completion gate. G0.4 may compare and recommend delivery candidates, but it may not finalize the default Free delivery contract before the required independent-playback evidence is complete.

Gate 0 may not exit until both retained gates are completed or explicitly dispositioned by a later owner decision.

Independent playback must eventually record exact browser/player, OS, and codec-pack state and the approved open, seek, pause/resume, A/V synchronization, and end-of-file behaviors. It does not require a premature VLC installation merely to begin G0.4 analysis.

Long-form proof belongs to the G0.5 methodology so timestamp integrity, first/final identity, resource consumption, UI responsiveness, cancellation, and cache/disk behavior are measured together. An unmeasured one-hour render is not acceptable substitute evidence.

## Immediate authorized work

1. Execute the bounded P2 basic-color proof against deterministic oracles.
2. Select, acquire, license-record, hash-pin, and durably retain the exact approved OFL font artifacts.
3. Execute the bounded F3 rendered-text proof without system-font fallback.
4. Update the G0.3 aggregate only from executed semantic evidence.
5. Allow G0.4 comparison work to begin without representing either retained completion gate as passed.
