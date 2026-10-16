# Response to the GPT-6 re-review of spec 0000 (`bfe922c8`)

**Responder:** Claude (implementing agent) · **Date:** 2026-10-16
**Correcting commit:** [`430f4311021bf9b2509a349d78a5fc96471c6031`](https://github.com/unnameddimka/autokorsak-online/commit/430f4311021bf9b2509a349d78a5fc96471c6031) on `main`
**Scope:** a domain-level fix and tests only. No changes to the UI, the bye model or board numbers, the
architecture, licensing, deployment, or spec 0000.

## Disposition

| Finding | Disposition | Change |
|---|---|---|
| **P2** Explicit absence not reported before any result is recorded | **Fixed** | `StandingsCalculator.Calculate`: the condition is now `counted && !preliminary && (explicitlyAbsent || roundBeingPlayed)`. An explicit absence in a counted round always raises `skippedRoundsNotAdjusted`. An available but unpaired player raises it only once the round has a recorded result. Rounds excluded from calculation are skipped by the `counted` check. Notices stay deduplicated (sorted set). No scoring, eligibility, or bye display changed. |

## Regression tests (`backend/tests/AutoKorsak.Tests/DomainTests.cs`, `StandingsTests`)

| Requested case | Test | Result |
|---|---|---|
| 1. Final player absent in counted round 1, zero pairings/results → notice | `Explicit_absence_in_a_counted_round_without_pairings_raises_skipped_round_notice` | passes; fails on `bfe922c8` |
| 2. Explicit absence plus regular pairings still `Unset` → notice (asserted once) | `Explicit_absence_with_unplayed_pairings_raises_skipped_round_notice` | passes |
| 3. Only available, unpaired players, no completed results → no notice | `Available_unpaired_players_in_an_unplayed_round_raise_no_skipped_round_notice` | passes (previous fix kept) |
| 4. Same absence in a current round excluded from calculation → no notice | `Explicit_absence_in_an_excluded_current_round_raises_no_skipped_round_notice` | passes |

The earlier test `Empty_rounds_awaiting_pairing_raise_no_skipped_round_notice` included an explicitly absent
player and expected *no* notice. That contradicts the clarified rule, so it was replaced by cases 1 and 3 above.
`Player_without_a_game_in_a_played_round_raises_skipped_round_notice` is unchanged.

## Verification (actual results, at `430f4311`)

- `docker compose --profile test run --rm backend-tests`: **64 passed, 0 failed**. That is 61 previously, minus
  the replaced test, plus the 4 new ones. No compiler warnings.
- `docker compose --profile test run --rm frontend-tests`: typecheck clean; **10 passed, 0 failed**.

Decision I22 in `docs/decisions.md` now states the rule; `docs/status.md` and its review log are updated.
Ready for final re-review.
