# Business and packaging direction

Status: accepted future product context; no commercial infrastructure or feature gating is implemented or approved

Research reconciled: 2026-08-19

## Purpose and interpretation

This document preserves the current commercialization thesis while ReelForge is still proving its product and editing architecture. It is not a price list, release promise, entitlement specification, financial forecast, or instruction to add accounts and billing.

The intended model has three independent dimensions:

| Dimension | User value | Authority |
| --- | --- | --- |
| Software entitlement | Free or a possible future Pro capability set | A future licensing/entitlement system, not a project file |
| Compute route | Bring Your Own Key (BYOK) or a possible ReelForge-managed route | Chosen per operation and constrained by provider capability |
| Managed usage | A possible prepaid/metered credit balance, provisionally called **Ingots** | A future server-authoritative account ledger |

A payment product may grant an entitlement, but `Pro` must not be modeled as a synonym for an active subscription. Likewise, a managed-compute balance does not determine whether a user is Free or Pro.

No current Core type, persistence DTO, application account, telemetry stream, licensing check, payment flow, or feature behavior changes because of this strategy. Commercial abstractions should be introduced only when an approved implementation creates real pressure for them.

## Settled strategic direction

Unless explicitly revisited:

1. **Free is a real product.** ReelForge should be free to start and remain genuinely useful indefinitely rather than act as a crippled trial.
2. **Local value is not artificially scarce.** Project/media management, Saved Frames, Saved Clips, meaningful basic editing, common export, and other foundational local workflows are strong Free candidates. A local FFmpeg operation should not consume managed credits merely because it could be metered.
3. **BYOK remains first-class.** Free users may configure and use their own supported provider accounts. BYOK is not hidden behind Pro, charged managed credits, or deliberately degraded to force a managed route.
4. **Managed compute sells convenience.** A future ReelForge-managed route may remove provider-account, credential, billing, and temporary-hosting setup. It may charge abstract managed-compute credits, provisionally branded as Ingots.
5. **Free users may buy managed compute.** A user should not have to buy Pro before buying Ingots. Free + BYOK, Free + Ingots, Pro + BYOK, and Pro + Ingots are all valid outcomes.
6. **Pro sells capability and productivity.** A possible Pro tier should provide meaningful advanced editing, repair, finishing, or workflow depth rather than merely remove arbitrary restrictions from the useful creative loop.
7. **Managed cost stays legible.** Before submission, the user should see the operation, model, material settings, estimated Ingot charge, available balance, and estimated remaining balance. Branding must not intentionally obscure real spending.
8. **Offline/local operation remains valuable.** Project management, editing, local materialization/export, and direct BYOK routes should remain usable without turning ReelForge into a cloud-only editor merely because managed services may exist.
9. **Provider neutrality is a business-continuity feature.** BYOK and managed catalogs may differ. A provider price, reliability, territory, or contract change must not become project-media identity or force a domain redesign.
10. **Commercialization must not weaken user ownership.** Existing projects, authoritative recipes, provenance, and durable media must not become unreadable because an entitlement lapses or a credit balance reaches zero. Future gating should govern creation/use of a capability, not custody of the user's work.

The exact Free/Pro boundary, pricing, payment model, Ingot rules, and release packaging remain provisional.

## Current behavior versus future optionality

Today ReelForge is a local Windows application with direct BYOK provider integrations. It has:

- no ReelForge user accounts;
- no managed-provider backend;
- no wallet, credit ledger, reservation, settlement, refund, or purchase system;
- no licensing or entitlement enforcement;
- no payment processor or product SKUs;
- no product analytics/telemetry pipeline;
- no ReelForge-owned provider credentials in the client;
- no approved Free/Pro feature gates.

The current `GenerationSubmissionAuthorization` is a short-lived safety capability created only after a human confirms a potentially billable direct provider request. It prevents tests, startup, project loading, autosave, and incidental UI work from spending money. It is not proof of payment, an account entitlement, or a managed-credit reservation.

The current provider catalog is BYOK. BytePlus, AtlasCloud, or another adapter being technically available does not mean ReelForge has the contractual right to resell that provider through managed credits.

## Future managed-compute boundary

ReelForge-owned provider credentials must never ship in the desktop executable. A future managed route requires a security boundary such as:

```text
ReelForge desktop
    |
    | authenticated operation request
    v
ReelForge managed-compute service
    |-- account and authoritative ledger
    |-- estimate / reserve / settle / refund policy
    |-- abuse, rate-limit, territory, and moderation controls
    |-- approved managed-provider adapters and credentials
    v
commercially approved provider route
```

The desktop may display a cached balance for responsiveness, but it cannot mint credits or act as financial authority. Purchase receipts require server verification. Financial records belong to the account ledger, not `.rfp` project state.

A project or generation record may eventually retain non-authoritative execution provenance such as BYOK versus managed route, provider/model identity, and a sanitized operation receipt when that is useful for history. It must not persist the wallet balance as truth or make billing mechanism part of an asset's logical/content identity. No current schema change is justified merely to anticipate this possibility.

### Reservation and settlement

The existing Undo Send timer maps naturally to a future reservation lifecycle, but the two mechanisms remain distinct. A possible managed flow is:

```text
estimate -> verify balance -> reserve -> Undo Send -> submit -> observe provider economics -> settle/release/refund
```

Settlement must follow actual economic events, not only the final UI status. The model must distinguish at least:

- cancellation before provider submission;
- provider moderation rejection;
- provider execution failure or cancellation;
- provider success followed by ReelForge download/ingestion failure;
- provider-specific partial or failed-job billing;
- reservation release, final settlement, and refund.

Do not globally assume that every failed job is free. Provider policies and the actual chargeable event require explicit evidence.

### Catalog and provider terms

Provider capability eventually needs to distinguish `SupportsByok` from `SupportsManaged`; neither implies the other. Before any managed launch, every route requires current review of commercial-integration/resale rights, thin-wrapper or customer-solution restrictions, end-user obligations, prohibited-use and moderation requirements, territories, account sharing, billing/refund behavior, rate limits, attribution, and support expectations. Ambiguous terms require written provider confirmation and qualified legal review where appropriate.

The current provider-neutral generation and future media-edit abstractions remain semantic operation boundaries. Managed transport, account authorization, and settlement wrap those operations at the application/service boundary; they do not leak payment concepts into media recipes.

## Future entitlement boundary

If feature packaging is implemented, the application should ask whether a named capability is available rather than inspect a payment SKU in UI handlers. Conceptually:

```text
payment product or beta grant
          |
          v
software entitlement
          |
          v
named capability availability
```

This preserves subscription, perpetual, major-version, beta, educational, promotional, and other possible grants without redesigning editor logic. A future centralized entitlement service is a plausible seam, not an interface to create now.

Feature authorization and compute authorization remain separate. For example, a Free user may be allowed to use managed generation but have zero Ingots; the appropriate choice is buy Ingots or use BYOK, not upgrade to Pro. A Pro user can have every locally entitled editing capability while holding no managed-compute balance.

Project and recipe formats must remain tier-neutral. They describe creative intent and provenance, not storefront products. If a user opens a project containing an operation they can no longer create, ReelForge must preserve and explain that state and allow safe access/export where technically possible rather than corrupt, erase, or silently reinterpret it.

## Provisional packaging hypotheses

These groupings guide future research; they are not approved gates.

### Strong Free candidates

- project and Project Media management;
- physical-media import and ordinary preview;
- Saved Frames and Saved Clips;
- generation history and provenance;
- BYOK generation and provider configuration;
- a useful basic timeline, trim/split/assembly, and source-audio workflow;
- common local export and ordinary FFmpeg-based operations.

Free should expose the complete generate-to-finish loop at a useful depth. Technical implementation cost alone is not a reason to gate a signature foundational feature.

### Possible Pro value

- advanced multitrack and composition/version workflows;
- generic keyframe automation and advanced transforms/compositing;
- advanced transitions, color/LUT, audio, captions, delivery, and performance controls;
- sophisticated repair, stabilization, continuity matching, and variant comparison;
- batch/professional workflow conveniences;
- selected high-quality local enhancement engines or optional packs, subject to licensing and product research.

These are candidates, not promises. Pro should follow demonstrated professional/creator value, competitive expectations, operating/support cost, and upgrade motivation—not a simplistic split between easy and difficult code.

### Appropriate managed-credit candidates

- managed video/image generation;
- generative repair, object/background replacement, or shot extension;
- externally billed restoration, interpolation, upscale, or speech enhancement;
- other operations with measurable external compute cost.

Credits should represent managed compute/services broadly rather than be hard-coded as “video generation seconds.” Model, provider, duration, resolution, mode, references, hosting, payment overhead, failure policy, fraud, support, tax, currency, and deliberate margin all influence an operation's credit price.

## Relationship to the editor roadmap

The accepted [Editor capability direction](editor-capability-direction.md) defines product semantics and architectural seams before commercial tiers. This strategy resolves its broad commercial lanes but does not decide exact gates:

- foundational local creative-loop capabilities lean Free;
- professional depth, productivity, and advanced finishing are Pro candidates;
- external metered compute is an Ingot candidate;
- optional local engines may be Free downloads, Pro capabilities, paid packs, or another model only after license, distribution, support, and value research.

Continuity matching, exact composition references, provider-neutral repair, and non-destructive derived-media behavior remain strong ReelForge product directions regardless of their eventual package. A premium decision must not compromise immutable recipes, provenance, universal logical references, project recovery, or safe export.

## Validation before commercial implementation

The strategy deliberately preserves options rather than pretending public competitor prices are ReelForge economics. Before approving commercial implementation, obtain evidence in the following areas.

### Product and pricing evidence

- target personas and their most frequent completed workflows;
- project length, generation attempts per usable shot, provider/model mix, and direct BYOK spend;
- where BYOK onboarding fails and whether managed convenience changes completion;
- actual use and perceived value of proposed Pro capabilities;
- preference and willingness to pay for subscription, perpetual, paid upgrade, or hybrid licensing;
- managed-route price sensitivity, top-up behavior, retention, support cost, and churn.

Transparent interest tests may show an estimated managed cost and offer BYOK or a notification option before managed billing exists. They must not imitate a completed purchase or use deceptive urgency, confusing currency, hidden renewals, or other dark patterns.

### Economic and operational evidence

- provider inference and billed-failure policy;
- upload storage, egress, backend compute, retries, and observability;
- payment fees, taxes, refunds, chargebacks, fraud, promotions, currency movement, and customer support;
- target gross margin and provider-price-change safety margin;
- reliability, support, incident response, reconciliation, and business-continuity requirements.

### Legal, security, and privacy gates

- provider-by-provider commercial and territory review;
- payment, tax, refund, consumer-protection, credit-expiration, and stored-value implications;
- account authentication, server-authoritative ledger, receipt verification, rate limiting, abuse/fraud controls, and key isolation;
- log redaction and separation of provider credentials, payment information, prompts, and project data;
- telemetry only under an explicit consent, privacy, retention, deletion, and regional-compliance policy;
- offline licensing/grace and project-access guarantees if Pro verification is introduced.

Promotional Ingots may lower onboarding friction, but account farming, abuse, identity, cost, and eligibility must be solved before any promise is made.

## Intentionally unresolved

The following remain explicit decisions for later discovery:

- subscription, perpetual, paid-upgrade, or hybrid Pro licensing;
- exact Free and Pro capability lists;
- whether a Pro grant includes managed credits or discounted purchases;
- Ingot branding, exchange rate, bundles, expiration, refunds, and margin target;
- promotional-credit eligibility;
- managed territories and provider/model catalog;
- whether each external repair capability requires only credits or also a software entitlement;
- packaging and entitlement of optional local ML engines;
- account requirements for Free-only/local-only use;
- privacy-respecting telemetry and experimentation policy;
- business/team plans, collaboration, and any marketplace;
- public-release compatibility/support baseline.

No current implementation choice should settle these accidentally.

## Staged future work

Commercial work is unscheduled and does not expand Milestone 2 or the Milestone 3 structural refactor.

1. **Observe and interview:** validate the creative loop, BYOK onboarding, editor value, and target personas without adding billing infrastructure.
2. **Define the offer:** choose a narrow Free/Pro hypothesis and managed-compute pilot from evidence; establish transparent pricing principles and project-access guarantees.
3. **Complete commercial reviews:** verify provider rights and territories, unit economics, payment/tax/refund obligations, security/privacy, and operational support.
4. **Design the cloud boundary:** specify accounts, authentication, authoritative ledger, reservation/settlement, provider brokerage, abuse controls, and failure reconciliation before implementation.
5. **Pilot one managed route:** use an explicitly approved provider/model, bounded territory, observable cost ledger, conservative limits, and BYOK fallback. Do not launch a generic credit platform first.
6. **Add capability entitlement only when needed:** centralize named capabilities and keep payment products outside editor/UI logic. Preserve offline/local and existing-project access.
7. **Expand from measured demand:** add providers, managed edit operations, Pro capability depth, optional engines, or team features only when evidence supports their cost and complexity.

## Principal risks

- **Premature architecture:** speculative account/entitlement interfaces can pollute Core before a real use case exists.
- **Loss-making compute:** provider list prices omit material overhead, failure, fraud, payment, tax, and support costs.
- **Commercial-rights mismatch:** a technically supported BYOK provider may prohibit or constrain a managed route.
- **Currency opacity:** abstract credits can damage trust if users cannot understand effective spend.
- **Entitlement lockout:** gating project access or export can hold user work hostage and undermine the Free/BYOK trust thesis.
- **Cloud drift:** account or managed-compute convenience can gradually make local operation second-class.
- **Catalog coupling:** tying assets or recipes to a commercial route makes provider changes destructive.
- **Security and privacy expansion:** accounts, balances, payments, prompts, and hosted media create a much larger threat and compliance surface.
- **Support burden:** subscriptions, perpetual versions, expiring credits, promotions, provider failures, and refunds create different long-term obligations.
- **Unvalidated optimism:** iterative generation may produce high transaction frequency, but no ReelForge data yet establishes conversion, retention, or margin.

The guiding principle is: build a product people want, keep Free useful and BYOK honest, preserve managed convenience and Pro optionality, and do not turn a commercial hypothesis into technical baggage before evidence warrants it.
