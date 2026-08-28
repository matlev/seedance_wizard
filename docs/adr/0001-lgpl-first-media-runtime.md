# ADR-0001: LGPL-first redistributable media runtime

Status: accepted
Date: 2026-08-27

## Context

ReelForge needs dependable media inspection, editing, preview, and delivery without allowing a convenient developer FFmpeg build to define the product's distribution obligations. Several familiar components and filters are GPL-path, while codec patents and platform facilities create questions separate from source-code licensing.

## Decision

The redistributable baseline uses LGPL-compatible, permissively licensed, or approved platform-provided components wherever an acceptable route exists. Candidate routes are evaluated in this order: approved LGPL-path FFmpeg/native component; another LGPL-compatible or permissive implementation; optional OS implementation that does not define portable project meaning; Enhanced Local Runtime; then narrow or defer.

No GPL or nonfree component may enter the baseline silently. A GPL shipping proposal stops for explicit owner approval, documented product justification, distribution-architecture review, and qualified legal review. User-configured GPL runtimes may expose optional local capabilities but never become portable project requirements.

## Consequences

- The baseline profile and CI reject `--enable-gpl`, `--enable-nonfree`, and named prohibited components.
- Project/domain concepts describe semantics, not FFmpeg filter or encoder names.
- H.264/AAC/MP3 remain conditional until exact runtime and legal review; Open WebM/Opus remains the guaranteed video alternative.
- Windows Media Foundation may be an optional implementation but cannot define cross-platform project meaning.
- Release engineering owns the exact redistributable build, SBOM/notices, signing, packaging, and legal gates.

## Alternatives considered

- Bundling a broad GPL FFmpeg build was rejected because convenience does not justify changing the product's distribution architecture.
- Making arbitrary user FFmpeg installations authoritative was rejected because availability, behavior, and license posture would be nondeterministic.
- Removing local-runtime extensibility was rejected because clearly labeled enhanced capabilities can remain useful without weakening the baseline.

## Verification and follow-up

The static baseline validator runs in CI. Runtime-profile changes trigger focused license/dependency review and representative media smoke tests. The final public binary still requires release-engineering and qualified legal review.
