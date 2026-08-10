# Seedance provider research

Research date: 2026-08-09. This replaces the earlier conclusion based on stale “coming soon” pages. Model contracts, pricing, quotas, and provider policies can change; re-verify before enabling paid submission.

## Corrected finding

Seedance 2.5 is currently available through BytePlus ModelArk and AtlasCloud.

BytePlus’s live Dreamina Seedance 2.5 tutorial and Video Generation API documentation were updated on August 7, 2026. They identify the ModelArk model as `dreamina-seedance-2-5-260628` and document text-to-video, reference-to-video, first-frame/first-and-last-frame generation, video editing, and video extension. The documented ceiling is 30 seconds per request and 50 multimodal reference assets per request, subject to per-media and task-specific limits. ModelArk access requires the applicable Seedance 2.5 resource package/quota.

AtlasCloud’s live Seedance 2.5 collection currently exposes three concrete models:

- `bytedance/seedance-2.5/text-to-video`
- `bytedance/seedance-2.5/image-to-video`
- `bytedance/seedance-2.5/reference-to-video`

The actual OpenAPI material embedded in those model pages is detailed enough to implement request submission without guessing fields from marketing copy. An AtlasCloud adapter is therefore included behind `IVideoGenerationProvider`. It is not selected in the desktop UI yet, and no paid generation request was made while implementing or testing it.

## BytePlus ModelArk contract confirmed from current documentation

- Model ID: `dreamina-seedance-2-5-260628`.
- Task creation: `POST https://ark.ap-southeast.bytepluses.com/api/v3/contents/generations/tasks`.
- Asynchronous workflow: create a task, then retrieve its status/result.
- Supports T2V, multimodal R2V, first-frame and first/last-frame I2V, editing, and extension.
- Maximum generated duration: 30 seconds.
- Maximum multimodal inputs: 50 in total, with task-specific sublimits. The current tutorial describes up to 30 reference images and up to 10 reference videos; it also documents audio-reference limits and total-duration constraints.
- References are addressed in prompts with tokens such as `@Image1`, `@Video1`, and `@Audio1`.
- Use of the 2.5 model is quota/resource-package gated; availability does not imply an account can submit without provisioning.

This milestone does not add a BytePlus adapter. Its schema differs from AtlasCloud’s, and implementing it should include recorded task-creation/status fixtures plus account-region and quota handling.

## AtlasCloud request contract used by the adapter

All three modes submit to:

`POST https://api.atlascloud.ai/api/v1/model/generateVideo`

Authentication uses a Bearer API key. Status retrieval is documented as:

`GET https://api.atlascloud.ai/api/v1/model/prediction/{prediction_id}`

Common verified request fields are:

- `model` and `prompt`
- `duration`: provider-selected `-1` or an integer from 4 through 30
- `resolution`: `480p` or `720p`
- `ratio`
- `generate_audio`, `watermark`, and `return_last_frame`
- `output_format`: `mp4` or `mov`

Mode-specific fields and limits:

| Mode | Model | Inputs and constraints |
| --- | --- | --- |
| T2V | `bytedance/seedance-2.5/text-to-video` | No reference media. Ratios: `16:9`, `4:3`, `1:1`, `3:4`, `9:16`, `21:9`, or `adaptive`. |
| I2V | `bytedance/seedance-2.5/image-to-video` | Required `image`; optional `last_image`; current schema restricts `ratio` to `adaptive`. |
| R2V | `bytedance/seedance-2.5/reference-to-video` | `reference_images` up to 30, `reference_videos` up to 10, and `reference_audios` up to 10. This permits the advertised maximum of 50 multimodal references when all per-type maxima are combined. |

The documented response carries a prediction `id`, model, status (`processing`, `completed`, `failed`, or `timeout`), outputs, timestamps/token accounting, and NSFW flags. Live examples wrap the prediction in `data`, while the component schema describes the prediction object itself; the adapter deliberately accepts both shapes.

## Implementation and safety boundaries

`AtlasCloudSeedance25Provider`:

- validates provider and mode constraints before reading credentials or sending HTTP;
- maps the three neutral generation modes to the verified model IDs;
- stores the API key through `ISecretStore` under `atlascloud.api-key` and never writes it to project JSON or diagnostics;
- submits only to the documented HTTPS endpoint;
- accepts only the documented optional parameter names and enum values;
- maps structured HTTP/provider errors into `GenerationError` through `VideoGenerationProviderException`;
- requires every local project asset to have an explicit AtlasCloud provider reference (public URL, Base64 value, or previously uploaded asset reference) rather than inventing an upload contract;
- is covered by mocked HTTP contract tests. Tests never contact AtlasCloud and cannot incur generation charges.

The current `IVideoGenerationProvider` abstraction covers submission but not polling. The provider records the returned prediction ID and running state; a later milestone should add first-class polling/cancellation and output ingestion before enabling AtlasCloud in the UI.

## Still not established by the inspected AtlasCloud model schemas

The generation schemas do not fully establish:

- a dedicated AtlasCloud asset-upload API or the lifecycle of provider asset references;
- callback/webhook contracts;
- rate-limit headers and quota behavior;
- media retention/deletion guarantees;
- complete error-code taxonomy;
- whether all 50 R2V references are accepted together under every account/model configuration beyond the published per-type maxima.

Those gaps do not block a schema-faithful submission adapter, but they do block automatic local-file uploading and production-ready job orchestration. The adapter therefore refuses unresolved local assets instead of guessing.

## Sources

- [BytePlus ModelArk: Dreamina Seedance 2.5 tutorial](https://docs.byteplus.com/en/docs/modelark/2607688)
- [BytePlus ModelArk: Video Generation API](https://docs.byteplus.com/en/docs/modelark/1520757)
- [AtlasCloud: Seedance 2.5 models](https://www.atlascloud.ai/models/seedance-2.5)
- [AtlasCloud: Seedance 2.5 text-to-video API](https://www.atlascloud.ai/models/bytedance/seedance-2.5/text-to-video)
- [AtlasCloud: Seedance 2.5 image-to-video API](https://www.atlascloud.ai/models/bytedance/seedance-2.5/image-to-video)
- [AtlasCloud: Seedance 2.5 reference-to-video API](https://www.atlascloud.ai/models/bytedance/seedance-2.5/reference-to-video)
