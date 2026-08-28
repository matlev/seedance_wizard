# Gate 0 G0.5 Stage 2A V2 producer-identity recovery approval

Date: 2026-08-27

Status: owner approved; one no-media correction and retention-only append authorized

## Approved correction

The producer runtime must be recorded as one exact paired identity:

- `repository:eng/gate0/manifests/p2-btbn-lgplv3-shared-windows-x64-20260820.json`
- `sha256:3ECF44CF6D5A878C69A0BD18C9C44777EFE8BD990C04C4F371383D2FAF1E1640`

Validation must require the exact repository-relative forward-slash path, prohibit rooted, traversal, URL, backslash, and machine-local forms, require the file to exist, bind its exact SHA-256, and confirm that the parsed manifest identifies the approved P2 profile. The runtime manifest remains byte-immutable.

The exact canonical compact projected shard bytes must pass the ordinary V2 reader before append-journal creation, transaction or destination creation, credential resolution, any R2 request, retained-payload or shard creation, or root mutation. Invalid producer metadata must leave all of those side effects absent. Positive coverage must use the real 23-artifact/six-attempt shape and the approved identity pair.

The correction is no-media. It must pass the complete focused suite, receive independent review, and refresh all affected implementation and authorization hashes before retention resumes.

## Authorized retention-only append

After the reviewed correction is committed and hash-bound, one retention-only append is authorized for the exact preserved replacement root:

- proof ID `g05-stage2a-continuation-r1-20260827-stress-720p-webm-eight`;
- cell ID `stress-720p-webm-eight`;
- 23 files;
- 15,746,570 bytes;
- inventory SHA-256 `253132EAD84D10D3D70072AF7B2C24384325A6A1F1E4C5E2349D112C5D632851`;
- cell-summary SHA-256 `085B16E8C1DF839F2600B35517FB226BEBA70EA542F6B6E2A4FC29FB364A79CC`;
- attempt-binding SHA-256 `FA9248FDB2F4BC3E27D2E355BC8CDD100F1F61562FFEA51F4F3A5FC2B22C5373`; and
- consumed-preflight SHA-256 `0857E89731800FD1B33A037D9877A866AA22A770484367BB5943EECCA135A50F`.

The source bytes, frozen cell summary, attempt bindings, preflight, proof/cell identities, and six passed dispositions must not be edited, regenerated, repaired, or reinterpreted. FFmpeg and ffprobe must not run.

The append must semantically validate before side effects, independently retrieve and byte-verify all 23 selected R2 objects, atomically retain the payload/shard/root entry, validate the complete local and R2 closure, and prove historical evidence and the previous V2 root remained immutable. Existing unindexed content-addressed R2 objects must not be deleted and may be reused only after independent byte verification.

Any mismatch or validation/append failure stops the unit with the exact blocked state preserved. It authorizes no repair, rerun, quarantine, or next cell.

## Approved accounting

Before a successful append:

- V1 remains 108 authoritative records, 38 physical executions, and 70 blocked without media;
- earlier non-authoritative harness work remains seven physical executions;
- earlier non-authoritative continuation work remains 12 physical executions;
- the latest replacement contributes six physical executions;
- cumulative physical executions are 63; and
- authoritative continuation is zero of 72.

After a successful retention-only append, authoritative continuation becomes six of 72 and cumulative physical executions remain 63. Retention, R2 verification, shard creation, and blocked-without-media records are not physical media executions.

## Completion and boundaries

The result packet and `docs/gate-0-current-status.md` update are mandatory before the next owner request. The packet must include the correction hashes and commit, focused tests, independent review, root revalidation, identity validation, pre-side-effect proof, shard/root/retention/R2 results, growth/headroom, accounting, historical immutability, and a recommendation about the remaining 66 attempts.

This approval authorizes no additional media execution, continuation cell, remaining continuation work, Stage 2B, concurrency comparison, long-form work, playback installation, product integration, runtime adoption, shipping selection, distribution, licensing, patent, or legal conclusion.
