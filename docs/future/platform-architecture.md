# Future Platform Architecture and Data Exchange

**Status: FUTURE DESIGN — NOT AUTHORIZED FOR IMPLEMENTATION**

**Discussion date:** 2026-10-24  
**Scope:** Long-term architecture of AutoKorsak referee applications, central data platform, external go data sources, identities, tournament publication, moderation, and personal-data handling.  
**Nature:** Design record and planning input, **not** an approved development specification, backlog, or instruction to implement anything.

> **Agent / contributor guardrail:** Do not implement, scaffold, refactor, migrate, deploy, configure services, create accounts, or modify code in response to this document. No item here authorizes work. Implementation requires a separately scoped, explicitly owner-approved numbered specification under [../devel-specs/](../devel-specs/) and a specific instruction to carry out that work. Existing approved specifications and implemented behavior remain authoritative.

## 1. How to read this document

This records the owner's architectural direction from an exploratory conversation. It intentionally separates:

- **Agreed direction** — a product or architectural decision reached in the discussion, but still **not authorized for implementation**.
- **Expected / potential** — a promising capability whose feasibility, detailed behavior, or timing has not been decided.
- **Open** — a question needing further discussion, feasibility study, input from Yulia, and/or qualified legal review.

It is **not** a final database schema, detailed API specification, implementation plan, or legal opinion. It does not retroactively change the current single-container SQLite prototype described in [../architecture.md](../architecture.md). Do not impose legacy AutoKorsak internals on the new design; legacy is evidence for workflows, import/export formats, and integrations, not an architectural template.

## 2. Main goals and boundaries

**Agreed direction**

1. The referee application should remain fully useful without the central server. Running a tournament is independent of publication.
2. Referee work and administration of the central player database are different responsibilities and may have different interfaces.
3. The central platform accumulates disparate external records and referee-supplied information, then maintains curated identities and audited links. Source records are not silently merged merely because names match.
4. Sharing a tournament with the central platform is an explicit action by its referee, subject to authentication, personal-data checks, and moderation.
5. The same referee-facing workflows and tournament rules should apply whether the application runs locally or online.
6. Actual legal and contractual permissions govern which personal data may be transferred, retained, shown, or sent to third parties. Software flags are evidence/metadata, not substitutes for a legal basis.

**Non-goals for this design record:** choosing exact tables or endpoints; building full live synchronization; imposing a universal go-server rating conversion; designing other games; or committing to a release date or rollout order beyond the initial priorities recorded below.

## 3. Logical products and deployments

### 3.1 Referee application: one codebase, two runtime modes

| Dimension | Workstation mode | Hosted/server mode |
| --- | --- | --- |
| Delivery | Downloadable self-contained Windows executable with ASP.NET Core/Kestrel serving a browser UI on localhost | Docker-hosted browser application, deployed alongside the central platform |
| Primary working store | Local SQLite | PostgreSQL-backed private referee workspace |
| Login to run a tournament | None | Required; the workspace belongs to an authenticated referee account |
| Connectivity to run a tournament | Not needed | Browser access to the hosted application is needed |
| Ownership of drafts | On the referee's machine, including local saves | Segregated by authenticated user/permissions on the hosted infrastructure |
| Central upload/publication | Explicit operation; authenticate when necessary | The same explicit operation despite proximity to the central service |
| Rules and UX | Shared tournament domain logic and user-facing workflow | Shared tournament domain logic and user-facing workflow |

In workstation mode the program hosts its website on 127.0.0.1 and maintains everything required for a tournament locally: registrations, player snapshots, setup, rounds, pairings, results, standings, and saved tournament files (including relevant legacy-compatible import/export). Local working data must not be silently uploaded. A referee can operate without creating a central account.

In hosted mode the user logs in and creates/edits private tournaments just as locally. A *draft hosted tournament is not a published central tournament*, even though its bytes are already stored on infrastructure operated by the service. That hosting fact has separate privacy implications. An isolated workspace does **not** imply a separate PostgreSQL database for every user; tenancy and access controls need detailed design later.

Both referee modes should use the same domain rules and, conceptually, the same central exchange/publication contract. Backend persistence and authentication adapters may differ. **This is future target architecture, not a claim that the current prototype already has both modes.**

### 3.2 Central platform

**Agreed direction:** A distinct central server/service, potentially in a separate repository, uses PostgreSQL, provides APIs, manages a curated cross-tournament player registry, ingests external sources, authenticates users, maintains permissions and moderation/audit records, and handles downstream integrations. It may share a PostgreSQL *instance* with hosted referee storage while keeping the logical ownership and data boundaries separate.

Its interfaces may include:

- **Administration:** imported data, candidate matches, reported identities, submitted tournaments, rights/consent assertions, moderation, EGD export.
- **Player portal (future, timing open):** account creation, review of one's records, personal-data choices, and requests to link central identity to external profiles.
- **Public or limited search:** exact scope and need for authentication are **open**, especially for names, ratings, minor players, bulk downloads, and other personal data.

Referee and central administration are not a single catch-all UI. The central registry is not a direct shared write target for the referee's routine edits.

## 4. Three conceptual data layers

These layers are a **conceptual model**, not yet a normalized relational schema.

### Layer 1 — Referee working data

Tournament-local participant records, tournament settings, rounds, pairings, results, local player catalogue and its snapshots. Participants need not be recognized by the central server and may have different names or starting ranks across tournaments. A selected participant may carry an optional link to a central player record. The local record and tournament history must remain usable when that link is absent, wrong, or subsequently removed.

### Layer 2 — AutoKorsak curated central records

A stable platform player identity distinct from both a referee's local participant and any user login. It can aggregate links to external source records, relevant tournament history, referee observations, and verified/contested associations. Server login identity is also separate: a person may already exist in this registry years before registering a user account.

Do not treat an uploaded name as proof of identity. Central identities and their links are the result of explicit matching/review processes.

### Layer 3 — Source-specific external records

Separate datasets for EGD, UFGo, OGS, KGS, IGS/Pandanet, Fox, Tygem, WBaduk, and other candidates. An imported source may have its own players, usernames, identifiers, rating history, games, tournaments, and possibly SGF records. **Different sources may retain different schemas and their own internal links.** A source record is not automatically a platform player.

These datasets can be richer than a referee tournament's records. Not every source supports bulk import: an integration may store full replicated records, fetched profiles, or only a server name plus nickname/profile URL. Record provenance, external identifiers, observation time, and source-specific semantics must not be lost during import.

An extra layer of **reviewed links/claims** associates items across these three conceptual layers without rewriting source data. Incorrect associations must be reversible while historical evidence remains available for audit.

## 5. Inputs, outputs, and source capabilities

### 5.1 Local referee application as a data source

**Agreed direction:** An authenticated referee may choose and upload player records from the local catalogue *without submitting any tournament*. This supports initial seeding of the central registry. It is an explicit, curated operation; the central system must handle possible duplicates and moderation rather than automatically upserting by name.

Tournament submissions are a **separate** data flow. They may additionally carry referee observations and claims of external-account ownership made during registration.

### 5.2 European Go Database (EGD)

- **Agreed direction:** EGD is an early-priority upstream source. The goal is recurring scheduled ingestion, approaching a local mirror **only to the extent data access and reuse rights permit**.
- A public per-player lookup interface was identified during research; availability of complete, current bulk exports and a reliable incremental change feed is **unconfirmed**.
- Use EGD-specific identifiers (e.g., the EGD player PIN when available), not names alone, to preserve source identity.
- **Agreed direction for outgoing exchange:** a central administrator reviews an uploaded tournament, downloads a compatible file for EGD (candidate format: EGD wallist), then submits it to EGD manually through the relevant EGD process. This is a central administrative operation, **not necessarily a referee-app button**.
- Obtain a sample of the file currently used by Yulia and verify the EGD import requirements. The handling of tournaments whose participants cannot lawfully be included in the export remains open.

### 5.3 Ukrainian Go Federation (UFGo)

- **Agreed initial priority:** Seed the server from published UFGo rating lists, potentially keeping historical rating-list snapshots.
- Public rating lists are presently exposed as web tables/archives. A dedicated public player API and stable underlying identity keys were **not confirmed**.
- Ask Yulia about her upstream files, workflows, historical inputs, and any nonpublic integration endpoint.
- Treat guessed name matches across dated lists as provisional; do not infer a permanent identity from identical names.

### 5.4 Online Go Server (OGS) and other game servers

- **OGS:** documented API; profile/rating data and potentially game records/SGFs are candidate imports. The exact allowed access scope is to be verified.
- **KGS:** a documented JSON client protocol is known; whether it enables suitable player-profile imports, stable bulk retrieval, or permitted automated use is **not established**.
- **IGS/Pandanet, Fox Weiqi, Tygem, WBaduk:** candidate sources, but official API availability, access rights, rate limits and reliability need individual evaluation.
- **Other candidates discussed:** GoQuest, Yike and Dragon Go Server; not committed integrations.
- **qGo and WeiqiHub:** third-party go *clients*, **not independent data sources**. Their code may help investigate server protocols; such technical hints do not grant permission to harvest data or guarantee ongoing compatibility. A third-party Fox API was mentioned but is not accepted as an approved integration.

Each source may support one or more modes: periodic bulk import, targeted lookup by account/name, or storing a nickname/profile link only. No promise is made to implement all integrations.

### 5.5 Data import ordering

**Early target once the central platform exists:** seed UFGo rating lists and ingest accessible EGD data. Exact implementation phases, source refresh intervals, permitted mirroring extent, and failure/retry mechanics are not approved.

## 6. Identity resolution, source claims, and moderation

**Agreed direction:**

- A central player record, a local tournament participant, a self-registered AutoKorsak login, and each external-server account are separate things.
- A claimed nickname or matching display name is not proof of a shared owner. A platform user claiming an EGD PIN or OGS account creates a *link request*, not an automatic verified link.
- Administrators establish, reject, correct, dispute, or revoke links. Claims and decisions retain their author, timestamp, origin, context, and history.
- Referees may report a claimed nickname/profile for an unrecognized participant, with or without live internet. If connected, they may look up available information through permitted central interfaces. If offline, they can record the claim and submit it later with the tournament.
- Such claims are reviewed on the server. Several independent referee observations can be useful evidence but do not automatically prove ownership.
- A player portal may let a registered player propose association with an existing central record and with external accounts. Those proposals require moderation. Account-based technical proof of control (e.g., via OAuth where provided) can be considered later, but control of a go account is not by itself proof of real-world identity.
- **Referee-submitted tournaments and referee-submitted assertions also require moderation**. Authentication records who submitted them; it does not make their contents authoritative.
- Prefer keeping immutable historical submissions/observations and recording corrected review decisions rather than deleting the original account of what was claimed.

The specific schema, lifecycle states, matching algorithms, verification policy, dispute workflow, and administrator authority are **open**.

## 7. Ratings and observed playing strength

These are distinct facts:

1. **Source rating:** a reported rating/rank at a particular external system and observation time.
2. **Referee-assigned starting rating/rank:** the referee's assessment for a specific tournament.
3. **Observed results:** recorded opponents, games, results, and any later tournament evidence.
4. **Possible platform inference:** later analysis of performance and plausibility, never a silent rewrite of upstream ratings or identity assertions.

**Agreed direction:** There is no universal reliable lookup table converting all servers' rating scales to one rank system. Referees use approximate, source-dependent rules of thumb. Conversion/assistance rules might be configured in a future design, **but none have been approved now**.

A referee can, at their discretion, use an unverified external account or rating to assign a tournament starting rank, with appropriate indication of uncertainty. That decision must remain a tournament-specific judgment and not automatically update the central identity. Tournament performance may help evaluate whether a claimed rank seems plausible; it cannot by itself demonstrate ownership of a claimed account.

## 8. Personal data, consent, and provenance

**Important:** This section records desired functionality and legal questions, **not a declaration of GDPR or Ukrainian-law compliance**.

### 8.1 Local vs hosted processing

- Workstation mode is self-contained; a referee or tournament organizer controls locally entered data, and the software should not silently send it to the platform or add undisclosed telemetry.
- Responsibility depends on who actually determines purposes and means of processing; it may be the organizer/club, rather than the person designated referee.
- Hosted referee workspaces reside on the operator's infrastructure **before** publication and thus create separate privacy, security, contractual and responsibility questions.
- Consent is one *possible* legal basis, not necessarily the required one for every local registration or publication use case. Local operation should not be blocked by inability to reach a centralized consent service.

### 8.2 Per-player, per-purpose state

**Agreed direction:** Before submitting a tournament, the referee needs a clear per-participant review of data-handling status. Potential purposes must be distinguishable, at least:

- transfer into the central AutoKorsak service;
- public display/publication on AutoKorsak;
- onward transmission to EGD.

Each purpose's legal basis and permissions require later legal determination; a single vague checkbox is insufficient. Evidence/state may include explicitly agreed, expressly refused, unknown/not asked, and revoked; do not conflate silence with agreement. For consent-based cases, distinguish orally reported, written, electronic, and self-service portal evidence; record what was asked, by whom, when, what terms/version applied, and what proof, if any, exists.

These are **conceptual distinctions**, not a final dropdown, consent wording, validation algorithm or declaration that oral assertions are legally sufficient.

### 8.3 Portal-originated permissions

A player who registers on the central platform may provide applicable personal-data choices there. Once the central player's linkage to that account is sufficiently verified, a referee selecting the player can fetch those current choices along with the player data. Source, scope and date of those choices must remain visible; the referee must not overwrite the user's original statement.

Locally cached choices may become stale, so the central service must recheck their **current validity and scope at submission**. The referee may add a separate first-hand statement or reported refusal; conflicts need defined review behavior.

### 8.4 Transfer and publication controls

- **Agreed direction:** Publication is explicit, not a side effect of saving a round. Before submission, show what is eligible, what needs evidence/review, and what cannot be sent under the applicable rules.
- The workstation's full local tournament remains intact when preparing any reduced outgoing representation.
- A display-name replacement or opaque participant number is often **pseudonymization**, not true anonymization; opponents, dates and pairings may identify a person. Neither moderation nor an opt-out flag automatically makes an upstream unlawful transfer lawful.
- Explicit agreement to be publicly listed on AutoKorsak does not automatically grant permission to send the same data to EGD. Conversely, not all lawful processing necessarily depends on consent. Resolve actual purposes and lawful bases with counsel.
- If excluding a player would corrupt pairings, standings, or EGD rating submissions, the product needs an approved legal and tournament-integrity policy rather than silently omitting games.

**Legal review required before public launch:** Ukrainian personal-data law, GDPR applicability, potential other applicable jurisdictions, controller/processor roles, minors/guardians, privacy notices, consent/evidence standard, rights requests and withdrawal, retention/deletion, overseas transfers, security controls, imported third-party data, license/terms of source APIs and replication, and how published tournament records interact with data rights.

Yulia is an intended subject-matter contact; a qualified legal advisor is required for legal determinations. Do not claim that an unchecked or checked UI field by itself solves compliance.

## 9. Tournament publication and downstream review

**Agreed end-to-end direction:**

1. The referee creates and operates a tournament privately (workstation SQLite or hosted private PostgreSQL workspace).
2. Player data may be entered locally or selectively retrieved from the central platform when connectivity and permissions allow.
3. The referee records any player statements about source accounts, source ratings, assigned starting ranks, and data permissions. Source facts and referee judgments are distinct.
4. Before publication, the referee reviews each participant's personal-data status and the intended transfer/publication purposes.
5. A deliberate **Publish / Submit** action sends only data authorized under the rules ultimately adopted. Workstation mode requires central authentication for protected write operations; hosted mode already has an authenticated workspace.
6. The central service validates the submitted package and the present permissions, records submitter and provenance, and queues it for moderation. Submission is **not** immediate acceptance into the curated registry or public visibility.
7. Administrators moderate results, participant associations, referee claims, and publication/third-party-transfer eligibility as separately meaningful decisions.
8. Approved records become available according to applicable access rules; when appropriate, an administrator obtains an EGD-compatible tournament export and submits it manually to EGD.
9. Subsequent EGD import can bring back updated results/ratings, without destroying the original referee submission or importing a duplicate verified identity by name.

**Open:** Specific workflow/status transitions; ability to correct/publish partial tournaments; upload format and transfer granularity; idempotency/versioning and repeat submissions; moderation conflicts; whether bulk non-public uploads are allowed at all; legal handling of participants who refuse public/third-party disclosure; whether entire submissions may be held in a restricted staging area; and how to handle rejected or withdrawn submissions.

## 10. Access controls and safety boundaries

**Agreed principles:**

- Workstation tournament operation needs no central login. Login is required for protected central writes, including publication and uploading player catalogues.
- Hosted referee spaces require login and isolation among users; the existence of an unpublished tournament does not authorize its public distribution.
- Anonymous reading of central records is **unresolved**. It may be disallowed or narrowly limited (e.g., targeted rating lookup rather than bulk player export).
- Administrators have distinct moderation rights. A referee's rights and the rights to view linked-source identities or sensitive evidence must be determined explicitly.
- Do not let the online referee app bypass the central API's policy simply because it shares a Docker network with it.
- Protect provenance, audit history, account linkage, and player-provided consents against unauthorized rewriting.

**Engineering to determine later:** tenancy enforcement, authentication/session strategy, encryption and backup policy, network boundaries, information minimization, logging hygiene, rate limits and anti-scraping rules, access revocation, retention, and threat model.

## 11. Research evidence and caveats

The old AutoKorsak 1.38 executable was inspected as a historical evidence source. Names including **DownloadUfgoRatingList**, **ImportUfgoRatingList**, **ExportWallListForRating**, and **EgdPlayer**, and a direct EGD lookup URL, indicate earlier import/export facilities. The current project already uses legacy XML fixtures for compatibility. These observations warrant further read-only investigation; they do **not** establish present-day endpoint availability or justify porting its architecture.

Source APIs and downloads mentioned in the discussion are research leads, **not verified contracts, permissions, or operational commitments**. Revalidate before designing an integration.

## 12. Open decisions and inputs still needed

1. **Yulia:** Actual UFGo raw-data source and any usable API; EGD bulk-access process; an authentic EGD submission file; tournament results workflow.
2. **Legal review:** Applicable jurisdictions, responsible entities, lawful basis for each data flow, permissions for minors, exact disclosure/synchronization policy, and evidence requirements for referee-recorded consent.
3. **Data model:** Central identity creation and verification, source key stability, player/participant/user-account relationships, provenance and evidence statuses, audit and disputes.
4. **Source availability:** EGD bulk/incremental access; UFGo stable identifiers; OGS/KGS/IGS/Fox/Tygem/WBaduk data formats, permitted automated use, and authentication requirements.
5. **Hosted storage:** Distinct logical stores vs separate physical databases/schemas, robust multi-tenant authorization, and privacy responsibility for unsubmitted drafts.
6. **Exchange:** Explicit import/export surfaces, package contracts, conflict policy, repeat publication, and handling of offline edits after publication. Do not assume simultaneous editing of one tournament from multiple copies is supported.
7. **Publication and EGD:** What to do with non-consenting/unknown participants while preserving pairings and official results; the precise meaning of "approved", "published", and "submitted to EGD".
8. **Rating assistance:** Optional rough source-specific guidance, without a universal conversion table.
9. **Player portal:** Exact scope and timing of registration, identity claims, player account ownership proofs, consent withdrawal, and self-service correction.
10. **Prioritization:** Later separate approved development specifications for each coherent slice; **no implementation work follows from this document**.

## 13. Conversion to executable work

When the owner explicitly chooses a small part of this future design for implementation:

1. Recheck the code and existing approved specifications; do not copy this document wholesale into a development task.
2. Resolve the relevant open questions, legal constraints, and data-contract boundaries.
3. Draft a new numbered English specification in [../devel-specs/](../devel-specs/) with testable acceptance criteria.
4. Obtain the owner's explicit approval and separate instruction to implement.
5. Update living architecture/status/decisions **only when actual behavior changes**.

**Until then this file is a record for future discussion, not a build request.**
