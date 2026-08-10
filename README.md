# Seedance Wizard

Seedance Wizard is a Windows desktop workspace for AI video-generation prompting, project provenance, and lightweight FFmpeg-based media workflows. It is intentionally not a full nonlinear editor.

Milestone 1 provides a working architectural slice:

- resizable WPF editor shell with project explorer, preview, inspector, generation panel, and timeline placeholder
- portable `project.json` projects with `assets/`, `generated/`, `exports/`, and `cache/`
- image, video, and audio import with collision-safe filenames
- video/image preview and basic playback controls
- FFmpeg/ffprobe PATH discovery plus saved explicit paths and executable browsing, cancellable process execution, safe argument handling, and ffprobe metadata parsing
- capability-driven `IVideoGenerationProvider` abstraction, a no-cost fake provider, and a schema-verified AtlasCloud Seedance 2.5 adapter
- generation provenance stored with the project
- Windows Credential Manager secret store for future provider API keys
- automated tests for persistence, provider validation, media parsing, and command construction

The desktop UI remains on the fake provider so development cannot incur generation charges. The AtlasCloud adapter is implemented and contract-tested with mocked HTTP, but needs credential setup, provider asset references, and job polling before it is enabled in the UI. See [provider research](docs/provider-research.md).

## Requirements

- Windows 10 or newer
- .NET 8 SDK
- FFmpeg and ffprobe on `PATH`, or their executable paths selected in the app's **Tools** tab (the app still runs and imports files without them)

## Build and run

```powershell
dotnet restore SeedanceWizard.sln
dotnet test SeedanceWizard.sln
dotnet run --project src/SeedanceWizard.App/SeedanceWizard.App.csproj
```

If this repository is running in a restricted agent environment, set `DOTNET_CLI_HOME`, `NUGET_PACKAGES`, `APPDATA`, and `LOCALAPPDATA` to writable task-specific directories before restoring.

## Project format

```text
MyProject/
  project.json
  assets/
    images/
    videos/
    audio/
  generated/
  exports/
  cache/
```

All paths stored in `project.json` are relative to the project root. Imported sources are copied; originals are never modified.

## Documentation

- [Architecture](docs/architecture.md)
- [Milestone plan](docs/milestones.md)
- [Seedance provider research](docs/provider-research.md)
