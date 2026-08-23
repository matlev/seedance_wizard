# Milestone 3 remaining architecture and migration plan

Status: approved architecture, ready for delegated execution

Assessment baseline: commit `896ed6c` on branch `milestone-3`

This document narrows the original repository-wide refactor plan to the work that remains after Stages 0–5 and the completed portions of Stage 6. It is the integration contract for the rest of Milestone 3. The architectural coordinator owns the decisions and integration described here; bounded implementation is delegated to refactor workers and validated against focused automated and human acceptance paths.

## Current state

The original four-layer direction remains sound and is now enforced at the principal portable boundaries:

```text
Core
  ^
Application
  ^
Infrastructure
  ^
Platform.Windows / WPF App
```

The Windows platform assembly, explicit runtime composition root, feature-organized production projects, decomposed persistence/materialization/provider/generation services, global job finalization, and focused WPF feature controls and coordinators are established. The portable suite contains 300 tests and the Windows platform suite contains 3 tests.

The principal remaining production concentration is `MainWindow.xaml.cs`, now about 1,473 physical lines. Its remaining responsibilities are shell and cross-feature integration:

- media-preparation entry/exit policy plus cross-feature projection of Saved Frame/Clip results;
- Project Media selection consequences and dialog/presentation routing to the focused operations coordinator;
- generation continuation and submission presentation adapters around the focused generation coordinators;
- narrow WPF presentation adapters around the focused composition workspace, preview, and render coordinators;
- shell workspace state, project lifecycle, refresh, status, and cross-feature routing.

The original largest non-UI exception, `WorkingCompositionService`, has now been decomposed behind its compatibility facade. Composition lifecycle, current-state access, transactional recipe revision edits, segment commands, audio commands, and exact split mutation have focused owners under `Editing/Composition` and `Editing/Audio`. The CI topology is now established: Ubuntu restores and runs the portable Release suite, while Windows restores once, builds Debug and Release, then runs every suite in Release. Test projects remain broad, and the final naming/dead-code audit has not begun.

## Remaining target architecture

### Production project boundaries

Keep the existing five production projects. Do not add a DI container, provider assembly, FFmpeg assembly, persistence assembly, or speculative cross-platform UI abstraction during Milestone 3.

- **Core** owns durable domain state and invariants only.
- **Application** owns project use cases, transactional composition editing, generation workflows, media contracts, and portable presentation calculations.
- **Infrastructure** owns persistence, filesystem realization, FFmpeg/ffprobe, providers, hosting, caching, and diagnostics.
- **Platform.Windows** owns Windows paths and credential integration.
- **App** owns WPF controls, WPF-local state, dialogs, workspace coordinators, and cross-feature presentation routing.

`ApplicationRuntime` remains the explicit composition root. A service locator or broad dependency container would conceal the ownership this milestone is intended to clarify.

### Working Composition application boundary

Keep `WorkingCompositionService` as a compatibility facade while callers are migrated. Its implementation must delegate to cohesive concrete collaborators rather than retain every operation privately:

- a **current-composition accessor** resolves the Working Composition asset, pinned current revision, and recipe;
- a **transactional revision editor** is the sole owner of recipe cloning, immutable commit, provenance refresh, draft refresh, save, and rollback;
- **segment commands** own add, reorder, removal, and source-audio inclusion;
- **audio commands** own add, timeline placement, mute/gain/pan/fades, and removal;
- **composition lifecycle** owns initial Working Composition creation;
- **split mutation** owns creation of exact boundary state, Saved Clip descendants, and replacement of one segment in one atomic project edit.

These are concrete application collaborators, not one-method interfaces. `CompositionSegmentSplitService` continues to own materialization and decoded-frame selection, then delegates the atomic domain mutation to the split owner. `CompositionSegmentAudioDetachmentService` continues to own physical extraction and delegates timeline insertion to the audio command owner. No `.rfp` schema or edit behavior changes are permitted.

### Composition timeline presentation boundary

Create one focused WPF timeline control under `Views/Editing` (a later folder rename to `Views/Timeline` is mechanical). It owns:

- ruler, zoom, duration, playhead, sticky identity badges, and lane rendering;
- segment/audio selection and local hover/context-menu presentation;
- pointer capture and local gesture state for scrubbing, segment reorder, audio movement, and Project Media drops;
- drag tokens, insertion markers, edge autoscroll, and the session-only playback autoscroll toggle;
- semantic intents for seek phases, selection, reorder, audio placement, drop, split, shift, detach, and removal.

It must not open projects, call `WorkingCompositionService`, materialize media, control `MediaPreviewPanel`, or decide whether a paid/network action occurs. The parent edit-workspace coordinator supplies timeline items and capabilities, executes application commands, coordinates preview/audition, and refreshes the control. Timeline and media viewer are not extracted or redesigned in the same unit.

### Remaining WPF coordinators

After timeline ownership is stable, add focused App-layer coordinators where cross-view workflows remain too large for the shell:

1. **Frame preparation coordinator** — exact-frame indexing/window refresh, cancellation, Saved Frame/Clip mutations, and preview coordination. `MediaPreparationPanel` retains presentation state.
2. **Project Media operations coordinator** — rename, export, extraction, delete dependency checks, move/copy, import, and selection consequences. `ProjectMediaPanel` retains list/context/drag presentation.
3. **Generation workspace coordinator** — draft capture/load, continuation preparation, Undo Send bridge, workflow invocation, owning-project reconciliation, and safe auto-preview policy. Provider submission remains in Application and stays human-authorized.
4. **Shell coordinator cleanup** — project lifecycle, workspace mode, project-local UI restoration, status/error routing, and wiring among focused controls. `MainWindow` should then be recognizable as composition and integration, not a feature implementation.

Coordinators may depend on WPF view contracts and therefore belong in App. Portable calculations and use cases belong in Application. Do not push WPF state inward merely to reduce a line count.

### Tests and enforcement

After production ownership stabilizes, replace the single broad portable test project with projects that enforce the production dependency direction:

- Core tests;
- Application tests;
- Infrastructure tests;
- Windows platform tests;
- Windows App presenter/control tests;
- network-isolated acceptance tests with the explicit MP4 fixture.

Add architecture checks for App and Platform.Windows references, portable non-Windows build/test CI, Windows full build/test CI, and focused composition-root lifetime coverage. Provider and acceptance tests must remain incapable of live paid submission.

## Ordered migration sequence

Each unit is independently buildable, testable, commit-sized, and reviewed before the next overlapping unit begins.

Behavior preservation is the default migration constraint, not a requirement to preserve a known defect. When a recorded bug's root cause belongs directly to the responsibility being extracted, the unit may intentionally correct it if the old failure is characterized, the new behavior has focused regression coverage where practical, and the acceptance notes identify the change explicitly. Do not expand a refactor into unrelated ownership merely to reach a deferred bug, and do not declare a bug fixed because behavior changed incidentally.

### Unit 1 — decompose Working Composition editing

Status: complete. Implementation, automated verification, deep review, and human smoke acceptance passed.

Write set: `ReelForge.Application/Editing/Composition/**`, existing composition services/facade, and focused composition tests.

Preserve the public facade while extracting the accessor, transaction owner, lifecycle, segment, audio, and split mutation responsibilities. Move the resulting files into the Editing/Composition and Editing/Audio topology. Run composition, persistence, split, detachment, and invariant tests. Human smoke: create a composition; add/reorder/remove/split video; change segment audio; add/move/mix/remove audio; close and reopen.

### Unit 2 — extract the Composition Timeline control

Status: complete. Implementation, automated verification, deep review, and human smoke acceptance passed.

Write set: `ReelForge.App/Views/Editing/CompositionTimeline*`, the inline timeline XAML, timeline-specific shell wiring, and focused timeline presentation tests if deterministic logic is extracted.

Move the complete timeline visual and local gesture state behind semantic events. Preserve current timeline calculations and application commands. Run layout, split, detachment, audition-plan, and WPF build checks. Human smoke: complete Timeline Arrangement and Audition rows, including rapid cross-cut scrubbing.

### Unit 3 — extract frame-preparation orchestration

Status: complete. Implementation, automated verification, deep review, and human smoke acceptance passed.

Write set: a focused App coordinator, frame-preparation shell wiring, and frame/Saved Frame/Clip tests.

Move indexing, cancellation, progressive contact-window refresh, keyboard navigation, save/update/delete, and clip creation orchestration out of the shell without moving `MediaPreviewPanel`. Human smoke: exact navigation, frame/clip lifecycle, non-video selection reset, short-clip replay, and cache-cleared reconstruction.

### Unit 4 — extract Project Media operations

Status: complete. The Project Media grouping/presentation and unified rename slices are complete with human acceptance. Export, audio extraction, dependency analysis, deletion, physical copy/move, cached-media copy, and import route through the focused operations coordinator and passed their internal automated, review, and human smoke gates. Saved Clip audio extraction and timeline detachment resolve incomplete legacy metadata through materialization and inspection. Cached Saved Frames, Saved Clips, and the Working Composition copy to another project as permanent physical media; cached Move is intentionally unavailable because it would conflate flattening with deletion of editable source state. Physical selection preparation (path resolution, missing-source persistence, and lazy ffprobe metadata persistence) routes through a focused Infrastructure service; cross-feature selection routing remains explicit in the shell.

Write set: a focused App coordinator, Project Media shell wiring, and file-operation tests.

Move rename/export/extract/delete/move/copy/import policy and dialogs behind typed outcomes. Keep cross-feature selection routing explicit. Add a Project Media context action for renaming a Saved Clip's project display name without changing its source media, recipe identity, or materialized filename. Preserve the planned presentation enhancement allowing each Project Media group (Videos, Audio, Saved Frames, Saved Clips, and Compositions) to be independently collapsed or expanded without changing project-domain state. Human smoke: every Project Media kind, group collapse/expand behavior, physical and Saved Clip display-name changes, dependency-protected deletion, copy/move between projects, audio extraction, and drag import.

### Unit 5 — extract generation workspace orchestration

Status: complete. Provider runtime selection, draft capture/load/reference mapping, prompt expansion, draft autosave, and exact-frame continuation preparation live behind focused Generation workspace collaborators. A focused submission coordinator owns credential checks, the explicit human paid-confirmation bridge, immediate submission, Undo Send scheduling and cancellation, global job tracking, finalizer presentation, and guarded auto-preview. Delayed submissions always reopen the captured owning `.rfp` in an isolated workspace, so project switching remains safe while submission and result projection are restricted to the owning project. The shell retains only thin event routing and Project Media selection. The portable generation/job suite, WPF build validation, and human fake-provider smoke matrix passed. Focused App-coordinator regression tests remain scheduled for Unit 7, when the App test topology exists.

Write set: a focused App coordinator, generation shell wiring, and generation/job tests.

Move draft/continuation/Undo Send/workflow/result coordination out of the shell while preserving explicit paid confirmation and global jobs. Human smoke uses the fake provider only: draft/reference preparation, Undo Send cancel/submit, project switch/restart recovery, result selection, and no paid network call.

### Unit 6 — shell and topology cleanup

Status: complete. Composition editor projection, segment/audio selection, timeline mutation routing, and Edit Tools mutation routing now live in a focused `CompositionWorkspaceCoordinator` and passed human smoke acceptance. Shared transport events, composition audition lifetime, timeline/playhead seeking, frame-step gating, and stale rendered-preview invalidation now live in `MediaPreviewCoordinator`. `CompositionRenderCoordinator` now owns the captured-target preview/export lifecycle, render gating, cancellation, interaction suppression, audition quiescence, stale-result rejection, lease adoption, and render status/error cleanup; the shell retains only event forwarding and narrow WPF dialog/viewer adapters. Project Media rename/export/extract/copy/move/delete presentation policy now routes through `ProjectMediaCommandCoordinator`, while the shell retains only event routing and a narrow dialog/refresh/selection handoff. `ProjectMediaProjectionBuilder` now owns the ordered Project Media, generation-reference, and generation-history projections while the shell retains observable-collection publication and selection/preparation lifecycle. `MediaImportCoordinator` now owns import eligibility, concurrent-import gating, Project Media action disable/enable lifetime, import invocation, refresh, and import result status while the shell retains Windows file-dialog and drag/drop presentation adapters. `ProjectLifecycleCoordinator` now owns dialog-driven create/open flows, automatic last-project reopen, recent-project memory, and project-local workspace/selected-media state persistence and restoration; it reads the current settings dynamically after Settings reload, while the shell retains WPF status, dialog, selection, and workspace-mode publication. A set of previously flat Application feature files has been moved mechanically into capability folders without namespace or behavior changes. The final ownership audit found no remaining feature state machine embedded in `MainWindow`; its retained selection consequences, refresh/projection publication, status/error routing, and coordinator host adapters are intentional shell responsibilities. Focused App-coordinator tests remain scheduled for Unit 7.

Remove superseded shell fields/methods, finish App folders/resources, move remaining cohesive Application and Platform files to their target folders, and explain any intentionally large retained production files. Do not split cohesive files solely to meet a number.

### Unit 7 — test topology and CI

Status: in progress. The dedicated WPF App test project now provides focused coordinator and preview-policy coverage. Deterministic shared-preview policies establish that ordinary Project Media ticks never move the composition timeline, retained baked-composition ticks do, and fast-audition positions yield to quiescence and active timeline seeks. Retained composition-preview identity now requires exact in-session `VideoProject` and `ProjectLocation` instances as well as composition and revision IDs, preventing a reopened or copied same-GUID project from restoring stale cached media. Deterministic App and Platform.Windows assembly-reference checks enforce their direct production-layer dependencies. The first portable split is complete: Application-only tests now live in `ReelForge.Application.Tests`, Infrastructure-focused tests now live in `ReelForge.Infrastructure.Tests`, and the remaining broad `ReelForge.Tests` suite retains cross-layer coverage. GitHub Actions restores and runs all three portable suites on Ubuntu; Windows builds Debug and Release, then runs all five suites in Release. The broader layer-by-layer split of the remaining portable suite remains staged work.

Split tests by layer/capability, centralize only proven shared fakes/builders, and continue adding focused App coordinator coverage. App-level playback coverage must distinguish the three shared-viewer modes: baked-composition ticks advance the composition playhead, ordinary Project Media ticks do not, and quiesced fast-audition sessions remain pinned to their audition position. It must also cover bake-selection lifecycle races: an unchanged session bake is restored after selecting other media, a changed revision falls back to audition, and stale physical inspection or cached-restore work cannot publish over a newer exact Project Media selection. CI builds Debug and Release, then runs all suites in Release.

### Unit 8 — final audit and acceptance

Remove dead or Seedance-era tracked names, refresh repository/contributor documentation, rerun the file/responsibility inventory, and execute the complete manual acceptance matrix. Every remaining large file must have a documented cohesive reason to exist.

The known behavior bugs—restoring a valid baked composition after restart, frame stepping across fast-audition cuts, and ordinary viewer frame-step controls not synchronizing the precision-frame strip while Select Frame or Make Clip is active—remain deferred unless a refactor unit naturally takes ownership of their root cause and fixes them under the characterization, regression-coverage, review, and acceptance rule above. They must not be accidentally declared fixed or silently changed by a structural commit.

The FFmpeg foreground-responsiveness investigation is also deliberately paused until the next work session. Current evidence shows copy/export materialization can run several sequential commands, including a normalized concat re-encode, without an explicit process-priority or thread-budget policy. Benchmarking and any execution-policy changes remain a separate performance unit; they must not be folded into the structural refactor without measured evidence.

Project-file scalability is now an explicit future persistence task. A small development project has already demonstrated that immutable recipe history can produce an approximately 650 KB, 20,000-line `.rfp`, so the current full-document JSON read/validate/map and validate/map/atomic-rewrite path must be benchmarked before real projects make the cost user-visible. Build representative fixtures by both serialized size and revision/generation/anchor count, measure open and ordinary-save latency, peak memory, and write amplification, and establish a warning threshold (initially 200 ms for an ordinary save or one second for open). Only then choose among history compaction, an authoritative sidecar/history store, SQLite, or another indexed representation. The existing project-store seam is useful but insufficient on its own: domain loading and validation must also stop requiring every historical record in memory before a different backend can materially help. This investigation must preserve immutable referenced history, atomic/corruption-safe commits, and the current pre-release freedom to replace the development format without a migration ladder.

Project import also has a pre-existing reliability limitation: a multi-file import can leave previously committed media files on disk if a later file fails, and a final project-save failure can leave imported files plus in-memory project mutations without durable project metadata. The Project Media coordinator extraction deliberately preserves this behavior. A future reliability unit must design and test the complete import transaction and rollback boundary rather than add partial cleanup in the WPF shell.

## Coordination and review policy

- Explorers perform read-only dependency tracing before a boundary is assigned.
- Refactor workers receive a concrete write set, preserved behaviors, focused tests, and forbidden scope.
- Parallel workers are allowed only when their write sets do not overlap and neither depends on the other's uncommitted result.
- Several bounded, sequential units may be grouped into one human-testing handoff when they form a coherent workflow and share an acceptance matrix. Each unit still receives its own automated validation, review gate, and commit before overlapping work continues.
- The architectural coordinator reviews every returned diff and resolves cross-cutting decisions.
- Use the refactor reviewer for transaction, persistence, media lifetime, generation authorization, job recovery, timeline/audition, and test-topology units.
- The primary thread performs final integration verification and commits each accepted work unit. It does not push unless explicitly requested.

## Completion gate

Milestone 3 is structurally complete when:

- `MainWindow` is a shell/integration surface and no feature state machine remains embedded solely for convenience;
- the Working Composition application facade coordinates focused owners and has one explicit transaction/rollback boundary;
- production folders make every major workflow discoverable without repository-wide search;
- test project references enforce the intended layers and both portable and Windows CI paths are defined;
- no major production file remains an unexplained mixed-responsibility monolith;
- Debug/Release builds, all automated tests, and the complete human acceptance matrix pass;
- network-isolated tests remain incapable of paid generation;
- deferred known bugs remain recorded for the post-refactor correction pass.
