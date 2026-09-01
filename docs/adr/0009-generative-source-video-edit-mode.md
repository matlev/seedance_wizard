# ADR-0009: Model source-video editing as a constrained generation mode

Status: accepted
Date: 2026-08-30

## Context

AtlasCloud Seedance 2.5 exposes source-video editing through its existing reference-to-video generation route. An explicit edit subtask requires exactly one source video, uses that video's geometry and length, and returns newly generated media. Leaving the subtask implicit allows incompatible ratio and duration settings to pass submission and fail asynchronously.

ReelForge's product definition separately reserves provider-assisted repair, object removal, background replacement, and direct usage replacement for a future media-edit provider family. Treating every provider API that uses the word “edit” as that future family would conflate two different product behaviors. Conversely, hiding Atlas's constraints in WPF or its adapter would make persisted drafts and immutable request snapshots unable to explain their meaning.

### Architecture preflight

```text
Feature/outcome: Add explicit Atlas Seedance source-video editing with provider-required source geometry and a newly ingested output.
Existing owners touched: Core generation modes/capabilities and persisted snapshots; Infrastructure Atlas request mapping and candidate project DTO version; App capability-driven generation form.
Proposed responsibility and extension point: Extend the existing generation capability contract with provider-neutral per-mode requirements; use the existing submission, authorization, job, provenance, and output-ingestion workflow.
Dependency and public-contract impact: Dependency direction is unchanged; the portable generation mode/capability contract gains VideoEdit and fixed-input requirements.
Persistence/format/compatibility impact: VideoEdit can appear in drafts and immutable request snapshots, so unpublished development format 4 is replaced by format 5. No supported beta-format marker or migration ladder is introduced.
Parallel-workflow or boundary risk: A second provider workflow, direct in-place mutation, generation-specific timeline replacement, and WPF-owned provider rules are prohibited.
Verification (tests and manual acceptance): Core capability validation; Atlas payload mapping; project-format round trip/version rejection; App locked-control policy; network-isolated job tests; generation-form manual acceptance without confirming a paid request.
ADR or architecture-debt decision: ADR required for the public/persistence and provider-boundary distinction. No architecture debt accepted.
```

## Decision

`VideoEdit` is a portable generation mode for a request that uses one source video and produces a new generated output. It is not an in-place media mutation, repair claim, or timeline replacement command. Successful output follows the existing ingestion path and becomes new durable Project Media with the existing immutable request, lineage, provider-job, and provenance records.

Generation provider capabilities may declare provider-neutral mode requirements: a fixed duration value, fixed aspect-ratio value, and exact reference counts by media type. Core validation enforces those requirements before provider submission. Presentation reads the same requirements to lock controls and explain the source constraint; provider-specific field names remain in Infrastructure.

AtlasCloud Seedance 2.5 alone declares the initial `VideoEdit` capability. It requires one video with a known duration from 4 through 30 seconds and no image or audio references, persists duration `-1` and ratio `adaptive`, and maps the request to the existing reference-to-video model with Atlas's explicit edit-subtask field. Providers whose current official contracts do not establish equivalent behavior do not advertise the mode.

Billable execution retains the existing fresh human confirmation. ReelForge does not expose post-submission cancellation for AtlasCloud because the provider publishes no cancellation endpoint; the existing Undo Send window remains cancellation-before-submission. A local “cancel” that merely stopped polling would be misleading and could orphan billable provider work.

## Consequences

- Persisted drafts and immutable history reproduce whether a request was explicitly a source-video edit and preserve the provider-required sentinel values.
- Invalid Atlas edit geometry is rejected locally instead of becoming an avoidable asynchronous paid-provider failure.
- Ordinary reference-to-video generation remains distinct and keeps flexible ratio and duration controls.
- Generated edit output is added as a new asset; no existing Project Media asset or timeline occurrence changes silently.
- Future provider-assisted repair and explicit replace semantics still require their separately approved contract and commands.

## Alternatives considered

- Inferring editing only from prompt wording was rejected because it cannot validate or persist provider-required semantics reliably.
- Storing Atlas field names in Core or the project format was rejected because project meaning must remain engine- and provider-neutral.
- Building the future media-edit provider family now was rejected because this request has an existing generation lifecycle and does not authorize repair, masks, in-place replacement, or broader provider semantics.
- Offering a post-submission local cancel was rejected because it would not cancel AtlasCloud execution or billing.

## Verification and follow-up

Automated provider tests remain network-isolated and cannot submit paid work. Manual acceptance stops before the final billable confirmation. Equivalent edit modes may be enabled for another provider only after its official contract, required inputs, output semantics, cancellation behavior, and legal/provider review are verified.
