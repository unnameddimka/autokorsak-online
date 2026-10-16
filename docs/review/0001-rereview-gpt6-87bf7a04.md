# Spec 0001 — GPT-6 re-review of fix `87bf7a04`

**Date:** 2026-10-16  
**Reviewed fix:** `87bf7a049c8432825583e42368e37322fadaa11c`  
**Response reviewed:** `docs/review/0001-review-gpt6-e7b57590-response.md` (commit `40d26771`)  
**Governing requirements:** approved spec 0001  
**Verdict:** **All five original review findings closed; two additional P2 issues remain.** Implementation otherwise accepted in architecture and core round-robin workflow; final acceptance pending corrections.

## Management summary

- **Architecture:** The fix retains the separate pairing-stage history, immutable Berger starting numbers, single continuous tournament results, and score-group preparation projection.
- **Referee-facing changes:** Frozen RR roster members can no longer be marked unavailable in a future RR round; the Settings system lock survives normal draw/delete cycles; unplayed Berger colour swaps require confirmation to restore; byes are treated as scored outcomes; late entrants have truthful pre-join availability markers.
- **Risks still present:** The Pairing Editor cannot actually delete a credited bye despite user confirmation, and the monotonic Settings lock can still be lost specifically for **tournaments persisted before 0001** after deleting all their old pairings.
- **Owner decisions:** None needed. Both remaining items are corrections to existing agreed semantics; do not modify the approved spec 0001.

## Independent checks

At local Windows repo HEAD `40d267711b076ac50945514db6d455fc9dea7ce2` (clean working tree), which contains correcting code `87bf7a04` and its reply:

- `docker compose --profile test run --rm backend-tests`: **106 passed / 0 failed**, exit code 0.
- `docker compose --profile test run --rm frontend-tests`: **12 passed / 0 failed**, exit code 0, TypeScript check completed successfully.
- Read the changed domain, API, React, localization and test files and compared them to the original review.
- No edits to running app, Docker service or persistent referee database; no live destructive tests.

## Disposition of original findings

| Original | Result | Evidence |
|---|---|---|
| P1-01 — frozen RR player could be made unavailable in an undrawn future round | **Closed** | `TournamentEditor.Apply` validates newly unavailable rounds against frozen roster, raises `participant.rosterMemberUnavailable`; PlayerEditor disables affected boxes. Domain/API tests included. |
| P2-01 — Settings system unlocks after deleting all generated pairings | **Closed for post-0001 pairings**, with legacy-data gap below | `PairingsEverDrawn` set on new manual/generated pairings; Settings lock checks it. Existing regression tests cover fresh 0001 tournaments but not old stored 0000 data. |
| P2-02 — unplayed RR colour swaps silently retained | **Closed** | `PlanRound` uses unordered opponent matching only for recorded results; unplayed pairs require exact Berger colours and replacement confirmation. |
| P2-03 — bye not recognized as scored outcome | **Closed in domain**, with UI follow-on bug below | `HasAwardedOutcome` treats real-player bye as earned outcome; joining defaults, stage-change barrier and generator use it, and manual editing/deletion requires confirmation. |
| P2-04 — late entrant shown available before joining | **Closed** | `roundParticipation` helper and Players list have separate not-joined state, styling and tooltips, with frontend tests. |

## Two remaining findings

### P2-01 — Deleting a credited bye from Pairing Editor now fails every time

**Paths:** `frontend/src/pages/PairingEditor.tsx`, `frontend/src/api/client.ts`, `backend/src/AutoKorsak.Domain/TournamentEditor.cs`.

**Exact cause:** The editor's `remove()` uses `const hasResult = pairing.result !== 'unset'` and calls `api.deletePairing(..., hasResult)`. An ordinary credited bye has `isBye=true` and `result='unset'`. The editor therefore sends no `confirmDiscardResult=true`, even **after** showing the referee the deletion confirmation. The server now correctly checks `pairing.HasAwardedOutcome`, and rejects the DELETE with 409 `pairing.resultWouldBeDiscarded`. Unlike the edit/save flow, the delete handler does not retry with the confirmation flag. The referee cannot delete the bye using the UI.

**Reproduce:** Create an odd-roster RR, generate round 1, open its bye pairing, click Delete, confirm. The server rejects it; the bye remains. The same applies to manually entered Swiss byes.

**Expected:** Derive the deletion confirmation flag from an **awarded outcome** (`result !== 'unset' || (isBye && blackId !== null)`) rather than two-player recorded results alone; use the scored-result confirmation text for credited byes; after the existing explicit user confirmation send the correct DELETE flag. Preserve the backend guard against unconfirmed deletion. Add a focused frontend interaction test and/or API+UI integration check covering deletion of a credited bye after confirmation.

### P2-02 — Pre-0001 persisted tournaments can still lose the monotonic Settings lock

**Paths:** `backend/src/AutoKorsak.Api/TournamentService.cs`, `backend/src/AutoKorsak.Domain/Model.cs` (`EnsureStages`), `TournamentEditor.IsSystemLocked`, `TournamentEditor.DeletePairing`.

**Exact cause:** The `PairingsEverDrawn` boolean defaults to false in JSON data saved **before 0001**, even where such tournaments already contain historical pairings. `TournamentService.Load` calls `EnsureStages()`, which creates the initial stage but does **not** backfill `PairingsEverDrawn` from the legacy pairings. Although Settings is initially locked by `Rounds.Any(r => r.Pairings.Count > 0)`, the user can delete every existing unplayed pairing. `DeletePairing` does not set `PairingsEverDrawn`; on the last deletion the lock disappears and Settings can rewrite the first-stage system. New imports do set the flag; the defect concerns **old persisted SQLite tournament JSON**.

**Reproduce:** Deserialize an otherwise valid 0000-style tournament JSON with no `stages` or `pairingsEverDrawn` properties, containing at least one unplayed pairing. Pass it through the normal load/migration path. Assert system initially locked. Delete its last pairing and re-check the lock: currently false. A Settings update with a changed system now succeeds despite previous draw history.

**Expected:** During migration/load of pre-0001 tournaments, when existing pairings prove the tournament was already drawn, set `PairingsEverDrawn=true` **before** those pairings can be deleted, and persist that monotonic fact on the subsequent save. Retain true after all pairings are removed. Add a regression test with an authentic pre-0001 JSON shape, not a newly created tournament that already sets the flag.

## Follow-up instructions

- Fix these two narrow issues inside approved spec 0001; leave other behavior and spec 0000 unchanged.
- Add explicit regression cases for frontend delete-confirmation of a credited bye and loading/clearing a 0000-era tournament.
- Run backend and frontend test profiles (and a browser check for bye deletion against a disposable fixture).
- Reply in `docs/review/0001-rereview-gpt6-87bf7a04-response.md` with exact test results.

**Reviewer's actions:** This review file only. No code edits, no Docker restarts, no modifications to tournament data.
