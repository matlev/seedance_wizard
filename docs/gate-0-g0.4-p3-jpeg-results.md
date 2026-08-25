# Gate 0 G0.4 P3 JPEG proof results

Status: completed; both authorized rows passed; retained locally; second private copy remains open

Date: 2026-08-25

Authority: [G0.4 common-input follow-up decisions](gate-0-g0.4-input-follow-up-decisions.md)

## Outcome

The exact retained `P3.LibjpegTurboCjpeg.WindowsX64.3.2.0` producer closure authored the bounded progressive 4:2:0 fixture, and the repository-owned byte writer authored the bounded EXIF-orientation fixture. The exact retained `P2.BtbnLgplShared.WindowsX64.20260820` native `mjpeg` decoder then passed both approved semantic rows.

This changes the corrected 256-row common-input result from 171 passed, 83 failed, and 2 blocked to **173 passed, 83 failed, and 0 blocked**. It does not select libjpeg-turbo as a ReelForge dependency, image encoder, shipping runtime, bundled component, redistribution route, or public-distribution approval.

| Evidence | Result |
|---|---|
| Proof | `Gate0.G04.P3.JpegInput.20260825` |
| Exact producer | libjpeg-turbo `3.2.0` build `20260630`; retained `cjpeg.exe` and `jpeg62.dll` closure |
| Runtime under test | exact P2 native `mjpeg` through explicit `image2` inspection and strict decode |
| Validated run | 2 passed; 0 failed; 0 blocked |
| Validated evidence SHA-256 | `679F5C79CFBA9C5FEBC3DB70714D867D7E9FBD8305F3129663C13A1E2FD88F45` |
| Retained group | `Gate0.G04.P3.JpegInput.20260825` |
| Retained path | `proofs/p3-jpeg-20260825` |
| Retained group closure | 53 files; 1,019,791 bytes |
| Corpus manifest after append | `8FC6FF0C427BF345EE54AD0198F85B6890356A12548C9D6A912C57EC9E937785` |

## Row evidence

### Progressive 4:2:0

`I-JPEG-PROGRESSIVE-420` passed with output SHA-256 `F9F34A9F0651066BFAD6646AB1E601FAFBFFBDDEBDB6A9FED45725857B5035F2`.

- `cjpeg` used the exact approved arguments `-quality 90 -dct int -progressive -sample 2x2,1x1,1x1`.
- The retained SOF parser found exactly one C2 marker, 8-bit precision, 320x180 geometry, and Y 2x2 / Cb 1x1 / Cr 1x1 sampling.
- FFprobe recorded exactly one `mjpeg` stream, one frame, one packet, and packet-data SHA-256 equal to the fixture bytes.
- Strict native P2 decode produced the expected 320x180 RGB24 raster with mean absolute error `1.6824479166666666`, below the approved maximum of `18`.

### EXIF orientation

`I-JPEG-EXIF-ORIENTATION` passed with output SHA-256 `A020131E12BD3E7A2210916FB8F24B2B261687320A52F6A5A75813CF82138CD7`.

- The repository writer inserted one 36-byte APP1 segment immediately after SOI, containing only the little-endian TIFF orientation tag with value 6.
- Every baseline byte after SOI remained byte-identical.
- FFprobe recorded exactly one `mjpeg` stream, one frame, one packet, and packet-data SHA-256 equal to the fixture bytes.
- Native no-autorotate decode was byte-identical to the separately pinned baseline raster.
- Native autorotate decode was exactly the expected 90-degree clockwise 180x320 raster.

## Superseded harness runs

Two earlier runs are retained as superseded evidence rather than erased:

1. Evidence SHA-256 `5A8DB2AB23C3B7AAD1EFF0A8A967AA0C1F3583FBF0F644A4888F247793695CBC` stopped on two PowerShell harness defects: single-result dictionary indexing and collision with the automatic `$input` variable.
2. Evidence SHA-256 `33EF3F3E32F2CA16F962B84D6FA46991A2A101B91657A12FE354FFEC79DCD9FC` reached both images but did not normalize FFprobe 8.1's combined `packets_and_frames` output shape.

Each defect was independently reviewed, corrected without changing an approved media oracle, covered by focused tests, and committed before the next run. The final normalizer derives split arrays only when both are absent and rejects malformed hybrid output.

## Retention and cleanup

The approved sibling corpus now verifies as 7 groups, 2,617 files, and 454,662,191 bytes. It retains the exact P3 installer, executable/DLL closure, Authenticode record, release/source provenance, IJG and Modified BSD license materials, all three proof runs, generated JPEGs and decoded rasters, commands/logs, exact runtime identity, and repository snapshots.

This remains one local copy. The disconnected OneDrive path is not a backup, hosted CI must not depend on it, and the required separately backed-up private copy remains incomplete. No temporary P3 installation or retained producer data has been removed.
