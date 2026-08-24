# Architecture governance

Status: active

The [architecture constitution](../ARCHITECTURE.md) is normative. [Architecture](architecture.md) records detailed rationale; [contributor guidance](contributor-guidance.md) defines local implementation and verification rules.

## Substantial-change preflight

Complete a short preflight before implementation when a change crosses production layers, adds or changes a public/persistence contract, introduces a provider or platform integration, adds an architectural pattern, or changes high-risk persistence, jobs, materialization, playback, or cancellation behavior. Small, local, behavior-preserving fixes do not require one.

```text
Feature/outcome:
Existing owners touched:
Proposed responsibility and extension point:
Dependency and public-contract impact:
Persistence/format/compatibility impact:
Parallel-workflow or boundary risk:
Verification (tests and manual acceptance):
ADR or architecture-debt decision:
```

Resolve an architectural conflict in the primary thread before delegating implementation.

## Completion architecture-impact report

For a substantial completed work unit, report:

```text
Dependency direction changed: [none / describe]
Responsibilities or abstractions added/extended: [none / describe]
Persistence or public-contract impact: [none / describe]
Platform/provider/presentation leakage check: [passed / exception]
Parallel-workflow check: [passed / exception]
ADR/debt: [not needed / references]
Architecture tests and relevant manual acceptance: [results]
```

## ADRs and deliberate debt

Create an ADR for a durable decision affecting dependency direction, a public or persistence contract, platform/provider boundary, paid-network authorization, or release/supply-chain policy. Do not create ADRs for routine implementation choices.

Record a temporary, intentional boundary exception in the [architecture-debt register](architecture-debt.md) before relying on it. It must state its scope, reason, prohibition on copying, and an objective removal condition. Do not record speculative debt.

## Growth review signals

File and class size are review signals, not quotas. At 500 lines, review cohesion and ownership; at 800, require a short justification in review; at 1,200, presume a design problem unless the file is generated, declarative, or otherwise demonstrably cohesive. Do not split coherent code merely to satisfy a number.

Run `pwsh ./eng/architecture-health.ps1` for the same advisory report produced in CI.

## Enforced policy

Assembly dependency tests and portable/Windows CI enforce the dependency graph. Add a narrow architecture regression test when a meaningful boundary could otherwise be bypassed. The lightweight source checks are deliberately conservative lexical guardrails: comments or strings that resemble prohibited code may need to be rephrased. Do not introduce a generic architecture framework or broad static-analysis gate without a demonstrated rule that existing tests cannot express.
