# Architecture

Status: accepted direction; Phase 2A complete; Phase 2B implementation complete with one successful human-run AtlasCloud MiniMax H3 generation and follow-up UX verification pending
Original platform decision: 2026-08-09
Recipe-model design revision: 2026-08-10

This document records the accepted target architecture. Schema-v2 logical assets, immutable recipe revisions and generation snapshots, migration, SHA-256 identity, provider-specific physical-reference preparation, BytePlus ModelArk plus AtlasCloud Seedance 2.5 and MiniMax H3 submission/polling, durable output ingestion, and their application boundaries are implemented. Virtual-recipe rendering, frame-anchor materialization, and general cache planning remain later Milestone 2 phases.

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
- **Infrastructure** implements JSON migration/persistence, durable-file import, content identity, cache storage, FFmpeg planning/rendering, provider upload integration, and Windows services.
- **App** treats physical and virtual assets uniformly in selection and editing UI. It requests previews, provider inputs, and exports through application services rather than resolving paths or launching FFmpeg itself.
- **Tests** verify recipes, migrations, graph validity, deterministic cache keys, render plans, cleanup, and provider payloads without making paid provider calls.

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
| `Timeline` / `TimelineClip` | Already references assets by ID and stores non-destructive in/out positions | No tracks or recipe/render-plan boundary; current code assumes an asset can be resolved directly to a path. |
| `PortableProjectStore` | Schema field, portable relative paths, atomic save | Supports only “reject newer schema”; it has no migration chain. It creates `cache/` but defines no deletion or reconstruction semantics. |
| `AssetImportService` | Copies user media into durable project storage and inspects it | Correct for physical imports, but it should not be reused for virtual outputs or cache promotion without explicit semantics. |
| `ProjectWorkspace.GetAbsoluteAssetPath` | Centralizes relative-path resolution | Cannot represent a virtual asset. Consumers must request materialization rather than assume all assets have paths. |
| `FfmpegCommandBuilder` / process runner | Pure arguments, safe process execution, cancellation, captured diagnostics | Commands operate on paths directly; there is no recipe compiler, dependency planner, cache key, or materialized-result lease. |
| WPF asset explorer/preview | Asset-ID-driven collections are a useful starting point | Preview calls `GetAbsoluteAssetPath`; it must become async and materialization-aware while preserving UI/domain separation. |
| `GenerationRequest.ReferenceAssetIds` | Provider requests already refer to logical asset IDs | Provider preparation currently expects pre-existing provider references. Virtual inputs need lazy local materialization followed by an explicit upload/reference step. |
| `ProviderReferences` | Can retain reusable remote references | Lifetime, source fingerprint, expiry, and whether a reference is safe to reuse are not modeled. |
| `GenerationRecord.Request` | Retains prompt/settings and is persisted before provider submission | Stores a mutable request object rather than an explicit immutable snapshot; references cannot target anchors or capture role, order, or logical revision. |
| `GenerationRecord.ParentGenerationId` | Already enforces the desired maximum of one lineage parent | Needs a paired relationship type and validation; it must not be used to infer actual provider inputs. |
| `GenerationRecord.OutputAssetId` / `AssetProvenance.GenerationId` | Provides the beginnings of bidirectional output provenance | The invariant is not enforced, remote completion versus local ingestion is not distinguished, and only one output is representable. |

The current project contains no significant timeline/editor output to migrate, so this is the least expensive point to establish the recipe foundation.

## Core invariants

1. Every project asset has a stable logical asset ID and media type.
2. A **physical asset** names durable media inside the portable project and records content identity.
3. A **virtual asset** contains a typed, versioned recipe and has no authoritative media path.
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

Use explicit typed recipes rather than an operation enum plus arbitrary parameter dictionary. Conceptual recipe variants include:

```text
TrimRecipe
  SourceAssetId
  StartBoundary             anchor, timestamp, or source start
  EndBoundary               anchor, timestamp, or source end
  RenderProfile

ConcatRecipe
  InputAssetIds[]
  CompatibilityPolicy
  Transition-free ordering initially
  RenderProfile

NormalizeRecipe
  SourceAssetId
  TargetMediaProfile

ExtractFrameRecipe
  SourceAssetId
  AnchorId
  ImageProfile

TimelineCompositionRecipe
  TimelineSnapshotId or immutable composition definition
  RenderProfile

ProviderSnippetRecipe
  SourceAssetId
  Boundaries
  Provider constraints/profile
```

Each serialized recipe needs its own recipe version. Project schema versioning handles the envelope; recipe versioning prevents one future operation change from forcing unrelated recipe shapes to change.

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

- missing asset or anchor references;
- missing or mismatched committed recipe revisions;
- direct or indirect recipe cycles;
- media-type-incompatible operations;
- non-monotonic or out-of-range trim boundaries once source duration is known;
- anchors attached to media without a stable time axis;
- deletion of a source that is still referenced, unless the user also deletes/relinks dependents;
- timeline clips whose logical source cannot produce the requested media type.

Deleting a virtual asset should not delete its physical sources. Deleting a physical source with dependents requires an explicit dependency-aware decision. Cache files never participate in referential integrity.

## Anchors

Anchors become first-class members of `VideoProject`. Conceptually:

```text
FrameAnchor
  Id
  AssetId
  Timestamp                 canonical logical position
  FrameNumber?              optional; retained only when reliable
  TimeBase?                 optional precision/reconciliation metadata
  Label?
  Notes?
```

Timestamp is authoritative for persistence unless the source provides a stable frame/time basis that can be retained. Variable-frame-rate media makes a mandatory frame number unsafe. An anchor does not own a PNG. An extracted image is either a virtual `ExtractFrameRecipe`, a cache result, or an explicitly saved physical asset.

Open question: whether anchors may target any time-addressable virtual video or only physical video. Allowing virtual targets is more expressive and still reproducible, but complicates time mapping when upstream recipes change. The recommended first slice limits anchors to physical video, then deliberately extends the rule after trim semantics are proven.

## Materialization boundary

Conceptual application contracts:

```text
IMediaMaterializer
  MaterializeAsync(assetId, purpose, cancellationToken)

IMaterializationPlanner
  BuildPlan(project, assetId, purpose)

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

`MaterializationPurpose` is conceptual and initially includes `Preview`, `ProviderUpload`, `FinalExport`, `FrameExtraction`, `Thumbnail`, and `Waveform`. Purpose influences quality, format, lifetime, provider limits, and whether a direct source path is acceptable.

`MaterializeAsync` should return a result/lease containing the usable path, resolved metadata, cache key or physical content identity, and ownership/lifetime information. Callers must not infer permanence from receiving a path. A lease prevents cleanup from removing an artifact while preview, upload, or export is using it.

The materializer follows this decision flow:

1. Resolve the logical asset and validate its dependency graph.
2. If a physical source already satisfies the purpose, return it without copying.
3. Build a deterministic render plan for a virtual asset.
4. Compute the cache key from the plan and transitive inputs.
5. Reuse a verified cache hit, otherwise render into a temporary file.
6. Atomically commit the completed artifact to cache and return a lease.
7. For explicit export/promotion, copy or render into durable storage and create/update the appropriate physical project asset only after success.

The UI, provider adapters, and timeline must not decide whether FFmpeg is required. They request an asset for a purpose; the materialization layer owns that choice.

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
- relevant anchor values;
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

### Main-video durability

`MainVideoAssetId` may point only to a durable physical video asset. Making a virtual asset or retained materialization the main video invokes durable promotion first; it cannot leave the project dependent on cache storage. Demoting a main video changes only the designation—the file remains an ordinary durable project asset. Deleting the main video requires the user to select another durable video or explicitly leave the project without one; the application must not silently promote or delete another asset.

## Timeline implications

Timeline clips should continue to reference logical asset IDs, so either physical or virtual media can be used. The persisted timeline should grow conceptually to include track identity/order, in/out boundaries, timeline position, audio enablement, and later gain/fade/transition properties.

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

The transient `GenerationRequest.ReferenceAssetIds` remains a low-level provider-input shape. `GenerationWorkflow` freezes the richer draft into a path-free immutable snapshot first, materializes and verifies physical references, uploads them through the documented provider-preparation boundary, and supplies temporary overrides only to the serializer. Recipes and UI remain provider-neutral. No paid submission appears in materialization, orchestration, polling, or provider contract tests.

Submission and monitoring are separate application responsibilities. `GenerationWorkflow.SubmitAsync` is reachable for a potentially billable provider only after the desktop creates a one-request authorization from a human confirmation. Once the provider returns a job ID, `GenerationJobCoordinator` records a provider-neutral active-job descriptor in `%LOCALAPPDATA%\ReelForge\active-jobs.json` and polls through `IAsyncVideoGenerationProvider`. The registry contains project identity, provider/model identity, elapsed-time origin, remote state, sanitized metadata, and any returned output URLs; it contains no API credentials or mutable generation inputs.

The coordinator is application-scoped rather than project-view-scoped. It continues while the user switches projects or has no project open, restores unresolved entries after application restart, and never calls `SubmitAsync`. A terminal result is merged into its owning `.rfp` project and successful outputs pass through the same verified ingestion/provenance path whether or not that project is currently visible. The project remains authoritative generation history; the application registry is recoverable operational state and is removed after terminal state and project ingestion are durably reconciled.

## Project schema and migration

The current schema is version 1. A schema bump is recommended because version 1 gives `ProjectAsset.RelativePath` physical-path semantics and has no persisted anchor collection or virtual recipe discriminator. Treating new fields as merely optional would deserialize, but it would leave important invariants ambiguous.

Conceptual version 2 changes:

- add an explicit physical/virtual asset discriminator;
- move physical-only path/content identity under physical storage metadata;
- add typed, versioned, immutable committed recipe revisions with previous-revision links and a current-revision pointer per virtual asset;
- keep mutable recipe drafts separate from committed revision history;
- add SHA-256 content identity for durable physical media without changing logical IDs or display/file names;
- add a project-owned anchor collection;
- replace asset-ID-only generation inputs with ordered/role-aware logical reference snapshots that can target assets or anchors;
- make submitted generation request snapshots explicitly immutable;
- add one autosaved mutable `GenerationDraft` per project without adding it to generation history;
- pair the existing nullable `ParentGenerationId` with a nullable, cycle-checked `RelationshipType` from the settled five-value vocabulary;
- support bidirectional generation/output provenance and multiple output asset IDs;
- allow timeline references to target either asset kind;
- formalize cache as non-authoritative and keep cache paths out of the `.rfp` project file;
- enrich reusable provider references with source/materialization identity, provider scope, and expiry if they remain persisted;
- allow retention preferences/receipts without making retained materializations authoritative project dependencies.

Migration strategy:

1. Deserialize the version-1 document into its version-1 shape.
2. Convert every version-1 asset with a `RelativePath` into a physical version-2 asset without moving its file.
3. Preserve IDs, origins, metadata, provenance, provider references, generation links, main-video selection, and timeline references unchanged.
4. Treat existing `EditorDerived` or `ExtractedFrame` assets as physical legacy-derived assets because a version-1 path is authoritative; do not attempt to reverse-engineer recipes from string provenance.
5. Mark migrated physical content identity as pending until SHA-256 is calculated; do not block safe metadata migration on hashing every large file, and do not invent a hash from file metadata.
6. Convert each version-1 `GenerationRequest.ReferenceAssetIds` entry, in order, into an asset-kind logical reference snapshot. Role and revision remain unspecified because version 1 did not record them.
7. Preserve `ParentGenerationId` when present and assign the conservative `BasedOn` relationship type unless stronger semantics are explicitly present in durable version-1 data; never guess `RetryOf`, `VariantOf`, or continuation semantics from timestamps or prompts.
8. Preserve the version-1 request values as the historical snapshot. The migration cannot prove that the in-memory request had never been mutated before its last save, so it preserves rather than embellishes the recorded state.
9. Convert the singular output asset ID into a one-element output collection and preserve the asset's existing generation provenance where present.
10. Add an empty anchor catalog because version 1 never persisted anchors at project level.
11. Validate the migrated asset/recipe/generation graphs and referenced files, report missing durable files without deleting metadata, and save version 2 only through an explicit/transactional migration path.
12. For this safe metadata-only version-1 to version-2 migration, create `project.backup-v1.json` first, migrate automatically, atomically save the selected project file, and report the upgrade. Never overwrite the known-good project if backup or migration fails. New projects use a project-named `.rfp` file; legacy `project.json` files remain supported and save in place.

Migration policy beyond version 2 is hybrid: safe/reversible metadata migrations run automatically after a versioned backup; destructive, expensive, lossy, media-rewriting, or risky folder-layout migrations require an explanation and explicit user confirmation after backup.

Downgrading a version-2 project to version 1 is not generally possible because virtual assets have no physical paths. Older applications should continue rejecting newer schemas rather than silently dropping recipes.

## Components that would change during implementation

- **Core:** `VideoProject`, `ProjectAsset`, `GenerationDraft`, immutable request snapshots, `GenerationRecord`, `GenerationSubmission`, `AssetProvenance`, frame anchors, recipes, reference roles, single-parent lineage validation, multi-output provenance, main-video invariants, timeline types, graph validation, content identity, and schema version.
- **Application:** replace path assumptions with materialization/export/provider-preparation ports; add submit/poll/cancel/download/ingest orchestration; expand `ProjectWorkspace` or split it into focused use cases.
- **Persistence:** add explicit versioned DTOs and a migration chain instead of deserializing all versions directly into the current domain model.
- **Import/download:** record durable content identity and distinguish imported/generated/exported physical media.
- **Media infrastructure:** add recipe planning/compilation, cache-key generation, atomic cache commits, active leases, cleanup, and operation-specific FFmpeg plans.
- **Provider orchestration:** resolve asset/anchor references, materialize when needed, prepare uploads, qualify remote references by source fingerprint/scope/lifetime, poll jobs, and ingest successful downloads.
- **Provider boundaries:** retain independently verified BytePlus and AtlasCloud request serialization; keep logical-reference resolution ahead of provider preparation; add upload, polling, and cancellation behavior only where each provider documents it. BytePlus desktop preparation uses provider-neutral R2 presigned HTTPS references, its no-host fallback supports inline image/audio only, and AtlasCloud retains multipart upload.
- **WPF:** display virtual assets without requiring paths; add provider/credential/settings/reference selection and generation status/actions; keep preview/export/provider work asynchronous.
- **Tests:** add migration fixtures, asset and generation graph/cycle tests, immutable snapshot tests, retry/branch semantics, cache deletion/reconstruction, deterministic-key fixtures, purpose-specific render plans, concurrent materialization, cancellation cleanup, output provenance, and paid-network isolation.

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
6. **Main video:** always a durable physical project asset. Promotion precedes selection; demotion does not make media ephemeral; deletion prompts for replacement or leaves no main video.
7. **Migration:** safe/reversible metadata migrations run automatically after backup; destructive, expensive, lossy, media-affecting, or risky layout migrations require explicit confirmation.
8. **Missing resources/history:** submitted generations persist until explicitly deleted. Missing physical media is reported as missing project state and never causes silent history deletion.
9. **Materialization retention:** intentionally unresolved; logical recipes/provenance remain authoritative under every future policy.
10. **Recipe revisions:** editable/uncommitted recipe drafts are mutable; every committed or referenced recipe revision is immutable, linked to its predecessor, and pinned explicitly by historical references.
11. **Content identity:** SHA-256 is the canonical fingerprint of durable media bytes and remains separate from stable `AssetId` and human-readable display/file name.

## Remaining questions and recommended defaults

These are not blockers for documenting the settled generation model, but the relevant item should be confirmed before its implementation slice:

1. **Anchor targets:** initially physical video only; add virtual-video anchors after time mapping is specified.
2. **Materialization retention:** retain the policy boundary without selecting minimal/balanced/persistent behavior in Phase 2A.
3. **Missing sources:** open in a degraded state, preserve every logical descendant/history record, and add relinking later.
4. **External exports:** default to export history without making reconstruction depend on an external path; decide catalog behavior with export UX.
5. **Provider cancellation:** distinguish local polling cancellation from remote cancellation and expose the latter only when verified.

## Phase gate

Phase 2A and the Phase 2B implementation were explicitly approved. BytePlus ModelArk is the preferred official provider for the first optional human-run paid acceptance test; AtlasCloud remains a selectable alternate route. Phase 2C frame/continuation UI and Phase 2D virtual-recipe rendering remain separate implementation gates.
