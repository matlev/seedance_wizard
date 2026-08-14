# Architecture

Status: accepted direction; Phases 2A and 2B complete; Phase 2C implementation in progress
Original platform decision: 2026-08-09
Recipe-model design revision: 2026-08-10
Generate/Edit workspace revision: 2026-08-13

This document records the accepted target architecture. Current-format logical assets, immutable recipe/anchor revisions and generation snapshots, SHA-256 identity, provider-specific physical-reference preparation, BytePlus ModelArk plus AtlasCloud Seedance 2.5 and MiniMax H3 submission/polling, durable output ingestion, and their application boundaries are implemented. Virtual-recipe rendering, frame-anchor materialization, and general cache planning remain later Milestone 2 phases.

## Architectural direction

Keep .NET 8 and WPF, but make the project model aggressively non-destructive and recipe-based:

```text
durable physical media + persisted recipes + edit state
                         |
                         v
              lazy materialization plan
                         |
                         v
                 disposable cache
                         |
             +-----------+-----------+
             |                       |
             v                       v
       temporary consumer       explicit export
       preview/provider/etc.    durable physical media
```

The project is authoritative; the cache is not. Deleting the entire `cache/` directory must never delete an asset definition, anchor, edit, generation record, or provenance required to reproduce the project.

## Generate and Edit workspaces

One open ReelForge project is presented through two application-level workspaces. **Generate** creates and prepares media: prompt authoring, provider configuration, generation history, job monitoring, reference selection, Saved Frames, and Saved Clips. **Edit** assembles and transforms the same project media through a logical Working Composition. Switching workspaces never reopens, clones, or changes the project.

The shared left-side concept is **Project Media**, a presentation union rather than a new persistence type. Physical and virtual `ProjectAsset` values and logical `FrameAnchor` Saved Frames appear as peers while retaining their distinct domain identities. A Saved Clip is a virtual video asset backed by an immutable trim-recipe revision. A Saved Frame remains an anchor and never masquerades as an image asset. A future Working Composition is a logical recipe/draft, not an evolving physical MP4.

The Generate viewer shows currently previewed media. The Edit viewer shows the Working Composition by default. A completed generation may auto-preview only when its owning project is open, Generate is active, and no explicit Select Frame or Make Clip operation owns the viewer. Otherwise ReelForge reports completion through the global Jobs surface without stealing the user's context.

FFmpeg-driven tools are explicit operations. Selecting a video previews and inspects it but does not index its decoded frames. The normally quiet Generate lower panel offers **Select Frame** and **Make Clip**; only entering one of those modes begins cancellable precision work. Jobs remains application-global and workspace-neutral, with room to track future render/materialization work as well as generation.

## Dependency direction

```text
ReelForge.App (WPF composition and interaction)
        |                 |
        v                 v
ReelForge.Application  <---  ReelForge.Infrastructure
        |                             |
        +-------------+---------------+
                      v
             ReelForge.Core
```

- **Core** owns project state, physical and virtual asset definitions, typed recipes, anchors, timelines, generation provenance, and graph invariants. It has no file-system, FFmpeg, HTTP, or WPF dependencies.
- **Application** owns use cases and ports for project persistence, materialization, cache access, exports, provider preparation, and graph validation.
- **Infrastructure** implements current-format JSON persistence, durable-file import, content identity, cache storage, FFmpeg planning/rendering, provider upload integration, and Windows services.
- **App** treats physical and virtual assets uniformly in selection and editing UI. It requests previews, provider inputs, and exports through application services rather than resolving paths or launching FFmpeg itself.
- **Tests** verify recipes, incompatible-format rejection, graph validity, deterministic cache keys, render plans, cleanup, and provider payloads without making paid provider calls.

### Paid-network execution boundary

The application defaults to `FakeVideoGenerationProvider`. Constructing the window, opening a project, autosaving a draft, validating settings, polling local state, and running tests cannot submit a BytePlus or AtlasCloud generation. A potentially billable submission requires a short-lived `GenerationSubmissionAuthorization`; its interactive factory is visible only to the desktop and test assemblies. The desktop creates a production authorization only inside the generation-button event after the human accepts a per-request charge warning. Tests use a separate internal network-isolated authorization with custom `HttpMessageHandler` instances, never the public internet.

BytePlus and AtlasCloud credentials are stored under separate keys through `ISecretStore` in Windows Credential Manager and are never persisted in project JSON. `IApiKeyVideoGenerationProvider` supplies the provider-specific credential key without making the workflow depend on a particular vendor. Temporary upload/data URLs and completed output URLs remain transport representations. Logical reference IDs/revisions/hashes and sanitized preparation scope are the durable history.

### Multi-provider boundary

`IVideoGenerationProvider` remains the provider-neutral submission boundary. `IAsyncVideoGenerationProvider` adds task retrieval, while `ProviderAssetPreparationRouter` dispatches reference preparation by provider ID. BytePlus ModelArk, AtlasCloud Seedance 2.5, and AtlasCloud MiniMax H3 therefore coexist as independently selectable adapters. Provider and model are frozen per submitted generation.

The official BytePlus adapter targets `dreamina-seedance-2-5-260628` at ModelArk's documented Video Generation API. It serializes typed text/image/video/audio content, polls documented task states, and accepts the provider's HTTPS or `asset://` references. In the desktop composition, its preparation service sends materialized local references through provider-neutral private R2 hosting and receives only a short-lived HTTPS URL; it does not invent a ModelArk Files API bridge. The no-host constructor retained for isolated tests can still inline documented image/audio data URLs and explicitly rejects local video. AtlasCloud retains its documented multipart media-upload path, shared credential, and async task transport while each model family keeps a distinct provider ID and serializer.

### Future local execution boundary

`IVideoGenerationProvider` is an execution abstraction, not a synonym for a paid remote HTTP API. A future local ComfyUI provider can implement the same semantic submission/status lifecycle by translating an immutable generation snapshot into a pinned API-format workflow, submitting it to ComfyUI's local queue, and mapping the returned prompt ID and progress/history back into provider-neutral job state.

Remote/local differences remain in Application and Infrastructure. Core continues to know only provider/model identity, normalized intent, ordered logical references and roles, lineage, and durable output provenance. It must not contain ComfyUI endpoints, workflow JSON, node IDs, model filenames, GPU types, staging paths, or WebSocket messages. Exact local reproducibility can be supplementary execution evidence—workflow digest, model/component hashes, engine versions, realized dimensions/frame count, seed/steps/sampler, and compute backend—without making those provider details domain invariants.

The output boundary must eventually accept a provider-neutral stream or verified local-file lease in addition to HTTPS results. Forcing a localhost-generated MP4 through a public-URL abstraction would leak transport assumptions and weaken local-file verification. Likewise, execution authorization should evolve from paid-only confirmation to a provider-neutral policy: remote providers can require a fresh charge confirmation, while local H3 can require license/territory acknowledgement and a hardware/resource warning.

Local-provider readiness is an application concern. A future environment probe should inspect ComfyUI version, node schemas, installed models, workflow digest, GPU/backend and VRAM, RAM, disk capacity, loopback binding, and an explicit benchmark result. Input staging and output acquisition remain provider services. ReelForge should attach only to loopback by default and should never expose an unauthenticated ComfyUI server on all interfaces automatically.

See [MiniMax H3 local execution research](minimax-h3-local-research.md) for the official native workflows, dependencies, limits, hardware gate, Server API mapping, and license restrictions. AtlasCloud-hosted H3 is implemented; local ComfyUI H3 remains researched but not scheduled or implemented.

## Current architecture assessment

| Existing component | Useful foundation | Gap against the proposed model |
| --- | --- | --- |
| `VideoProject.Assets` / `ProjectAsset` | Stable asset IDs, media type, origin, metadata, provenance, provider references | Every asset effectively assumes a durable `RelativePath`; physical storage and logical identity are conflated. |
| `AssetOrigin.EditorDerived` and `ExtractedFrame` | Can describe how an artifact originated | They classify an already materialized file; they do not persist an executable recipe. |
| `AssetProvenance` | Links source IDs, generation IDs, operation names, and parameters | Stringly typed provenance cannot safely serve as the recipe itself. Provenance should describe history; a typed recipe should define reproduction. |
| `FrameAnchor` | Has an ID, source asset, timestamp, frame number, and label | `VideoProject` has no anchor collection, so anchors are not currently durable project state. Frame number is mandatory even when unreliable. Notes and time-basis semantics are absent. |
| `CompositionRecipe` / `CompositionSegment` | Project-owned Working Composition with revision-pinned sources and exact recipe boundaries | Multitrack editing, audio-specific timing, general rendering, and export remain later slices. |
| `PortableProjectStore` | Development-format marker, portable relative paths, atomic save | Intentionally rejects obsolete development formats. It creates `cache/` but does not yet define deletion or reconstruction semantics. |
| `AssetImportService` | Copies user media into durable project storage and inspects it | Correct for physical imports, but it should not be reused for virtual outputs or cache promotion without explicit semantics. |
| `ProjectWorkspace.GetAbsoluteAssetPath` | Centralizes relative-path resolution | Cannot represent a virtual asset. Consumers must request materialization rather than assume all assets have paths. |
| `FfmpegCommandBuilder` / process runner | Pure arguments, safe process execution, cancellation, captured diagnostics | Commands operate on paths directly; there is no recipe compiler, dependency planner, cache key, or materialized-result lease. |
| WPF asset explorer/preview | Asset-ID-driven collections are a useful starting point | Preview calls `GetAbsoluteAssetPath`; it must become async and materialization-aware while preserving UI/domain separation. |
| `GenerationRequest.ReferenceAssetIds` | Provider requests already refer to logical asset IDs | Provider preparation currently expects pre-existing provider references. Virtual inputs need lazy local materialization followed by an explicit upload/reference step. |
| `ProviderReferences` | Can retain reusable remote references | Lifetime, source fingerprint, expiry, and whether a reference is safe to reuse are not modeled. |
| `GenerationRecord.Request` | Retains prompt/settings and is persisted before provider submission | Stores a mutable request object rather than an explicit immutable snapshot; references cannot target anchors or capture role, order, or logical revision. |
| `GenerationRecord.ParentGenerationId` | Already enforces the desired maximum of one lineage parent | Needs a paired relationship type and validation; it must not be used to infer actual provider inputs. |
| `GenerationRecord.OutputAssetId` / `AssetProvenance.GenerationId` | Provides the beginnings of bidirectional output provenance | The invariant is not enforced, remote completion versus local ingestion is not distinguished, and only one output is representable. |

The pre-release project contains no supported external format baseline, so this is the least expensive point to establish the correct recipe foundation.

## Core invariants

1. Every project asset has a stable logical asset ID and media type.
2. A **physical asset** names durable media inside the portable project and records content identity.
3. A **virtual asset** contains a typed recipe and has no authoritative media path.
4. Committed recipes reference physical assets/anchors by logical ID and virtual assets by logical ID plus exact recipe revision, forming a directed acyclic graph.
5. Source/generated physical media, project metadata, recipes, anchors, and timeline state are sufficient to reconstruct every virtual asset.
6. Cache entries are implementation details. No persisted recipe, timeline, or provider request may depend on a cache-relative path.
7. A materialization becoming missing, corrupt, stale, or incompatible causes regeneration, not project corruption, regardless of how long that representation was retained.
8. Retention is a policy applied to materialized representations, not a property that changes the authoritative logical asset. The architecture must permit ephemeral, normal-cache, frequently-used, and persistent retention policies without changing recipes or provenance.
9. Explicit **Export**, **Save as Asset**, or **Keep Rendered Copy** actions promote a derived result into durable physical media. Merely retaining a cache representation does not promote it.
10. Generation output downloaded into `generated/` is durable physical media; media prepared solely for provider upload is a non-authoritative representation.
11. A submitted generation records an immutable request snapshot containing logical references. It never uses cache paths as its source of truth.
12. Generation lineage explains why a generation was created; generation references record what was actually submitted. Neither may be inferred from the other.
13. Provenance is retained for physical and virtual assets, but provenance metadata is not a substitute for an executable recipe.
14. A generation has zero or one lineage parent and any number of children. Multiple contributing media belong in its reference list, not additional lineage edges.
15. One generation represents one submitted provider request/job and may produce multiple output assets. Provider and model are selected per generation, never fixed at project level.
16. Each project may autosave one mutable generation draft, but only submitted snapshots belong to immutable generation history.
17. The designated main video, when present, is always a durable physical project asset. Selecting a virtual/materialized result as main video first promotes it into durable project storage.

## Conceptual asset model

A single asset catalog should allow the UI, timeline, and generation requests to refer to either storage kind by the same asset ID. The conceptual shape is:

```text
ProjectAsset
  Id
  DisplayName
  MediaType                 Image | Video | Audio
  StorageKind               Physical | Virtual
  Origin
  CreatedAt
  Declared/observed metadata
  Provenance
  ProviderReferences
  Physical                   present only when StorageKind = Physical
    RelativePath
    ContentIdentity
    Durability               Source | Generated | Exported
  Virtual                    present only when StorageKind = Virtual
    CurrentRecipeRevisionId
    ExpectedMediaProperties
```

`AssetId`, display/file name, and content identity are deliberately separate. `AssetId` is stable logical project identity. The display/file name is human-readable and may be renamed. `ContentIdentity` fingerprints the exact physical bytes using SHA-256 and also records length and enough observed file information to diagnose replacement or corruption. Renaming does not change `AssetId` or SHA-256; replacing bytes under the same name produces a mismatch.

SHA-256 is the canonical durable-media content fingerprint and may support verification, modification detection, deduplication, materialization keys, provenance integrity, and recognizing identical imports. It is not a user-facing name and does not replace asset IDs. Hashing should occur while importing/downloading where practical and may complete in background for very large media. Any operation that requires verified identity must await a completed hash rather than silently using filename/timestamp as equivalent identity.

The stored expected hash must not silently change when replacement is detected. Until the user explicitly accepts or relinks the new bytes, the asset is reported as modified/mismatched. Historical generation/materialization receipts retain the content hash they actually referenced; if those prior bytes are no longer available, history remains intact but reconstruction is reported as unavailable rather than resolving to different media.

Cross-project transfer preserves the same separation. Copying physical media creates a new destination `AssetId`, copies and re-verifies the bytes, retains the same SHA-256 when the copy is exact, and records sanitized source-project/source-asset identity without importing a generation ID that is not part of the destination project. An unreferenced source asset can then be removed for a true move. If immutable generation history, recipes, anchors, timeline state, or another durable object references the source asset, a requested move becomes a copy with an explicit explanation and retains the source asset so project history is not corrupted.

The `.rfp` asset catalog remains authoritative. Moving a media file between project folders in Explorer does not transfer its logical asset record: the source project detects the recorded relative path as missing, while the destination project ignores the unregistered file until the user imports or copies it through ReelForge. A future relink workflow may use the expected SHA-256 to recognize identical bytes, but must never silently adopt or rewrite project state merely because a matching filename appears.

Use explicit typed recipes rather than an operation enum plus arbitrary parameter dictionary. Conceptual recipe variants include:

```text
TrimRecipe
  Source                    exact asset/recipe revision reference
  StartBoundary             anchor revision + edge, timestamp, or source start
  EndBoundary               anchor revision + edge, timestamp, or source end
  RenderProfile

ConcatRecipe
  Inputs[]                  exact asset/recipe revision references
  CompatibilityPolicy
  Transition-free ordering initially
  RenderProfile

NormalizeRecipe
  SourceAssetId
  TargetMediaProfile

ExtractFrameRecipe
  Source                    exact asset/recipe revision reference
  Anchor                    exact anchor revision reference
  ImageProfile

TimelineCompositionRecipe
  TimelineSnapshotId or immutable composition definition
  RenderProfile

ProviderSnippetRecipe
  Source                    exact asset/recipe revision reference
  Boundaries
  Provider constraints/profile
```

Recipes are serialized through explicit current-format DTOs. During pre-release development, operation shapes change in place and obsolete files are rejected. Per-operation compatibility markers can be introduced with the first supported public format if later evolution requires them.

### Recipe revisions and edit drafts

A virtual asset has stable logical identity plus immutable committed revisions:

```text
VirtualAsset
  AssetId
  CurrentRecipeRevisionId

RecipeRevision
  Id
  VirtualAssetId
  RevisionNumber
  PreviousRevisionId?
  RecipeType
  RecipeSchemaVersion
  ImmutableRecipePayload
  CreatedAt

RecipeDraft                     mutable UI/application state
  VirtualAssetId?
  BasedOnRevisionId?
  EditableRecipePayload
```

An actively edited recipe draft may mutate freely and need not create permanent history for every control change. Committing creates a new immutable `RecipeRevision`, links it to the previous revision when present, and advances the virtual asset's current-revision pointer. A committed revision is never edited in place once authoritative or referenced by another recipe, generation, timeline operation, export, or other durable object.

Durable references pin both the virtual asset ID and exact recipe revision ID. They never mean “whatever revision is current later.” References from one committed recipe to another likewise pin the dependency revision. Deleting or compacting a revision is prohibited while any authoritative project object references it. Cache/materialization keys include the committed revision identity and canonical payload, plus transitive content identities.

### Graph rules

The domain should reject:

- missing asset, anchor, or anchor-revision references;
- missing or mismatched committed recipe revisions;
- direct or indirect recipe cycles;
- media-type-incompatible operations;
- non-monotonic or out-of-range trim boundaries once source duration is known;
- anchors attached to media without a stable time axis;
- deletion of a source that is still referenced, unless the user also deletes/relinks dependents;
- timeline clips whose logical source cannot produce the requested media type.

Deleting a virtual asset should not delete its physical sources. Deleting a physical source with dependents requires an explicit dependency-aware decision. Cache files never participate in referential integrity.

## Anchors

`FrameAnchor` is a general project-media primitive, not a generation-specific object and not a persistent screenshot. It identifies an exact visual position in an exact version of a source video. Generation references, visual bookmarks, frame export/promotion, trim recipes, splice/concat recipes, and future timeline compositions may all consume the same anchor revision.

The user-facing name is **Saved Frame**. The internal domain name remains `FrameAnchor`. Durable anchor state, disposable extracted images/thumbnails, and explicitly promoted physical image assets are distinct:

```text
Saved Frame / FrameAnchor       durable logical project state
Extracted frame / thumbnail    disposable reproducible cache
Saved frame image asset        explicitly promoted durable physical media
```

The current project format uses a stable logical anchor plus immutable media-semantic revisions:

```text
FrameAnchor
  Id
  CurrentRevisionId
  DisplayLabel?
  Notes?
  IsArchived
  CreatedAt

FrameAnchorRevision
  Id
  AnchorId
  RevisionNumber
  PreviousRevisionId?
  SourceAssetId
  SourceContentHash
  VideoStreamIndex
  PresentationTimestamp   integer presentation timestamp
  TimeBaseNumerator
  TimeBaseDenominator
  FrameNumber?            informational when reliable
  CreatedAt
```

Stream index plus integer presentation timestamp and rational stream time base are authoritative. Display seconds are derived. Variable-frame-rate media makes a mandatory frame number unsafe. Earlier development projects that stored only floating-point seconds are intentionally incompatible with this pre-release format rather than being elevated to false exactness.

An uncommitted anchor draft may move freely. Once an anchor revision is committed or referenced, its extraction-defining state is immutable. Moving the Saved Frame creates a new revision linked to the prior revision. Label, notes, and archived state remain mutable logical-anchor metadata; submitted generation references freeze their own label and notes independently. Old revisions remain resolvable but are not exposed through an ordinary revision browser in Phase 2C. A future restore action creates a new revision copying an old revision's frame state rather than mutating or reactivating history.

An unreferenced anchor may be deleted. Once any generation, committed recipe, timeline/composition, export, or other authoritative object pins one of its revisions, Delete archives the logical anchor from ordinary working UI while retaining all required history. Missing or hash-mismatched source media leaves the anchor visible in a degraded state and blocks materialization; it never deletes descendants or provenance.

Phase 2C limits anchors to durable physical video assets. Virtual-video anchors wait until Phase 2D defines recipe time mapping. Any imported or generated physical video may be anchored. An imported source can use the Continue After/Before workflow without inventing generation lineage.

### Editing-boundary semantics

An anchor revision identifies a presentation frame. An editing boundary additionally identifies which edge of that frame is intended:

```text
AnchorBoundary
  AnchorId
  AnchorRevisionId
  Edge                  BeforeFrame | AfterFrame

BeforeFrame             start boundary of the identified presentation frame
AfterFrame              next presentation boundary, or SourceEnd for the final frame
```

Composition and trim plans normalize to half-open intervals `[start, end)`. This prevents duplicate frames at adjoining cuts while allowing creator-facing actions such as **Cut before this frame** and **Cut after this frame**. Committed `TrimRecipe`, `ExtractFrameRecipe`, and future composition segments pin exact anchor revisions rather than a mutable `AnchorId`. The final decodable presentation frame is the canonical Last Frame selection; `AfterFrame` on it resolves to `SourceEnd`.

An extracted image is only one materialization of an anchor position. Editing may use the anchor strictly as a temporal boundary and never extract an image. When extraction is needed, preview and provider preparation must resolve the same exact decoded source frame. Purpose-specific derivatives may legitimately differ in size, encoding, compression, or hosting representation; receipts tie them to the same anchor revision, canonical extracted-frame hash, and transformation profile.

## Materialization boundary

Conceptual application contracts:

```text
MaterializationTarget
  AssetRevisionReference
  | AnchorRevisionReference

IMediaMaterializer
  MaterializeAsync(target, purpose, profile?, cancellationToken)

IMaterializationPlanner
  BuildPlan(project, target, purpose, profile?)

IAssetRecipeCompiler
  Compile(recipeGraph, renderContext)

IMediaCache
  TryOpenAsync(cacheKey)
  CommitAsync(cacheKey, producedFile, metadata)
  Invalidate/PurgeAsync(...)

IAssetExportService
  ExportAsync(assetId, destination/profile)
  SaveAsPhysicalAssetAsync(assetId, ...)
```

`MaterializationPurpose` includes `Preview`, `ProviderUpload`, `FinalExport`, `FrameExtraction`, `Thumbnail`, and `Waveform`. Purpose influences quality, format, lifetime, provider limits, and whether a direct source path is acceptable. `MaterializationRequest` now accepts an explicit `AssetMaterializationTarget` or `AnchorMaterializationTarget`; anchor revisions never masquerade as `ProjectAsset` instances. The physical materializer verifies the pinned anchor source identity before handing exact extraction to the Phase 2C.3 renderer.

`MaterializeAsync` should return a result/lease containing the usable path, resolved metadata, cache key or physical content identity, and ownership/lifetime information. Callers must not infer permanence from receiving a path. A lease prevents cleanup from removing an artifact while preview, upload, or export is using it.

The materializer follows this decision flow:

1. Resolve the logical asset revision or anchor revision and validate its dependency graph.
2. Verify every durable physical source against its pinned SHA-256 identity.
3. If a physical source already satisfies the purpose, return it without copying.
4. For an anchor target, resolve the exact video stream and presentation timestamp before any purpose-specific transformation.
5. Build a deterministic render plan for a virtual asset or extracted anchor frame.
6. Compute the cache key from the plan and transitive inputs.
7. Reuse a verified cache hit, otherwise render into a temporary file.
8. Atomically commit the completed artifact to cache and return a lease.
9. For explicit export/promotion, copy or render into durable storage and create/update the appropriate physical project asset only after success.

The UI, provider adapters, and timeline must not decide whether FFmpeg is required. They request an asset for a purpose; the materialization layer owns that choice.

Phase 2D begins that generalization with `RecipeRenderPlanner`. A virtual asset request is first expanded into a provider-neutral tree of physical sources, pinned trim revisions, extract-frame revisions, and ordered composition segments. Each node has a deterministic identity derived from its authoritative payload and transitive dependency identities; the enclosing plan additionally includes materialization purpose and profile. The planner rejects unpinned virtual dependencies, missing revisions, incompatible media types, and cycles before an external process runs.

The first executor slice recursively materializes nested trim nodes, preserving an active lease on every dependency until its consumer finishes. This permits a Saved Clip to use another pinned Saved Clip and permits the initial single-segment Working Composition to resolve a virtual source. It deliberately does not disguise recursive intermediate rendering as optimized planning: multi-segment concat, normalization decisions, operation fusion, and virtual-source anchor time mapping remain later Phase 2D work.

The next executor slice treats media compatibility as an explicit result rather than an eager side effect. Ordered composition inputs are `Compatible`, `RequiresNormalization`, or `Unknown`, with differences in video codec, dimensions, pixel format, frame rate, audio presence, codec, sample rate, channel count, and layout retained as planning data. Missing metadata is inspected from the realized media when possible. Compatible sets use a timestamp-resetting concat graph. Mismatched sets normalize video to a common even-sized canvas, square pixels, frame rate, and pixel format; audio is resampled to a common stereo profile, while silent or audio-disabled segments receive duration-matched silence. Composition renders use deterministic cache identity, active dependency leases, unique temporary output, atomic commit, and failure cleanup.

The first editor-facing consumer is deliberately narrower than a timeline. The Edit workspace projects the current immutable Working Composition revision as an ordered list. Add, move, and remove are discrete commits that create a new revision and refresh a mutable draft based on it; earlier revisions remain unchanged. Virtual segment sources pin their exact recipe revision. Preview requests the current composition revision from the shared materializer, so the project persists logical sources and order while the rendered MP4 remains disposable cache state.

The same committed composition offers two explicit durable exits. **Save as asset** copies the `FinalExport` materialization atomically into `assets/videos`, verifies its SHA-256 and encoding, and only then adds a physical `Promoted` asset whose provenance pins the virtual source and recipe revision. **Export** atomically writes the materialization to a user-selected MP4 path without changing project state. Neither action replaces or mutates the virtual Working Composition.

## Disposable cache

Recommended layout:

```text
ProjectName.rfp          JSON-formatted authoritative project file
assets/
  images/
  videos/
  audio/
generated/
exports/
cache/
  materialized/
  previews/
  frames/
  thumbnails/
  waveforms/
  index.json              optional and disposable
```

The subdivision is operational, not semantic. Every entry—including `index.json`—must be safe to delete while the app is closed. Startup should tolerate a missing or partially populated cache and remove abandoned temporary files safely.

A cache key should use canonical, culture-invariant serialization of:

- recipe type and recipe version;
- normalized recipe values and render parameters;
- transitive source content identities, not only file names;
- exact anchor revisions, timing values, and boundary edges;
- materialization purpose/profile where output differs;
- renderer identity and behavior-affecting FFmpeg/build information.

Including the exact FFmpeg version maximizes correctness but reduces reuse after upgrades. A renderer compatibility fingerprint containing only behavior-affecting versions/settings offers better reuse but is harder to maintain. Start conservatively with an explicit renderer version plus command/profile version; optimize only after evidence shows cache churn matters.

Concurrent requests for the same key should coalesce into one render. Writers use a unique temporary file and atomic promotion; failed or cancelled renders never become valid entries. Cache cleanup must honor active leases.

The final retention policy is intentionally unresolved. A future `IMaterializationRetentionPolicy`-style boundary may choose minimal, balanced, persistent, per-purpose, or per-asset retention. That decision changes eviction behavior only: a retained materialization remains replaceable and non-authoritative, while a deleted one remains reproducible. Project JSON may record a retention preference or promotion event, but must not require a particular cache filename to exist.

## Durable export and promotion

`FinalExport` as a materialization purpose does not itself imply that an output becomes a catalog asset. The export use case must make the durable boundary explicit:

- **Export** writes a user-selected durable result, normally under `exports/` or another chosen destination.
- **Save as Asset** adds a durable physical copy to the project catalog, retaining provenance back to the virtual asset/recipe.
- **Keep Rendered Copy** promotes a specific cached rendition, only if its profile is suitable and its content identity is recorded.

The virtual source remains intact after promotion. Future recipe edits produce a new cache identity and do not silently overwrite the promoted physical file.

### Viewer selection and Working Composition

The starred Main Video concept is removed. Generate has only currently previewed media; this is ephemeral UI selection, not privileged domain state. Per-project viewer selection and the last workspace are machine-local preferences. The project persists the identity of its Working Composition because that composition is durable creative state.

The initial Working Composition is one project-designated virtual composition identified by ID so multiple named compositions can be added later without restructuring the project root. Its active edit is a mutable recipe draft. A submitted generation, export, or historical dependency freezes an immutable recipe revision. Preview materializations are disposable and export is the explicit durable-file boundary.

## Composition and future timeline implications

The obsolete floating-point `TimelineClip` model has been removed rather than becoming the editor foundation. A `CompositionSegment` references a logical asset plus an exact physical or virtual revision and expresses video ranges through source edges or pinned exact-position revisions with `BeforeFrame`/`AfterFrame` intent. Audio will require an appropriate rational/sample-based boundary rather than pretending every boundary is a video frame.

Preview and export compile the current timeline state into a render plan. They do not rewrite source assets or persist FFmpeg intermediates as project assets. If a timeline composition is exposed as an asset elsewhere, it should be represented by an immutable/snapshotted composition recipe or by explicit version semantics; referencing a mutable live timeline directly would make old generation provenance non-reproducible.

Recommended decision: a provider request or export made from a timeline captures an immutable recipe snapshot, not a pointer whose meaning changes as the timeline is edited.

## Generation provenance

The application is primarily an AI-video-generation workbench. A generation is therefore a durable historical record, not merely a mutable job-status row. Once submitted, it should retain an immutable snapshot of the user intent and logical project inputs even if the editable draft, recipes, provider references, or cache later change.

Conceptually:

```text
GenerationDraft                  zero or one per project
  ProviderId/ModelId?
  Mode
  Prompt
  Duration/AspectRatio/Resolution
  Editable References[]
  Editable ProviderParameters

GenerationRecord
  Id
  RequestSnapshot
    ProviderId
    ProviderModelId/Version
    Mode
    Prompt
    Duration
    AspectRatio
    Resolution/Quality
    ProviderParameters
    References[]
  ParentGenerationId?           maximum one
  RelationshipType?             present exactly when parent is present
  SubmittedAt
  ProviderJobId
  Status
  CompletedAt?
  OutputAssetIds[]
  ProviderResponseMetadata
  StructuredError?

GenerationReferenceSnapshot
  LogicalObjectKind          Asset | FrameAnchor
  LogicalObjectId
  RecipeRevisionId?          required for virtual assets
  ContentHash?               expected SHA-256 for physical media involved
  Role?                      first frame, last frame, character, motion, audio, etc.
  Order?                     when provider ordering is significant
  MaterializationReceipt?    supplementary diagnostics, never authoritative identity

MaterializationReceipt
  Recipe/plan hash?
  Transitive source identity?
  Produced media hash?
  Dimensions/duration/encoding?
  Provider upload/reference ID?
  Provider/account scope and expiry?
```

Provider/model selection belongs to `GenerationDraft` and the resulting request snapshot. A project may freely mix generations from Seedance, Wan, or other providers/models; no provider identity belongs in project-level configuration beyond UI defaults or credentials.

Physical and virtual assets share the `Asset` logical-reference kind; their storage kind is resolved through the asset catalog. An anchor is a distinct logical object because the provider may need a lazily extracted image while provenance must continue to identify the selected position, not a generated PNG.

The settled provider-neutral reference-role vocabulary is:

- `GeneralReference`
- `StartFrame`
- `EndFrame`
- `Character`
- `Style`
- `Environment`
- `Motion`
- `Audio`

Role is optional where appropriate. A separate optional user-facing label/notes field allows specificity without expanding the enum. Provider adapters map application intent to supported provider fields, fall back to a generic reference when valid, or reject the role/type combination with a pre-submission validation message. Provider-specific field names never enter the domain model.

The materialization receipt is useful for audit, retry optimization, and diagnosing exactly what bytes were sent. It supplements the logical reference and must remain meaningful when its local file is gone. Secrets, signed URLs, and other expiring credentials must not be persisted as unrestricted diagnostics.

Each submitted generation stores materialization receipts by reference occurrence ID on the generation record. This keeps duplicate uses of one logical asset or Saved Frame distinct. For an anchor occurrence, the receipt plan identity is the immutable anchor revision; the receipt separately preserves the source-video hash and the produced extracted-image hash. Provider reference IDs/scopes and expiry metadata may be retained, but the actual signed URL or data URL is not authoritative project state.

Exact Saved Frame realization uses ffprobe to enumerate decoded presentation timestamps from the selected video stream and FFmpeg's `select=eq(pts,...)` filter to extract the pinned presentation frame. The current cache key includes the verified source SHA-256, immutable anchor revision, stream/PTS/time-base tuple, materialization purpose/profile, extraction-algorithm version, and detected FFmpeg version. Cache files are disposable PNG derivatives written through unique temporary files and atomically committed; deleting them never changes project state and the same anchor revision can reconstruct them.

Presentation timestamps are signed media-native values; ReelForge does not reject a valid decoded frame merely because its container timeline begins before zero. UI playback time may clamp that leading display position to zero, while the anchor continues to preserve the exact signed PTS.

### Snapshot immutability

The current `GenerationRecord.Request` stores the supplied mutable `GenerationRequest` object directly. The proposed submission use case should instead deep-copy or construct a snapshot at the submission boundary before materialization or network work begins. Failed and cancelled submissions retain that snapshot and their own error/job state.

Each project may persist one autosaved mutable `GenerationDraft` so accidental closure or project switching does not discard current work. A draft is project/UI state, not generation history. Submission creates a new immutable `GenerationRecord`; later edits operate on a new/repopulated draft and never mutate the historical snapshot. Submitted records remain until explicitly deleted, including failed and cancelled attempts.

For a virtual reference, the snapshot retains its exact immutable recipe revision; a bare logical ID is insufficient because the virtual asset's current revision may later advance. Physical-media evidence retains the expected SHA-256. Editing a committed recipe creates a new revision while historical generations continue resolving the prior revision exactly.

### Bidirectional generation output provenance

A successfully downloaded AI result is normally a durable physical asset under `generated/`. Both directions are explicit:

```text
GenerationRecord.OutputAssetIds -> generated physical asset(s)
PhysicalAsset.Provenance.GenerationId -> originating generation
```

The output is added only after download, integrity/inspection, and atomic placement succeed. Job completion without a verified local download remains a completed provider job with pending/failed ingestion, not a valid physical asset. `OutputAssetIds` is a collection because one submitted request/job may return multiple media outputs. Each output becomes its own durable physical asset linked to the same originating generation. Independently submitting similar prompts to different providers creates separate generations; any future batch/experiment grouping sits above, and does not redefine, a generation.

## Generation relationships and branching

Lineage records why a new generation exists; it never substitutes for its actual references. The generation history is a forest: each generation has at most one parent and any number of children.

```text
GenerationRecord
  ParentGenerationId?       nullable; at most one
  RelationshipType?         nullable; paired with ParentGenerationId
```

The settled vocabulary is `RetryOf`, `VariantOf`, `ContinueAfter`, `ContinueBefore`, and `BasedOn`. Persisted values are historical facts and must not be reinterpreted later.

Rules:

- **Retry** creates a new generation with its own ID, job ID, state, errors, timestamps, and output links. It copies the original immutable request snapshot and records `RetryOf`; it does not mutate the failed generation.
- **Variant** intentionally changes prompt, references, settings, or other request parameters and records `VariantOf`.
- **Continue after/before** records the relationship to the earlier generation and separately records the actual anchor/asset references sent to the provider.
- **Based on** is the generic fallback when meaningful lineage exists but the specialized meanings do not fit.
- **Duplicate** and **Branch** are UI actions, not persisted relationship names. Submission after either action records `VariantOf` or `BasedOn` according to user intent.
- `ParentGenerationId` and `RelationshipType` are either both absent or both present. The parent must exist, cannot be the same generation, and cannot create a lineage cycle.
- Additional contributing generations/assets never create extra parents; they appear as unlimited logical references.
- Deleting or hiding a historical generation must not silently destroy the provenance of descendants or output assets.

Example:

```text
Generation 31
  relationship: ContinueAfter Generation 30
  references:
    FrameAnchor anchor_52
      source: Generation 30 output asset
    Asset image_07
      role: character reference
```

The parent plus relationship type answers “why was 31 created?” The references answer “what did the provider receive?” Neither is inferred from the other. A continuation based on a non-generation clip has no lineage parent unless there is an actual earlier generation; the clip/anchor is represented through references.

## Logical references, materialization, and providers

Provider-facing generation orchestration should follow this separation:

```text
Domain request snapshot
  logical asset/anchor references
             |
             v
Application orchestration
  resolve revisions -> validate -> materialize for ProviderUpload
             |
             v
Provider asset preparation
  upload/encode/reuse qualified provider representation
             |
             v
Provider adapter
  serialize verified provider contract -> submit/poll/cancel
```

Every generation-reference occurrence has a stable `ReferenceId` distinct from the referenced logical object. The same asset or anchor may intentionally appear more than once with different roles, order, labels, preparation profiles, or provider treatment. Transient prepared representations are keyed by occurrence ID rather than `AssetId`/`AnchorId`:

```text
PreparedGenerationReference          transient application/provider contract
  ReferenceId
  LogicalObjectKind
  LogicalObjectId
  MediaType
  Role?
  Order
  ProviderRepresentation             URL, data URL, provider asset ID, etc.
```

`ProviderRepresentation` is never persisted as Core domain meaning. By the time a provider adapter receives this descriptor, it needs the realized media type, role/order, and qualified representation; it does not need to know whether the representation originated from a physical asset, virtual recipe revision, or anchor revision. The immutable project snapshot retains that provenance.

The UI selects logical project objects and never creates intermediate files manually. For each reference, orchestration:

1. freezes its logical identity/revision in the request snapshot;
2. determines the media representation required by the selected provider/model/role;
3. passes through a compatible physical asset or calls `IMediaMaterializer` with `ProviderUpload` purpose for a virtual asset or anchor;
4. validates realized duration, dimensions, encoding, size, and reference limits before paid submission;
5. uploads/encodes through a provider-specific asset-preparation boundary using only verified contracts;
6. optionally records a sanitized materialization receipt and qualified provider reference;
7. submits the verified API payload, then releases local leases according to the active retention policy.

A provider-side ID or URL is a representation of a project reference, not the reference itself. Reuse is permitted only when it is bound to the same source/materialization hash, provider account/region/model scope, and known lifetime. Expired or unqualified IDs are regenerated/reuploaded from the logical source.

For providers that require HTTPS-accessible local references, `ITemporaryAssetHost` is a separate application port between materialization and the provider adapter. The Cloudflare R2 implementation uses deterministic SHA-256 object keys, checks the private bucket before uploading, and issues a fresh short-lived presigned GET URL for each preparation. BytePlus knows only the returned HTTPS representation; it does not know R2 account, bucket, credentials, or signing details. Signed URLs remain transient request overrides and never replace logical provenance. Sanitized receipts may retain the provider, object key, content hash, and expiry.

Application settings are likewise outside the project domain. `IApplicationSettingsStore` merges checked-in defaults with `%LOCALAPPDATA%\ReelForge\appsettings.local.json`; `ISecretStore` keeps credential values in Windows Credential Manager. One requirement catalog drives Settings labels, required/secret status, placeholders, credential key discovery, and validation. The checked-in JSON declares secret names and target keys using a non-secret marker, while load/save normalization prevents that property from carrying plaintext.

The asset-ID-keyed provider override map has been removed. Transient provider representations now use occurrence-identified `PreparedGenerationReference` values carrying logical kind/ID, realized media type, role, order, and provider representation, so repeated uses of one logical object remain distinct. `GenerationRequest.ReferenceAssetIds` remains temporarily as the Phase 2B capability-validation input until Phase 2C.5 moves logical anchor selection through the full UI/provider path. `GenerationWorkflow` freezes the richer draft into a path-free immutable snapshot first, resolves and verifies logical references, materializes only when required, and supplies transient prepared representations only to the serializer. Recipes and UI remain provider-neutral. No paid submission appears in materialization, orchestration, polling, or provider contract tests.

Submission and monitoring are separate application responsibilities. `GenerationWorkflow.QueueAsync` validates the draft, freezes the immutable generation snapshot, and persists a local queued record without contacting a provider. When the application-level Undo Send value is positive, `GenerationJobCoordinator` records that entry and its captured deadline in `%LOCALAPPDATA%\ReelForge\active-jobs.json`. Cancellation is an atomic transition available only while the entry is still awaiting submission; it guarantees reference preparation, uploads, and provider submission have not started. Deadline expiry atomically claims the entry before `GenerationWorkflow.SubmitQueuedAsync` can perform provider work, so a simultaneous cancel cannot misrepresent an already-started request. A zero-second setting follows the immediate `SubmitAsync` path.

Potentially billable submission remains reachable only after the desktop creates a one-request authorization from a human confirmation. The maximum 30-second delay remains inside the authorization's freshness window. Each queued entry owns its original deadline; later settings edits do not mutate work already in the queue. Once the provider returns a job ID, `GenerationJobCoordinator` replaces the local pending state with a provider-neutral active-job descriptor and polls through `IAsyncVideoGenerationProvider`. The registry contains project identity, provider/model identity, elapsed-time origin, local deadline where applicable, remote state, sanitized metadata, and any returned output URLs; it contains no API credentials or mutable generation inputs.

The coordinator is application-scoped rather than project-view-scoped. It continues remote monitoring while the user switches projects or has no project open, restores unresolved provider jobs after application restart, and never calls a provider's `SubmitAsync`. A locally queued entry is executed by the desktop submission workflow after its deadline without restricting New/Open or project switching. At expiry, the workflow uses the active workspace only when it still owns the captured project path; otherwise it opens an isolated workspace for that `.rfp`, locates the immutable generation ID, resolves that project's references, and persists submission state back to the owning project. If the owning project becomes active during isolated work, only the matching generation's mutable provider state is merged into the active model rather than replacing unrelated in-memory edits. If shutdown interrupts an unclaimed local entry, restore converts it to a reconciled local cancellation instead of submitting without a live authorization. A terminal result is merged into its owning `.rfp` project and successful outputs pass through the same verified ingestion/provenance path whether or not that project is currently visible. The project remains authoritative generation history; the application registry is recoverable operational/notification state. A reconciled terminal entry remains across restarts until the Jobs tab has displayed it and the user subsequently leaves that tab, preventing unattended completions from disappearing before acknowledgement.

## Pre-release project format policy

ReelForge maintains one current `.rfp` development format. Persistence DTOs remain separate from domain models, but there is no migration ladder and the domain does not carry a schema-version concern. The file contains a simple `formatVersion` marker used only to reject incompatible development files clearly.

While ReelForge is pre-release and its media semantics are still changing:

- optimize the current format and domain for correctness and clarity;
- update the format in place when the model changes;
- reject an absent or unsupported format marker with a clear obsolete-development-format message;
- do not deserialize historical shapes into current domain objects or preserve migration-only representations;
- keep atomic temporary-file saves, validation-before-save, and replace-on-success corruption protection;
- keep cache paths and transient provider representations out of authoritative project state;
- expect disposable development projects to be recreated and media to be re-imported when the format changes.

The current format includes physical/virtual asset discrimination, immutable recipe and anchor revisions, SHA-256 physical content identity, ordered occurrence-identified generation references, immutable submitted snapshots, generation lineage, multi-output provenance, main-video selection, and timeline references. Exact anchor revisions always store source identity, video stream index, integer presentation timestamp, and rational time base; there is no approximate or migration-only timing variant.

At the first externally supported beta/public project-format baseline, compatibility becomes a product requirement. That release must define its supported marker and migration policy before later breaking changes ship. Until then, older builds and older development `.rfp` files are intentionally not guaranteed to interoperate.

## Components that would change during implementation

- **Core:** `VideoProject`, `ProjectAsset`, `GenerationDraft`, immutable request snapshots, `GenerationRecord`, `GenerationSubmission`, `AssetProvenance`, frame anchors, recipes, reference roles, single-parent lineage validation, multi-output provenance, main-video invariants, timeline types, graph validation, and content identity.
- **Application:** replace path assumptions with materialization/export/provider-preparation ports; add submit/poll/cancel/download/ingest orchestration; expand `ProjectWorkspace` or split it into focused use cases.
- **Persistence:** retain explicit current-format DTOs without deserializing files directly into the domain model; reject obsolete development formats instead of migrating them.
- **Import/download:** record durable content identity and distinguish imported/generated/exported physical media.
- **Media infrastructure:** add recipe planning/compilation, cache-key generation, atomic cache commits, active leases, cleanup, and operation-specific FFmpeg plans.
- **Provider orchestration:** resolve asset/anchor references, materialize when needed, prepare uploads, qualify remote references by source fingerprint/scope/lifetime, poll jobs, and ingest successful downloads.
- **Provider boundaries:** retain independently verified BytePlus and AtlasCloud request serialization; keep logical-reference resolution ahead of provider preparation; add upload, polling, and cancellation behavior only where each provider documents it. BytePlus desktop preparation uses provider-neutral R2 presigned HTTPS references, its no-host fallback supports inline image/audio only, and AtlasCloud retains multipart upload.
- **WPF:** display virtual assets without requiring paths; add provider/credential/settings/reference selection and generation status/actions; keep preview/export/provider work asynchronous.
- **Tests:** add current-format rejection/round-trip tests, asset and generation graph/cycle tests, immutable snapshot tests, retry/branch semantics, cache deletion/reconstruction, deterministic-key fixtures, purpose-specific render plans, concurrent materialization, cancellation cleanup, output provenance, and paid-network isolation.

## Risks and tradeoffs

- **Model complexity:** a typed recipe graph is more work than storing output paths, but it prevents intermediate-file sprawl and makes provenance executable.
- **Reproducibility:** FFmpeg upgrades, hardware encoders, nondeterministic metadata, and replaced source files can change byte output. Reproducible intent is achievable; bit-identical output may require pinned software/settings and deterministic metadata.
- **Hashing cost:** canonical SHA-256 improves correctness but can be expensive for large video. Compute it during durable copy/download where possible, permit an explicit pending state, and perform background completion when needed.
- **Graph evolution:** immutable committed recipe revisions prevent downstream provenance drift but retain revision metadata over time. Cleanup may remove only unreferenced drafts/revisions under an explicit policy.
- **Nested render cost:** naïvely materializing every node creates the very intermediate sprawl being avoided. The planner should fuse compatible operations and materialize only true boundaries.
- **Frame accuracy:** timestamps, time bases, keyframes, and variable frame rate need explicit semantics. “Frame accurate” cannot rely only on UI milliseconds.
- **Disk pressure:** cache reuse improves speed but needs size/age policy, purge UX, failure handling, and active-lease protection.
- **Source loss:** portable projects remain reconstructable only while durable sources exist and match their identities. Missing-file relinking is eventually required.
- **Remote references:** provider asset IDs may expire or be account/region scoped. Persisting them without expiry/fingerprint can submit stale or wrong media.
- **Mutable timelines:** a live timeline reference is convenient but weakens historical reproducibility; immutable snapshots use more metadata but preserve generation/export intent.
- **Historical drift:** a logical ID without an immutable revision/hash can resolve to different media later. Submitted snapshots must freeze meaning without duplicating entire media files.
- **Lineage taxonomy:** too few relationship types lose intent; too many provider-specific types make migrations and graph UI brittle. Relationships should remain high-level and references retain exact inputs.
- **Partial generation completion:** provider completion, download, media verification, asset creation, and project save are separate failure points. State must distinguish a successful remote job from a successfully ingested durable output.
- **Provider retry cost:** retry is a new paid submission unless a provider offers a verified idempotent recovery mechanism. UI must not label polling/download recovery as a new retry.
- **Local resource cost:** a local run is not monetarily billed per request but can consume substantial GPU time, RAM, storage bandwidth, power, and foreground responsiveness. Cost behavior and confirmation policy must not assume that `Local` means free or harmless.
- **Local engine drift:** ComfyUI nodes, API workflow formats, model quantizations, PyTorch/CUDA behavior, and official templates can change independently. Pin and validate workflow/node/model fingerprints instead of assuming an installed server is compatible.
- **Territory restrictions:** MiniMax H3's current community license excludes the EU, UK, Republic of Korea, and United States and restricts use and distribution of outputs there. Technical availability cannot override license eligibility.

## Settled Phase 2A decisions

1. **Lineage:** zero or one parent generation, unlimited children and logical references. Relationship values are `RetryOf`, `VariantOf`, `ContinueAfter`, `ContinueBefore`, and `BasedOn`.
2. **Duplicate/branch:** UI actions only; submitted lineage records `VariantOf` or `BasedOn` according to intent.
3. **Drafts:** zero or one autosaved mutable `GenerationDraft` per project; drafts are not history. Submission creates an immutable record.
4. **Generation identity:** one submitted provider request/job per generation, with provider/model selected per request and zero or more durable output asset IDs.
5. **Reference roles:** optional provider-neutral roles are `GeneralReference`, `StartFrame`, `EndFrame`, `Character`, `Style`, `Environment`, `Motion`, and `Audio`, plus separate user label/notes.
6. **Viewer and composition:** Generate uses ephemeral currently previewed media; Edit uses a project-owned logical Working Composition. There is no starred Main Video domain role.
7. **Pre-release format:** maintain one current development format and reject obsolete files clearly; establish migrations only after the first supported beta/public format baseline.
8. **Missing resources/history:** submitted generations persist until explicitly deleted. Missing physical media is reported as missing project state and never causes silent history deletion.
9. **Materialization retention:** intentionally unresolved; logical recipes/provenance remain authoritative under every future policy.
10. **Recipe revisions:** editable/uncommitted recipe drafts are mutable; every committed or referenced recipe revision is immutable, linked to its predecessor, and pinned explicitly by historical references.
11. **Content identity:** SHA-256 is the canonical fingerprint of durable media bytes and remains separate from stable `AssetId` and human-readable display/file name.

## Settled Phase 2C decisions

1. **Meaning:** an anchor is a provider-neutral and editor-neutral exact position in source media; Saved Frame is the initial user-facing name.
2. **Revisioning:** stable logical anchors own immutable extraction-defining revisions; moving a committed/referenced anchor creates a new revision.
3. **Timing:** new revisions use video stream index, integer presentation timestamp, and rational time base; frame number is informational.
4. **Exactness:** every anchor revision stores stream index, integer presentation timestamp, rational time base, and source content identity; approximate timing is not admitted into the current model.
5. **Metadata:** display label, notes, and archived state remain mutable on the logical anchor; submitted references freeze their own label/notes.
6. **Targets:** Phase 2C anchors any imported or generated durable physical video; virtual-video anchors wait for Phase 2D time mapping.
7. **Deletion:** unreferenced anchors may be deleted; referenced anchors are archived/tombstoned and their pinned revisions remain resolvable.
8. **Boundaries:** editing references an anchor revision plus `BeforeFrame` or `AfterFrame`, normalized internally to `[start,end)`.
9. **Last frame:** Last Frame means the final decodable presentation frame; `AfterFrame` on it resolves to `SourceEnd`.
10. **Materialization:** canonical preview and provider preparation resolve the same decoded frame, while purpose-specific derivative encoding may differ and remains evidenced.
11. **Reference occurrences:** every generation reference has its own stable `ReferenceId`; providers consume transient prepared media descriptors rather than project assets or anchor objects.
12. **Continuation:** mode recommendations remain explicit; multiple parent outputs require selection; an imported video may use continuation UX without false generation lineage.
13. **UI:** exact frame work is activated explicitly through Select Frame; the lower Generate workspace is otherwise a quiet media-preparation surface. Old anchor revisions stay outside the ordinary workflow.
14. **Promotion:** a logical Saved Frame is sufficient for Phase 2C cherry-picking; saving a standalone durable image remains later work.
15. **Acceptance provider:** AtlasCloud MiniMax H3 is the first optional human-run paid anchor continuation; automated tests make no live or paid calls.
16. **Exact positions versus Saved Frames:** clip boundaries use the same exact-position semantics, but not every exact position is automatically a user-facing Saved Frame. Internal boundary state must not clutter Project Media and may be promoted later.
17. **Saved Clips:** a Saved Clip is a virtual video asset backed by an immutable trim recipe. Narrow trim materialization for Preview and ProviderUpload moves into revised 2C; concat, normalization, full composition rendering, and generalized export remain later work.
18. **Navigation:** the exact-frame browser progressively loads a bounded sliding window around the user's approximate playhead and does not require eager full-video indexing.

## Remaining questions and recommended defaults

The Phase 2C product-direction questions are settled. Remaining cross-phase items are:

1. **Virtual-video anchor mapping:** define only after Phase 2D establishes recipe time mapping; Phase 2C is physical-video-only.
2. **Materialization retention:** retain the policy boundary without selecting minimal/balanced/persistent behavior.
3. **Missing-source recovery:** Phase 2C preserves degraded state and blocks materialization; user-driven relinking/recovery remains later work.
4. **External exports:** default to export history without making reconstruction depend on an external path; decide catalog behavior with export UX.
5. **Provider cancellation:** distinguish local polling cancellation from remote cancellation and expose the latter only when verified.
6. **Saved-frame promotion:** direct logical Saved Frame use satisfies Phase 2C; general Save Frame as Asset UI remains with later promotion/export work.
7. **Boundary representation:** clip-only boundaries remain hidden from Project Media whether implemented as internal anchors or a more general exact-position reference; explicit promotion creates a user-facing Saved Frame.

## Phase gate

Phases 2A, 2B, and revised 2C are complete. Phase 2C human acceptance covered the Generate/Edit shell, Project Media projection, explicit progressive Select Frame workflow, first-class Saved Frames, Saved Clip creation and replay, cache reconstruction, missing-source handling, reference preparation, and narrow trim materialization. AtlasCloud MiniMax H3 remains the first optional human-run paid reference acceptance route; automated verification remains network-isolated. General recipe planning, concat, normalization, composition rendering, and promotion/export move to Phase 2D; full multitrack editing remains a separate later gate.
