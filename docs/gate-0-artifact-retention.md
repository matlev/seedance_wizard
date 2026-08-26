# Gate 0 durable artifact retention

Status: private R2 target approved; credential configuration and corpus migration incomplete; Stage 2 execution remains blocked

Authority: owner durable-retention direction dated 2026-08-26 and the [Gate 0 media capability charter](gate-0-media-capability-charter.md)

## Boundary

The dedicated private Cloudflare R2 bucket `reelforge-artifacts` is the durable engineering copy of the curated Gate 0 proof corpus. It has no automatic object-deletion lifecycle. It is separate from temporary provider-reference hosting and is not an application feature, general artifact service, production release store, user-media store, or public distribution surface.

The existing `eng/gate0/artifact-retention-manifest.json` remains the canonical local byte inventory. It currently identifies 3,007 logical artifacts and 517,763,820 bytes that were already curated into the local `ReelForge.Gate0Artifacts` working root; unmanifested staging and scratch files are excluded. `eng/gate0/artifact-manifest.json` separately records durable R2 verification status and inherits each artifact's provenance, producer/runtime, proof identity, and license records from that source inventory.

R2 object identity is always:

```text
objects/sha256/<first-two-lowercase-hex>/<full-lowercase-sha256>
```

Logical names remain manifest metadata. Upload tooling never uses `latest`. A missing-object HEAD is followed by a signed create-only `If-None-Match: *` PUT, so a concurrent creator cannot be overwritten; either outcome is followed by retrieval and byte verification. HEAD, ETag, or an upload response alone never establishes retention success.

## Credential contract

R2 operations read only from the Microsoft PowerShell SecretStore vault `ReelForgeEngineering`:

| Secret name | Value |
| --- | --- |
| `ReelForge.Engineering.R2.AccountId` | 32-character Cloudflare account ID |
| `ReelForge.Engineering.R2.AccessKeyId` | Bucket-scoped R2 S3 Access Key ID |
| `ReelForge.Engineering.R2.SecretAccessKey` | Bucket-scoped R2 S3 Secret Access Key |

The tooling does not read AWS credential environment variables, application settings, Windows Credential Manager entries used by ReelForge, or the temporary-provider R2 configuration. Credentials, authorization headers, signed queries, and endpoint URLs are never written to either manifest or normal command output.

The required `Microsoft.PowerShell.SecretManagement` and `Microsoft.PowerShell.SecretStore` modules are intentionally not installed by the repository scripts. After those modules are available, register the vault and enter values interactively:

```powershell
Register-SecretVault -Name ReelForgeEngineering -ModuleName Microsoft.PowerShell.SecretStore
Set-Secret -Vault ReelForgeEngineering -Name ReelForge.Engineering.R2.AccountId -Secret (Read-Host 'Cloudflare account ID' -AsSecureString)
Set-Secret -Vault ReelForgeEngineering -Name ReelForge.Engineering.R2.AccessKeyId -Secret (Read-Host 'R2 Access Key ID' -AsSecureString)
Set-Secret -Vault ReelForgeEngineering -Name ReelForge.Engineering.R2.SecretAccessKey -Secret (Read-Host 'R2 Secret Access Key' -AsSecureString)
```

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

## CI and completion

Normal pull-request CI may parse and validate the manifests and compile/test the proof-only signer. It receives no R2 credential and makes no network call. Hosted CI does not currently read or write the bucket. A later deliberate read-only CI identity is separate from the local engineering write identity.

The private second-copy prerequisite becomes complete only when every artifact in the current source inventory has a `remote-verified` record produced by a retrieved byte stream with the expected size and SHA-256. Until then, G0.5 pre-matrix smoke and measured Stage 2 execution remain blocked.
