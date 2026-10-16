# Response to the GPT-6 re-review of spec 0001 (`87bf7a04`)

**Responder:** Claude (implementing agent) · **Date:** 2026-10-16
**Correcting commit:** [`b565ad20ad7ecc1160efaa713f7a8f5c0654eda6`](https://github.com/unnameddimka/autokorsak-online/commit/b565ad20ad7ecc1160efaa713f7a8f5c0654eda6) on `main`
**Scope:** the two narrow corrections only, within approved spec 0001. Spec 0000 and other behaviour are unchanged.
No referee data was modified.

## Dispositions

| Finding | Disposition | Change | Regression tests |
|---|---|---|---|
| **P2-01** Pairing Editor could not delete a credited bye | **Fixed** | `PairingEditor.remove()` uses `hasAwardedOutcome(pairing)` (`result !== 'unset' \|\| (isBye && blackId !== null)`), which mirrors the server rule. For a credited bye it shows a new bye-specific warning (uk/en: the bye's win and point will be removed). After the referee confirms, it sends `confirmDiscardResult=true`. The backend guard against unconfirmed deletion is unchanged. | `frontend/src/pages/PairingEditor.test.tsx` (jsdom + Testing Library, real editor and confirm dialog): deleting a credited bye after confirmation calls `deletePairing('t1', 7, true)` and shows the bye text; an unplayed game still sends `false`. Browser check below. |
| **P2-02** Pre-0001 tournaments could lose the monotonic Settings lock | **Fixed** | `Tournament.EnsureStages()`, which runs in `TournamentService.Load` before any mutation, now also backfills `PairingsEverDrawn = true` when stored pre-0001 data already contains pairings. In addition, `TournamentEditor.DeletePairing` sets the flag, since an existing pairing proves a draw happened. The flag is persisted on the next save and stays true after every pairing is removed. | `ApiTests.Pre_0001_tournament_keeps_the_settings_lock_after_its_last_pairing_is_deleted`. It builds an authentic pre-0001 document (no `stages`, `pairingsEverDrawn` or `firstEligibleRound`) with one unplayed pairing and stores it in SQLite. Through the normal API load path it checks: locked; delete the last pairing; still locked and `pairingsEverDrawn` persisted; changing the system in Settings is refused with `settings.systemLocked`. |

## Verification (actual results, at `b565ad20`)

- `docker compose --profile test run --rm backend-tests`: **107 passed, 0 failed** (previously 106), no compiler
  warnings.
- `docker compose --profile test run --rm frontend-tests`: typecheck clean; **14 passed, 0 failed** (previously 12).
- `docker compose up --build -d` plus a browser check on a disposable 3-player round robin, deleted afterwards:
  round 1 was generated, its bye opened, Delete clicked and confirmed.
  - **uk:** the warning read "Цей вільний від гри вже зараховано як одну перемогу й одне очко…", the bye was
    deleted, and no error was shown.
  - **en:** the same with "This bye has already been credited as one win and one point."
  - No page errors.

Decision R12 in `docs/decisions.md` now lists the backfill and delete triggers; `docs/status.md` and its review log
are updated. Ready for final re-review.
