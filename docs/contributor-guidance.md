# Contributor guidance

Status: active rules for Milestone 3 and later work

## Dependency direction

- Core owns domain concepts and invariants. It references no other ReelForge project.
- Application owns use cases and contracts. It may reference Core, not Infrastructure, WPF, or native Windows APIs.
- portable Infrastructure implements persistence, providers, hosting, diagnostics, and media execution against Application/Core contracts.
- Platform.Windows owns Windows-only paths, credentials, and native shell behavior.
- the WPF App is the outer composition/presentation layer. Concrete construction belongs in `Bootstrap`, not feature controls or `MainWindow`.
- do not use service location or global mutable registries to bypass these directions.

## Feature ownership and files

- place code under the feature/capability that owns its reason to change; follow the target map in the [Milestone 3 refactor plan](milestone-3-refactor-plan.md).
- do not add root-level grab-bag files, generic `Helpers`/`Utils` folders, or unrelated types to an existing large file because it is convenient.
- one file may contain closely related private/internal value types. Split public concepts when they have different owners or reasons to change.
- file length is a review signal, not a quota. Prefer cohesive types over artificial micro-files.
- optimize physical structure for human exploration and review: a contributor should be able to locate a feature owner and understand a local change without traversing unrelated workflows or oversized coordinator state.
- name application operations for creator intent (`DetachSegmentAudio`, `PrepareGenerationReferences`), and infrastructure for mechanism (`FfmpegAudioExtractionEngine`, `JsonProjectStore`).

## WPF presentation

- keep feature-local gestures and visual state in focused controls/presenters/view models.
- UI code may request application operations and present results; it must not construct providers/stores, execute FFmpeg/HTTP, or mutate persistence DTOs.
- keep one authoritative project, selection, playback, and job state when views are docked, switched, or eventually floated.
- continuous gestures use disposable draft state and commit once; discrete changed choices commit once; no-ops create no revision.

## Domain and persistence

- preserve AssetId, user-facing filename, and SHA-256 content identity as separate concepts.
- committed recipe/anchor/generation revisions and snapshots are immutable. Editable drafts may remain mutable until committed.
- persist logical IDs, exact revisions/boundaries, relative project paths, and typed DTOs. Never persist an authoritative cache path.
- pre-release ReelForge maintains one current `.rfp` format marker. Update the current schema clearly when approved; do not add a migration ladder merely to preserve disposable development files.
- keep domain models separate from persistence DTOs and validate the aggregate before save and after load.
- project/settings/job writes remain unique-temporary plus atomic commit with failure cleanup.

## Media and platform behavior

- the application requests media by logical target and purpose; materialization owns FFmpeg/cache decisions.
- preserve deterministic render/cache identity, dependency leases, cancellation, exact boundaries, and partial-file cleanup.
- machine-local logs/settings/jobs/cache/defaults come from the active platform implementation.
- explicit advanced FFmpeg/ffprobe paths override packaged tools; packaged tools precede PATH fallback. Do not remove manual/PATH routes when packaging is added.
- project media remains relative and cache-independent. Cross-OS `.rfp` interchange is not a release acceptance requirement.

## Providers, security, and cost safety

- provider request/response schemas live only in provider adapters and must be verified from actual API documentation/contracts.
- secrets remain in the platform secure store and never enter project files, ordinary settings values, logs, or durable diagnostics.
- sanitize provider diagnostics while preserving actionable verbose logs.
- automated tests must use isolated transports and test-only authorization. They must be physically incapable of billable generation, upload to a real provider, or destructive remote actions.
- paid requests remain a fresh, explicit human action through the application.

## Testing and refactor execution

- add characterization before moving high-risk persistence, job, materialization, playback, or cancellation behavior.
- mirror production feature ownership in tests; share builders/fakes only when semantics are genuinely repeated.
- use `ReelForge.Core.Tests`, `ReelForge.Application.Tests`, and `ReelForge.Infrastructure.Tests` for tests that require only that production layer. Keep cross-layer provider, filesystem, media, and offline-acceptance coverage in `ReelForge.Tests`; keep Windows integration and WPF presentation coverage in their dedicated Platform.Windows and App suites.
- non-Windows CI runs the four portable suites. Windows CI restores the solution, builds Debug and Release, then runs all six suites in Release. Run `dotnet test ReelForge.sln --configuration Release --no-build` only after a Windows solution build; on other platforms, invoke the four portable test projects explicitly as shown in the README.
- keep the full suite green after every work unit. Run the relevant human workflow after a high-risk WPF/media change.
- refactor in behavior-preserving vertical slices. Do not mix schema redesign, editor features, or provider contract changes into a structural commit.
- keep each commit independently buildable and rollback-friendly with a brief declarative message.
