# Gate 0 G0.5 Stage 2A V2 producer-identity block

Date: 2026-08-27

Status: replacement cell passed all six media attempts; authoritative retention failed closed after R2 object verification; owner decisions required

## Outcome

The owner-approved compact-V2 recovery completed through its exact execution boundary:

- deterministic compact shard serialization and the one-cell authorization were implemented and independently reviewed GO;
- 75 focused continuation and V2 containment tests passed;
- the prior malformed 23-file activation was reverified against every owner-approved identity and atomically quarantined under [the third quarantine receipt](../eng/gate0/g0.5-stage2a-continuation-quarantine-receipt-3.json);
- a fresh full local/R2, runtime, resource, corpus, and headroom preflight passed; and
- exactly one replacement cell ran under the fresh proof identity `g05-stage2a-continuation-r1-20260827-stress-720p-webm-eight`.

The warm-up and all five measured attempts passed their semantic checks. No second cell ran.

The V2 append then failed with `V2 producer identity is not portable and scoped.` The runner supplied the profile label `P2.BtbnLgplShared.WindowsX64.20260820`, while the V2 shard reader permits only portable `repository:` and `sha256:` identities. This is a runner-to-writer metadata defect, not a media-route failure.

## Exact preserved result

The current replacement root remains untouched at the fresh proof-relative staging path. Its identity is:

- 23 files;
- 15,746,570 bytes;
- deterministic path/size/SHA-256 inventory SHA-256 `253132EAD84D10D3D70072AF7B2C24384325A6A1F1E4C5E2349D112C5D632851`;
- cell-summary SHA-256 `085B16E8C1DF839F2600B35517FB226BEBA70EA542F6B6E2A4FC29FB364A79CC`;
- attempt-binding SHA-256 `FA9248FDB2F4BC3E27D2E355BC8CDD100F1F61562FFEA51F4F3A5FC2B22C5373`;
- consumed-preflight SHA-256 `0857E89731800FD1B33A037D9877A866AA22A770484367BB5943EECCA135A50F`;
- execution authorization SHA-256 `C1B4652921662E9017F730D014D1C06CFDF0B22EFBF4B7F6C87838A5954AE4FD`; and
- execution implementation commit `8879242`.

All six attempt dispositions are `passed`; all six started a media process. The measured wall-clock observations were 19,377, 18,965, 18,961, 18,885, and 18,902 milliseconds. Every attempt produced the same 632,098-byte output and the same maximum frame mean absolute error, approximately 1.18861. The frozen cell summary remains the authority for the complete semantic and performance evidence.

The retention failure occurred after the writer created its temporary transaction and independently retrieved and verified the cell's content-addressed R2 objects. Because the shard failed semantic validation, the writer removed its journal, temporary shard, and transaction-staging copy. It did not create the retained payload directory, immutable shard, or V2 root entry. The V2 root remains unchanged at SHA-256 `299B3F20D602602B9FD31A66CBFEF4330C0BC55C16BC4FE3F1FC0FD28D27A10E`, with two infrastructure runs and zero continuation runs.

The R2 bucket may now contain unindexed content-addressed objects uploaded or deduplicated during this failed transaction. They do not constitute an authoritative evidence mutation because no retained shard or root entry binds them. The next accepted append must independently retrieve and verify every selected object again.

No append journal, transaction-staging directory, retained payload, shard, or media process remains active.

## Safeguard gap

The compact-V2 approval required the projected shard to be parsed and validated through the ordinary V2 reader before journal or remote work. The implementation used the same compact serializer for projection and final bytes and enforced both caps before journal creation, but it did not run the projected bytes through the ordinary semantic reader. The focused test parsed the completed valid shard, so it did not expose this invalid live metadata value.

The correction therefore needs two parts:

1. The continuation runner must supply a portable, content-bound runtime identity. The recommended exact identity is:
   - `repository:eng/gate0/manifests/p2-btbn-lgplv3-shared-windows-x64-20260820.json`; and
   - `sha256:3ECF44CF6D5A878C69A0BD18C9C44777EFE8BD990C04C4F371383D2FAF1E1640`.
2. The V2 writer must parse and semantically validate the exact compact projected shard bytes through the ordinary reader before writing an append journal or contacting R2. Regression coverage must prove that an invalid producer identity fails before journal, transaction staging, destination, or credential/R2 access.

The fix must remain no-media, refresh every affected authorization hash, pass the focused suite, and receive independent review before retention resumes.

## Recommended recovery

Do not execute the media cell again. The current root is the fresh owner-approved replacement, has correct compact closure references, and passed all six semantic attempts. Another execution would add cost without addressing the metadata defect.

After the two no-media corrections above are implemented, tested, reviewed, committed, and hash-bound:

1. reverify the current root against the exact 23-file inventory and three named hashes above;
2. invoke only the V2 retention writer for the same proof ID, cell ID, attempt-binding document, and source bytes;
3. independently retrieve and verify all 23 selected R2 objects;
4. validate the accepted local and R2 V2 closure; and
5. stop after this one cell and return the result.

The retained preflight must remain unchanged because it truthfully binds the authorization and implementation under which the media was executed. The later retention-only authorization and implementation must be recorded separately; they must not rewrite the historical preflight or cell evidence.

If any staged byte differs, projected semantic validation fails, or post-append closure does not pass, stop and return the blocked result. Do not repair the staged evidence, rerun media, quarantine it, or advance to another cell without a new owner decision.

## Owner decisions required

1. Approve the exact portable runtime identity pair proposed above.
2. Approve ordinary-reader validation of the exact compact projected shard before journal or R2 work, with the stated regression boundary.
3. Authorize one no-media retention-only append of the exact preserved 23-file replacement root after the fix is reviewed and hash-bound. This does not authorize another media execution.
4. Approve the accounting below:
   - authoritative V1 remains 108 records: 38 physical executions and 70 blocked without media;
   - earlier non-authoritative harness work remains seven physical executions;
   - earlier non-authoritative continuation work remains 12 physical executions;
   - this replacement adds six physical executions, making 63 cumulative physical executions;
   - authoritative continuation remains 0 of 72 until the retention-only append passes; and
   - if that append passes, authoritative continuation becomes six of 72 while cumulative physical execution remains 63.

No remaining continuation cell, Stage 2B scenario, concurrency comparison, long-form run, playback installation, product integration, shipping-runtime selection, distribution action, or legal conclusion is authorized by these decisions.

## What remains after this block

If the retention-only append passes, Gate 0 still requires separate authorization and execution for:

1. the remaining 11 Stage 2A continuation cells, comprising 66 scheduled attempts;
2. Stage 2A result analysis and proposed numeric performance/resource thresholds;
3. Stage 2B proof-only WPF media-load scenarios, including dispatcher and whole-system responsiveness, cancellation, preview/cache/disk behavior, and any required WPR trace;
4. the owner-approved G0.5 long-form methodology and resource/free-space sizing before 60-minute route runs;
5. the remaining independent-playback rows and final default-delivery disposition;
6. documentation-only G0.6 preliminary Pro continuity/repair dispositions; and
7. the G0.7 capability contract, runtime/profile and CI procedure, architecture/roadmap reconciliation, and final owner exit packet.

Those units must remain separately bounded. A blocked result remains valid and must not be hidden by broadening the runtime, weakening semantic or portability requirements, or silently substituting components.
