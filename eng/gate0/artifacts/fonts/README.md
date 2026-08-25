# Gate 0 F3 font proof artifacts

These checked-in Git paths are durable, project-controlled proof-artifact retention for the Gate 0 F3 Unicode proof. They are not a ReelForge shipping font bundle, public-distribution approval, or a final runtime selection.

The exact approved stack is recorded in `../../font-proof-artifacts.json`:

- Noto Sans Regular v2.015: Latin, punctuation, and diacritics;
- Noto Sans Arabic Regular v2.013: Arabic fallback and shaping evidence;
- Noto Sans CJK SC Regular Sans2.004: Simplified Chinese fallback for the approved fixture locale.

Each retained font and its OFL-1.1 text is byte- and SHA-256-pinned. Validate the complete closed file set before any F3 proof run:

```powershell
pwsh ./eng/gate0/Validate-FontProofArtifacts.ps1
```

The validator is offline, accepts only explicit rooted paths, rejects path escape and reparse points, and rejects missing, added, resized, or hash-drifted artifacts. System-font fallback and PATH/font-discovery are prohibited. Font presence alone does not prove Unicode glyph selection, fallback, wrapping, captions/titles, or Arabic shaping; the later executable F3 proof must render and inspect those semantics. Color emoji remains optional and blocked for Gate 0.
