# ReelForge

ReelForge is a Windows desktop workspace for AI video generation, logical project media, exact-frame preparation, and non-destructive FFmpeg-based composition workflows. It is an AI-native finishing workbench rather than a general-purpose professional NLE.

Milestones 1–3 establish and structurally harden a working generation-to-editing loop:

- Generate/Edit WPF workspaces with shared Project Media, preview, inspector, global job monitoring, and application settings
- portable, JSON-formatted `.rfp` projects with `assets/`, `generated/`, `exports/`, and `cache/`
- image, video, and audio import plus drag/drop, rename, transfer, collision-safe storage, and last-project/recent-project recovery
- image/video preview with seek, volume, frame stepping, and exact decoded-frame selection
- FFmpeg/ffprobe PATH discovery plus saved explicit paths and executable browsing, cancellable process execution, safe argument handling, and ffprobe metadata parsing
- capability-driven `IVideoGenerationProvider` abstraction with a no-cost fake route, official BytePlus ModelArk Seedance 2.5, and AtlasCloud Seedance 2.5/MiniMax H3
- immutable generation/reference snapshots, recoverable global jobs, durable output ingestion, verbose diagnostics, and explicit Undo Send
- Saved Frames and Saved Clips backed by exact immutable positions/recipes and disposable reconstructable materializations
- a duration-aware composition timeline with direct drag/drop/reorder/remove, exact splitting, fast audition, cancellable preview/export, and immutable recipe revisions
- independent layered audio with placement, mute, gain, pan, fades, extraction, exact segment detachment, audition, and final mixing
- application-level Settings with local JSON overrides and Windows Credential Manager storage for R2, BytePlus, and AtlasCloud secrets
- private Cloudflare R2 temporary reference hosting with SHA-256 deduplication and short-lived presigned GET URLs
- network-isolated automated coverage for persistence, recipes, providers, jobs, media parsing/materialization, FFmpeg commands, and cost safety

The desktop defaults to the fake provider. BytePlus ModelArk and AtlasCloud are independently selectable, but a real submission requires a stored credential, an explicit click, and a fresh human-accepted charge warning. Provider tests use in-memory HTTP handlers and cannot make paid generation calls. See [provider research](docs/provider-research.md).

## Requirements

- Windows 10 or newer
- .NET 9 SDK, feature band 9.0.3xx (the application continues to target .NET 8)
- FFmpeg and ffprobe on `PATH`, or their executable paths selected in **Settings → Media Tools** (the app still runs and imports files without them)

## Build and run

On Windows, restore, build both configurations, run every test suite, and launch the app with:

```powershell
dotnet restore ReelForge.sln
dotnet build ReelForge.sln --configuration Debug --no-restore
dotnet build ReelForge.sln --configuration Release --no-restore
dotnet test ReelForge.sln --configuration Release --no-build
dotnet run --project src/ReelForge.App/ReelForge.App.csproj
```

The portable Core, Application, Infrastructure, and cross-layer acceptance suites can run without the Windows/WPF projects:

```powershell
dotnet test tests/ReelForge.Core.Tests/ReelForge.Core.Tests.csproj --configuration Release
dotnet test tests/ReelForge.Application.Tests/ReelForge.Application.Tests.csproj --configuration Release
dotnet test tests/ReelForge.Infrastructure.Tests/ReelForge.Infrastructure.Tests.csproj --configuration Release
dotnet test tests/ReelForge.Tests/ReelForge.Tests.csproj --configuration Release
```

If this repository is running in a restricted agent environment, set `DOTNET_CLI_HOME`, `NUGET_PACKAGES`, `APPDATA`, and `LOCALAPPDATA` to writable task-specific directories before restoring.

## Project format

```text
MyProject/
  MyProject.rfp
  assets/
    images/
    videos/
    audio/
  generated/
  exports/
  cache/
```

All project-media paths stored in the `.rfp` file are relative to the project root. The file remains ordinary, human-readable JSON despite its project-specific extension. Imported sources are copied; originals are never modified. Pre-release ReelForge supports one current development format and clearly rejects obsolete development project files rather than maintaining a migration ladder.

## Documentation

- [Architecture constitution](ARCHITECTURE.md)
- [Architecture](docs/architecture.md)
- [Architecture governance](docs/architecture-governance.md)
- [Architecture debt register](docs/architecture-debt.md)
- [Architecture decision records](docs/adr/README.md)
- [ReelForge 1.0 product definition](docs/reelforge-1.0-product-definition.md)
- [Gate 0 media capability charter](docs/gate-0-media-capability-charter.md)
- [Gate 0 Checkpoint A decision packet](docs/gate-0-checkpoint-a.md)
- [Gate 0 G0.5 Stage 2 planning packet](docs/gate-0-g0.5-stage2-workload-proposal.md)
- [Gate 0 G0.5 Stage 2 owner decisions](docs/gate-0-g0.5-stage2-owner-decisions.md)
- [Gate 0 G0.5 retained-audio results](docs/gate-0-g0.5-retained-audio-results.md)
- [Gate 0 G0.5 marker-survivability results](docs/gate-0-g0.5-marker-survivability-results.md)
- [Gate 0 G0.5 WPF measurement-adapter boundary](docs/gate-0-g0.5-wpf-measurement-adapter-boundary.md)
- [Gate 0 durable artifact retention](docs/gate-0-artifact-retention.md)
- [Milestone plan](docs/milestones.md)
- [Manual regression acceptance matrix](docs/manual-acceptance.md)
- [Contributor guidance](docs/contributor-guidance.md)
- [Seedance provider research](docs/provider-research.md)
- [MiniMax H3 local execution research](docs/minimax-h3-local-research.md)
- [Application configuration and Cloudflare R2](docs/configuration.md)
