# Gate 0 durable artifact retention

Status: private R2 credentialed write/read smoke passed; complete corpus migration remains incomplete; G0.5 pre-matrix smoke remains blocked

Authority: owner durable-retention direction dated 2026-08-26 and the [Gate 0 media capability charter](gate-0-media-capability-charter.md)

## Boundary

The dedicated private Cloudflare R2 bucket `reelforge-artifacts` is the durable engineering copy of the curated Gate 0 proof corpus. It has no automatic object-deletion lifecycle. It is separate from temporary provider-reference hosting and is not an application feature, general artifact service, production release store, user-media store, or public distribution surface.

The existing `eng/gate0/artifact-retention-manifest.json` remains the canonical local byte inventory. It currently identifies 3,967 logical artifacts and 996,614,118 bytes that were already curated into the local `ReelForge.Gate0Artifacts` working root; unmanifested staging and scratch files are excluded. This includes every retained-audio evaluator attempt, the owner-approved 90,000-marker atlas, all marker-qualifier attempts, the superseded WPF no-media attempt, and the authoritative clean-build WPF no-media control. `eng/gate0/artifact-manifest.json` separately records durable R2 verification status and inherits each artifact's provenance, producer/runtime, proof identity, and license records from that source inventory.

R2 object identity is always:

```text
objects/sha256/<first-two-lowercase-hex>/<full-lowercase-sha256>
```

Logical names remain manifest metadata. Upload tooling never uses `latest`. A missing-object HEAD is followed by a signed create-only `If-None-Match: *` PUT, so a concurrent creator cannot be overwritten; either outcome is followed by retrieval and byte verification. HEAD, ETag, or an upload response alone never establishes retention success.

## Credential contract

R2 operations read only from three dedicated Windows Credential Manager **Generic Credentials**:

| Secret name | Value |
| --- | --- |
| `ReelForge.Engineering.R2.AccountId` | 32-character Cloudflare account ID |
| `ReelForge.Engineering.R2.AccessKeyId` | Bucket-scoped R2 S3 Access Key ID |
| `ReelForge.Engineering.R2.SecretAccessKey` | Bucket-scoped R2 S3 Secret Access Key |

The engineering targets are read by exact name and do not use ReelForge's application credential prefix. The tooling does not read AWS credential environment variables, application settings, application-owned Credential Manager entries, or the temporary-provider R2 configuration. Credentials, authorization headers, signed queries, and endpoint URLs are never written to either manifest or normal command output.

Create the three entries with Windows Credential Manager's **Add a generic credential** action. Set each Internet or network address to the exact secret name above; the user-name field is not used by the tooling, and the password field contains the corresponding value. No PowerShell module or additional software is required.

Use a token scoped only to object read/write for `reelforge-artifacts`. Do not reuse temporary-provider or future CI credentials.

## Tooling

Offline structural validation is safe for normal CI and requires no credentials, local corpus, or network:

```powershell
./eng/gate0/Test-Gate0ArtifactManifest.ps1
```

Local byte validation uses the explicit root, `REELFORGE_GATE0_ARTIFACT_ROOT`, or the repository-sibling default:

```powershell
./eng/gate0/Test-Gate0ArtifactManifest.ps1 -Local
```

Upload one artifact or every currently pending artifact. Every successful record includes a post-upload/reuse download and byte verification:

```powershell
./eng/gate0/Upload-Gate0Artifact.ps1 -ArtifactId '<logical-artifact-id>'
./eng/gate0/Upload-Gate0Artifact.ps1 -AllPending
```

Perform a separate complete remote read and update tracked verification receipts:

```powershell
./eng/gate0/Test-Gate0ArtifactManifest.ps1 -Remote -UpdateManifest
```

Retrieve one already verified object to a new absolute destination:

```powershell
./eng/gate0/Get-Gate0Artifact.ps1 -ArtifactId '<logical-artifact-id>' -DestinationPath 'C:\approved\new-file.bin'
```

When the local source inventory changes, `-RefreshSourceInventory` adopts its new manifest hash only if every previously verified logical artifact still has the same size, SHA-256, and object key. It resets the overall retention condition to incomplete until new artifacts are remotely verified.

Receipt and source-refresh writes take a machine-wide, manifest-specific mutex, reload and revalidate the current ledger while holding that lock, then atomically replace the JSON file. Concurrent operators may perform redundant immutable-object verification, but they cannot overwrite the object or silently discard one another's receipts. The reader enforces a closed manifest schema and derives completion, counts, and byte totals from the verified receipt set; hand-edited status flags cannot establish the second-copy gate.

## Credentialed R2 smoke result

On 2026-08-26, the exact 8-byte retained artifact `Gate0.G04.P3.JpegInput.20260825/superseded-initial-harness/logs/inspect-orientation-6.stdout.txt` was locally verified, create-only uploaded, retrieved, and verified against SHA-256 `5E6510D6F9B52E78BE1A51958964211463800E000E3CE278DDEC2480E2A405DC`. A separate remote validation then repeated the HEAD, retrieval, size, and SHA-256 checks successfully. The durable ledger records the content-addressed object at `objects/sha256/5e/5e6510d6f9b52e78be1a51958964211463800e000e3ce278ddec2480e2a405dc` without storing credentials or an endpoint.

This proves the configured engineering identity can execute the intended immutable write/read path. It does not satisfy the second-copy prerequisite: 1 of 3,967 logical artifacts is remotely verified, and the pre-matrix smoke remains blocked until the complete current inventory passes the same process.

## CI and completion

Normal pull-request CI may parse and validate the manifests and compile/test the proof-only signer. It receives no R2 credential and makes no network call. Hosted CI does not currently read or write the bucket. A later deliberate read-only CI identity is separate from the local engineering write identity.

The private second-copy prerequisite becomes complete only when every artifact in the current source inventory has a `remote-verified` record produced by a retrieved byte stream with the expected size and SHA-256. Until then, G0.5 pre-matrix smoke and measured Stage 2 execution remain blocked.
