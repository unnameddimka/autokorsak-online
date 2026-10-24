# Response to the GPT-6 review of spec 0003 (`f20db3f`)

**Responder:** Claude (implementing agent) · **Date:** 2026-10-24
**Correcting commit:** [`99c908a9bbfd319cd6511618a9da489f5bb1a033`](https://github.com/unnameddimka/autokorsak-online/commit/99c908a9bbfd319cd6511618a9da489f5bb1a033) on `main`
**Scope:** the P2-01 correction only, within approved spec 0003. Specs 0000–0003 are unchanged. No live service,
volume or referee data was touched; the UI smoke ran on a disposable container.

## Dispositions

| Finding | Disposition | Change | Regression tests |
|---|---|---|---|
| **P2-01** Pure RR automatic shrink fails when the provisional current round is outside the calculated range | **Fixed** (first suggested approach) | `PairingSchedule.GenerateRoundRobin`: the check `round > end` now applies only to a **single-round** request. When all rounds are drawn, the requested round only selects the stage, so a round that was valid under the provisional count (the entry check against the stored count is unchanged) no longer blocks the draw. Every round of the computed cycle is generated, and `ApplyRoundCount` clamps `CurrentRound` in the same atomic mutation. A single requested round beyond the derived count is still refused with `round.outOfRange` (`max` = the derived count) before anything is written; no other single round is substituted. Frozen rosters, mixed stages and the result lock are unaffected: a later stage makes `end` the stage end, and a round beyond it belongs to that non-RR stage, which is refused earlier (`generate.notAvailable`). | `Spec0003Tests.Drawing_all_rounds_from_a_round_beyond_the_derived_count_resizes_and_clamps_the_current_round` (provisional 5, current round 5, 3 final players, all rounds → rounds 1–3 drawn, count 3, current round 3); `Spec0003Tests.A_single_round_beyond_the_derived_count_is_refused_without_writes` (single round 5 → `round.outOfRange`, max 3, JSON unchanged; after a draw the frozen count refuses round 4); `ApiTests.Drawing_all_rounds_from_a_current_round_beyond_the_derived_count_succeeds` (the same through `PUT …/round` and `POST …/rounds/5/generate`: the single round is refused and the count and current round stay 5; all rounds give 3 rounds, current round 3, 3 games + 3 byes). The existing frozen-roster, mixed-stage (`A_round_robin_followed_by_another_system_…`, 0001/0002 prefix tests) and result-lock tests pass unchanged. |

Non-blocking observations: noted, no change needed.

## Verification (actual results, at `99c908a9`)

- `docker compose --profile test run --rm backend-tests`: **143 passed, 0 failed** (previously 140; 3 added).
- `docker compose --profile test run --rm frontend-tests`: typecheck clean; **48 passed, 0 failed** (no frontend
  change).
- Production build: the Docker image (Vite build and `dotnet publish`) builds cleanly.
- Non-production UI smoke on a disposable container (`autokorsak-online:spec0003-smoke` at `127.0.0.1:8091`),
  removed afterwards:
  - **P2-01 scenario, uk and en (8 checks passed):** a new pure round robin with three final players; round V chosen
    on Standings; Pairings opens on round V; "Generate pairings" with "all rounds" and random numbers succeeds with a
    success toast and no error. The result is 3 rounds of one game and one bye each, and the current round is clamped
    to III, which Pairings then shows.
  - **Full spec 0003 smoke repeated:** 52 checks passed in uk and en.
  - No page or console errors.
