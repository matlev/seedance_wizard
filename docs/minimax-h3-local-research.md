# MiniMax H3 local execution research

Research date: 2026-08-10. Status: researched and architecturally planned; not scheduled for implementation. No ComfyUI installation was changed and no model weights were downloaded.

## Recommendation

MiniMax H3 is a credible future local provider for ReelForge, but it should remain behind a hardware-and-license feasibility gate. ComfyUI 0.30.0 or later now has native H3 nodes and official T2V, I2V, and R2V templates. The integration can use ComfyUI's local Server API and fit `IVideoGenerationProvider` without placing workflow node IDs, local paths, GPU details, or ComfyUI protocol fields in the domain model.

The first feasibility experiment should use only the official native T2V template and the recommended pruned/quantized FL2VA files. It should not begin until the application can report the machine's GPU, VRAM, system RAM, disk headroom, ComfyUI/PyTorch versions, installed nodes, installed model files, and license eligibility. Ref2VA should be a second opt-in download because it requires a separate 21 GB diffusion model.

## What is actually local

The open release is H3-Base. It produces approximately 768p audio-video locally. The complete first-party H3 system also includes:

- **H3-Context-IR**, a hosted multimodal instruction-understanding stage that is not open-sourced;
- **H3-Base**, the locally released generator;
- **H3-Regenerate-2K**, a hosted regeneration stage that is not yet open-sourced.

Consequently, the official ComfyUI native workflows can be fully local for H3-Base output, but they do not reproduce the complete hosted 2K pipeline. A "local H3" provider must not advertise native offline 2K or imply that it uses the hosted Context-IR. The adapter may use MiniMax's published prompting guidance locally; calling either hosted stage would be a separate remote provider or hybrid execution profile with its own credential and cost policy.

## Native ComfyUI workflows and model families

ComfyUI's template library ships three native workflows:

| ReelForge intent | Native H3 family/node | Inputs |
| --- | --- | --- |
| Text-to-video | FL2VA / `MiniMaxH3ImageToVideo` | Text and zero images |
| First- or last-frame video | FL2VA / `MiniMaxH3ImageToVideo` | Text and one image, with start/end role |
| First-and-last-frame video | FL2VA / `MiniMaxH3ImageToVideo` | Text and two ordered images |
| Reference-to-video | Ref2VA / `MiniMaxH3ReferenceToVideo` | Text plus ordered image, video, and/or audio references |

The official UI workflow JSON must be exported or maintained in ComfyUI's API workflow format before submission. A future adapter should pin a reviewed API-format template version, replace only named semantic binding points, verify expected node classes through `/object_info`, and hash the final workflow. It must not let ComfyUI node IDs or graph wiring become project-domain fields.

## Capabilities and limits

- Output duration: 4-15 seconds.
- Output rate: 24 FPS.
- Local H3-Base resolution: a 768-pixel short edge by default; the Comfy template describes roughly 1344x768 for a one-megapixel 16:9 output and requires dimensions on a multiple-of-32 grid.
- Aspect ratios include 21:9, 16:9, 4:3, 1:1, 3:4, and 9:16, with other dimensions supported by the model's resolution rules.
- Output: MP4 containing generated video and native 32 kHz stereo audio. Dialogue, sound effects, and music are generated jointly.
- Duration is represented on the native 17-frames-per-block grid (`17k+5`) at 24 FPS; the adapter must translate requested seconds to an allowed frame count and disclose the realized duration.
- FL2VA accepts zero, one, or two images. Zero images is T2V; one is first- or last-frame generation; two is first-and-last-frame generation.
- Ref2VA accepts at most 9 images, 3 videos, and 3 standalone audio clips, with at most 12 files total.
- Each Ref2VA video is 2-15 seconds and all reference-video duration totals at most 15 seconds.
- Each standalone audio reference is 2-15 seconds and total reference-audio duration is at most 15 seconds. Audio cannot be the only reference modality; it must accompany an image or video.
- Ref2VA references are ordered and prompt-addressed as `<Picture 1>`, `<Video 1>`, and `<Audio 1>`. The adapter should derive those tags from the immutable logical-reference order and role.
- `ref_image_size=match` favors speed by matching generation resolution; `max` retains up to a 2048-pixel short edge for stronger identity at additional compute cost.

## Local model dependencies and disk impact

The official ComfyUI templates recommend these files:

| Component | Recommended file | Approximate size |
| --- | --- | ---: |
| FL2VA diffusion model | `minimax_h3_fl2va_pruned_int8_convrot.safetensors` | 21.0 GB |
| Ref2VA diffusion model | `minimax_h3_ref2va_pruned_int8_convrot.safetensors` | 21.0 GB |
| Shared text encoder | `qwen3vl_32b_minimax_h3_nvfp4_awq.safetensors` | 15.7 GB |
| Shared video VAE | `minimax_h3_video_vae_fp16.safetensors` | 5.21 GB |
| Shared audio VAE | `minimax_h3_audio_vae_fp32.safetensors` | 605 MB |

A minimal FL2VA installation is therefore about 42.5 GB of weights. Adding Ref2VA raises the selected set to about 63.5 GB, before ComfyUI, temporary files, decoded frames, generated media, caches, and download headroom. The 465 GB Comfy-Org repository contains multiple alternative precisions and both task families; ReelForge must never offer to download the entire repository by default.

The recommended `int8_convrot` diffusion files prefer a current PyTorch CUDA 13.0 build. The NVFP4/AWQ text encoder is documented as usable without a Blackwell GPU. The official examples use standard attention; Sage Attention is optional and may roughly double speed, but adds CUDA/PyTorch-version-sensitive packages and optional custom nodes. It should not be part of a first native-support test.

## Hardware feasibility and discovery

Neither MiniMax nor the official ComfyUI H3 page states a supported minimum VRAM/RAM configuration or a guaranteed generation time. Weight size alone does not equal peak VRAM because ComfyUI can offload between VRAM, RAM, and storage. It also means that claiming support merely because a CUDA device exists would be misleading.

Before offering H3, a future environment probe should collect and display:

- ComfyUI reachability and version (minimum 0.30.0);
- OS, Python, PyTorch, CUDA/ROCm/backend, and launch arguments;
- every device name/type plus total and currently free VRAM from `/system_stats`;
- total/free system RAM;
- free space on model, Comfy input/output, project, and temporary volumes;
- expected model filenames and sizes from `/models` or the configured model roots;
- required H3 node classes and their live input schemas from `/object_info`;
- workflow-template version and a hash of the adapter's pinned API workflow;
- whether the configured precision/backend combination is supported;
- whether ComfyUI is bound only to loopback;
- a small explicit benchmark result: resolution, frames/duration, steps, wall time, peak memory if available, and success/failure.

Suggested readiness levels are `Unavailable`, `Installed but incompatible`, `Ready but unbenchmarked`, and `Benchmarked`. No fixed "minimum GPU" should be placed in the product until ReelForge has reproducible target-machine tests. Given the approximately 42.5 GB minimal weight set and reliance on offloading, fast NVMe storage and substantial system RAM are operationally important even when VRAM is adequate.

## Local ComfyUI Server API integration

The documented Server API supports the needed asynchronous shape:

1. Connect to a configured loopback ComfyUI endpoint, normally `127.0.0.1:8188`.
2. Probe `/system_stats`, `/object_info`, `/models`, `/features`, and template/model availability.
3. Materialize ReelForge logical references for local consumption and stage them into ComfyUI's input scope.
4. Bind normalized request values into a pinned H3 API-format workflow.
5. Submit the complete workflow with `POST /prompt`; retain the returned `prompt_id` as the provider job ID.
6. Monitor `/ws` events for execution start, node execution, progress, completion, and errors. Reconnect and recover through `GET /history/{prompt_id}`.
7. Resolve the saved video from history and acquire it through the configured local-output boundary or `/view`, then use ReelForge's normal verification and durable-output ingestion.
8. Remove queued work with the queue API where possible. Treat `POST /interrupt` carefully because it interrupts the server's current execution rather than providing provider-style per-job cancellation.

ComfyUI documents `/upload/image`, but the inspected core route documentation does not establish generic video/audio upload endpoints for an attached server. Before implementation, verify the exact native H3 API workflow's video/audio loader inputs and output-history shape. A ReelForge-managed same-machine ComfyUI process could stage verified files under a configured input directory; an arbitrary LAN server needs an explicitly verified transport and must not rely on shared local paths.

Security default: attach only to a loopback address. ComfyUI's local Server API should not be assumed to provide authentication, and ReelForge should never launch it with `--listen 0.0.0.0` or permissive CORS by default. Remote/LAN ComfyUI should be a separately secured advanced configuration.

## Provider-neutral architecture

`IVideoGenerationProvider` should continue to represent semantic generation and an asynchronous job lifecycle, not HTTP billing. A local implementation can submit a Comfy workflow and return its `prompt_id` exactly as a remote implementation returns a vendor job ID.

Provider-specific details remain outside Core:

```text
Core generation snapshot
  provider/model identity
  prompt + normalized duration/size/audio intent
  ordered logical references and roles
                 |
                 v
Application generation workflow
  resolve/materialize references
  ask selected provider for readiness and execution policy
                 |
        +--------+---------+
        |                  |
        v                  v
Remote adapter        Local Comfy adapter
HTTP schema/auth      workflow template + node bindings
upload/URL refs       input staging + hardware probe
remote polling        prompt/ws/history
        |                  |
        +--------+---------+
                 v
Provider-neutral output acquisition
  stream/verified local file -> durable generated asset
```

Conceptual application/infrastructure responsibilities:

- `IVideoGenerationProvider`: validate semantic capabilities, submit, and retrieve job state for either execution location.
- `IProviderExecutionEnvironmentProbe`: report readiness, versions, installed dependencies, hardware, and benchmark status without changing project state.
- `IProviderAssetPreparationService`: turn materialized logical media into ephemeral provider inputs; for ComfyUI this means validated input staging rather than remote upload.
- `IProviderOutputAcquirer`: return a readable output stream or verified local-file lease. This avoids forcing local output through the remote-provider assumption of a public HTTPS URL.
- `IProviderExecutionPolicy`: describe whether a run requires paid confirmation, license acknowledgement, resource warning, or no prompt. The current paid authorization should evolve into a provider-neutral execution authorization rather than giving local providers a fake billing token.
- Adapter-owned workflow registry: pin official workflow source/version, API-format graph, semantic binding map, expected node classes, and digest.

Core should not understand `ExecutionKind`, ComfyUI URLs, node IDs, workflow JSON, GPU types, file staging, sampler names, model filenames, or HTTP/WebSocket messages. Historical reproducibility can retain a sanitized provider execution receipt—workflow digest, ComfyUI/model versions and hashes, normalized realized dimensions/frames, seed/steps/sampler, and backend—without making those values domain invariants or authoritative source references.

Existing neutral modes map cleanly: T2V remains `TextToVideo`; one/two-image FL2VA remains `ImageToVideo` with start/end-frame roles; Ref2VA remains `ReferenceToVideo`. H3's reference tags are derived from snapshot order, not stored as provider-specific prompt tokens.

## License and territory gate

MiniMax H3 is not under a permissive open-source license. The MiniMax H3 Community License dated August 2, 2026 defines the applicable territory as worldwide **excluding the European Union, United Kingdom, Republic of Korea, and United States**. It says the H3 works and their outputs may not be used, displayed, or distributed outside the applicable territory without a separate license. This is a release blocker for broad distribution, even if the software can technically run there.

Other important terms include:

- commercial products/services over USD 20 million annual revenue require prior written MiniMax authorization;
- a commercial product using H3 must prominently display "MiniMax H3" in its UI;
- downstream access must be governed by protective terms and reasonable safeguards;
- H3 outputs may not be used to improve another AI model, except H3 or its derivatives;
- public machine-generated content has disclosure obligations in the Acceptable Use Policy;
- MiniMax claims no ownership in generated outputs, but users remain responsible for them;
- the Qwen3-VL-32B encoder has its own Apache 2.0 notice;
- ComfyUI itself is GPL-3.0. Communicating with a separately installed process over localhost is architecturally preferable to embedding or redistributing ComfyUI, but distribution strategy still requires license review.

ReelForge should therefore require an explicit H3 license/territory acknowledgement before enabling the provider, keep it disabled when the user cannot attest eligibility, display the required model attribution for commercial use, and avoid bundling weights. This documentation is an engineering assessment, not legal advice.

## Staged future decision

1. **Documentation only (now):** retain this research and the provider-neutral design; download nothing.
2. **Read-only probe:** detect an existing user-managed ComfyUI server, hardware, installed H3 nodes/models, disk capacity, and license eligibility. No workflow submission.
3. **Benchmark gate:** user explicitly installs the minimal official FL2VA files and runs a clearly labeled local T2V benchmark. Record performance; do not enable by default.
4. **Local T2V:** implement pinned official API workflow submission, progress, cancellation semantics, and durable output ingestion.
5. **FL2VA:** add first/last-frame staging from logical image/anchor references.
6. **Ref2VA:** separately authorize/download its diffusion model, then add verified image/video/audio staging and limits.
7. **Hybrid/2K only if desired:** treat hosted Context-IR and Regenerate-2K as a separate paid network execution profile, never as part of the offline-local claim.

The project should defer steps 2-7 until the target hardware is known and the territory/distribution strategy is acceptable.

## Primary sources

- [ComfyUI: MiniMax H3 native workflow examples](https://docs.comfy.org/tutorials/video/minimax/minimax-h3)
- [ComfyUI Server API routes](https://docs.comfy.org/development/comfyui-server/comms_routes)
- [ComfyUI Server API examples](https://docs.comfy.org/development/comfyui-server/api-examples)
- [MiniMax H3 official repository and model card](https://github.com/MiniMax-AI/MiniMax-H3)
- [MiniMax H3 official Hugging Face repository](https://huggingface.co/MiniMaxAI/MiniMax-H3)
- [Comfy-Org H3 repack and workflow models](https://huggingface.co/Comfy-Org/MiniMax-H3)
- [MiniMax H3 Community License](https://huggingface.co/MiniMaxAI/MiniMax-H3/blob/main/LICENSE)
- [Official ComfyUI I2V workflow](https://github.com/Comfy-Org/workflow_templates/blob/main/templates/video_minimax_h3_i2v.json)
- [Official ComfyUI T2V workflow](https://github.com/Comfy-Org/workflow_templates/blob/main/templates/video_minimax_h3_t2v.json)
- [Official ComfyUI R2V workflow](https://github.com/Comfy-Org/workflow_templates/blob/main/templates/video_minimax_h3_r2v.json)
