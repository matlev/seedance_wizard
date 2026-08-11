# Milestone plan

Status: Milestone 1 and Milestone 2 Phase 2A complete; Phase 2B implementation complete with human live acceptance pending.

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

## Milestone 1 â€” project and media foundation

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

## Milestone 2 â€” AI generation loop on logical media

### Phase 2A â€” foundation for logical media and generation history

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

### Phase 2B â€” complete the generation loop

Implemented on the `milestone-2` branch. All automated verification is network-isolated and no paid request was made. The desktop defaults to the fake provider; official BytePlus ModelArk and AtlasCloud are independently selectable and either real submission is reachable only from an explicit button click followed by a per-request human charge confirmation. BytePlus is the preferred route for the first human acceptance test; AtlasCloud remains an alternate provider. Virtual recipe and frame-anchor representations remain deliberately rejected until their materializers arrive in Phases 2D and 2C respectively.

Build the real provider lifecycle before general-purpose editing:

1. Add per-provider BytePlus ModelArk and AtlasCloud credential configuration backed by Windows Credential Manager.
2. Add per-draft/provider selection and capability-driven Seedance 2.5 mode/settings UI; never bind one provider/model to the project.
3. Add logical project-reference selection with optional settled role, order, and user label/notes.
4. Freeze the immutable request snapshot before materialization or network work.
5. Resolve physical, virtual, and anchor references through application orchestration.
6. Materialize provider-compatible media only when required.
7. Prepare/upload provider-side references using only verified API contracts; do not infer an asset-upload API.
8. Submit generation jobs through the existing provider abstraction.
9. Poll verified asynchronous job state and display queued, running, completed, failed, ingestion-pending/failed, and useful diagnostics.
10. Distinguish stopping local polling from cancelling a remote job; expose remote cancellation only when verified and supported.
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
4. Route provider-specific preparation: BytePlus local image/audio data URLs, BytePlus existing HTTPS/`asset://` video references, and AtlasCloud multipart upload.
5. Keep all contract tests network-isolated and retain the same human-only paid-submission authorization boundary.
6. Add virtual/anchor reference preparation once materialization is available.
7. Add retry/duplicate/branch/continuation actions after immutable snapshots and output links are proven.

Phase acceptance checks:

- one explicitly confirmed live submission through the selected real provider can proceed through job polling to a durable generated asset, while automated tests remain network-isolated;
- normal application startup and unit/integration tests cannot accidentally make paid requests;
- remote completion without a valid local download is not reported as a successfully ingested project asset;
- a downloaded output answers which generation, prompt, model, settings, references, and lineage produced it;
- retry creates a distinct potentially billable job and never overwrites the original failure;
- clearing materialization cache does not affect job history, output assets, or logical references.

### Phase 2C â€” frame and continuation workflow

Enable the core generate â†’ inspect â†’ anchor â†’ continue loop:

1. Add frame-range inspection/contact browsing without retaining every review frame.
2. Create, label, edit, and delete persistent frame anchors.
3. Add first-frame and last-frame convenience anchors/selections.
4. Materialize an anchor as an image only for preview, provider preparation, promotion, or export.
5. Allow generation requests to select frame anchors directly as logical references.
6. Record anchor logical identity/revision in the submitted snapshot and optional extracted-image/materialization evidence separately.
7. Implement Continue After and Continue Before UX using explicit lineage plus explicit anchor/other references.
8. Preserve continuation history even after extracted PNG cache files are removed.

Phase acceptance checks:

- generating clip A, selecting an exact ending anchor, and generating continuation B produces an explicit `ContinueAfter` edge plus the actual anchor reference;
- the anchor survives close/reopen without owning a durable PNG;
- cache deletion removes no continuation provenance;
- a provider-incompatible anchor representation is converted by materialization/provider preparation rather than by the UI.

### Phase 2D â€” basic recipe-based media operations

1. Add virtual trims for source-start â†’ anchor, anchor â†’ source-end, and anchor â†’ anchor.
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

### Phase 2E â€” timeline

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
