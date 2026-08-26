# Gate 0 G0.5 P2 Windows WPF measurement-adapter boundary

Status: owner-approved boundary with frozen evidence contract; implementation and live evidence pending

Authority: [G0.5 Stage 1 owner decisions](gate-0-g0.5-stage1-owner-decisions.md)

Machine-readable authority: `eng/gate0/g0.5-wpf-measurement-adapter-contract.json`. It imports the already-executed Stage 2 workload contract by SHA-256 instead of changing that contract after marker qualification.

## Boundary

The adapter is a proof-only `net8.0-windows` WPF executable under `eng/gate0`. It references no ReelForge product assembly. It owns a real STA `Application`, a visible minimal `Window`, the WPF dispatcher, direct exact-P2 process execution, proof-only staging/cache paths, measurement, cancellation, and closed JSON/NDJSON evidence.

It must report its boundary as `p2-windows-wpf-measurement-adapter`. It may prove dispatcher and proof-adapter behavior beside exact P2 work. It may not claim current product render, preview, cache, cancellation, portable UI, or project behavior.

The adapter does not belong in `src/`, Core, Application, Platform.Windows, `CompositionRenderCoordinator`, `ExternalProcessRunner`, or `MediaRenderCache`. It does not open a ReelForge project, read/write project persistence, use user settings/logs, discover tools through `PATH`, or touch the product cache.

## Measurement

- A monotonic background scheduler posts at most one outstanding dispatcher callback every 16 ms at `System.Windows.Threading.DispatcherPriority.Normal`.
- Evidence records enqueue and execution timestamps, expected/observed/missed/overdue probes, p95, p99, and maximum latency. Probing starts one second before media work and ends one second after it closes. A completed 30-second scenario requires at least 1,800 observations; a cancellation scenario requires at least 60 after active progress. It never silently drops a delayed sample.
- Adapter command return is measured from the WPF command handler to successful background scheduling.
- Cold preview readiness uses one separately bounded exact-P2 child to render exactly one 960x540 BGRA frame to stdout. It may overlap the declared heavyweight render jobs, but no second lightweight child may overlap it. Warm preview starts no media child and loads only the paired cold scenario's hash-validated BGRA proxy. Both paths update a proof-only WPF `WriteableBitmap` and require a subsequent `CompositionTarget.Rendering` turn; they are recorded as distinct request-to-visible metrics. `MediaOpened` alone is not first-visible-frame evidence.
- Process samples identify the complete observed process tree and keep root, child, and aggregate limitations explicit.
- The media root is placed in a Windows Job Object with kill-on-close immediately after start and before media progress is accepted. Evidence retains job membership, observed parent/descendant identities, and a zero-active-process close condition; a root-process sample alone is insufficient.
- `proof-adapter-cold` starts with a new empty staging root. `proof-adapter-warm` starts in another empty root and copies only the hash-validated preview proxy and manifest from its paired cold scenario; it does not reuse output, process, decoder, or measurement state. Neither is ReelForge cache evidence.

## Cancellation

The cancellation handler first sets a visible `CancellationRequested` state; UI acknowledgement is the next dispatcher turn that observes that state. It then requests graceful FFmpeg termination by writing and flushing `q` plus a newline to redirected stdin; adapter jobs therefore prohibit `-nostdin`. It waits 750 ms, then uses `Process.Kill(entireProcessTree: true)` if necessary. Evidence separately timestamps handler entry, displayed acknowledgement, flushed request, grace expiry, process-tree exit, and closed cleanup.

Forced termination may pass the owner-approved initial 1.0 cancellation threshold only when acknowledgement is at most 100 ms, total process-tree exit is at most two seconds, no orphan remains, all unvalidated output is removed, and state is consistent. It remains reported as forced fallback rather than graceful encoder shutdown.

## No-media and human controls

Before media evidence, `no-media-control` keeps the same real window, dispatcher heartbeat, 30-second interval, and closed evidence schema visible and unminimized while starting zero media children. It records exact Windows build/UBR, display-adapter and driver identity, active power scheme, display topology/state, DPI, window bounds, native/WPF visibility, interactive desktop state, and every expected dispatcher cadence as executed or missed. The control requires at least 1,800 executed callbacks and the same p95/p99/maximum latency thresholds as a completed media scenario.

At least one representative media scenario additionally requires an explicit human record of pointer and desktop responsiveness. A pointer or desktop stall fails the scenario even if the dispatcher passes. It requires a private WPR trace or explicit owner disposition before an accepted rerun. The adapter never launches WPR automatically, never installs it, and never uploads a privacy-sensitive trace into the normal corpus without a separate owner disposition.

## Portability and future production proof

WPF and Windows measurement stay entirely in proof infrastructure. No WPF type, Windows API, executable path, codec name, or proof-cache identity enters Core or persisted creative intent.

The current product command path still selects `libx264` and cannot stand in for P2 evidence. Gate 0 does not introduce the production runtime-profile mapping or ADR. After that architecture is deliberately implemented, a smaller appropriate proof must repeat through the real product path before any adapter result is promoted.

## CI and execution

Normal hosted CI may parse the contract, run structural tests, and compile the future adapter. It must not acquire P2, depend on machine-local artifacts, execute media, or require an interactive desktop. Live WPF/media evidence remains explicit manual opt-in after every owner-approved execution gate passes.
