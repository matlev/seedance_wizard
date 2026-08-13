# Milestone plan

Status: Milestone 1 and Milestone 2 Phases 2A/2B complete; Phase 2C frame/continuation implementation is in progress, with Phase 2C.1 delivered.

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
- schema migration is not implemented;
- timeline types are placeholders rather than a renderable composition model.

## Milestone 2 — AI generation loop on logical media

### Phase 2A — foundation for logical media and generation history

Implemented on the Milestone 2 branch. The materialization/provider-preparation items in this phase are contracts only; rendering, uploading, and paid submission remain later work.

Complete this foundation before enabling paid submission:

1. Approve the physical/virtual asset architecture and retention-neutral materialization invariant.
2. Define the schema-v2 logical asset envelope, project-owned anchors, and SHA-256 physical content identity kept separate from logical ID and display/file name.
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
14. Add explicit version-1/version-2 persistence DTOs and a transactional migration: automatic after `project.backup-v1.json` for safe metadata changes, confirmation for media-affecting/risky work. Migrated media may begin with pending SHA-256 rather than blocking migration.

Phase acceptance checks:

- version-1 projects migrate without moving media or changing existing IDs;
- asset-only version-1 references preserve their original ordering and values;
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

### Phase 2C — frame and continuation workflow

Approved scope. Enable the core generate → inspect → Saved Frame/anchor → continue loop while establishing anchors as reusable media-editing boundaries rather than generation-specific screenshots.

#### Phase 2C.1 — anchor semantics and schema version 3

Delivered: the current domain and `.rfp` persistence schema are version 3; stable anchors, immutable exact/legacy revisions, pinned recipe/history references, occurrence IDs, dependency-safe archive/removal, automatic backup-backed v1/v2 migration, and validation/round-trip coverage are implemented. Migrated v2 anchor boundaries use `LegacyUnspecified` because v2 never recorded before/after-frame inclusion semantics.

1. Replace mutable schema-v2 anchors with stable logical `FrameAnchor` objects and immutable `FrameAnchorRevision` chains.
2. Make video stream index plus integer presentation timestamp and rational time base authoritative for new anchors; derive display seconds and retain frame number only when reliable.
3. Preserve schema-v2 floating-point anchor timestamps as explicitly legacy precision during transactional v2 → v3 migration; never fabricate exact PTS.
4. Keep display label, notes, archive state, and current-revision pointer on the logical anchor.
5. Pin exact anchor revisions from committed extract-frame/trim recipes and future editing consumers.
6. Add `BeforeFrame` and `AfterFrame` anchor-boundary edges and normalize composition intervals to `[start,end)`.
7. Add a stable occurrence `ReferenceId` to every generation draft/history reference.
8. Archive/tombstone referenced anchors instead of destroying required history; retain degraded anchors when source media is missing or hash-mismatched.

#### Phase 2C.2 — provider-neutral materialization and prepared references

1. Generalize materialization targets from asset-only requests to typed asset-revision or anchor-revision targets.
2. Replace asset-ID-only provider overrides with occurrence-keyed transient prepared references carrying media type, role, order, and provider representation.
3. Keep expiring URLs, data URLs, upload IDs, cache paths, and other provider/materialization details out of authoritative Core provenance.
4. Verify the pinned source SHA-256 and resolve the exact decoded anchor frame before purpose-specific preview/provider transformations.
5. Record receipts tying derived encodings/uploads back to the same anchor revision, canonical extracted-frame hash, and transformation profile.

#### Phase 2C.3 — exact frame indexing and extraction

1. Add cancellable FFmpeg/ffprobe-backed exact-frame discovery and extraction for physical video sources.
2. Define First Frame and Last Frame using decoded presentation frames; Last Frame is the final decodable presentation frame rather than `duration - epsilon`.
3. Use deterministic cache keys based on source content, stream/PTS/time base, anchor revision, purpose/profile, and renderer fingerprint.
4. Coalesce duplicate work, write through unique temporary files, commit cache entries atomically, and clean up cancellation/failure artifacts.
5. Reconstruct missing thumbnails/extracted frames after cache deletion without modifying logical anchors or history.

#### Phase 2C.4 — frame browser and Saved Frames UI

1. Replace the central lower Timeline placeholder with an initial precision-frame workspace, without claiming the full timeline editor is implemented.
2. Add a local contact strip centered near the playhead, initially approximately nine frames with selectable spacing/range.
3. Debounce navigation, extract the center/selected frame first, opportunistically prefetch a bounded neighborhood, and cancel stale work.
4. Create Saved Frames from exact extracted frames; add first/last-frame shortcuts, cached thumbnails, jump-to-frame, label/notes editing, and dependency-safe delete/archive.
5. Keep Saved Frames visually distinct from physical Project Assets. Historical revisions remain resolvable through history/dependency details but do not receive a general revision browser in Phase 2C.

#### Phase 2C.5 — generation-reference integration

1. Select Saved Frames directly as logical references with explicit role, order, label, and notes.
2. Support duplicate occurrences of the same logical object through distinct `ReferenceId` values.
3. Freeze exact anchor revision, source identity, stream/timing semantics, and reference occurrence metadata before any paid work.
4. Materialize and prepare anchors through the same provider-neutral workflow used by assets, then route through AtlasCloud upload or BytePlus/R2 as required.
5. Allow anchor plus source-video references when supported; provider capability validation remains authoritative.

#### Phase 2C.6 — Continue After and Continue Before UX

1. Require explicit source-output selection when a parent generation has multiple outputs.
2. Preselect the first/final decoded frame as appropriate, show its canonical extracted preview, and require confirmation before creating/using the anchor.
3. Recommend a compatible I2V or R2V mode while keeping provider/model/mode visible and user-selectable.
4. Map Continue After anchors conceptually to `StartFrame` and Continue Before anchors to `EndFrame`, subject to provider capabilities.
5. Record parent plus `ContinueAfter`/`ContinueBefore` only when the source actually originates from a generation; imported-video continuation UX uses the explicit anchor reference without invented lineage.

#### Phase 2C.7 — verification and acceptance

1. Add migration, immutable-revision, dependency, legacy-timing, exact-frame/VFR, cache reconstruction, failure cleanup, provider-preparation, duplicate-reference, and continuation-lineage tests.
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

1. Add virtual trims for source-start → anchor, anchor → source-end, and anchor → anchor.
2. Persist trims as recipes without eagerly retaining MP4s.
3. Reuse and expand existing media encoding inspection.
4. Add compatibility analysis as data/planning, not an eager normalization side effect.
5. Add typed normalize/match recipes and purpose-specific materialization.
6. Add concat recipes accepting physical and virtual inputs, with explicit compatibility decisions.
7. Add lazy frame-extraction recipes.
8. Add explicit Export, Save as Asset, and Keep Rendered Copy promotion actions.
9. Permit retention-policy experiments without changing logical asset IDs, recipes, or generation provenance.

Phase acceptance checks:

- a virtual trim used for generation is recorded as the logical reference, never as `cache\*.mp4`;
- materializing the same unchanged recipe can reuse a valid representation;
- source/anchor/recipe/profile/renderer changes invalidate the relevant representation;
- cancellation and failed FFmpeg runs leave no authoritative partial file;
- clearing all materializations leaves recipes openable and reproducible;
- a promoted rendition becomes a physical asset while its virtual source remains intact.

### Phase 2E — timeline

Only after the generation, anchor, and recipe foundations are stable:

1. Add tracks, ordering, clip properties, audio enablement, and basic gain/fade metadata.
2. Let timeline clips reference physical or virtual assets by logical ID.
3. Compile preview and export through the shared materialization planner.
4. Capture immutable composition snapshots for provider requests and historical exports.
5. Add render progress, cancellation, and purpose-specific preview/export profiles.

Timeline editing must compose the same recipe graph and must not introduce authoritative intermediate paths into the `.rfp` project file.

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
- **Persistence:** golden version-1 fixtures, version-2 round trips, autosaved draft recovery, committed revision history, conservative legacy-parent/reference conversion, pending/completed SHA-256 identity, backup-before-migration, interrupted migration, newer-schema rejection, and no authoritative cache paths.
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

## Smallest implementation slice after approval

Implement Phase 2A as a persistence/domain-only vertical slice:

1. add explicit version-1 and version-2 persistence DTOs plus recoverable migration;
2. introduce physical/virtual asset discrimination, project-owned anchors, and pending/verified SHA-256 physical content identity separate from IDs/names;
3. add mutable recipe drafts, immutable committed revisions, predecessor links, and exact revision-pinned references;
4. introduce one autosaved generation draft plus immutable submitted snapshots and the settled provider-neutral reference roles;
5. add the single-parent relationship pair and multi-output/bidirectional provenance invariants;
6. migrate existing asset references, parent IDs as conservative `BasedOn` lineage, and singular output links without guessing missing semantics;
7. prove safe automatic backup/migration, recipe and generation draft recovery, revision/snapshot immutability, hash mismatch detection, lineage validation, main-video durability, multi-provider history, and cache-path independence with fixtures.

This first slice should not run FFmpeg, call AtlasCloud, add credential UI, migrate real user files in place without backup, or enable paid submission. Its purpose is to freeze the durable schema and invariants required by the later generation loop.

## Approval gate

Stop at this revised design and plan. Do not modify domain models, source code, schemas, persistence, migrations, tests, media services, provider behavior, or UI until the user explicitly approves implementation.
