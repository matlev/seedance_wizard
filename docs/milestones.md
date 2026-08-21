# Milestone plan

Status: Milestones 1 and 2 complete. The post-Milestone 2 architecture review and Milestone 3 planning gate is next.

## Product priority

ReelForge is primarily an AI-video-generation workbench. Editing exists to prepare references, inspect results, create continuations, and assemble useful outputs. The highest-priority loop is:

```text
prompt
  -> logical project references
  -> materialize only when required
  -> Seedance submission
  -> asynchronous job status
  -> downloaded durable project asset
  -> frame inspection and anchor
  -> retry, variant, branch, or continuation
```

A sophisticated timeline follows this loop rather than delaying it.

## Milestone 1 — project and media foundation

Delivered:

- Windows WPF shell and resizable editor layout
- portable project creation, opening, atomic save, and durable media import
- asset explorer, selected-media preview, playback, seek, and ffprobe inspection
- FFmpeg/ffprobe PATH discovery, explicit saved paths, executable browsing, cancellation, output capture, and safe argument construction
- project, physical-asset, generation, frame-anchor type, and preliminary timeline models
- provider abstraction, capability validation, fake provider, and persisted generation history
- schema-verified AtlasCloud Seedance 2.5 submission adapter covered by mocked HTTP tests
- Windows Credential Manager secret-storage boundary
- automated tests with no paid generation calls

Architectural debt carried into Milestone 2:

- every `ProjectAsset` still assumes physical-path semantics;
- derived operations are provenance labels, not durable typed recipes;
- `FrameAnchor` is not owned/persisted by `VideoProject`;
- generation references support asset IDs only, without anchor, role, order, or revision semantics;
- submitted requests are not explicit immutable snapshots;
- `ParentGenerationId` has no retry/variant/continuation meaning;
- remote job completion, output download, and durable asset ingestion are not modeled as separate outcomes;
- preview/provider code cannot materialize virtual inputs;
- `cache/` has no explicit non-authoritative materialization contract;
- the project format is intentionally pre-release and incompatible development files are rejected rather than migrated;
- timeline types are placeholders rather than a renderable composition model.

## Milestone 2 — AI generation loop on logical media

### Phase 2A — foundation for logical media and generation history

Implemented on the Milestone 2 branch. The materialization/provider-preparation items in this phase are contracts only; rendering, uploading, and paid submission remain later work.

Complete this foundation before enabling paid submission:

1. Approve the physical/virtual asset architecture and retention-neutral materialization invariant.
2. Define the current logical asset envelope, project-owned anchors, and SHA-256 physical content identity kept separate from logical ID and display/file name.
3. Define mutable recipe edit drafts and immutable committed recipe revisions with predecessor links and exact revision-pinning by every authoritative reference.
4. Define immutable submitted-generation request snapshots.
5. Define ordered/role-aware logical generation references targeting assets or frame anchors, pinning exact virtual recipe revisions and expected physical SHA-256 where applicable.
6. Define supplementary materialization receipts that never replace logical reference provenance.
7. Define the settled single-parent lineage pair: nullable `ParentGenerationId` plus nullable `RelationshipType` (`RetryOf`, `VariantOf`, `ContinueAfter`, `ContinueBefore`, or `BasedOn`).
8. Keep relationships independent from the references actually submitted.
9. Define one autosaved mutable `GenerationDraft` per project that becomes an immutable history record only when submitted.
10. Define the settled optional reference-role vocabulary: general, start/end frame, character, style, environment, motion, and audio, with user label/notes kept separate.
11. Define bidirectional generation/output provenance, multiple output asset IDs per job, per-generation provider/model selection, and remote-completion versus local-ingestion state.
12. Enforce that a main video is durable physical media and that virtual/cache selection requires promotion first.
13. Introduce the minimum materialization, provider-preparation, and retention-policy boundaries needed by generation workflows; do not select a final cache-retention policy.
14. Keep explicit persistence DTOs separate from domain models, save atomically, and reject obsolete pre-release project formats with a clear message rather than maintaining migration code.

Phase acceptance checks:

- current-format projects round-trip without changing IDs, immutable revisions, reference occurrence IDs, or authoritative media identity;
- obsolete development formats are rejected without modifying the original file;
- a submitted request snapshot cannot drift when its source draft changes;
- committed recipe revisions cannot drift when a virtual asset receives a newer revision;
- renaming physical media preserves `AssetId` and SHA-256, while changed bytes under the same name produce a detectable mismatch;
- lineage permits at most one cycle-checked parent and does not determine provider inputs;
- retry creates a new record while preserving the failed record;
- duplicate/branch remain UI actions that submit as `VariantOf` or `BasedOn`;
- an autosaved draft survives reopen but does not appear in immutable history before submission;
- generations from different providers/models coexist in one project and each job may own multiple outputs;
- a generation can reference a physical asset, virtual asset revision, or frame anchor without storing a cache filename;
- a main video cannot depend on cache storage;
- deleting the entire cache does not invalidate generation history.

### Phase 2B — complete the generation loop

Implemented on the `milestone-2` branch. All automated verification is network-isolated and no paid request was made. The desktop defaults to the fake provider; official BytePlus ModelArk, AtlasCloud Seedance 2.5, and AtlasCloud MiniMax H3 are independently selectable, and every real submission is reachable only from an explicit button click followed by a per-request human charge confirmation. BytePlus remains the official Seedance route; AtlasCloud supplies alternate Seedance and H3 routes. Virtual recipe and frame-anchor representations remain deliberately rejected until their materializers arrive in Phases 2D and 2C respectively.

Build the real provider lifecycle before general-purpose editing:

1. Add per-provider BytePlus ModelArk and AtlasCloud credential configuration backed by Windows Credential Manager.
2. Add per-draft/provider selection and capability-driven Seedance 2.5 mode/settings UI; never bind one provider/model to the project.
3. Add logical project-reference selection with optional settled role, order, and user label/notes.
4. Freeze the immutable request snapshot before materialization or network work.
5. Resolve physical, virtual, and anchor references through application orchestration.
6. Materialize provider-compatible media only when required.
7. Prepare/upload provider-side references using only verified API contracts; do not infer an asset-upload API.
8. Submit generation jobs through the existing provider abstraction.
9. Poll verified asynchronous job state in an application-scoped Jobs view and display project, provider/model, elapsed time, current state, and useful diagnostics across project switches and restarts.
10. Keep monitoring automatic and separate from remote cancellation; expose remote cancellation only when a provider contract is verified and supported.
11. Download completed output, verify/inspect it, atomically place it in `generated/`, and ingest it as a durable physical video asset.
12. Link every returned output asset to its one originating generation in both directions and select the first successfully ingested generated video as main video when none exists.
13. Persist sanitized provider/materialization receipts where useful, without depending on local temporary files or expiring secrets.
14. Implement new-record semantics for Retry, Variant, Continue After, Continue Before, and Based On; expose Duplicate/Branch as draft-creation UI actions mapped to `VariantOf` or `BasedOn` at submission.
15. Preserve every submitted attempt, including failed and cancelled attempts, as immutable history.
16. When deleting the main video, require replacement selection or an explicit no-main-video state; demotion never removes or caches the durable asset.

Implemented vertical order within Phase 2B:

1. Implement AtlasCloud text-to-video, polling, download, inspection, durable ingestion, and provenance end to end.
2. Add credential/provider/settings UI and documented AtlasCloud multipart reference preparation.
3. Add official BytePlus ModelArk Seedance 2.5 as a peer adapter using model `dreamina-seedance-2-5-260628`.
4. Route provider-specific preparation: BytePlus local references through provider-neutral private R2 presigned HTTPS URLs (with inline image/audio as a no-host fallback), existing qualified HTTPS/`asset://` references, and AtlasCloud multipart upload.
5. Keep all contract tests network-isolated and retain the same human-only paid-submission authorization boundary.
6. Add virtual/anchor reference preparation once materialization is available.
7. Add retry/duplicate/branch/continuation actions after immutable snapshots and output links are proven.
8. Consolidate machine configuration into a discoverable Settings window and layered JSON settings model; keep every credential behind `ISecretStore` and out of projects.
9. Add provider-neutral temporary asset hosting and a private Cloudflare R2 implementation with content-addressed SHA-256 keys, deduplicating `HEAD`/`PUT`, and transient presigned GET URLs.
10. Route BytePlus materialized references through `ITemporaryAssetHost` without placing Cloudflare details or signed URLs in domain history.
11. Add AtlasCloud MiniMax H3 T2V, first/end-frame I2V, and multimodal R2V as a distinct provider route sharing the verified AtlasCloud transport, credential, and upload preparation.
12. Split human-authorized submission from application-scoped monitoring, persist unresolved jobs outside project files, restore them on startup, and reconcile terminal results into the owning project regardless of the currently open project.
13. Add collision-safe cross-project physical-asset copy/move operations: copies receive a destination-local asset ID and source receipt, unreferenced moves remove the source, and referenced moves retain the source rather than breaking immutable history.
14. Retain reconciled success/failure/cancellation entries in the Jobs pane across restarts until the user views Jobs and then leaves the tab; freeze displayed elapsed time at terminal status.
15. Add application-level Undo Send (0-30 seconds): persist an immutable local queued generation before any provider work, capture each job's deadline independently of later setting changes, expose guaranteed local cancellation in Jobs, and safely cancel unclaimed entries interrupted by application shutdown.

Phase acceptance checks:

- explicitly confirmed AtlasCloud MiniMax H3 text-to-video and reference-to-video submissions have proceeded through job polling to durable generated assets; active monitoring survived restart with correct wall-clock elapsed time, the owning project reconciled correctly, and an existing main video remained selected while the new continuation arrived unstarred;
- normal application startup and unit/integration tests cannot accidentally make paid requests;
- remote completion without a valid local download is not reported as a successfully ingested project asset;
- a downloaded output answers which generation, prompt, model, settings, references, and lineage produced it;
- retry creates a distinct potentially billable job and never overwrites the original failure;
- clearing materialization cache does not affect job history, output assets, or logical references.

### Phase 2C — Generate/Edit workspace and media preparation

Complete. Human visual/E2E acceptance covered workspace switching and persistence, progressive Saved Frame creation, all Saved Clip workflows, cache reconstruction, missing-source behavior, reference preparation, short-clip replay, and concurrent UI-settings persistence. The exact-frame/anchor/provider foundation is preserved behind explicit Generate/Edit workspaces, shared Project Media, progressive Select Frame navigation, non-destructive Saved Clips, guarded output preview, visual references, and one project-owned Working Composition shell.

#### Phase 2C.8 — workspace and Project Media revision

1. Remove the starred Main Video UX and domain field; Generate tracks only ephemeral currently previewed media.
2. Add application-level Generate/Edit switching without reopening the project.
3. Keep Jobs global and workspace-neutral in the top application chrome.
4. Project physical assets, virtual assets, and Saved Frames into one grouped Project Media presentation without merging their domain types.
5. Display physical images/videos/audio, materialized Saved Frames, and materialized Saved Clips in the shared viewer.
6. Auto-preview a completed output only when its project is open, Generate is active, and Select Frame/Make Clip is not active.
7. Establish an intentional Edit empty/start state without implementing the full timeline.
8. Replace the obsolete floating-point timeline root with one project-owned, ID-addressed Working Composition whose segments pin exact asset/recipe identities.

#### Phase 2C.9 — explicit progressive Select Frame

1. Selecting a video performs ordinary preview/inspection only; it does not start frame indexing.
2. Replace the permanent Precision Frames surface with default Media Preparation actions: Select Frame and Make Clip.
3. Remove the spacing selector and frame-browser Continue After/Before actions.
4. Load a bounded exact-frame window around the approximate player position and progressively prefetch adjacent decoded frames near either edge.
5. Preserve frame-by-frame arrow navigation, exact First Frame, final decodable Last Frame, cancellation, deterministic extraction, and disposable caches.
6. Move Saved Frames into Project Media with thumbnail, source navigation, metadata, degraded state, and exact-revision behavior.

#### Phase 2C.10 — Saved Clips and narrow trim materialization

1. Create Saved Clips for SourceStart→anchor, anchor→SourceEnd, and anchor→anchor using natural `BeforeFrame`/`AfterFrame` intent.
2. Do not automatically expose clip-only exact boundaries as user-facing Saved Frames; retain explicit future promotion.
3. Commit each Saved Clip as a virtual video asset plus immutable trim-recipe revision without a permanent subclip MP4.
4. Pull forward only the trim recipe compiler/materializer required for Preview and ProviderUpload.
5. Reuse deterministic cache, active leases, source verification, provider-neutral preparation, AtlasCloud upload, and BytePlus/R2 hosting.
6. Pin the selected virtual recipe revision in generation drafts and immutable snapshots.

#### Phase 2C.11 — visual references and acceptance

1. Give reference choices thumbnails, media-type identity, human labels, hover preview, and click-to-view behavior while retaining the checkbox grid temporarily.
2. Verify workspace switching, Project Media persistence, progressive long-video navigation, Saved Frame reopening, all three Saved Clip boundary cases, and non-disruptive background completion.
3. Prove Saved Clip preview/provider preparation through network-isolated tests that cannot submit paid generation.
4. Keep full Add Reference picking, multitrack editing, composition rendering, audio tooling, and generalized export outside this surgical pass.

#### Phase 2C.1 — anchor semantics and current project format

Delivered: stable anchors, immutable exact revisions, pinned recipe/history references, occurrence IDs, dependency-safe archive/removal, explicit current-format DTOs, atomic persistence, incompatible-format rejection, and validation/round-trip coverage are implemented.

1. Use stable logical `FrameAnchor` objects and immutable `FrameAnchorRevision` chains.
2. Make video stream index plus integer presentation timestamp and rational time base authoritative for new anchors; derive display seconds and retain frame number only when reliable.
3. Require exact presentation timing in the current model; obsolete floating-point-only development formats are rejected rather than converted.
4. Keep display label, notes, archive state, and current-revision pointer on the logical anchor.
5. Pin exact anchor revisions from committed extract-frame/trim recipes and future editing consumers.
6. Add `BeforeFrame` and `AfterFrame` anchor-boundary edges and normalize composition intervals to `[start,end)`.
7. Add a stable occurrence `ReferenceId` to every generation draft/history reference.
8. Archive/tombstone referenced anchors instead of destroying required history; retain degraded anchors when source media is missing or hash-mismatched.

#### Phase 2C.2 — provider-neutral materialization and prepared references

Delivered: typed asset/anchor materialization targets, occurrence-keyed prepared references, source hash verification, exact decoded-frame realization, and persisted per-occurrence materialization receipts are implemented. Provider-ready URLs/upload IDs remain transient while receipts retain the logical source hash, extracted-byte hash, pinned anchor/recipe plan identity, media encoding, provider scope/reference ID, and expiry when available.

1. Generalize materialization targets from asset-only requests to typed asset-revision or anchor-revision targets.
2. Replace asset-ID-only provider overrides with occurrence-keyed transient prepared references carrying media type, role, order, and provider representation.
3. Keep expiring URLs, data URLs, upload IDs, cache paths, and other provider/materialization details out of authoritative Core provenance.
4. Verify the pinned source SHA-256 and resolve the exact decoded anchor frame before purpose-specific preview/provider transformations.
5. Record receipts tying derived encodings/uploads back to the same anchor revision, canonical extracted-frame hash, and transformation profile.

#### Phase 2C.3 — exact frame indexing and extraction

Delivered engine slice: exact decoded presentation-frame indexing and PTS-selected PNG extraction are implemented for physical video sources. Cache identity includes source content, immutable anchor revision, exact stream/timing coordinates, purpose/profile, extraction algorithm, and the detected FFmpeg version. Duplicate extraction is coalesced, cache commits are atomic, cancellation artifacts are removed, and deleted derivatives reconstruct without changing anchor state. The Phase 2C.4 UI is the first user-facing consumer.

1. Add cancellable FFmpeg/ffprobe-backed exact-frame discovery and extraction for physical video sources.
2. Define First Frame and Last Frame using decoded presentation frames; Last Frame is the final decodable presentation frame rather than `duration - epsilon`.
3. Use deterministic cache keys based on source content, stream/PTS/time base, anchor revision, purpose/profile, and renderer fingerprint.
4. Coalesce duplicate work, write through unique temporary files, commit cache entries atomically, and clean up cancellation/failure artifacts.
5. Reconstruct missing thumbnails/extracted frames after cache deletion without modifying logical anchors or history.

#### Phase 2C.4 — frame browser and Saved Frames UI

Delivered initial workspace: selecting a physical video indexes exact decoded presentation frames, replaces the Timeline placeholder with a nine-position contact strip, supports frame/quarter-second/half-second/second spacing, refreshes around the playhead with debounced cancellation, extracts the center frame first, and provides first/final decoded-frame shortcuts. Named Saved Frames persist independently from disposable thumbnails, can be revisited/jumped to/edited, and use dependency-safe removal or archive.

1. Replace the central lower Timeline placeholder with an initial precision-frame workspace, without claiming the full timeline editor is implemented.
2. Add a local contact strip centered near the playhead, initially approximately nine frames with selectable spacing/range.
3. Debounce navigation, extract the center/selected frame first, opportunistically prefetch a bounded neighborhood, and cancel stale work.
4. Create Saved Frames from exact extracted frames; add first/last-frame shortcuts, cached thumbnails, jump-to-frame, label/notes editing, and dependency-safe delete/archive.
5. Keep Saved Frames visually distinct from physical Project Assets. Historical revisions remain resolvable through history/dependency details but do not receive a general revision browser in Phase 2C.

#### Phase 2C.5 — generation-reference integration

Delivered: the reference grid now combines physical assets and Saved Frames, freezes an exact anchor revision at submission, supports duplicate occurrences with independent IDs/roles/order/labels/notes, validates Saved Frames as image references, and routes their extracted PNGs through the existing AtlasCloud or BytePlus preparation service. Offline acceptance coverage proves extraction, preparation, immutable snapshotting, typed receipts, persistence, and provider input without network access.

1. Select Saved Frames directly as logical references with explicit role, order, label, and notes.
2. Support duplicate occurrences of the same logical object through distinct `ReferenceId` values.
3. Freeze exact anchor revision, source identity, stream/timing semantics, and reference occurrence metadata before any paid work.
4. Materialize and prepare anchors through the same provider-neutral workflow used by assets, then route through AtlasCloud upload or BytePlus/R2 as required.
5. Allow anchor plus source-video references when supported; provider capability validation remains authoritative.

#### Phase 2C.6 — Continue After and Continue Before UX

Delivered initial UX: generation-history continuation resolves a sole output automatically and requires explicit output selection for multi-output generations. ReelForge chooses the final decoded frame for Continue After or first decoded frame for Continue Before, shows the canonical extracted image and exact PTS/time-base before confirmation, creates a Saved Frame, assigns `StartFrame`/`EndFrame`, recommends a compatible visible mode, and preserves lineage only for generated sources. The precision workspace also creates continuation drafts from imported media without inventing a parent generation.

1. Require explicit source-output selection when a parent generation has multiple outputs.
2. Preselect the first/final decoded frame as appropriate, show its canonical extracted preview, and require confirmation before creating/using the anchor.
3. Recommend a compatible I2V or R2V mode while keeping provider/model/mode visible and user-selectable.
4. Map Continue After anchors conceptually to `StartFrame` and Continue Before anchors to `EndFrame`, subject to provider capabilities.
5. Record parent plus `ContinueAfter`/`ContinueBefore` only when the source actually originates from a generation; imported-video continuation UX uses the explicit anchor reference without invented lineage.

#### Phase 2C.7 — verification and acceptance

Automated and human visual/E2E acceptance are complete. Tests cover exact index parsing, deterministic cache reconstruction, duplicate-work coalescing, cancellation cleanup, exact FFmpeg arguments, Saved Frame materialization/preparation/snapshot persistence, duplicate occurrences, provider boundary-role mapping, and current-format anchor invariants. All test authorization remains network-isolated and incapable of paid submission.

1. Add incompatible-format, immutable-revision, dependency, exact-frame/VFR, cache reconstruction, failure cleanup, provider-preparation, duplicate-reference, and continuation-lineage tests.
2. Keep every automated provider path network-isolated and incapable of paid calls.
3. Use AtlasCloud MiniMax H3 for the first optional human-confirmed paid anchor-continuation test so Phase 2C validates anchors without simultaneously introducing an unproven provider route.
4. Treat a later BytePlus live smoke test as separate provider-confidence work.

Phase acceptance checks:

- any imported or generated physical video can be browsed at exact decoded presentation frames and named Saved Frames can be revisited after close/reopen;
- generating clip A, confirming its exact final-frame anchor, and generating continuation B produces an explicit `ContinueAfter` edge plus a reference occurrence pinned to the exact anchor revision;
- invoking continuation from imported media creates the anchor reference without inventing a parent generation;
- committed generation and editing references do not drift when the logical Saved Frame receives a newer current revision;
- edit boundaries can express before/after-frame intent and normalize without duplicate frames at adjoining cuts;
- deleting referenced Saved Frames archives them while all pinned revisions remain resolvable;
- clearing the entire frame/thumbnail cache removes no anchor, recipe, continuation provenance, or generation history and required images rematerialize correctly;
- preview and provider preparation resolve the same exact decoded source frame even when their purpose-specific derivative bytes differ;
- a provider-incompatible anchor representation is converted by materialization/provider preparation rather than by the UI;
- missing or changed source media leaves anchors visible in degraded state and prevents incorrect materialization/submission;
- no automated test can submit a live or paid provider request.

### Phase 2D — basic recipe-based media operations

Complete. ReelForge now has a provider-neutral recursive render-plan tree with deterministic plan identities, explicit compatibility analysis, normalized multi-segment concat, and a user-tested composition workflow. The Edit workspace can add selected physical videos or Saved Clips, reorder/remove segments through immutable recipe revisions, persist the result, preview it, and export it. A default-off application preference can retain modified-media representations as permanent project-owned files without changing their logical Project Media grouping. Human acceptance covered mixed-format and mixed-audio normalization/export, modified-media persistence, composition editing and reconstruction, and cache-backed replay. Virtual-video anchor time mapping is intentionally deferred to the Phase 2E timeline-position design rather than being hidden inside 2D materialization.

#### Phase 2D.1 — recursive planning and virtual sources

1. Resolve a requested virtual asset/revision into an explicit immutable plan tree before invoking FFmpeg.
2. Include pinned recipe revisions, canonical recipe boundaries, physical content identities, materialization purpose, and profile in deterministic plan identities.
3. Reject missing assets/revisions, unpinned virtual dependencies, incompatible media types, and dependency cycles before rendering.
4. Execute nested Saved Clip trims recursively while retaining per-node cache reuse and active leases.
5. Resolve a single-segment Working Composition through a pinned physical or Saved Clip source; keep multi-segment concat explicit and unsupported until its compatibility planner is delivered.

#### Phase 2D.2 — compatibility analysis and compatible concat

1. Represent composition compatibility as an explicit `Compatible`, `RequiresNormalization`, or `Unknown` planning result with property-level differences.
2. Inspect realized media when virtual-source metadata is incomplete rather than guessing compatibility from labels or file extensions.
3. Compile compatible ordered video segments into one FFmpeg concat filter graph with explicit audio handling and reset timestamps.
4. Render through unique temporary files, atomically commit the composition cache entry, retain dependency leases for the full operation, and reuse unchanged results.
5. Normalize mismatched dimensions, frame rates, pixel formats, and audio layouts into a common preview profile. Generate duration-matched silence for audio-disabled or silent segments so mixed compositions remain synchronized.

#### Phase 2D.3 — testable Working Composition editor

1. Present the committed Working Composition as an ordered segment list in the Edit workspace.
2. Add selected physical videos or Saved Clips while pinning each virtual source to its exact current recipe revision.
3. Move and remove segments through new immutable composition revisions; retain at least one segment.
4. Persist every discrete composition edit and reconstruct the same current revision after reopening the project.
5. Render or reuse the committed multi-segment composition preview through the shared materializer without creating an authoritative intermediate asset.
6. Expose **Export** for Saved Frames, Saved Clips, and the Working Composition without changing the project catalog.
7. Keep physical-versus-virtual promotion terminology out of normal UI. The default-off **Persist modified media on disk** setting retains materialized frames, clips, and compositions under `assets/modified/` while Project Media continues to present the same logical items and groups.

Human acceptance path:

1. Open a project containing at least two physical videos or Saved Clips, enter Edit, and start the Working Composition if necessary.
2. Select another video or Saved Clip in Project Media and use **Add selected media**; verify the ordered segment list updates.
3. Move the selected segment up and down, remove a non-final segment, and verify each action immediately updates the displayed recipe revision.
4. Preview a composition containing differently encoded or silent media; verify the result plays in list order with stable dimensions and synchronized audio/silence.
5. Use **Export** and verify the selected MP4 is written without adding another Project Media item.
6. Enable **Persist modified media on disk**, preview a Saved Frame, Saved Clip, and composition, and verify permanent representations appear under `assets/modified/` without duplicate Project Media rows.
7. Close and reopen ReelForge; verify the same current composition order returns and preview can use or reconstruct its representation again.

Phase acceptance checks:

- a virtual trim used for generation is recorded as the logical reference, never as `cache\*.mp4`;
- materializing the same unchanged recipe can reuse a valid representation;
- source/anchor/recipe/profile/renderer changes invalidate the relevant representation;
- cancellation and failed FFmpeg runs leave no authoritative partial file;
- clearing all materializations leaves recipes openable and reproducible;
- enabling modified-media persistence creates durable project-owned representations without duplicating or replacing their logical Project Media entries.

### Phase 2E — timeline

Complete. ReelForge projects the committed Working Composition into a duration-aware, zoomable timeline without introducing a second persistence model. Human acceptance covered timeline and Project Media drag/drop, reorder and removal, exact playhead splitting under both boundary preferences, frame stepping, fast source-by-source audition, authoritative preview/export, timeline seeking and auto-scroll, cancellation, audio placement and layering, mute/gain/pan/fades, whole-source extraction, exact segment detachment, and persistence across edits/reopen. Automated coverage verifies layout/time mapping, immutable composition changes, exact source/revision boundaries, deterministic rendering and mix behavior, cancellation cleanup, durable derived-audio provenance, and current project invariants without making paid provider calls.

#### Phase 2E.1 — visual composition timeline foundation

1. Project committed composition segments as ordered, duration-aware visual blocks.
2. Keep short segments selectable without pretending all segments have equal duration.
3. Use the timeline as the sole segment-selection surface rather than duplicating its content in a secondary list.
4. Map composition-preview playback through the projected segment spans and invalidate stale preview state after recipe edits.
5. Remove the redundant text projection beneath the visual timeline; the timeline is the composition interaction surface.
6. Insert physical videos or Saved Clips at the visual drop position and retain direct block selection after switching Project Media items. Project Media drags use a compact fixed-size insertion token and leave committed segment geometry, ruler timestamps, and total duration unchanged until drop.
7. Place physical audio on a separate timed row, persist it in immutable composition revisions, and mix it with source-video audio during preview/export.
8. Keep clip widths, ruler ticks, scroll position, and playhead position out of the `.rfp` schema.
9. Reorder video segments directly by dragging timeline blocks; neighboring blocks reflow during the transient preview and only a non-no-op mouse release commits one immutable recipe revision.
10. Expose selected video-segment source audio as **On / Muted**; commit each actual change as one immutable revision, invalidate stale previews, persist the choice, and render muted single- or multi-segment compositions correctly.
11. Reposition a timed audio clip by dragging its timeline block horizontally; pointer movement is transient UI state, mouse-up commits at most one immutable millisecond-precise start time, and clicks, sub-millisecond pointer noise, or other no-op drags create no revision. Keep the dropped visual stable while the commit completes.
12. Zoom the visual timeline from its default scale to 800% without changing recipe state. Uniformly scale blocks and ruler geometry, preserve approximate viewport focus during zoom, and keep playhead, insertion, reorder, drop, and audio-placement mappings tied to composition time. Provide an explicit reset to the default scale.
13. Seek a rendered composition preview or fast source-by-source audition by clicking or dragging its timeline ruler. Preserve the prior play/pause state, clamp ruler positions to composition bounds, and create no recipe revision. Fast audition avoids a whole-composition bake, may hiccup while switching sources, and synchronizes a cached audio-only mix of independent clips while deferring authoritative mix fidelity to **Preview composition**.
14. Auto-scroll a zoomed timeline when the playing composition's playhead leaves the visible viewport, returning it near the left edge or clamping at the final scroll extent. Keep a default-on **Auto-scroll during playback** checkbox beside the Timeline heading; toggling it acts immediately and remains session-only rather than becoming project or application settings state.
15. Auto-scroll a zoomed timeline while Project Media is held near either horizontal viewport edge. Continue scrolling while the pointer remains stationary, increase speed toward the edge, keep the insertion marker synchronized with the content passing beneath the pointer, and stop on drop, leave, cancellation, or the scroll boundary.
16. Apply the same timer-driven edge scrolling while reordering an existing video segment or repositioning an audio clip. Recompute the transient reorder target or millisecond placement against content revealed beneath the stationary pointer, then retain the one-revision-on-release and no-op/cancellation behavior.
17. Keep each visible video segment and audio clip identifiable while horizontally scrolled by pinning a compact identity badge near the viewport's left edge, constrained and clipped inside its owning block. Compress the badge as the block's trailing edge approaches and preserve the full identity/details in its tooltip.
18. Keep composition preview and export rendering responsive with an inline indeterminate activity indicator and explicit **Cancel render** action. Propagate cancellation through materialization to FFmpeg, remove partial exports, and report cancellation as an expected outcome rather than an error.
19. Split a selected video segment from its context menu at the current composition playhead without requiring a complete composition render. Snap to the nearest decoded source frame, honor the application-level **Media split behavior** choice (`BeforeFrame` by default or `AfterFrame` with the following decoded frame as its half-open boundary), create a hidden immutable exact-position boundary, create two distinctly named and reusable Saved Clips over the shared boundary, and replace the original occurrence with revision-pinned references to those clips in one recipe revision. Preserve the original source and support both physical sources and pinned Saved Clip revisions without creating physical intermediates.
20. Remove the permanent composition action footer. Keep insertion and reordering on drag/drop; show a red hover trash control on timeline objects; expose **Split at playhead**, **Shift Left**, **Shift Right**, and **Remove** in segment context menus; and expose Remove for audio clips.
21. Add previous/next decoded-frame controls beside playback so paused source, Saved Clip, fast-audition, and rendered-composition video can be positioned precisely without resuming playback.
22. Project overlapping independent audio clips onto automatic non-overlapping visual lanes so every clip remains readable and selectable. Keep lane assignment as derived UI geometry rather than persistent track identity; timing and overlap/mix semantics remain authoritative recipe state.

#### Phase 2E.2 — context-sensitive Edit Tools

1. Make the right-side Edit Tools panel respond to the single timeline object currently selected; show concise selection guidance when nothing editable is selected.
2. Keep structural actions such as insert, remove, split, drag, and reorder on the timeline while moving recipe-affecting properties into Edit Tools.
3. Move selected video-segment source audio into an **Audio** section in Edit Tools, preserving the existing immutable On/Muted behavior and renderer semantics.
4. Establish selection-specific sections: Clip/Source, Audio, Transform, and Timing for video segments; Timing and Audio for audio clips; and transition controls for a future selected junction.
5. Keep read-only media identity, encoding, source, and history in Inspector rather than duplicating them as editable controls.
6. Commit discrete changed values once and treat unchanged choices as no-ops. Hold continuous slider/drag manipulation as mutable UI draft state, then create one immutable recipe revision when the interaction is applied or completed; cancellation creates no revision.
7. Add selected audio-clip **On / Muted** and gain controls. Persist typed per-clip mix values, include them in deterministic render identity, compile them into FFmpeg before timeline delay/mixing, and commit a gain gesture only when the slider interaction completes.
8. Add selected audio-clip fade-in/fade-out controls. Persist millisecond-normalized durations, commit at most one revision per completed slider gesture, and apply fades before timeline delay and mixing. Anchor fade-out to the clip's audible end when the composition truncates a longer audio source.
9. Add selected audio-clip stereo pan from full left through center to full right. Persist normalized pan values, include them in render/cache identity, and commit at most one revision per completed slider gesture. Normalize every mixed input and source-video audio explicitly to 48 kHz floating-point stereo and encode 48 kHz AAC output rather than relying on FFmpeg's implicit sample-rate/channel negotiation.

#### Phase 2E.3 — durable audio extraction utility

1. Add **Extract audio…** to physical videos and Saved Clips in Project Media; keep the action hidden for images, Saved Frames, audio files, and compositions.
2. Resolve physical sources directly and Saved Clips through their exact committed recipe revisions, then create an audio-only M4A/AAC file with FFmpeg without changing the source.
3. Inspect and hash the completed file before adding it as a durable physical Audio asset under `assets/audio/`, with source asset/revision provenance and non-destructive duplicate naming.
4. Disable extraction when stored encoding metadata confirms the video has no audio; re-inspect/materialize sources whose metadata is not yet known before invoking FFmpeg.
5. Add timeline **Detach audio…** for a selected video segment. Materialize that segment's exact revision-pinned boundaries, extract a permanent M4A/AAC asset, add it at the segment's composition start, and mute only the segment's embedded audio in the same composition revision so the mix does not double. Preserve all pre-existing audio clips and allow the detached clip to overlap/mix with them.
6. Record composition/revision/segment provenance on detached audio, prevent accidental duplicate detachment of the same segment, inspect and hash the completed file before committing it, and roll back the partial file/asset if the composition update fails.

#### Editor capability research outcome

The market-research synthesis and the owner's fifteen product decisions are accepted as post-foundation direction in [Editor capability direction](editor-capability-direction.md). ReelForge targets an AI-native finishing editor for AI short-film makers, social creators, and hobby filmmakers rather than a general-purpose professional NLE. The research establishes future architecture for typed effect stacks, generic parameter automation, multitrack/time semantics, analysis artifacts, durable repair/Bake outputs, provider-neutral media editing, universal logical generation references, optional ML engines, and continuity-focused differentiation.

This does not expand Phase 2E into the complete researched feature set or make every researched “minimum” capability a first-release gate. Feature order and commercial tiering remain provisional until the business-model handoff is reconciled. The approved post-Milestone 2 architecture review and Milestone 3 structural refactor retain their place before a large editor-feature program.

Post-Milestone 2 editor work, to be rescheduled after the architecture review and structural refactor:

1. Extend the initial sequential-video plus timed-audio model with persistent multitrack identity, richer clip properties, track controls, snapping, trim/ripple/gap semantics, and later transitions/effects.
2. Capture immutable exact composition/range snapshots when compositions or their subranges become provider references or durable historical dependencies.
3. Add richer render progress and purpose-specific preview/export profiles beyond the current responsive cancellable render boundary.
4. Add waveform-guided audio placement and sample-aware editing semantics without forcing audio positions into video-frame anchors.

Timeline editing must compose the same recipe graph and must not introduce authoritative intermediate paths into the `.rfp` project file.

Future workspace UX should support detaching the composition timeline and Edit Tools into separate floating windows, then docking either surface back into the Edit workspace. The windows must share the same selection, composition, history, playback, and project state; floating a surface must never create a second editing session or media owner. Remember layout as machine-local, preferably per-project UI state, and restore off-screen windows safely when monitor topology changes. This capability is intentionally unscheduled beyond Milestone 2 and does not add layout data to `.rfp` files.

## Post-Milestone 2 architecture review and Milestone 3 planning gate

After Milestone 2 is complete, pause feature implementation and review the entire ReelForge codebase before finalizing the Milestone 3 execution plan. `MainWindow.xaml.cs`, currently approximately 4,800 lines, is one visible symptom rather than the complete scope. The review must cover every project, layer, folder, production file, test fixture, resource, and cross-component dependency so Milestone 3 addresses structural debt systematically instead of moving one monolith into several arbitrary files.

The review deliverables are:

1. A repository-wide inventory of responsibilities, file sizes, dependency directions, ownership boundaries, duplicated behavior, high-coupling areas, and classes with multiple reasons to change.
2. A proposed target solution, project, folder, and subfolder map covering Core, Application, Infrastructure, WPF presentation, provider integrations, media tooling, persistence, diagnostics, resources, and tests.
3. A SOLID-oriented responsibility map identifying appropriate domain services, application coordinators, provider adapters, repositories/stores, utilities, presentation controllers/view models, WPF controls, and genuinely shared primitives.
4. Explicit recommendations for oversized or mixed-purpose files throughout the repository—not only `MainWindow.xaml.cs` and `MainWindow.xaml`—including which responsibilities should be extracted, combined, renamed, relocated, or deleted.
5. A dependency audit for layer violations, UI-to-infrastructure shortcuts, provider-specific leakage, static/global state, duplicated utility code, and unclear lifecycle ownership.
6. A test-organization review covering characterization gaps, oversized fixtures, reusable fakes/builders, integration boundaries, and the appropriate mirrored structure for production tests.
7. A risk-ranked, staged Milestone 3 plan with small reviewable slices, behavioral acceptance checks, expected file moves, and rollback-friendly commit boundaries.
8. Updated architecture and contributor guidance defining the target structure, naming conventions, ownership rules, dependency rules, and extension points.
9. A platform-portability audit identifying WPF/native-Windows dependencies, portable compilation boundaries, genuine OS variation points, and machine-local path ownership without implementing a macOS client or selecting its UI framework.
10. A media-tool distribution recommendation covering CI-built, pinned, license-audited LGPL FFmpeg/ffprobe artifacts; verified upstream source; exact configuration and dependency/SBOM capture; Windows/macOS and architecture-specific outputs; SHA-256/provenance manifests; installer integration; security updates; licensing/source obligations; a later commercial codec/patent review; and advanced explicit-path/PATH/manual fallbacks.
11. Enforceable dependency/build checks that keep Core, Application, and reusable media/provider/persistence code free of WPF and native Windows facilities, plus a proposal for cross-platform non-UI CI where practical.

The review is planning work only. It must not begin opportunistic file moves or partial refactors before the proposed Milestone 3 plan is reviewed and approved.

## Milestone 3 — whole-codebase structural refactor and reorganization

Milestone 3 will execute the approved architecture-review plan across the complete repository. Its primary goal is to cut down the severe file bloat caused by too many concerns and make the code human-readable, explorable, reviewable, and maintainable. Its physical organization must communicate responsibility so a contributor can locate and meaningfully review a feature without reading thousands of unrelated lines. This is not satisfied by moving the same monolith into partial classes or arbitrary folders; components must have cohesive reasons to change and be independently understandable, changeable, and testable.

The repository-wide baseline, target project/folder map, responsibility audit, portability and FFmpeg-distribution boundaries, risk ordering, enforceable checks, and staged execution proposal are recorded in [Milestone 3 architecture review and refactor plan](milestone-3-refactor-plan.md). Review and approve that plan before Stage 1 changes production ownership.

Progress: the plan is approved. Stage 0 established executable dependency-direction checks, a manual acceptance matrix, and concurrent atomic active-job persistence. Stage 1 introduced the Windows platform/test projects, moved machine-local path and Credential Manager ownership behind portable contracts, injected explicit settings/job/log/cache/project locations, neutralized portable secure-store wording, and moved service construction, provider HTTP-client ownership, job-coordinator construction, and disposal into `Bootstrap/ApplicationRuntime`. Stage 2 is complete: Core models are organized by domain family, project invariants delegate through aggregate-specific validators, Application configuration/contracts have capability-owned homes, and the unused duplicate `ProjectWorkspace.SubmitGenerationAsync` path was removed after migrating its characterization to `GenerationWorkflow`. Stage 3 is complete: project persistence DTOs and mappers are separated by aggregate family behind the unchanged atomic facade; canonical project-path, filename, collision, and same-directory atomic-commit policies replace duplicated filesystem logic; and Infrastructure now groups project/settings/job persistence separately from project-media file operations. Stage 4 is in progress: the former process grab-bag has been separated into process contracts/execution, media-tool discovery, pure FFmpeg command construction, audio extraction, and ffprobe inspection/parsing. The complete suite currently consists of 213 portable tests and 3 Windows-platform tests.

Expected scope includes:

1. Reorganize the solution into coherent projects, feature folders, and subfolders while preserving intentional architectural layers.
2. Decompose oversized and mixed-purpose classes, services, UI files, provider files, persistence components, utilities, and tests into cohesive units with explicit responsibilities.
3. Split the WPF shell into focused controls and presentation components, moving orchestration and state transitions out of code-behind where doing so creates a clearer boundary.
4. Consolidate duplicated helpers into appropriately scoped utilities or services; do not create generic dumping-ground `Helpers` or `Utils` folders.
5. Clarify interfaces and dependency injection at meaningful seams without producing one-method abstractions or abstraction for its own sake.
6. Align provider, generation, media-processing, project-lifecycle, persistence, settings, diagnostics, and editing components with stable dependency directions.
7. Reorganize tests to mirror production responsibilities, introduce focused fixtures/builders/fakes, and retain end-to-end characterization of Milestone 2 behavior.
8. Remove dead code, obsolete presentation paths, duplicate refresh/orchestration flows, misleading names, and superseded files discovered during the review.
9. Complete the documented target folder map and contributor guidance as the physical structure settles.
10. Separate genuine platform implementations—secure storage, application-data defaults, media-tool discovery, native shell integration, and related Windows facilities—from reusable application/media/provider/persistence behavior.
11. Add dependency and build checks that keep reusable assemblies platform-neutral; do not create speculative interfaces or split projects without a concrete ownership, variation, or compilation boundary.

Execution guardrails:

- execute Milestone 3 as a sequence of reviewable behavior-preserving vertical refactor slices, not a big-bang rewrite;
- do not mix the structural work with unrelated product features or schema redesign;
- preserve the current `.rfp` contract, atomic settings/project persistence, immutable recipe semantics, job recovery, and the no-paid-automated-call rule unless a separately approved change explicitly supersedes one;
- preserve Windows behavior while preparing reusable components for a possible future macOS host; Milestone 3 does not implement macOS, replace WPF, or select a Mac UI framework;
- do not treat cross-OS `.rfp` folder interchange as an acceptance requirement. Keep project state logically relative and cache-independent, while using explicit physical-media export/import as the supported cross-machine handoff;
- resolve logs, settings, job state, cache, and other machine-local defaults through platform-owned locations rather than embedding Windows directory conventions in reusable application logic;
- plan CI-built, pinned, license-audited LGPL FFmpeg/ffprobe as the normal installed experience. A valid Advanced-setting local path overrides the packaged executable; manual browsing configures that override, and PATH remains a lower-priority fallback. Retain these routes after packaging so developers and power users can supply builds ReelForge cannot redistribute;
- establish characterization coverage before relocating or decomposing risky behavior, and keep the complete automated suite passing after every slice;
- re-run the primary Milestone 2 human workflows after major structural boundaries move, especially project switching, generation recovery, exact-frame tools, composition editing, preview/export, and settings changes;
- judge completion across the whole repository: no major subsystem should remain an unexplained monolith merely because the WPF shell became smaller.

## Unscheduled future product and commercialization discovery

The accepted [Business and packaging direction](business-and-packaging.md) preserves a genuinely useful Free product, first-class BYOK, a possible capability-based Pro entitlement, and a separate possible managed-compute credit route. This is product context, not current behavior or an implementation milestone. It does not expand Milestone 2 or the Milestone 3 structural refactor.

Do not schedule billing, accounts, licensing, entitlements, telemetry, or managed-provider infrastructure until product evidence and an explicit approval establish a narrow first use case. Before implementation, the planning gate must:

1. validate target users, completed workflows, BYOK onboarding friction, editor-feature value, and willingness to pay;
2. choose a provisional Free/Pro offer without gating custody of existing projects, recipes, provenance, or safe export;
3. verify provider-specific managed-integration rights, territories, moderation duties, and billing/failure behavior;
4. model full unit economics including provider compute, storage/egress, retries, payment, taxes, fraud, refunds, promotions, support, currency movement, and deliberate margin;
5. define privacy, consent, security, account recovery, offline use, and commercial-support obligations;
6. design server-authoritative account, ledger, reservation, settlement, refund, and provider-secret boundaries before client work;
7. preserve BYOK as a first-class Free/Pro route and keep managed-provider availability independent of the BYOK catalog;
8. pilot one bounded managed operation before building a broad credit platform, if the earlier gates support it.

Exact pricing, Ingot rules, subscriptions versus perpetual licensing, Free/Pro feature lists, promotional credits, local-engine packaging, telemetry, accounts for local-only users, team plans, and marketplaces remain intentionally unresolved.

## Unscheduled exploration — local ComfyUI / MiniMax H3

This is intentionally outside the committed Milestone 2 implementation path. MiniMax H3 now has official native ComfyUI workflows, but its weight footprint, runtime performance, incomplete local 2K stack, and territory-restricted license make it inappropriate to schedule before hardware and distribution feasibility are known. No model download or implementation is approved by this roadmap entry.

Potential staged work, only after explicit approval:

1. Add a read-only local-execution environment probe for an existing user-managed ComfyUI installation: version, device/backend, VRAM/RAM, disk, installed node schemas, official workflow compatibility, model files, and license eligibility.
2. Generalize provider output acquisition to accept verified streams/local-file leases as well as remote HTTPS outputs, and generalize execution authorization beyond paid-network confirmation.
3. Pin a reviewed API-format derivative of the official native H3 T2V workflow and validate its semantic binding map against `/object_info`; keep all ComfyUI graph details out of Core.
4. Require a user-started benchmark on the target machine before marking the provider ready. Record resolution, frame count/duration, steps, elapsed time, and memory/resource observations.
5. If acceptable, implement FL2VA T2V first using the approximately 42.5 GB recommended minimal weight set, local queue submission, WebSocket/history recovery, careful interrupt semantics, and durable output ingestion.
6. Add first/last-frame FL2VA using logical image/anchor roles without changing the domain vocabulary.
7. Treat Ref2VA as a separate approximately 21 GB diffusion-model download; then add verified ordered image/video/audio staging and its 9-image, 3-video, 3-audio, 12-file combined limits.
8. Keep hosted H3-Context-IR and H3-Regenerate-2K outside the offline-local profile. Any hybrid 2K workflow is a separate credentialed, potentially paid execution route.

Go/no-go gates:

- MiniMax H3 license eligibility and distribution strategy are reviewed. The August 2, 2026 community license excludes the EU, UK, Republic of Korea, and United States.
- The target machine completes the explicit local benchmark at acceptable quality, latency, stability, power use, and foreground responsiveness.
- Model and temporary-file disk requirements are acceptable; ReelForge never downloads the complete multi-precision repository by default.
- ComfyUI remains loopback-only unless the user separately configures and secures a remote server.
- Native video/audio input staging and H3 video output-history shapes are verified from the running ComfyUI version before implementation.

Detailed findings: [MiniMax H3 local execution research](minimax-h3-local-research.md).

## Future generation graph

The domain should support a later tree/forest view without requiring a migration. Generation nodes may expose thumbnail, prompt summary, provider/model, status, multiple outputs, references, and their one optional typed parent relationship. Each node may have unlimited children; multiple contributing media remain references rather than lineage parents.

No graph visualization belongs in Milestone 2 unless the basic history UI requires a small tree/list indicator.

## Materialization retention remains open

Milestone 2 defines a retention-policy boundary but does not choose a permanent policy. Future options may include minimal, balanced, persistent, per-purpose, per-operation, or per-asset retention. In every mode:

- logical recipes and generation references are authoritative;
- retained representations remain replaceable and verifiable;
- cache paths are not persisted as the only provenance;
- active leases are protected;
- explicit durable promotion remains distinct from retention.

## Cross-phase test strategy

- **Domain:** asset/recipe graph validity, mutable-draft-to-immutable-revision commits, exact revision pinning, draft-to-immutable-generation snapshots, logical reference roles/order, one-parent lineage/cycle validation, retry/variant semantics, anchor semantics, main-video durability, and multi-output provenance.
- **Persistence:** current-format round trips, obsolete-format rejection without rewriting, autosaved draft recovery, committed revision history, pending/completed SHA-256 identity, interrupted atomic saves, and no authoritative cache paths.
- **Generation orchestration:** state transitions, polling recovery, remote-complete/local-ingestion-failed distinction, idempotent local ingestion, cancellation semantics, and first-main-video selection.
- **Materialization:** deterministic plans/keys, retention-policy independence, cache hits/misses, deletion recovery, concurrent requests, cancellation, corrupt entries, and unusual paths.
- **Provider:** mocked HTTP and fake uploads only by default; live paid tests require separate credentials, explicit opt-in, and unmistakable confirmation.
- **UI/application:** logical references remain path-free, history is immutable, virtual/anchor preparation is non-blocking, and status/error states remain understandable.

## Foundational acceptance workflows

### Generation lifecycle

1. Compose a prompt and settings snapshot.
2. Submit a text-to-video job only after explicit user action.
3. Poll until remote completion or failure.
4. Download and verify a successful output.
5. Add it as a durable generated project asset.
6. Link generation and output asset in both directions.
7. Close/reopen and recover the complete request, status, lineage, and output provenance.

### Logical reference reconstruction

1. Import or generate durable source video A.
2. Create anchor X.
3. Define a virtual trim from X to source end.
4. Submit or prepare a generation referencing logical trim X, not a cache path.
5. Close and reopen the project.
6. Clear the entire materialization cache.
7. Confirm the generation history still identifies the trim/anchor and the virtual media can be regenerated.

### Continuation lineage

1. Generate clip A and ingest its durable output.
2. Select an ending frame anchor X.
3. Submit clip B with `ContinueAfter A` and explicit references to X plus any other assets.
4. Confirm both the lineage edge and actual submitted references survive close/reopen and cache deletion.
