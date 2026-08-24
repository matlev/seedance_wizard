# Repository agent workflow

## Git workflow

- Commit every completed work unit before handing it back to the user, including documentation and agent-instruction changes.
- Use a brief, declarative commit summary describing what changed.
- Keep unrelated user-owned changes out of the commit.
- Do not push commits unless the user explicitly requests a push.

## Agent roles and delegation

The primary agent is the technical lead. It owns architectural decisions, task decomposition, integration, and final judgment.

Delegate work when the desired result can be specified without delegating an unresolved architectural decision.

Use the available sub-agents according to their specialties:

- `explorer`: read-only codebase discovery, dependency tracing, caller analysis, and locating existing patterns.
- `bug_investigator`: reproduce non-trivial defects, trace root causes, and recommend a fix before implementation.
- `implementer`: bounded feature implementation where architecture and intended behavior are already established.
- `bug_fixer`: implement understood defect fixes and regression coverage.
- `test_worker`: run focused tests, add straightforward coverage, and summarize routine failures.
- `reviewer`: independently review completed changes for correctness, regressions, architectural violations, and missing tests.
- `docs_researcher`: investigate framework, library, API, language, and version-specific behavior using authoritative documentation.
- `refactor_worker`: perform bounded code reorganization and rewrites according to an established refactoring plan; specializes in refactoring tasks.
- `refactor_reviewer`: independently review refactoring work performed by `refactor_worker` for correctness, behavioral preservation, and compliance with the established architecture.

Subagents must not stage, commit, or push changes unless the primary agent explicitly delegates that operation. The primary agent owns integration and work-unit commits.

### Keep work in the primary agent when it requires

- choosing or changing architecture,
- establishing a new implementation pattern,
- resolving ambiguous requirements,
- changing public contracts or persistence models,
- reasoning about cross-cutting behavior,
- resolving conflicting sub-agent findings,
- complex correctness decisions where implementation and design cannot be separated cleanly.

The primary agent may implement code directly when delegation would cost more coordination than the work itself, especially for small or tightly coupled changes.

## Architecture lifecycle

- Start from [ARCHITECTURE.md](ARCHITECTURE.md) for the normative dependency map; use [architecture governance](docs/architecture-governance.md) for substantial-change preflight, completion reporting, ADRs, and deliberate debt.
- The primary agent completes the preflight before delegating a substantial change and resolves any architecture conflict before implementation.
- A substantial completion reports architecture impact, applicable ADR/debt decisions, and architecture-test/manual-acceptance results.
- If a requested implementation conflicts with the architectural constitution, stop and report the conflict rather than bypassing the boundary.

### Delegation rules

- Resolve architectural ambiguity before handing implementation to a worker.
- Give workers bounded tasks with explicit constraints and expected behavior.
- Treat architectural decisions made by the primary agent as authoritative unless new evidence makes them invalid.
- Do not delegate broad instructions such as "refactor this subsystem" when the worker would need to invent the architecture.
- Prefer read-only exploration before modifying unfamiliar or cross-cutting code.
- Parallelize read-only investigation freely when useful.
- Parallelize write-heavy work only when file ownership and responsibilities do not overlap.
- Avoid spawning agents for trivial work where delegation overhead exceeds the task itself.
- Sub-agents should return concise findings and summaries rather than large raw logs or file dumps.
- Escalate unexpected architectural decisions back to the primary agent instead of silently making them in a worker.

## Typical routing

- Small, obvious change: primary agent handles it directly.
- Bounded feature with established design: `implementer`.
- Straightforward understood bug: `bug_fixer`.
- Unclear or non-trivial bug: `bug_investigator` → primary-agent decision → `bug_fixer`.
- Unfamiliar code path: `explorer` → primary agent.
- Significant implementation: `implementer` → `reviewer`.
- Routine test work: `test_worker`.
- External or version-specific technical question: `docs_researcher`.
- Major feature or refactor: exploration → primary-agent design → bounded implementation agents → review → primary-agent integration.
- Established refactoring task: `refactor_worker` → `refactor_reviewer`.
- Architectural refactor: exploration → primary-agent design → bounded `refactor_worker` tasks → `refactor_reviewer` → primary-agent integration.
