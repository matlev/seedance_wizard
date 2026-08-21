# Milestone 3 manual acceptance matrix

Milestone 3 reorganizes ReelForge without intentionally changing its behavior. Run this concise matrix after a slice touches the named area. Run the complete matrix before merging the milestone.

Automated tests remain network-isolated and must never submit a paid provider request. Any live generation check is a deliberate human-only action and is not required for refactor acceptance.

| Area | Manual acceptance path | Expected result |
| --- | --- | --- |
| Project lifecycle | Create, close, reopen, switch between two projects, then restart ReelForge. | The last project reopens; project-specific selection/workspace state does not leak between projects; recent projects remain usable. |
| Jobs and recovery | Queue a fake-provider job, switch projects, restart while it is running, then observe completion. Exercise Undo Send cancellation separately. | Global job state and elapsed time survive restart; finalization targets the owning project; queued work cancelled during Undo Send is never submitted. |
| Generation preparation | Prepare text, physical-media, Saved Frame, and Saved Clip references without confirming a paid request. | Provider capabilities constrain the form correctly; reference order/role/label/notes are preserved; no request occurs before explicit confirmation. |
| Frame and clip tools | Navigate exact frames in both directions, save/update/delete a frame, create/replay/delete a short Saved Clip, and reconstruct after clearing cache. | Frame navigation stays exact and responsive; derived media rebuilds from durable recipes; missing sources produce a clear failure. |
| Timeline arrangement | Add media by drag/drop, reorder, split under both split preferences, mute a segment, layer/move audio, and reopen the project. | Exact boundaries, ordering, audio placement/settings, and immutable recipe revisions persist. |
| Audition | Audition an unbaked composition, seek/frame-step, and play across muted segments plus independent layered audio. | Playback follows the current recipe without advancing during unrelated Project Media playback; mute/gain/pan/fade and independent audio are respected. |
| Preview and export | Preview after several timeline edits, cancel one render, retry, then export. | Preview/export materializes the current recipe; cancellation leaves no partial result; retry succeeds and export remains a normal durable file. |
| Settings and platform integration | Change settings rapidly, configure/browse/auto-detect each media tool, update/remove a credential, change log/cache locations, and restart. | Atomic settings remain readable; platform defaults and overrides resolve correctly; credentials never appear in JSON/logs; configuration survives restart. |
| Cache and diagnostics | Hold one derived item in use while producing enough media to trigger eviction; inspect the configured verbose log. | Active leases are not evicted; disposable entries can rebuild; diagnostics identify failures without exposing credentials. |

The Windows shell remains the current manual presentation target. Screenshot-perfect automation is not required; extracted presenter/state logic should carry deterministic tests as it is introduced.
