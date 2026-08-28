# ReelForge media-runtime engineering baseline

`baseline-profile.json` is a development-baseline candidate derived from the pinned P2 BtbN LGPLv3-path build. It is not a shipping-runtime, distribution, patent, or legal approval.

`Validate-MediaRuntime.ps1` is offline and credential-free in static mode. Add `-Live -RuntimeRoot <root>` only to verify the exact ffmpeg/ffprobe pair and configuration against this profile.

`Invoke-MediaSmokeTests.ps1` is an explicit local engineering command. Its smoke families use generated temporary fixtures, strict inspection/decode, small semantic assertions for stream shape/duration/dimensions/content changes, WebM cues, and the explicitly pinned font files under `fonts/`; it never downloads, installs, calls R2, or references product assemblies. It deletes temporary output by default; use `-KeepArtifacts` only when investigating a result.

The conditional MP4 route is technical evidence only. WebM VP9/Opus is the open delivery path. Forbidden baseline components include GPL/nonfree configuration, libx264, libx265, libvidstab, librubberband, `eq`, and `hqdn3d`.
