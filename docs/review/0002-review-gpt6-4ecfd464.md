# Spec 0002 — Independent GPT-6 implementation review of `4ecfd464`

**Date:** 2026-10-16  
**Reviewed commit:** [`4ecfd464e26f44de2c9606be761dd3e7ea1fb76f`](https://github.com/unnameddimka/autokorsak-online/commit/4ecfd464e26f44de2c9606be761dd3e7ea1fb76f)  
**Source of truth:** `docs/devel-specs/0002-round-robin-starting-numbers-and-draw-reset.md` (Approved, amended).  
**Verdict:** **Changes requested: 1× P1 and 2× P2.** Core design accepted, but the player-card starting-number field currently blocks ordinary valid saves; two additional frontend/data-handling cases need correction. No owner decision needed.

## Management summary

- **Architecture:** The implementation mostly follows 0002: editable tournament-local `Participant.StartingNumber` drafts with canonical frozen `PairingStage.FrozenRosterIds`; explicit entered/random starting-number source; preflight confirmations for adjusting the terminal RR round count and replacing unplayed fixtures; staged, transactional draw/clear commands; the current-entered-results RR draw lock (excluding byes); and the narrowly conditional Settings unlock after an explicit successful single-stage Clear. Automatic non-RR generators remain unimplemented.
- **Referee-visible behavior:** A compact editable starting-number table appears on Pairings, numbers also appear in the player card; the referee can draw randomly or with entered values, resize to one Berger cycle after confirmation, clear all RR-stage pairs, and undo an erroneous RR system choice after clear. These main server-side flows have meaningful regression tests.
- **Blocking/remaining findings:** One P1 makes the player-card input unusable for nonempty numbers, including otherwise routine edits of already numbered participants. Two P2 issues cause a typed zero to silently clear a number, and allow unsaved table edits to be lost when clearing a manually assembled RR stage.
- **Owner decisions:** None. Keep approved 0002 immutable; correct code and tests, then request re-review.

## Independent verification

On the authorized Windows checkout (`D:\pet_prj\autokorsak-online`), clean HEAD `4ecfd464`:

- `docker compose --profile test run --rm backend-tests`: **124 passed, 0 failed**, exit code 0.
- `docker compose --profile test run --rm frontend-tests`: **17 passed, 0 failed**, exit code 0. TypeScript checking completed.
- Reviewed changed domain, API, migration/compatibility, localization, React code and both new test files against approved spec 0002 and retained 0001 invariants.
- Did **not** restart the running app, deploy, edit code, change Docker volumes or touch actual referee tournament data. Claude documents an independent disposable-instance browser smoke in `docs/status.md`; this review did not repeat that interactive browser check.

Passing tests do not cover the following concrete bugs.

## Findings

### P1-01 — Player card rejects **every nonempty numeric starting number**, including unchanged frozen ones

**File:** `frontend/src/pages/PlayerEditor.tsx`, `save()`, lines **49–55**.

**Code:**
```ts
const trimmed = numberText.trim();
if (trimmed !== '' && !/^d+$/.test(trimmed)) {
  setError(new ApiError('startingNumbers.invalidValue', { value: trimmed, max: 999 }, 400));
  return;
}
```

The regex is `/^d+$/` (literal letter **d**), **not** `/^\d+$/` (digits). Every normal value such as `1`, `2`, or `12` therefore triggers a client-side validation error **before the player save request**.

**Reproduction A:** Create an RR tournament, open a player card, enter starting number `1`, click Save. The card rejects the input as invalid. Thus the spec-required manual player-card entry is impossible.

**Reproduction B, more severe:** Assign numbers through the Pairings table, generate RR, then open any now-numbered participant and change only their name, rating or comment. Since `numberText` initializes from the saved frozen starting number and the check is unconditional (runs even when the input is disabled or unchanged), Save fails as soon as the stored number is nonempty. This violates 0001's independent name/rating edits after the RR roster freezes as well as 0002 §2.1.

**Expected:** Correct the digit validation and only validate the starting-number text when it is relevant/editable or changed; an unchanged read-only frozen number must never prevent unrelated edits. Preserve server-side range/eligibility validation and the existing null=unchanged / 0=clear card payload convention. Add React component tests for a new `1`, editing an unfrozen assigned number, and editing an unrelated field after the number has been frozen. Include an actual edit/save check in the bilingual smoke test.

### P2-01 — Entering the literal `0` in the Pairings number table **silently clears** the field

**Files:** `frontend/src/pages/RoundRobinPanel.tsx`, lines **64–73**; `backend/src/AutoKorsak.Domain/TournamentEditor.cs`, `SetStartingNumbers`, lines **305–316**.

The table accepts `0` as a syntactically valid string (`/^\d+$/`), converts it to numeric zero and sends `{ participantId, number: 0 }`. The server translates `0` to `null` before checking the number, treating it as a **clear**, although the 0002 table API contract is `number: null` to clear; user-entered numbers are positive (1–999) and generation requires 1..N. The localization even says “whole number from 1 to 999”.

**Reproduce:** Set a draft number to `3` in the table and save. Change its visible field to `0`, save again. The server silently erases the saved value and the UI displays a blank rather than pointing to invalid input. This is silent data loss of an entered draft.

**Expected:** In the Pairings table, an **empty field** sends `null` (clear); a typed literal zero must give a specific localized invalid-value error with no mutation. Enforce `0` invalid in the **batch API** as well, not only via React, so a direct request cannot bypass the rule. The existing player-card `0` sentinel can remain an internal compatibility convention, but user-entered literal `0` in that card should likewise be rejected rather than silently cleared. Add table/component plus domain/API regression tests.

### P2-02 — Clear RR Draw can silently discard unsaved batch-table edits

**File:** `frontend/src/pages/RoundRobinPanel.tsx`, lines **39–40**, **134–153**, **158–165**, **203–210**.

The panel keeps unsaved starting-number edits only in local React `drafts`. It disables **Generate** when `dirty`, but **Clear round-robin draw** is disabled only when a result lock exists. After a successful Clear the server tournament timestamp changes and the `useEffect` unconditionally calls `setDrafts({})`.

**Real in-scope case:** The referee manually adds one or more unplayed pairings within the RR stage (so clearing is permitted, stage is not frozen), begins changing starting numbers in the Pairings table without clicking the batch Save button, then clicks **Clear** and confirms. All local edited numbers silently disappear when the response updates the tournament, even though they were not part of the server-side draw that the referee intended to clear. No warning says the unsaved table fields will be discarded.

**Expected:** Prevent Clear while the table has unsaved edits, with the same “save or discard starting-number changes first” behavior already used by Generate; alternatively provide an explicit separate confirmation that the unsaved **draft edits** will be discarded. Prefer disallowing Clear while dirty for consistency. Add a UI regression asserting Clear cannot run while edits are dirty and becomes available after an explicit Save or Discard. Keep the existing confirmation about removing pairings/byes and the server-side result lock.

## Regression/test requests

1. Add a `PlayerEditor` interaction test covering save after typing a positive starting number and save of another field when the RR number is already frozen.
2. Add test cases for typed zero in the table **and** direct batch API; empty `null` remains a valid clear.
3. Cover unsaved `RoundRobinPanel` drafts when attempting Clear (no silent loss).
4. Run both Docker test profiles, a frontend build and a short **non-production** UI smoke (uk/en for number entry).
5. Reply in `docs/review/0002-review-gpt6-4ecfd464-response.md` with per-finding disposition and exact test results.

## Accepted aspects (do not regress)

- Valid manual permutation and its canonical frozen roster; backward compatibility with frozen 0001 schedules and draftless undrawn stages.
- Inactive players' retained draft numbers are ignored until eligible, and a proper batch save can temporarily hold duplicates.
- Number-source choice first, round-count confirmation second; canceled or unsafe generation has no partial state change.
- Single-cycle count adjustment, prefix RR→Swiss preservation and protected deletion of out-of-range metadata/pairings on shrink.
- Clear removes the whole RR-stage fixture, including byes and unplayed manual pairs; entered two-player results lock both clear and generation, while a bye alone does not.
- Explicit single-stage clean Clear can release Settings system choice; manual per-pair deletion and multi-stage Clear cannot.
- Original 0000 and accepted 0001 specs remain untouched.

**Review action:** only this review document is committed. Code, runtime services and referee data were not changed.
