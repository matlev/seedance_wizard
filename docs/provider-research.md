# Video generation provider research

Research dates: Seedance 2.5 on 2026-08-10; AtlasCloud MiniMax H3 on 2026-08-11. This supersedes earlier conclusions based on stale "coming soon" material. Model contracts, pricing, quotas, and provider policies can change; re-verify them before production release.

## Current conclusion

BytePlus ModelArk is the official international ByteDance API route for Dreamina Seedance 2.5 and is the preferred provider to evaluate first. AtlasCloud remains implemented as a selectable alternate route behind `IVideoGenerationProvider`; no AtlasCloud work has been removed.

BytePlus documentation updated on August 7, 2026 identifies model `dreamina-seedance-2-5-260628` and documents text-to-video, first-frame/first-and-last-frame generation, multimodal reference-to-video, video editing, and video extension. The current ceiling is 30 generated seconds and 50 total multimodal references, subject to per-type and task-specific limits.

BytePlus Seedance 2.5, AtlasCloud Seedance 2.5, and AtlasCloud MiniMax H3 are selectable in ReelForge alongside the no-cost fake provider. No route is project-global: every immutable generation snapshot records its selected provider and model.

## BytePlus ModelArk contract

### Task lifecycle

- Model ID: `dreamina-seedance-2-5-260628`.
- Create: `POST https://ark.ap-southeast.bytepluses.com/api/v3/contents/generations/tasks`.
- Retrieve: `GET https://ark.ap-southeast.bytepluses.com/api/v3/contents/generations/tasks/{id}`.
- Cancel/delete record: `DELETE https://ark.ap-southeast.bytepluses.com/api/v3/contents/generations/tasks/{id}`. A queued task can be cancelled; a running task cannot. ReelForge does not expose this remote operation yet, so stopping local monitoring never claims to cancel the remote job.
- Authentication: `Authorization: Bearer <API key>`.
- Create responses contain the task `id`. Retrieval states are `queued`, `running`, `succeeded`, `failed`, `cancelled`, and `expired`; successful content contains `video_url` and may contain `last_frame_url`.
- Output URLs are documented as valid for 24 hours with at most 100 downloads. Task records are retained for seven days, so prompt local ingestion is required for durable project media.

The request uses a typed `content` array. Prompt text is a `text` item. Media items use `image_url`, `video_url`, or `audio_url`, each with its documented URL object and a task-specific role. ReelForge serializes this contract directly rather than translating AtlasCloud fields.

### Capabilities and limits

| Capability | Verified BytePlus Seedance 2.5 contract |
| --- | --- |
| Workflows | T2V, first/last-frame I2V, multimodal R2V, editing, extension |
| Duration | 4-30 seconds, or provider-selected `-1` in supported workflows |
| Resolution | `480p`, `720p` |
| Ratios | `16:9`, `4:3`, `1:1`, `3:4`, `9:16`, `21:9`, `adaptive`; I2V uses `adaptive`, and editing/extension have additional adaptive-ratio rules |
| Output | `mp4` or `mov`; optional generated audio, provider watermark, and returned last frame |
| References | Up to 30 images, 10 videos, and 10 audio files; 50 combined |
| Images | HTTPS URL, Base64 data URL, or ModelArk `asset://` reference; less than 30 MB each |
| Audio | HTTPS URL, Base64 data URL, or ModelArk `asset://` reference; WAV/MP3, up to 15 MB each |
| Video | HTTPS URL or ModelArk `asset://` reference; MP4/MOV, up to 200 MB each, 2-30 seconds, with at most 30 seconds of input video in total |
| Inline body | Request body must remain below 64 MB |

References can be addressed in prompts as `@Image1`, `@Video1`, and `@Audio1`. Direct uploads containing identifiable real people have additional restrictions documented by BytePlus and should be handled as a product-policy concern before release.

### Pricing finding

BytePlus's August 7 pricing page lists Seedance 2.5 online inference at USD 10.70 per million tokens without input video and USD 6.40 per million tokens when the request includes input video. It states that only successfully generated videos are charged and gives the estimate:

`tokens = (input video duration + output duration) * width * height * fps / 1024`

Its no-input-video examples price a five-second 480p output at approximately USD 0.514 and a five-second 720p output at approximately USD 1.156. The potential saving versus AtlasCloud is material, but it is not one fixed percentage: exact economics vary with resolution, duration, input-video duration, account terms, and current third-party pricing. A real account estimate should therefore be shown before any paid acceptance test rather than encoding "33% cheaper" as a permanent product rule.

## BytePlus implementation in ReelForge

`BytePlusModelArkSeedance25Provider` implements the existing async provider abstraction and:

- validates T2V, one/two-image I2V, and multimodal R2V settings before reading credentials or sending HTTP;
- stores its API key through `ISecretStore` under `byteplus.modelark.api-key`, outside the `.rfp` project;
- serializes the verified typed-content request to the documented ModelArk endpoint;
- maps documented task states, output URLs, usage values, and structured failures into provider-neutral records;
- refuses any potentially billable submission without a fresh authorization created only by the desktop after a human accepts the per-request charge warning;
- is independently selectable without replacing or changing the AtlasCloud adapter.

`BytePlusModelArkAssetPreparationService` does not invent a generic ModelArk upload flow. In the ReelForge desktop it hands materialized local image, video, and audio references to `ITemporaryAssetHost`; the Cloudflare R2 implementation uploads/reuses private content-addressed objects and returns short-lived presigned HTTPS GET URLs that satisfy the documented Seedance reference contract. BytePlus remains unaware of Cloudflare credentials and bucket details. The no-host constructor used by isolated tests retains documented Base64 data URLs for eligible image/audio references and refuses local MP4/MOV. Existing qualified HTTPS or `asset://` references can also be submitted.

The output downloader remains provider-neutral: it downloads a successful HTTPS result, verifies and inspects it, atomically places it under `generated/`, and records the durable asset and generation links.

All BytePlus provider tests use custom in-memory `HttpMessageHandler` instances. Asset-preparation tests read only temporary local fixture bytes. They cannot reach BytePlus or incur charges, and no live request was made during this implementation.

## Deliberate current BytePlus UI subset

The API is broader than ReelForge's current generation panel. These are known application gaps, not provider-availability gaps:

- The UI currently exposes T2V, I2V, and R2V, but not distinct Edit or Extend task modes.
- The duration control exposes explicit 4-30 second values, not provider-selected `-1`.
- ReelForge currently requires prompt text even though BytePlus documents some audio-driven workflows where text is optional.
- ReelForge does not create ModelArk `asset://` references because no separate ingestion lifecycle was verified; local references instead use private R2 presigned HTTPS URLs, while existing qualified HTTPS/`asset://` references continue to work.
- Remote queued-task cancellation is documented but not yet surfaced in the application.
- Resource packages, regional access, quotas, moderation policy, and account authorization still require human account setup.

## AtlasCloud remains an alternate provider

AtlasCloud's three verified model IDs remain implemented:

- `bytedance/seedance-2.5/text-to-video`
- `bytedance/seedance-2.5/image-to-video`
- `bytedance/seedance-2.5/reference-to-video`

AtlasCloud submits to `POST https://api.atlascloud.ai/api/v1/model/generateVideo`, polls `GET https://api.atlascloud.ai/api/v1/model/prediction/{prediction_id}`, and prepares local references with multipart `POST https://api.atlascloud.ai/api/v1/model/uploadMedia`. Its adapter continues to support documented T2V, I2V, and R2V schemas, temporary uploads, polling, durable output ingestion, structured errors, Windows Credential Manager storage, and the same interactive paid-submission gate.

AtlasCloud's live schemas may wrap predictions in `data` while component examples show the prediction object directly; the adapter accepts both verified shapes. No verified AtlasCloud remote cancellation contract was found, so ReelForge exposes only local monitoring cancellation for that provider.

## AtlasCloud MiniMax H3

AtlasCloud's live model catalog and machine-readable model references currently expose three H3 routes:

- `minimax/h3/text-to-video`
- `minimax/h3/image-to-video`
- `minimax/h3/reference-to-video`

All three submit to the existing AtlasCloud `POST /api/v1/model/generateVideo` endpoint and use the same `GET /api/v1/model/prediction/{prediction_id}` lifecycle. They share the AtlasCloud account, API base URL, API key, and multipart `uploadMedia` preparation route with the Seedance adapter, but ReelForge gives H3 its own provider ID (`atlascloud.minimax-h3`) so drafts, immutable snapshots, polling, and provider selection remain unambiguous.

The verified H3 schema supports integer durations from 4 through 15 seconds and resolutions `768P` and `2K`. Text-to-video requires a concrete ratio from `21:9`, `16:9`, `4:3`, `1:1`, `3:4`, or `9:16`. Image-to-video accepts one required first-frame image plus one optional end-frame image as a public URL or supported image Base64 data URL, and its ratio is always `adaptive`. Reference-to-video accepts an ordered `refers` array of HTTPS image, video, and audio URLs with an explicit or inferred type; at least one image or video is required, so audio alone is invalid. R2V accepts `adaptive` or any of the concrete ratios above.

AtlasCloud's current H3 R2V schema specifies a minimum of one `refers` item but does not publish per-type or combined maximum reference counts. ReelForge therefore does not invent a smaller limit. Provider-side validation may still reject an undocumented excessive request, and this omission should be rechecked before production release.

The catalog advertises pricing from USD 0.10 per generated second. The machine-readable page labels that starting rate authoritative, while lower-page vendor description material mentions resolution-specific figures. ReelForge does not hard-code an H3 estimate from the conflicting descriptive section; the human confirmation displays the selected model, resolution, duration, and reference count before every potentially billable request.

`AtlasCloudMiniMaxH3Provider` serializes only the verified fields, shares the existing AtlasCloud credential and async transport, and uses the same human-only paid-submission authorization boundary. Automated H3 tests use in-memory secrets and custom `HttpMessageHandler` instances. They cannot contact AtlasCloud or incur charges, and no live generation request was made during implementation.

## Sources

Official BytePlus sources are primary for the ModelArk implementation:

- [BytePlus ModelArk: Dreamina Seedance 2.5 tutorial](https://docs.byteplus.com/en/docs/ModelArk/2607688#2.5_compatibility)
- [BytePlus ModelArk: Create video-generation task](https://docs.byteplus.com/en/docs/ModelArk/1520757)
- [BytePlus ModelArk: Retrieve video-generation task](https://docs.byteplus.com/en/docs/ModelArk/1521309)
- [BytePlus ModelArk: Cancel or delete video-generation task](https://docs.byteplus.com/en/docs/ModelArk/1521720)
- [BytePlus ModelArk: Model pricing](https://docs.byteplus.com/en/docs/ModelArk/1544106)
- [AtlasCloud: Seedance 2.5 models](https://www.atlascloud.ai/models/seedance-2.5)
- [AtlasCloud: Seedance 2.5 text-to-video API](https://www.atlascloud.ai/models/bytedance/seedance-2.5/text-to-video)
- [AtlasCloud: Seedance 2.5 image-to-video API](https://www.atlascloud.ai/models/bytedance/seedance-2.5/image-to-video)
- [AtlasCloud: Seedance 2.5 reference-to-video API](https://www.atlascloud.ai/models/bytedance/seedance-2.5/reference-to-video)
- [AtlasCloud: MiniMax H3 text-to-video API](https://www.atlascloud.ai/models/minimax/h3/text-to-video)
- [AtlasCloud: MiniMax H3 image-to-video API](https://www.atlascloud.ai/models/minimax/h3/image-to-video)
- [AtlasCloud: MiniMax H3 reference-to-video API](https://www.atlascloud.ai/models/minimax/h3/reference-to-video)
- [AtlasCloud: model catalog](https://www.atlascloud.ai/models)
- [AtlasCloud: Predictions](https://www.atlascloud.ai/docs/en/predictions)
- [AtlasCloud: Upload Files](https://www.atlascloud.ai/docs/en/upload-files)
