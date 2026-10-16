# Spec 0002 — GPT-6 re-review of fixes at `6821a5b6`

**Date:** 2026-10-16  
**Reviewed implementation:** `6821a5b63c253bc16b971433b5c311327398103b`  
**Response:** `docs/review/0002-review-gpt6-4ecfd464-response.md` (commit `45dac492`)  
**Previous review:** `docs/review/0002-review-gpt6-4ecfd464.md`  
**Governing requirements:** Approved spec `docs/devel-specs/0002-round-robin-starting-numbers-and-draw-reset.md`.  
**Verdict: ACCEPTED.** All three earlier findings are closed. No new blocking issue identified in the modified code.

## Management summary

- **Architecture:** No new design change or pairing-algorithm revision. The implementation now centralizes parsing of typed starting numbers in the frontend, preserves the distinction between player-card clear sentinel and batch-table null-clear semantics on the backend, and protects unsaved Pairings table edits against draw clearing.
- **Visible referee behavior:** Player-card starting numbers such as 1 or 12 save correctly, including edits after a previous number was assigned. A frozen numbered participant's ordinary name/comment changes are no longer blocked by the number field. Typing literal `0` in the player card/table yields an error rather than deleting the number; clearing the field explicitly still works. Clear RR Draw cannot run while the starting-number table has unsaved changes.
- **Remaining issues:** None blocking from this re-review. The main 0002 workflows—manual/random numbering, round-count adjustment, stage-level reset and conditional Settings recovery—were independently code-reviewed in the earlier review and remain unchanged by this narrow fix.
- **Owner decisions needed:** None.

## Finding dispositions

### P1-01 — Player card rejects nonempty numbers: CLOSED

**Evidence:** `frontend/src/format.ts` exports `parseStartingNumber`: trimmed empty becomes null, digits matching `/^\d+$/` with numeric value 1–999 become a number, and invalid strings (including 0) are rejected. `frontend/src/pages/PlayerEditor.tsx` invokes the shared parser but only rejects when the number **changed**. An unchanged, disabled frozen number is sent as `startingNumber: null` (the card-specific “unchanged” convention), leaving unrelated edits possible.

**New regression:** `frontend/src/pages/PlayerEditor.test.tsx`: typing 1 on a new editable card, changing 2 to 12, editing a comment with a frozen number, typed-zero rejection, and clearing a field. Five passing component tests.

### P2-01 — Table `0` silently clears: CLOSED

**Evidence:** `RoundRobinPanel.saveNumbers()` uses the same parser; empty becomes `null`, and `0` is rejected before an API write. `TournamentEditor.SetStartingNumbers` no longer normalizes batch `0` to null, and the existing `CheckDraftEditable` rejects it. The player-card internal `0` clear sentinel is only emitted after the user **empties** the field, not after typing `0`.

**Regressions:** `Spec0002Tests.Batch_zero_is_rejected_without_changes_and_null_clears`; direct `PUT /starting-numbers` with number 0 in `ApiTests`; `RoundRobinPanel.test.tsx` rejects typed 0 without saving. These passed.

### P2-02 — Clear loses unsaved table edits: CLOSED

**Evidence:** `RoundRobinPanel` sets `disabled={locked || dirty}` on Clear, mirroring Generate. The translated hint now requests saving or discarding edits before drawing **or** clearing. The Clear confirmation and result-based server lock remain intact.

**Regression:** `RoundRobinPanel.test.tsx` asserts that while the table is dirty, Clear is disabled and no API request occurs, and that explicit Discard reenables the button. Passed.

## Independent verification

At the authorized Windows checkout `D:\pet_prj\autokorsak-online`, clean local HEAD `45dac49226d66f04349dddd2125d695ac9e46c89`, which contains the reviewed implementation `6821a5b6` and the implementation reply:

- `docker compose --profile test run --rm backend-tests` — **125 passed, 0 failed**, exit code 0.
- `docker compose --profile test run --rm frontend-tests` — **24 passed, 0 failed**, exit code 0, with successful TypeScript type-checking.
- Reviewed changes from `40938893` to `6821a5b6`, the Claude response, the new frontend tests, the domain/API regressions, and localization changes.
- Claude additionally reports a successful disposable-instance browser smoke in **uk** and **en** and a successful Vite build; these are **Claude's verification statements**, not independent repeat runs by this reviewer.
- Reviewer made **no implementation edits**, restarted **no running application**, and did not modify any production/demo referee tournament data or persisted volumes.

## Final disposition

**Spec 0002: ACCEPTED** at implementation commit `6821a5b6`, with all P1/P2 findings from `0002-review-gpt6-4ecfd464.md` resolved.

Recommended next step is referee-facing demonstration of the starting-number table, entered/random source choice, terminal RR round-count adjustment, Clear, result lock and conditional Settings recovery. Deployment remains a separate owner decision.
