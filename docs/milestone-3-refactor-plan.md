# Milestone 3 architecture review and refactor plan

Status: approved and in execution

Review baseline: 2026-08-20, branch `milestone-2`, after Milestone 2 completion

## Executive conclusion

ReelForge's four-layer dependency direction is worth preserving:

```text
Core
  ^
  |
Application
  ^
  |
Infrastructure
  ^
  |
WPF App / composition root
```

The main problem is not a failed architecture or an incorrect domain model. It is that rapid Milestone 2 delivery accumulated too many responsibilities inside physically flat projects and a handful of oversized coordinators. The most visible example is `MainWindow.xaml.cs`, but treating that file alone would merely move the same coupling into arbitrary partial classes.

Milestone 3 should be a behavior-preserving structural program. Its primary human goal is to make ReelForge meaningfully readable, explorable, reviewable, and maintainable. It must reduce the huge file bloat caused by types accumulating too many concerns—not simply redistribute the same bloat across arbitrary partial classes or folders. A reviewer should be able to locate a feature, understand its collaborators, and assess a change without first reconstructing thousands of lines of unrelated state and behavior.

The refactor should establish explicit feature ownership, platform boundaries, focused application services, a small WPF shell, cohesive media infrastructure, mirrored tests, and enforceable dependency checks. It must not redesign the `.rfp` schema, add editor features, add paid network behavior, or perform a big-bang rewrite.

One new production project is justified: `ReelForge.Platform.Windows`. Windows Credential Manager and Windows-specific application-path ownership need a compilation/lifecycle boundary. Provider, persistence, and FFmpeg code should first become feature folders inside the existing portable Infrastructure project; extra assemblies are warranted only if later packaging or dependency evidence establishes a real boundary.

## Repository evidence

The tracked C#/XAML footprint at the review baseline is:

| Project | Tracked C#/XAML files | Approximate lines | Principal concern |
| --- | ---: | ---: | --- |
| `ReelForge.App` | 20 | 8,701 | WPF shell, feature UI, service construction, and application orchestration are concentrated together. |
| `ReelForge.Application` | 15 | 4,449 | Use cases and contracts are sound but physically flat; several files combine unrelated capabilities. |
| `ReelForge.Core` | 2 | 1,038 | The domain is concentrated in one model file and one monolithic validator. |
| `ReelForge.Infrastructure` | 28 | 6,174 | Persistence, providers, FFmpeg, caching, platform services, and filesystem policies share one flat namespace/folder. |
| `ReelForge.Tests` | 30 | 6,721 | Good coverage exists, but every layer shares one project and many fixtures/fakes are repeated. |

The complete automated suite passes 199 tests. Warnings are errors and deterministic builds are already enabled. These are strong refactor guardrails.

Largest production surfaces:

| File | Approximate lines | Mixed responsibilities observed |
| --- | ---: | --- |
| `MainWindow.xaml.cs` | 6,028 | composition root, project lifecycle, generation, jobs, provider runtime, Project Media, preview player, frame tools, timeline rendering/gestures, edit controls, dialogs, transfer, inspectors, and UI models |
| `RecipeMediaMaterializer.cs` | 1,144 | facade, recursive execution, cache/lease coordination, retention, trim/frame/composition renderers, audio audition/mixing, exact-boundary resolution, fingerprints, and durable representation copies |
| `MainWindow.xaml` | 1,113 | shell chrome and every primary feature surface |
| `ProjectPersistenceModels.cs` | 797 | every persistence DTO plus every domain-to-DTO mapping |
| `ProcessInfrastructure.cs` | 678 | process execution, executable discovery, all FFmpeg argument builders, audio extraction, and ffprobe parsing |
| `WorkingCompositionService.cs` | 660 | composition creation, structural edits, audio properties, split asset/anchor construction, recipe cloning, validation, and save rollback |
| `SettingsWindow.xaml.cs` | 639 | settings navigation, dynamic control factory, validation, save behavior, credential lifecycle, media discovery, and connection tests |
| `GenerationWorkflow.cs` | 606 | drafts, immutable snapshots, reference preparation, queue/submission, monitoring, ingestion, retries, and failures |
| `DomainModels.cs` | 580 | all project, asset, recipe, anchor, media, generation, and provider capability types |
| `GenerationJobCoordinator.cs` | 543 | durable job registry, timing, restoration, submission scheduling, monitoring, reconciliation, and event publication |
| `Abstractions.cs` | 540 | unrelated project, media, provider, hosting, settings, diagnostic, platform, and workspace contracts/types |
| `ProjectInvariantValidator.cs` | 458 | validation for every current domain aggregate and cross-reference |

The line counts are review signals, not proposed hard limits. Cohesion and reasons to change decide boundaries. Small files must not be manufactured solely to satisfy a number.

## Human-centered acceptance goals

Milestone 3 is successful only if a human contributor can meaningfully review and navigate the result:

- feature folders reveal where project lifecycle, generation, providers, media tooling, editing, jobs, settings, and persistence live;
- a production file has a coherent reason to change and does not require reviewing unrelated workflows to understand a local edit;
- `MainWindow` becomes a recognizable shell, not a renamed or repartitioned implementation monolith;
- backend facades coordinate explicit collaborators rather than retaining every mechanism privately in one oversized class;
- names communicate creator intent at application boundaries and technical mechanism at infrastructure boundaries;
- common workflows can be followed from presentation to use case to domain/infrastructure through a short, predictable dependency path;
- pull requests can be reviewed in feature-sized diffs with relevant tests beside the affected responsibility;
- new contributors can browse the repository map and locate the likely owner before using full-text search.

No arbitrary maximum line count is an acceptance test. The test is whether the remaining size is explained by genuine cohesion, not accumulated unrelated concerns.

## Current strengths to preserve

- Core has no project dependency. Application references only Core. Infrastructure references Application/Core. WPF App is the outer composition layer.
- Core, Application, Infrastructure, and tests target portable `net8.0`; only the WPF App targets `net8.0-windows`.
- Current `.rfp` persistence keeps DTOs separate from domain objects and validates the aggregate on load/save.
- Project/settings/job writes use unique temporary files and atomic replacement; settings writes are serialized per normalized path.
- Media materialization has deterministic identities, leases, cancellation, partial-file cleanup, and purpose-specific requests.
- Paid provider submission requires explicit human authorization. Automated provider tests use isolated handlers/authorization and cannot incur charges.
- Immutable recipe/anchor/generation semantics and logical Project Media references are established and tested.
- The current-format-only development schema policy avoids migration ladders while retaining a clear incompatible-format marker.

## Problems and recommended ownership

### WPF presentation

`MainWindow` currently owns both view state and application behavior. It directly constructs concrete stores, FFmpeg services, providers, HTTP clients, credential storage, diagnostic logging, and job coordination. It also implements `IGenerationJobFinalizer`. This makes a UI window the service owner, runtime configuration manager, and generation recovery endpoint.

The shell should eventually own only global layout and workspace navigation. Focused feature controls/controllers should own local WPF gestures and presentation state. Application coordinators should own project/generation/media operations. The composition root should create services and lifetimes outside `MainWindow`.

Recommended presentation units:

- `Shell`: top chrome, Generate/Edit selection, Jobs drawer, project title/status, global drag overlay;
- `ProjectMedia`: grouped media list, selection, context actions, transfer/import/drop;
- `MediaViewer`: image/video presentation, transport, seek/volume, preview lease lifecycle;
- `GenerateWorkspace`: prompt/draft, provider selection, reference occurrences, submission confirmation;
- `MediaPreparation`: Select Frame and Make Clip state machines and progressive frame contact window;
- `EditWorkspace`: composition selection and edit layout;
- `Timeline`: projection, ruler, zoom, playhead, selection, drag/drop/reorder, lanes, context commands;
- `EditTools`: selected segment/audio properties and commit-on-completed-gesture behavior;
- `Jobs`: global active/terminal job presentation and notification state;
- `Inspector`: read-only asset/generation/source/history formatting;
- `Settings`: category navigation plus one editor control per setting kind;
- `Dialogs`: small purpose-specific windows only.

Use focused view models/presenters and ordinary WPF controls. Do not adopt a large MVVM framework merely to perform the refactor. WPF event handlers are acceptable when they translate a local gesture into a controller/view-model command; they should not open stores, construct infrastructure, or mutate the project aggregate directly.

### Application layer

`Abstractions.cs` is a contract catalog with no common reason to change. Split it by capability: Projects, Media, Materialization, Generation, Providers, Hosting, Settings, Diagnostics, and Platform.

`ProjectWorkspace`, currently nested in `Abstractions.cs`, combines project session lifecycle/import/save with an older direct generation-submission path. That `SubmitGenerationAsync` path is used only by one snapshot test; the application uses `GenerationWorkflow`. Characterize the frozen snapshot rule in the current workflow, migrate the test, and remove the duplicate submission path.

`WorkingCompositionService` should retain one transactional revision-editing boundary but delegate commands by responsibility:

- composition lifecycle/current-revision access;
- structural segment commands;
- independent audio commands;
- exact split/boundary construction;
- recipe clone/commit/rollback support.

Do not create a one-method interface for every command. A cohesive command service may expose several related operations.

`GenerationWorkflow` should become an orchestrator over focused collaborators:

- `GenerationSnapshotFactory` for immutable domain snapshots;
- `GenerationReferencePreparationService` for materialization/hosting reuse;
- `GenerationSubmissionService` for provider submission authorization;
- `GenerationMonitoringService` for polling and terminal mapping;
- `GenerationOutputFinalizer` for verified ingestion/project update.

Job registry/recovery remains application-global. Its durable state transitions should not depend on a WPF window being open.

`ApplicationSettings.cs` should separate settings data, catalog/validation, editing, recent projects, and default-path resolution. Application settings data remains portable; an injected platform path service supplies machine-local defaults.

### Core domain

Split `DomainModels.cs` into cohesive feature folders without changing public semantics:

- Projects and Assets;
- Media metadata and content identity;
- Recipes and Composition;
- Anchors and exact positions;
- Generations and reference snapshots;
- Provider capability/value types.

Keep domain mutation rules on aggregates/value objects where they express invariants. Keep orchestration, filesystem paths, FFmpeg, HTTP, and UI state outside Core.

`ProjectInvariantValidator` should remain a single public validation facade but delegate to internal validators by aggregate/cross-reference family. This preserves one authoritative gate while making individual rule sets navigable and testable.

### Persistence and filesystem

Split project persistence into DTOs and mappers by the same domain families. `PortableProjectStore` remains the atomic store facade and retains a single current format marker; no migration ladder is introduced.

Filesystem behavior is repeated across import, generation ingestion, audio extraction, detachment, promotion, cache persistence, settings, jobs, and project saves. Introduce narrowly named policies/services for:

- safe user-supplied filename validation;
- collision-free destination naming;
- atomic file commit/cleanup;
- project media folder resolution and containment;
- machine-local application data paths.

Do not create generic `Helpers` or `Utils` folders. Each reusable behavior must have an owner and explicit semantics.

The current project containment check uses case-insensitive string-prefix comparison. Replace it during the persistence slice with canonical relative-path containment using host-appropriate path semantics. Cross-OS `.rfp` interchange is not an acceptance requirement, but portable code should not embed Windows case behavior.

### FFmpeg, ffprobe, and materialization

Split `ProcessInfrastructure.cs` into process execution, tool discovery, ffprobe parsing/inspection, audio extraction, and FFmpeg command construction by operation. Keep argument builders pure and unit tested.

Retain `RecipeMediaMaterializer` as the `IMediaMaterializer` facade, but delegate to cohesive services:

- render-plan execution and dependency lease scope;
- exact boundary resolution;
- trim/frame rendering;
- composition video normalization/concat;
- source-audio and independent-audio mixing/audition;
- cache identity, lock, open, and eviction coordination;
- optional durable modified-media retention;
- renderer fingerprint/tool-version identity.

The facade must not become a service locator. Collaborators should be explicit constructor dependencies with clear lifetimes. Preserve deterministic cache keys, per-key coalescing, process-tree cancellation, and atomic completion after every extraction.

### Providers, hosting, and diagnostics

Organize Infrastructure under `Providers/BytePlus`, `Providers/AtlasCloud`, `Hosting/CloudflareR2`, and `Diagnostics`. Keep the shared AtlasCloud transport/client and provider-specific payload/capability adapters distinct.

Provider errors should refer to the configured secure credential store, not hard-code “Windows Credential Manager.” The Windows UI may display the actual platform store name through platform metadata.

Preserve the network-isolated authorization factory and recording-handler tests. No refactor test may call a real provider, upload real media, or make a billable request.

### Tests

The test suite has valuable behavioral coverage but repeats `UnusedImporter`, stub inspectors/materializers, workspace setup, physical asset builders, and polling helpers. Create focused test-support builders/fakes only after at least two tests share identical semantics. Avoid one enormous fixture superclass.

Organize tests to mirror production capabilities:

- Core domain/invariants;
- Application projects/generation/editing/media/settings;
- Infrastructure persistence/media/providers/hosting/diagnostics/platform;
- network-isolated acceptance workflows;
- WPF presenter/view-model behavior and a small Windows-only smoke layer.

Eventually split the single test project into layer-specific projects so references enforce direction. Keep the MP4 fixture in an acceptance/media-integration test project. Do not pursue fragile screenshot/pixel automation as the primary UI safety net; extract deterministic state/command logic and retain a concise human WPF acceptance checklist.

## Target solution and folder map

The exact filenames can adjust as responsibilities become clearer, but this is the target topology:

```text
src/
  ReelForge.Core/
    Projects/
    Assets/
    Media/
    Recipes/
    Editing/
    Anchors/
    Generations/
    Providers/
    Validation/

  ReelForge.Application/
    Projects/
      ProjectSession.cs
      Transfers/
    Configuration/
    Diagnostics/
    Generation/
      Drafts/
      Snapshots/
      Submission/
      Jobs/
      Outputs/
    Editing/
      Composition/
      Timeline/
      Audio/
    Media/
      Inspection/
      Frames/
      Extraction/
      Materialization/
    Providers/
    Hosting/
    Platform/

  ReelForge.Infrastructure/
    Persistence/
      Projects/
        Dtos/
        Mapping/
      Settings/
      Jobs/
    Files/
    Media/
      Processes/
      Tools/
      Ffprobe/
      Ffmpeg/
        Commands/
        Rendering/
      Frames/
      Materialization/
        Cache/
        Execution/
        Retention/
    Providers/
      AtlasCloud/
      BytePlus/
      Fake/
    Hosting/
      CloudflareR2/
    Diagnostics/

  ReelForge.Platform.Windows/
    Paths/
    Credentials/
    MediaTools/
    Shell/

  ReelForge.App/
    Bootstrap/
    Shell/
    ProjectMedia/
    MediaViewer/
    Workspaces/
      Generate/
      Edit/
    MediaPreparation/
    Timeline/
    EditTools/
    Jobs/
    Inspector/
    Settings/
    Dialogs/
    Resources/
      Styles/
      Icons/
      Sprites/

tests/
  ReelForge.Core.Tests/
  ReelForge.Application.Tests/
  ReelForge.Infrastructure.Tests/
  ReelForge.Platform.Windows.Tests/
  ReelForge.App.Tests/
  ReelForge.AcceptanceTests/
    Fixtures/
```

Folders express feature ownership; projects express dependency/platform boundaries. Provider and FFmpeg folders should not automatically become separate assemblies during Milestone 3.

## Platform and media-tool distribution boundary

Introduce an application/platform contract for machine-local locations and platform presentation metadata. A Windows implementation owns Local Application Data, Documents defaults, secure credential-store naming, and any native shell behavior. Settings, logs, cache, active jobs, and recent-project defaults receive resolved paths; shared application code does not call `Environment.SpecialFolder.LocalApplicationData` directly.

Keep project media paths relative and cache-independent. Do not add cross-Windows/macOS `.rfp` interchange as an acceptance criterion. Explicit export/import of physical media remains the supported cross-machine handoff.

The eventual installed FFmpeg/ffprobe lookup order should be:

1. valid explicit Advanced-setting override (manual Browse writes this value);
2. verified ReelForge-packaged binary for the current OS/architecture;
3. PATH auto-detection;
4. not-configured state with Browse/Auto-detect guidance.

The packaged artifacts should come from CI, not developer machines:

```text
verified FFmpeg source/tag
  -> pinned ReelForge build recipe and LGPL-compatible configuration
  -> captured compiler/dependency/SBOM and license/source notices
  -> Windows/macOS architecture artifacts
  -> ffmpeg + ffprobe version/provenance metadata
  -> SHA-256 manifest
  -> signed/notarized ReelForge installer inputs
```

Do not package GPL/nonfree builds accidentally. Preserve source-offer/license obligations and perform the planned commercial codec/patent review before release. Milestone 3 establishes discovery/path/provenance seams and CI recommendations; it does not need to ship the final redistributable binaries or installer.

## Enforceable architecture and CI checks

Add checks incrementally rather than relying only on documentation:

1. Keep project references acyclic and inward-facing. Core references nothing; Application references Core; portable Infrastructure references Application/Core; Platform.Windows references portable contracts; WPF App references the required outer implementations.
2. Add a non-Windows CI job that builds/tests the portable subset. WPF and Platform.Windows build/test in Windows CI.
3. Fail a portable build if Core/Application/portable Infrastructure acquire WPF references. Once Platform.Windows exists, keep native credential P/Invoke out of portable Infrastructure.
4. Preserve warnings-as-errors, nullable analysis, deterministic builds, and full Debug/Release solution builds.
5. Keep provider contract tests network-isolated. A test-only authorization token must remain impossible to use with real network transport.
6. Keep current-format `.rfp` round-trip and invariant tests plus atomic-save failure tests.
7. Add focused composition-root/lifetime tests where practical and Windows smoke checks for startup, project open, workspace switch, and clean shutdown.
8. Run the established human Milestone 2 checklist after high-risk UI, persistence, media-materialization, or job-lifecycle slices.

## Staged Milestone 3 execution

Each numbered stage is a program of small, independently buildable commits. Do not combine all bullets into one commit.

### Stage 0 — baseline and characterization

- Preserve this inventory and the 199-test green baseline.
- Add reusable test project/workspace builders and fakes only for already duplicated setup.
- Add characterization for the duplicate workspace generation snapshot rule, project/settings/job atomic writes, provider authorization isolation, cache leases, and composition audition lifecycle before moving owners.
- Maintain the [Milestone 3 manual acceptance matrix](milestone-3-manual-acceptance.md) for project switching, jobs/recovery, generation preparation, frame/clip tools, timeline arrangement, audition, preview/export, settings, cache, and diagnostics.

Exit: stronger guardrails; no production behavior or physical ownership changes.

### Stage 1 — platform paths, credentials, and composition root

- Introduce `ReelForge.Platform.Windows` for Windows paths and Credential Manager.
- Make settings/job/log/cache/project defaults explicit injected platform values.
- Replace platform-specific provider/UI wording with secure-store metadata.
- Move concrete service construction, HTTP-client ownership, and disposal into `Bootstrap/ApplicationRuntime` outside `MainWindow`.
- Keep configuration refresh explicit; do not introduce a service locator.

Exit: portable projects have no machine-location ownership or Windows credential implementation; `MainWindow` receives a coherent runtime/services object.

### Stage 2 — domain and application physical organization

- Split Core models and validators by feature without changing serialized names or behavior.
- Split Application contracts by capability.
- Move `ProjectWorkspace` into Projects and rename it to reflect project-session ownership if that improves clarity.
- Migrate the lone legacy direct-submission snapshot test to `GenerationWorkflow`; remove the unused duplicate submission implementation.
- Split settings data/catalog/editor/recent-project responsibilities.

Exit: no root-level grab-bag model/abstraction files; dependency direction and `.rfp` behavior unchanged.

### Stage 3 — project persistence and file policies

- Split DTOs/mappers by aggregate family behind the same store facade.
- Introduce owned safe-name, unique-destination, atomic-commit, and project-path-containment policies.
- Consolidate duplicate audio/promotion/ingestion filename and partial-file logic without changing visible filenames.
- Retain one current format marker, atomic save, corruption protection, missing-media state, and explicit obsolete-format failure.

Exit: persistence is navigable and filesystem safety is shared by explicit owners; current development projects round-trip unchanged.

### Stage 4 — media process and materialization decomposition

- Split external process execution, discovery, command builders, ffprobe parsing, and audio extraction.
- Keep pure command builders exhaustively tested.
- Decompose materialization behind the unchanged facade, starting with cache/lease/fingerprint ownership, then trim/frame execution, then composition/video/audio execution.
- Preserve purpose/profile identities, coalescing, cancellation, exact boundaries, retention preference, and cleanup after every commit.

Exit: the materializer facade coordinates focused collaborators and no longer implements every render operation itself.

### Stage 5 — generation, providers, hosting, and jobs

- Organize provider/hosting implementations by vendor/capability.
- Split immutable snapshot creation, reference preparation, submission, monitoring, and output finalization from `GenerationWorkflow` while keeping one public workflow coordinator.
- Move job finalization out of `MainWindow`; publish UI-neutral job outcomes/events.
- Keep jobs global across projects and restarts, Undo Send semantics unchanged, diagnostics sanitized, and paid calls human-only.

Exit: WPF no longer owns generation execution/recovery; provider additions have an obvious folder and contract path.

### Stage 6 — WPF feature extraction

Extract one vertical UI feature at a time. For each feature: characterize state/commands, extract application logic, create a focused control/presenter, wire it into the shell, build/test, and run its human check.

Recommended order:

1. Jobs and active-job mascot;
2. Settings and dialogs;
3. Project lifecycle plus Project Media;
4. generation draft/references/submission;
5. Inspector;
6. Media Viewer transport and lease lifecycle;
7. Select Frame/Make Clip;
8. Edit Tools;
9. composition timeline and fast audition;
10. final shell/workspace/status cleanup.

The timeline is last because it currently shares the densest selection, preview, timer, drag, and cancellation state. Do not merely convert `MainWindow.xaml.cs` into feature-named partial files; ownership must move into focused types and controls.

Exit: `MainWindow` is a shell rather than the feature implementation. `MainWindow.xaml` composes controls instead of declaring every surface.

### Stage 7 — test topology and CI enforcement

- Move tests into layer/feature projects as production boundaries stabilize.
- Add portable Linux CI and full Windows CI.
- Add architecture/dependency checks and Windows startup smoke coverage.
- Keep network-isolated acceptance tests and the media fixture explicit.
- Verify Debug/Release, current `.rfp` round trip, cache-cleared reconstruction, and the Milestone 2 human workflows.

Exit: the physical structure is enforceable and future changes fail early when they cross platform/layer boundaries.

### Stage 8 — dead-code and naming cleanup

- Remove superseded paths, obsolete Seedance-era ignore names, duplicate refresh/format/path helpers, unused types, and stale documentation only after replacements are proven.
- Update contributor guidance and the final repository map.
- Re-run repository inventory and explain any remaining large or mixed-purpose file.

Exit: no major subsystem remains an unexplained monolith; all tests and human acceptance checks pass.

## Risk order and rollback policy

| Risk | Area | Required protection |
| --- | --- | --- |
| Highest | WPF shared selection/playback/timer/cancellation state | extract one feature at a time; manual workflow check; never move viewer and timeline simultaneously |
| Highest | materialization cache, leases, FFmpeg cancellation, exact boundaries | characterization and command tests before extraction; preserve facade; verify failure cleanup/cache-cleared rebuild |
| Highest | `.rfp` DTO/mapping/atomic save | byte/semantic round-trip tests and invariant validation after every mapper split; no schema redesign |
| High | job recovery, Undo Send, project-isolated finalization | restore/crash/project-switch acceptance and deterministic fake providers |
| High | settings concurrency, paths, credential storage | atomic/concurrent store tests and Windows credential tests without exposing secret values |
| Medium | generation/provider decomposition | recording handlers only; authorization isolation; sanitized diagnostics |
| Medium | exact frame/Saved Clip services | fixture-backed FFmpeg/ffprobe tests and pinned revision/hash assertions |
| Lower | pure file/type moves and command/layout helpers | compiler, focused unit tests, no mixed behavioral edits |

Every commit must leave the solution buildable and tests green. A structural slice should be revertible without reverting unrelated behavior. Do not mix schema changes, new editor features, or provider contract changes into these commits.

## Recommended decisions

- Use focused presenters/view models and controls without committing to a third-party MVVM framework during the refactor.
- Add `ReelForge.Platform.Windows`; do not create Provider, FFmpeg, or Persistence assemblies until a dependency or packaging requirement proves the boundary.
- Keep one current `.rfp` format marker and no migration ladder during pre-release development.
- Keep explicit constructor composition. Consider a DI container only if the extracted composition root still has demonstrably unmanageable lifetime wiring.
- Treat file length as an audit trigger, not a lint rule.
- Prefer deterministic presenter/state tests plus a small Windows smoke layer over broad fragile pixel automation.
- Keep future editor capabilities out of Milestone 3. The researched roadmap resumes only after this structural plan is executed and accepted.

## Questions that do not block Stage 0–1

1. Whether layer-specific test projects should be created early or only after production folders settle. Recommendation: organize shared test support first, split projects near Stage 7.
2. Whether to adopt `Microsoft.Extensions.DependencyInjection`. Recommendation: start with an explicit `ApplicationRuntime` composition root and revisit only if lifecycle wiring remains painful.
3. Which Windows UI smoke technology to adopt. Recommendation: defer the tool choice until deterministic presenter extraction exposes the few shell behaviors that still need automation.
4. Whether final packaged FFmpeg artifacts are built in ReelForge's repository or a dedicated audited build repository. Recommendation: decide at the packaging milestone; preserve an artifact/provenance contract now.
5. Whether future macOS support shares portable Infrastructure as-is or introduces OS-specific media-process implementations. Recommendation: let the first concrete Mac host expose real variation before adding speculative interfaces.
