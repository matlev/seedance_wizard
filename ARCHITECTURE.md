# ReelForge architecture constitution

This is the short normative map for contributors and agents. Detailed rationale and product decisions remain in [docs/architecture.md](docs/architecture.md).

## Dependency direction

```text
All arrows mean "depends on".

ReelForge.App
  |--> ReelForge.Core
  |--> ReelForge.Application --> ReelForge.Core
  |--> ReelForge.Infrastructure --> ReelForge.Application / ReelForge.Core
  `--> ReelForge.Platform.Windows --> ReelForge.Application
```

- **Core** owns domain state and invariants; it has no filesystem, provider, FFmpeg, WPF, or OS dependency.
- **Application** owns use cases and contracts; it references Core only.
- **Infrastructure** implements Application/Core contracts for persistence, media, providers, hosting, and diagnostics; it owns no desktop-OS defaults.
- **Platform.Windows** owns genuine Windows integrations behind Application contracts.
- **App** is the WPF presentation and composition boundary. Its outer-layer project references enable runtime composition, but concrete runtime construction belongs in `Bootstrap/ApplicationRuntime`; exact legacy exceptions are recorded as architecture debt.

## Non-negotiable rules

1. Preserve the dependency direction above. Do not bypass it with service location, global mutable registries, UI-owned persistence, or concrete provider references in Application.
2. Keep domain state authoritative, immutable where committed, project-relative where persisted, and independent of disposable cache paths.
3. Request media by logical target and purpose. Only materialization owns FFmpeg, cache, and render decisions.
4. Keep provider request/response details in provider adapters; keep OS facilities in a platform boundary.
5. Extend the existing owner of a responsibility. Do not create a parallel media, persistence, provider, or workflow path for feature convenience.
6. Automated work must remain incapable of paid-provider submission. Billable generation requires a fresh, explicit human action in the application.
7. Keep continuous UI gestures as disposable draft state. Commit at most one immutable revision when a real change completes; cancellation and no-ops commit none.
8. Do not add an abstraction without a present, distinct responsibility or implementation variation.

If a requested change conflicts with these rules, stop and report the conflict to the technical lead. Do not silently work around it.

## Working process

For substantial changes, follow [architecture governance](docs/architecture-governance.md) before implementation and include its completion report when handing work back. Consult relevant [ADRs](docs/adr/README.md) and the [architecture-debt register](docs/architecture-debt.md). The detailed contributor rules and required verification remain in [contributor guidance](docs/contributor-guidance.md).

Architecture boundary tests are executable policy and must remain green in CI.
