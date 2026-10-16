# Spec 0001 — Final independent GPT-6 re-review (`b565ad20`)

**Date:** 2026-10-16  
**Reviewed implementation commit:** `b565ad20ad7ecc1160efaa713f7a8f5c0654eda6`  
**Claude's response commit:** `889aa1097495d3f9d93749bb6d963befd9d2fd82`  
**Prior review:** `docs/review/0001-rereview-gpt6-87bf7a04.md`  
**Result: ACCEPTED.** Both remaining P2 findings are closed. No further blocking spec-0001 implementation issues found in the changed code.

## Management summary

- **Architecture:** Existing staged pairing design and global scoring remain intact. The fixes align the frontend's recognition of awarded byes with the domain contract and backfill the monotonic “pairings ever drawn” fact for older saved tournament aggregates.
- **Referee-visible behavior:** A credited bye can now be explicitly deleted after a bye-specific loss-of-point warning. Changing the tournament system through Settings remains impossible after a draw, even for a pre-0001 save with all historical pairings subsequently removed.
- **Risks and outstanding issues:** No open functional review findings. One pre-existing SettingsPage navigation-guard test showed a timing-sensitive failure when backend/frontend suites were run concurrently, and passed on a subsequent standalone rerun. Track test robustness separately; this did not reproduce as a code or user-flow regression.
- **Owner decisions required:** None.

## Closure of the final two findings

### P2-01 — Credited bye deletion via Pairing Editor: CLOSED

Verified:

1. `frontend/src/format.ts` exposes `hasAwardedOutcome(pairing)` mirroring the domain rule: a recorded result **or** a bye credited to a real player.
2. `frontend/src/pages/PairingEditor.tsx` uses that predicate in `remove()`. For an awarded bye it shows a localized warning that removing the bye also removes a win and point, and sends `confirmDiscardResult=true` to the API **only after referee confirmation**.
3. Backend `TournamentEditor.DeletePairing` still rejects unconfirmed removal of an awarded outcome.
4. `frontend/src/pages/PairingEditor.test.tsx` exercises the real editor and confirm dialog. It verifies that a credited bye sends the true flag and that an unplayed regular game still sends false. Both tests passed independently.

This resolves the frontend/server mismatch described in the prior re-review without weakening the outcome protection.

### P2-02 — Pre-0001 persisted tournament system lock: CLOSED

Verified:

1. `Tournament.EnsureStages()`, called by the normal application load path, now sets `PairingsEverDrawn=true` if a loaded tournament has existing pairings and the flag has not been set. This covers pre-0001 JSON documents missing the property.
2. `TournamentEditor.DeletePairing` also sets that flag before removing an existing pairing. The value stays true when the last pairing has gone.
3. `TournamentEditor.IsSystemLocked` enforces the flag server-side, while the UI receives the lock state in the tournament view.
4. `ApiTests.Pre_0001_tournament_keeps_the_settings_lock_after_its_last_pairing_is_deleted` constructs pre-0001-shaped tournament JSON without `stages`, `pairingsEverDrawn`, or `firstEligibleRound`, stores it in SQLite, loads and edits it through the real API, then proves that the persisted lock survives the last pairing's deletion and that a Settings system change is refused. This test passed independently.

## Independent verification on `DESKTOP-INM65PV`

At local Git HEAD `889aa109` (containing `b565ad20`), using the repository's unchanged Docker test profiles:

- **Backend:** `docker compose --profile test run --rm backend-tests` — **107 passed, 0 failed**, process exit 0.
- **Frontend, concurrent first run:** `docker compose --profile test run --rm frontend-tests` — **13 passed, 1 failed**, process exit 1. The sole failure was the older `SettingsPage.test.tsx` navigation-guard scenario waiting for its **second** discard-confirmation dialog; the new `PairingEditor.test.tsx` tests both passed.
- **Frontend, standalone repeat after backend finished:** identical `frontend-tests` command — **14 passed, 0 failed**, process exit 0, including the prior failing SettingsPage scenario and all pairing-editor tests; TypeScript compilation also completed.

**Nonblocking test-quality observation:** The existing SettingsPage test's timing around dismissal/reopening of its navigation confirmation can be flaky when run alongside the backend suite. This is not a newly introduced user-visible failure, and it is outside the final two fixes; consider stabilizing its asynchronous waiting in a later test-maintenance change. Do not report that both frontend runs passed: one failed and one passed.

Verification deliberately did **not** mutate the running referee database, restart the production/demo app or alter implementation files. Claude's response additionally reports a successful disposable-tournament browser deletion check in both UI languages; that particular browser procedure was **not independently repeated** during this re-review, so it is not counted among the independent checks above.

## Final disposition

**Spec 0001 implementation: ACCEPTED** at `b565ad20`, incorporating earlier fixes from `87bf7a04`.

All five findings from `0001-review-gpt6-e7b57590.md` and both follow-up P2 findings from `0001-rereview-gpt6-87bf7a04.md` are closed. The normal next step is referee validation/demo, not another implementation rework. No deployment or code changes were made by the reviewer.
