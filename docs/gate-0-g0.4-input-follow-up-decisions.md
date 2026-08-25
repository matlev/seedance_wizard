# Gate 0 G0.4 common-input follow-up decisions

Status: bounded F7 experiment executed and blocked; Matroska pilot remains blocked; P3 proof remains authorized

Approved: 2026-08-25

Authority: [Gate 0 G0.4 common-input proof results](gate-0-g0.4-input-proof-results.md) and [common-input owner decisions](gate-0-g0.4-input-owner-decisions.md)

## F7 fixture correction

One bounded correction to the F7 fixture-authoring recipe is authorized. It must preserve:

- exactly five frame identities;
- PTS `1000,1040,1120,1130,1200`;
- intervals `40,80,10,70`;
- a signed non-zero start;
- time base `1/1000`;
- an 800-tick terminal video-frame duration; and
- video presentation end at tick 2000.

Only already approved components may execute. The correction may not add a sentinel frame, change the contract, add a component, or weaken an oracle. One bounded re-authoring attempt and correction of a clearly identified script defect are allowed. If the semantics still cannot be represented, execution stops and returns a narrower proposal.

## Direct Matroska fixture production

Direct Matroska fixture production is authorized using only the exact previously approved fixture-producing encoders, audio components, and Matroska muxer recorded in the result proposal. Native P2 decoders remain the capabilities under test.

Before expanding across failed Matroska rows, a representative pilot must contain at least:

- H.264/AAC CFR;
- H.264/PCM;
- VP9/Opus;
- VP8/Vorbis;
- one video-only case; and
- one corrected F7 VFR/non-zero-PTS case.

Each pilot row binds raw authored truth, exact producer/runtime identity, exact command, artifact hash, timing evidence, and a fresh strict complete-decode result. Expansion is permitted only if the pilot succeeds without changing the approved semantics or oracles.

If direct production cannot preserve the approved semantics, the existing 32 passed Matroska rows remain the truthful guaranteed subset and the remaining rows stay failed or capability-qualified. No fixture producer becomes a shipping dependency, export capability, portable-runtime requirement, or public-distribution approval.

## Deterministic JPEG producer

`P3.LibjpegTurboCjpeg.WindowsX64.3.2.0` is authorized as third-party fixture-production infrastructure only.

Before execution, the proof must:

- acquire only the exact approved official release asset;
- verify its expected SHA-256 and available digital signature;
- record and hash the complete executable/DLL closure actually used;
- retain official source/release provenance; and
- retain applicable IJG and Modified BSD license materials.

`cjpeg` may author only the exact progressive 4:2:0 JPEG fixture. A small repository-owned deterministic APP1/EXIF writer may add only the orientation tag to a separately hash-pinned JPEG. P2 native `mjpeg` remains the decoder under test.

If the producer cannot be acquired and preserved reproducibly, progressive JPEG and EXIF-orientation stay capability-qualified. No alternate tool may be substituted silently. This approval does not select libjpeg-turbo as a shipping dependency, bundled component, image encoder, or public-distribution route.

## Durable evidence retention

Before unattended CI or later Gate 0 work depends on the corpus, project-controlled durable artifact storage must retain:

- the exact approved P2 runtime archive;
- all producer-sensitive fixture bytes;
- corrected retained evidence;
- fixture and contract manifests;
- exact commands and hashes;
- environment and producer identities; and
- applicable license and provenance records.

The current temporary local path is not durable retention. A configured upload target and successful verified preservation are required; a local copy or documented intent is insufficient.

## Retained boundary

These approvals authorize fixture production and proof correction only. They do not change current import behavior, persistence, render commands, the guaranteed-common contract, shipping-runtime selection, public-distribution policy, or legal conclusions.

The existing 171 passed rows remain valid evidence. Every failed or blocked row retains that disposition until fresh evidence executed under this record changes it.

## F7 `setts` experiment amendment

One narrow proof-only use of FFmpeg's `setts` bitstream filter is approved after the first bounded re-authoring attempt showed that the approved concat/setpts components could not carry the terminal duration through both direct encoders.

Before execution, the proof must verify `setts` presence in exact P2, record its component/source/license disposition, and add it only to the experiment's proof-runtime mapping. The experiment preserves the five identities, exact PTS and DTS behavior, `1/1000` time base, signed non-zero start, every non-terminal packet duration, and encoded payload/frame identity. It must identify the terminal presentation packet robustly rather than assuming packet order equals presentation order. Only that packet may receive duration 800 and end at tick 2000.

Direct MP4 and WebM F7 cases execute first. The direct-Matroska pilot resumes only if they pass. A muxer rewrite, sentinel requirement, new component requirement, or semantic change stops the experiment with a blocked result. `setts` remains fixture-authoring proof infrastructure and does not enter product behavior, shipping-runtime selection, or the public media contract.

The experiment is complete with a blocked result. The first direct MP4 case preserved packet payloads but rewrote every video PTS to its DTS, so the unique PTS-1200 packet was not assigned the required terminal duration. The approved stop condition prevented every remaining direct case and the Matroska pilot from running. See the [F7 `setts` experiment results](gate-0-g0.4-f7-setts-results.md). No expression variant or other continuation is authorized by this record.

## Interim local retention amendment

No external artifact backend will be configured during this unit. A project-controlled sibling directory named `ReelForge.Gate0Artifacts` may retain the corpus outside temporary storage. The owner's OneDrive client is intentionally disconnected, so this directory counts as one local copy only and must not be described as synced or backed up. A genuinely separate private copy remains required before the two-copy retention condition is complete. This is interim local retention, not a production artifact repository.

The tracked repository manifest must use stable artifact IDs and relative filenames only, and record size, SHA-256, provenance, producer/runtime identity, license records, proof-run identity, and retention status. Heavy proof stays manual or opt-in, hosted CI may not depend on machine-local artifacts, and temporary-provider R2 remains prohibited for this corpus.

## P3 cleanup amendment

The temporary libjpeg-turbo installation may be removed only after both JPEG proofs succeed and the exact installer, executable/DLL hashes, release/source provenance, license materials, generated fixtures, producer manifests, and final evidence have been retained and verified against the tracked manifest.
