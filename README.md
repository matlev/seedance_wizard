# ReelForge

ReelForge is a Windows desktop workspace for AI video-generation prompting, project history, and lightweight FFmpeg-based media workflows. It is intentionally not a full nonlinear editor.

Milestone 1 provides a working architectural slice:

- resizable WPF editor shell with project explorer, preview, inspector, generation panel, and timeline placeholder
- portable, JSON-formatted `.rfp` projects with `assets/`, `generated/`, `exports/`, and `cache/`
- image, video, and audio import with collision-safe filenames
- video/image preview and basic playback controls
- FFmpeg/ffprobe PATH discovery plus saved explicit paths and executable browsing, cancellable process execution, safe argument handling, and ffprobe metadata parsing
- capability-driven `IVideoGenerationProvider` abstraction with a no-cost fake provider plus official BytePlus ModelArk and AtlasCloud Seedance 2.5 adapters
- generation provenance stored with the project
- application-level Settings with local JSON overrides and Windows Credential Manager storage for R2, BytePlus, and AtlasCloud secrets
- private Cloudflare R2 temporary reference hosting with SHA-256 deduplication and short-lived presigned GET URLs
- automated tests for persistence, provider contracts, paid-network isolation, media parsing, and command construction

The desktop defaults to the fake provider. BytePlus ModelArk and AtlasCloud are independently selectable, but a real submission requires a stored credential, an explicit click, and a fresh human-accepted charge warning. Provider tests use in-memory HTTP handlers and cannot make paid generation calls. See [provider research](docs/provider-research.md).

## Requirements

- Windows 10 or newer
- .NET 9 SDK, feature band 9.0.3xx (the application continues to target .NET 8)
- FFmpeg and ffprobe on `PATH`, or their executable paths selected in **Settings → Media Tools** (the app still runs and imports files without them)

## Build and run

```powershell
dotnet restore ReelForge.sln
dotnet test ReelForge.sln
dotnet run --project src/ReelForge.App/ReelForge.App.csproj
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

All paths stored in the `.rfp` file are relative to the project root. The file remains ordinary, human-readable JSON despite its project-specific extension. Imported sources are copied; originals are never modified. Legacy `project.json` projects remain openable and are saved in place.

## Documentation

- [Architecture](docs/architecture.md)
- [Milestone plan](docs/milestones.md)
- [Seedance provider research](docs/provider-research.md)
- [MiniMax H3 local execution research](docs/minimax-h3-local-research.md)
- [Application configuration and Cloudflare R2](docs/configuration.md)
