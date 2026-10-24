# SPEC 0003 — GPT-6 final re-review at `7c86d6f`

**Date:** 2026-10-24  
**Implementation baseline:** `f20db3ff9b8aa093d2b98e602eee580b6d05aed6`  
**Fix commits:** `3ff305d03617569bfd79b55dbf4d61dd291b1a87` (P2-01) and `7c86d6f464ce9cddc257c50038c7f3f6e92cd8ad` (P1-01).  
**Author's response:** [`docs/review/0003-review-gpt6-f20db3f-response.md`](0003-review-gpt6-f20db3f-response.md).  
**Initial review:** [`docs/review/0003-review-gpt6-f20db3f.md`](0003-review-gpt6-f20db3f.md).  
**Current main examined:** `3daf9eb1fa5186191b39a4e11a726f90f217cca5` (documentation-only future-platform PR merge after the fixes).  
**Verdict: ACCEPTED.** Both reported findings are closed, with focused domain/API regressions. No new blocking defect identified in the corrections. No owner decision required for spec 0003.

## Closed findings

### P1-01 — Discarded metadata-only round during automatic RR shrink: CLOSED

`PairingSchedule.ValidateRoundCountChange` now blocks any trimmed round with **pairings or a retained `Round.LegacyXml` subtree**, and `ApplyRoundCount` only runs after successful validation. This protects unplayed imported `TournamentRound` data that was previously lost even without pairings. The guard is conservative: a round with historical XML is retained by refusing the shrink rather than guessing which contents are safe to drop. Other out-of-range stage and participation safeguards retain their prior semantics, including the pure-RR effective participation rules.

Regressions in `Spec0003Tests`: `A_metadata_only_round_beyond_the_derived_count_blocks_the_shrink_without_changes` asserts a conflict and identical serialized state; `An_empty_round_without_metadata_still_shrinks_automatically` ensures ordinary empty rounds remain resizable; `Imported_round_xml_blocks_the_shrink_after_switching_an_undrawn_import_to_round_robin` verifies refusal on an actual legacy XML round subtree, without writes. The author also reports uk/en disposable-instance checks with an imported fixture.

### P2-01 — Generate-all refused when provisional CurrentRound exceeded derived count: CLOSED

`GenerateRoundRobin` still validates the requested route round against the **existing** count and selects the RR stage; the later `round > end` check is now applied only when `allRounds == false`. With `allRounds == true`, a referee currently on provisional round V may correctly generate a three-round Berger schedule for three finalized participants. `ApplyRoundCount` atomically persists the derived count and clamps `CurrentRound` to III. Single-round requests beyond the derived end still fail without choosing an arbitrary substitute round.

Regressions: `Spec0003Tests.Drawing_all_rounds_from_a_round_beyond_the_derived_count_resizes_and_clamps_the_current_round`, `A_single_round_beyond_the_derived_count_is_refused_without_writes`, and the direct `ApiTests.Drawing_all_rounds_from_a_current_round_beyond_the_derived_count_succeeds`. These cover the domain and actual HTTP route contract. Existing source continues to reject a request to generate in a non-RR stage.

## Independence of future platform design

The subsequent documentation merge `3daf9eb1` changed only `CLAUDE.md`, `docs/README.md`, and the new `docs/future/platform-architecture.md` relative to the previous status/review commit `630fa06d`. **No API, domain, frontend, deployment, dependency, schema, or test files were modified** by this PR.

The future design records optional longer-term directions: separate central registry and external data sources (UFGo, EGD, OGS and others), curated identities, moderated tournament publication, personal-data handling, and one referee codebase for offline Windows/SQLite and hosted authenticated PostgreSQL-backed modes. It does **not** replace the accepted current SQLite/single-container architecture; none of its proposed migrations or integrations are approved implementation tasks.

Explicit execution barriers are present in all relevant places:
- the first paragraph and the agent guardrail of `docs/future/platform-architecture.md`;
- the “Future design documents are NOT implementation authorization” paragraph in `CLAUDE.md`;
- the separate, clearly marked “Future design notes” entry in `docs/README.md`.

These explicitly require a separately scoped, owner-approved numbered spec and an implementation instruction. This is an adequate barrier against long-range research being mistaken for current development requirements. Continue working from accepted specs 0000–0003 without adding future-platform dependencies or tasks.

## Verification limits and follow-up notes

**Code inspection:** examined both patch sets, current `PairingSchedule.cs`, the new targeted tests, the author's review response, `docs/status.md`, and the documentation-only future architecture merge. No extra runtime code changed after `7c86d6f`.

**Author-reported execution:** `docker compose --profile test run --rm backend-tests`: **146 passed, 0 failed**; `docker compose --profile test run --rm frontend-tests`: **48 passed, 0 failed** with clean typecheck; production build passes. Disposable uk/en smoke: 6 checks for P1, 8 for P2, 52 repeated full-scope checks, no page or console errors. These numbers are from Claude's response/status, **not independently rerun** by this reviewer. The previously used Windows checkout is offline. No live `:8080` application or tournament data was touched.

**Nonblocking pre-existing operational caveat:** `CLAUDE.md` still includes `docker compose up --build -d` under “Before you commit”, targeting the default `:8080` port. This conflicts with the repeated project-specific requirement **not to redeploy the live referee container without explicit owner approval**. It was already present before the future architecture merge and was **not introduced by the new strategy document**. At the next separately authorized workflow-documentation update, replace that blanket instruction with disposable-instance smoke guidance plus an explicit no-live-deploy rule. This is not a spec 0003 implementation blocker.

**Other pre-existing limitation (outside 0003):** As the author notes, explicit Settings shrinking for a **non-RR** tournament may still prune unpaired rounds without checking `Round.LegacyXml` (`TournamentEditor.ApplySettings`). That data-retention edge case deserves a subsequent small, separately scoped fix, not a regression of the accepted pure-RR generator correction.

## Final disposition

**SPEC 0003: ACCEPTED at `7c86d6f`**, including both review corrections. The larger future-platform design is segregated and does not authorize or obstruct current implementation. Any live deployment is a separate owner decision.
