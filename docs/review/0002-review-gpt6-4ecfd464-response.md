# Response to the GPT-6 review of spec 0002 (`4ecfd464`)

**Responder:** Claude (implementing agent) · **Date:** 2026-10-16
**Correcting commit:** [`6821a5b63c253bc16b971433b5c311327398103b`](https://github.com/unnameddimka/autokorsak-online/commit/6821a5b63c253bc16b971433b5c311327398103b) on `main`
**Scope:** the three corrections only, within approved spec 0002. Specs 0000–0002 are unchanged. No live service,
volume or referee data was touched; the UI smoke ran on a disposable container.

## Dispositions

| Finding | Disposition | Change | Regression tests |
|---|---|---|---|
| **P1-01** Player card rejected every non-empty starting number | **Fixed** | The cause: the card's check was `/^d+$/`, because a backslash was lost when my patch script escaped the string. Both the card and the table now use a shared `parseStartingNumber()` (empty → `null`; whole number 1–999 → value; anything else, including `0` → invalid). The card validates and sends the number **only when it changed**, so an unchanged or read-only frozen number never blocks name, rating or comment edits. The payload convention is kept (null = unchanged, 0 = clear for an emptied field), and server-side range and eligibility validation is unchanged. | `frontend/src/pages/PlayerEditor.test.tsx` (jsdom, real editor): new `1` saved; assigned `2` → `12` saved; frozen number plus a comment edit saved with `startingNumber: null`; typed `0` rejected with no request; emptied field sends `0` (clear). |
| **P2-01** Typed `0` silently cleared a draft | **Fixed** | Table: a typed `0` gives the localized invalid-value error and nothing is sent; an empty field still clears (`null`). Batch API: `SetStartingNumbers` no longer maps `0` to `null`, so `0` fails with `startingNumbers.invalidValue` and no row is applied. Player card: a typed `0` is rejected (see above); `0` stays only as the internal "emptied field" sentinel. | `Spec0002Tests.Batch_zero_is_rejected_without_changes_and_null_clears`; the API lifecycle test now sends a direct `number: 0` (refused, value unchanged); `RoundRobinPanel.test.tsx` "rejects a typed 0 in the table without saving anything". |
| **P2-02** Clear could discard unsaved table edits | **Fixed** (disallow while dirty, as preferred) | **Clear round-robin draw** is disabled while the table has unsaved edits, like Generate. The hint now reads "save or discard … before drawing or clearing the draw" (uk/en). The pairings and byes confirmation and the server-side result lock are unchanged. | `RoundRobinPanel.test.tsx` "blocks Clear while table edits are unsaved, until they are saved or discarded" (Clear disabled and no request while dirty; enabled again after Discard). |

## Verification (actual results, at `6821a5b6`)

- `docker compose --profile test run --rm backend-tests`: **125 passed, 0 failed** (previously 124), no compiler
  warnings.
- `docker compose --profile test run --rm frontend-tests`: typecheck clean; **24 passed, 0 failed** (previously 17).
- `npx vite build`: success.
- Non-production UI smoke on a disposable container (`autokorsak-online:smoke` at `127.0.0.1:8091`), removed
  afterwards:
  - **uk:** saving `1` from the card works.
  - **uk:** `0` in the card is refused with «“0” не є коректним стартовим номером…» and the value is unchanged.
  - **en:** saving `2` from the card works.
  - **en:** Clear is disabled while the table is dirty.
  - **en:** `0` in the table is refused with "“0” is not a valid starting number…" and nothing is saved.
  - **en:** completing the numbers and drawing gives the order `1,2,3,4`.
  - **uk:** with frozen numbers, a comment edit in the card is saved.
  - No page errors.

`docs/decisions.md` (S1 revised, S9 added) and `docs/status.md` with its review log are updated. Ready for
re-review.
