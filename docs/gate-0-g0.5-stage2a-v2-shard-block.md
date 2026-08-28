# Gate 0 G0.5 Stage 2A V2 shard block

Date: 2026-08-27

Status: first continuation cell executed; retention stopped before journal or remote work; owner decision required

## What happened

The corrected continuation runner passed its full local/R2 and resource preflight and executed the six fixed attempts for `stress-720p-webm-eight` (global ordinals 109 through 114). The warm-up and all five measured attempts passed their semantic checks. The retention plan preserved one measured complete closure and five compact summaries.

The V2 writer then stopped before creating an append journal or performing remote work because the projected shard manifest was 353 lines against the exact 300-line cap. The candidate was only 19,828 bytes against the independent 65,536-byte cap. The retained source contains 23 files and 15,768,093 bytes.

This is a proof-containment contract mismatch, not a media-route failure or retention-byte overrun. A normal approved Stage 2A cell necessarily carries 23 artifact records plus six attempt bindings, and that shape cannot fit in 300 pretty-printed JSON lines.

The failed append left no destination, shard, root-index entry, journal, transaction staging directory, or R2 mutation. No FFmpeg or ffprobe process remains active. The normal empty append-lock file remains.

## Additional harness defect

Review found that the current staged attempt-binding document contains malformed compact closure references. The runner transformed the already continuation-prefixed complete-attempt identity `stage2a-continuation-110` into `stage2a-continuation-continuation-110`.

The current staged cell must therefore remain non-authoritative and must not be repaired or appended. Its current inventory identity is:

- 23 files;
- 15,768,093 bytes;
- deterministic path/size/SHA-256 inventory SHA-256 `C0C88D263CDD4BFA70F608F881BBD0D2617C69E70AF387FE1E80357DF91EF75D`;
- cell summary SHA-256 `591C2815EA9375B7DADE5635FF11C8A6E35D325BA6DB2BF90F37405EEF6C11DC`;
- attempt-binding SHA-256 `EA7AE21C7166CC41300C90D50FF31ADF3EC73113E0487A14E422D74837F4379E`;
- consumed preflight SHA-256 `07FA9C0ABF96820FFA990A1AA484C85BF72089D0FE5C85A5BEA4F31C3372D123`.

The runner now preserves the binding helper's exact complete-closure identity instead of rewriting it. The V2 live writer now independently requires every compact reference to resolve to exactly one complete, passed attempt in the same six-attempt binding document and rejects complete attempts that carry a reference. The focused continuation and V2 containment suites pass 71 of 71 tests, including executable doubled-reference rejection and valid-reference prepared-journal tests.

## Serialization decision

### Recommended: canonical compact shard JSON

Authorize V2 shard manifests to use PowerShell's deterministic `ConvertTo-Json -Compress` representation with a trailing newline. Apply this only to the V2 shard candidate and final shard bytes; keep root indexes, attempt summaries, evidence payloads, and human-facing documents unchanged.

For the actual blocked candidate, this representation projects to 16,967 bytes and one line. It preserves the exact schema, values, artifact records, attempt bindings, hashes, and 65,536-byte cap. The existing 300-line cap remains satisfied without changing the V2 root contract or its hash, so the accepted infrastructure shards and V5 root binding do not require reinterpretation.

Required safeguards:

- generate the preflight candidate and final shard through the same compact serializer;
- retain the exact 65,536-byte cap and all schema, artifact, attempt, capacity, chain, journal, and remote-verification checks;
- add an executable no-media test using the real 23-artifact/six-attempt shape;
- parse and validate the compact bytes through the ordinary V2 reader before any append;
- prove oversize compact shards still fail before journal or remote work;
- refresh the exact V2-writer and continuation-authorization hashes before media resumes.

Trade-off: the line cap becomes only a serialization guard for V2 shards; byte and schema limits remain the substantive growth controls. Reviewability moves to parsed/pretty-rendered inspection rather than the retained shard's raw layout.

### Alternative: amend the V2 line contract

Changing `maxShardLines` would preserve pretty-printed shard bytes but would change the V2 root-index and containment-contract hashes. It would require a bounded worst-case artifact-shape projection, updates to the root index, containment module, writer, tests and continuation authorization, plus revalidation of the V5 result that binds the current V2 root. Splitting a cell across shards or dropping approved evidence is not recommended and is outside the current V2 schema.

## Owner decisions required

1. Approve canonical compact JSON for V2 shard manifests within the exact safeguards above, or request the broader line-contract amendment instead.
2. Approve preserving and atomically quarantining the current 23-file malformed-reference staging root with a complete hash receipt. It will not be reused or automatically deleted.
3. Authorize one replacement execution of the six-attempt `stress-720p-webm-eight` cell after the selected serialization path is implemented, reviewed, committed, hash-bound, and passes a fresh full preflight.
4. Require the final Stage 2A accounting to report the six non-authoritative physical attempts from this blocked run separately from the 72 scheduled authoritative continuation attempts.

No Stage 2B, concurrency comparison, long-form, WPF/product behavior, shipping-runtime, distribution, or legal conclusion is implicated or authorized by these decisions.
