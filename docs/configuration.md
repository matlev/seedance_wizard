# ReelForge application configuration

ReelForge keeps machine/application configuration separate from portable `.rfp` projects. Project files may identify providers, models, logical references, and remote job IDs, but they never contain API keys, R2 credentials, local application paths, or presigned URLs.

## Files and precedence

Configuration is loaded in this order:

1. built-in model defaults;
2. checked-in `src/ReelForge.App/appsettings.json` structure and defaults;
3. `%LOCALAPPDATA%\ReelForge\appsettings.local.json` machine/user overrides.

The local file wins property by property and is gitignored. It is ordinary, editable JSON. The checked-in file is also a configuration map: it names every current non-secret setting, every required secret, and the exact Windows Credential Manager key used for that secret.

The old `%LOCALAPPDATA%\ReelForge\settings.json` FFmpeg/ffprobe file is read as a one-time compatibility source when no local application settings file exists. New edits are written to `appsettings.local.json`.

## Settings window and auto-save

Use **Settings** in the top application toolbar. Categories currently cover General, Media Tools, Cloudflare R2 temporary hosting, BytePlus, and AtlasCloud. Enabling AtlasCloud exposes both its Seedance 2.5 and MiniMax H3 routes; they share one AtlasCloud endpoint and credential.

General settings include **Undo Send**, an integer slider from 0 through 30 seconds. Zero is displayed as **Send Immediately** and preserves immediate submission. A positive value creates a local, cancellable Jobs entry after the user confirms the request; ReelForge does not prepare references, upload media, or contact the generation provider until that entry's captured countdown expires. Changing the setting later affects only subsequently queued generations. The queued entry retains its owning project path and immutable generation ID, so New/Open and project switching remain available throughout the countdown. At expiry, submission uses the active workspace only if it still owns that project; otherwise ReelForge opens the owning `.rfp` through an isolated submission workspace.

General settings also expose **Log location**. Its default remains `%LOCALAPPDATA%\ReelForge\Logs`. When the location changes and the old folder contains ReelForge JSONL logs, Settings asks whether those files should move to the new folder. Choosing No intentionally leaves them behind; choosing Yes moves them without overwriting a same-named destination log. New diagnostics use the updated folder immediately.

Media Tools includes a **Media cache limit**, displayed in selectable megabytes or gigabytes and stored as canonical bytes. The default is 10 GB. ReelForge removes the least-recently-used disposable derivatives when the cache exceeds the configured limit while protecting files that an active operation has leased. Reducing the limit applies immediately after Settings closes. A lower limit saves disk space, but may force repeated materialization and can make some video-editing operations perform poorly or become impossible.

ReelForge also stores the last successfully created or manually opened project file in the machine-local settings file. On the next launch it reopens that project automatically. If the file has been moved, deleted, or is temporarily unavailable, startup continues without a project and prompts the user through the status bar to use **Open**; the unavailable path is retained until another project is opened.

Non-secret fields show their actual merged value. Edits are marked dirty and written only when an edit is committed: keyboard focus leaves the field, another category is selected, the window is minimized, or the window closes. ReelForge does not rewrite the file on every keystroke and skips writes when nothing changed. A failed write leaves the edited value in the open window and displays the error.

FFmpeg and ffprobe may be discovered from `PATH` or selected explicitly. Media-tool, R2, provider enablement, provider base-URL, and credential changes take effect after the Settings window closes. A newly configured provider runtime handles new submissions; an operation already in flight retains the provider instance and endpoint with which it started.

## Secrets and Windows Credential Manager

The Settings UI asks `ISecretStore` only whether a credential exists. Existing plaintext is never returned to or bound into the Settings window. A configured field always displays exactly `*****`, regardless of secret length.

**Replace** opens an empty password control. After a successful Credential Manager write, the control is cleared and returns to `*****`. **Remove** is a separate confirmed action; clearing or cancelling a replacement never deletes a credential.

Credential Manager targets are:

| Service | Requirement | Target name |
|---|---|---|
| Cloudflare R2 | Access Key ID | `ReelForge:cloudflare.r2.access-key-id` |
| Cloudflare R2 | Secret Access Key | `ReelForge:cloudflare.r2.secret-access-key` |
| BytePlus ModelArk | API key | `ReelForge:byteplus.modelark.api-key` |
| AtlasCloud | API key | `ReelForge:atlascloud.api-key` |

`appsettings.json` and locally saved settings contain only `<MANAGED BY WINDOWS CREDENTIAL MANAGER>` for secret-value properties. ReelForge normalizes those properties back to that marker while loading/saving and never interprets it as a credential. Do not put real secret values in either JSON file.

## Cloudflare R2 requirements

ReelForge uses a private R2 bucket through Cloudflare's S3-compatible endpoint. Configure:

- Account ID;
- bucket name;
- HTTPS account endpoint, normally `https://<ACCOUNT_ID>.r2.cloudflarestorage.com`;
- R2 Access Key ID;
- R2 Secret Access Key;
- presigned read-URL lifetime (1 second through 7 days; the UI stores minutes).

Use an R2 API token scoped to the intended bucket with object read/write permission. Public bucket access and a custom public domain are not required. Cloudflare documents region `auto`, AWS Signature Version 4, and presigned URLs on the S3 API domain. See the official [S3 setup](https://developers.cloudflare.com/r2/get-started/s3/), [.NET SDK notes](https://developers.cloudflare.com/r2/examples/aws/aws-sdk-net/), [authentication](https://developers.cloudflare.com/r2/api/tokens/), and [presigned URL security guidance](https://developers.cloudflare.com/r2/api/s3/presigned-urls/).

**Test R2 Connection** is an explicit human-triggered, read-only signed bucket probe. It does not upload a test object. No connection test runs at startup or in automated tests.

## Temporary reference hosting

For a local reference required over HTTPS, generation preparation follows:

```text
logical asset / immutable recipe revision / frame anchor
  -> materialized local representation
  -> verified SHA-256
  -> ITemporaryAssetHost
  -> private R2 object
  -> short-lived presigned HTTPS GET URL
  -> provider request override
```

R2 keys are content-addressed as `references/sha256/<first-two-hex>/<sha256>.<extension>`. The host performs `HEAD` on that deterministic key and uploads only when it is absent. Reusing identical bytes generates a new presigned GET URL without uploading again. Cloudflare lifecycle rules may delete old objects; a missing object is regenerated from authoritative project media on the next use.

The signed URL is a transient bearer token. It is never written into project history, application settings, hosted-object metadata, or diagnostic errors. Receipts may retain the hosting provider, content hash, object key, and expiration time. The original logical reference remains the authoritative provenance.

The current materializer can execute this flow for physical project assets. The host and BytePlus preparation accept materialized virtual-asset revisions and frame anchors without changing provenance; their actual FFmpeg materializers remain scheduled for Milestone 2D and 2C respectively.

## Provider requirements

- **BytePlus:** enabled flag, HTTPS API base URL, and ModelArk API key. Local references are sent through R2 when configured; the adapter remains independent of Cloudflare and receives only the temporary HTTPS representation.
- **AtlasCloud:** enabled flag, HTTPS API base URL, and API key shared by the selectable Seedance 2.5 and MiniMax H3 routes. Both use AtlasCloud's multipart asset preparation independently of R2.

Configuration status means required values exist, not that they are valid. Only explicit test or generation actions may make external calls. Every real video generation still requires the human to click Generate and accept a fresh potentially-billable confirmation.

Closing ReelForge during an Undo Send window safely cancels the unsent entry on the next launch rather than silently submitting it. The cancelled entry remains in Jobs under the same viewed-then-leave acknowledgement rule as other terminal jobs.

## Diagnostic logs

ReelForge writes verbose AtlasCloud HTTP failure diagnostics to daily newline-delimited JSON files under the configured log location, which defaults to `%LOCALAPPDATA%\ReelForge\Logs`. A failed generation's inspector shows the exact log file and event ID, allowing the detailed entry to be correlated without displaying the full provider response in the normal GUI. Logs include the operation, provider ID, HTTP status, provider code, sanitized request payload, response body, and parsing exception where applicable.

Authorization headers and API keys are never logged. Inline Base64 media is replaced with a size marker, and query strings and fragments are removed from URLs before persistence. Logs can still contain prompts, filenames, provider messages, and other project-related context needed for diagnosis; treat the log directory as private user data when sharing reports.

## Safe testing

Automated tests use local files, in-memory settings/secrets, fake temporary-host clients, and mocked HTTP handlers. They cannot access a real R2 bucket or submit BytePlus/AtlasCloud jobs. The fixture-backed acceptance tests serve a committed ten-second MP4 only through a strict in-memory HTTP handler; an unexpected URL throws before any network request can be made. These tests cover reference preparation and immutable history, successful output ingestion and persistence, retries, restart reconciliation into the owning project while another project is open, and output-download failure cleanup.

Manual verification consists of configuring the values in Settings, using **Test R2 Connection**, and—only when intentionally spending money—using the application's confirmed Generate action. Actual WPF media playback and machine-specific codec behavior remain manual smoke tests because the automated suite validates durable media bytes and metadata without opening a desktop media session.
