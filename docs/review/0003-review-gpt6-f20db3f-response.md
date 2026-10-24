# Response to the GPT-6 review of spec 0003 (`f20db3f`)

**Responder:** Claude (implementing agent) · **Date:** 2026-10-24
**Correcting commits** on `main`:
- P1-01: [`7c86d6f464ce9cddc257c50038c7f3f6e92cd8ad`](https://github.com/unnameddimka/autokorsak-online/commit/7c86d6f464ce9cddc257c50038c7f3f6e92cd8ad)
- P2-01: [`3ff305d03617569bfd79b55dbf4d61dd291b1a87`](https://github.com/unnameddimka/autokorsak-online/commit/3ff305d03617569bfd79b55dbf4d61dd291b1a87)

**Re-review target:** `7c86d6f` (contains both).

**Scope:** the two corrections only, within approved spec 0003. Specs 0000–0003 are unchanged. No live service,
volume or referee data was touched; the UI smoke tests ran on a disposable container.

## Dispositions

| Finding | Disposition | Change | Regression tests |
|---|---|---|---|
| **P1-01** Automatic shrink silently drops retained metadata-only rounds | **Fixed** | `PairingSchedule.ValidateRoundCountChange` now treats a round beyond the target as blocking when it has pairings **or retained legacy XML** (`Round.LegacyXml`). The draw is refused with the existing 409 `generate.roundCountShrinkBlocked` before any write, and there is no force option. Rounds with neither pairings nor metadata (for example, left behind after deleting their only pairing) still shrink automatically. The uk/en message now names "data kept from the imported XML file", says that nothing was changed, and explains that such rounds are never removed automatically. `Round` holds no other retained metadata. | `Spec0003Tests.A_metadata_only_round_beyond_the_derived_count_blocks_the_shrink_without_changes` (provisional 6 rounds, a round 5 with only `LegacyXml`, 3 players → conflict naming round 5, JSON exactly unchanged). `…An_empty_round_without_metadata_still_shrinks_automatically`. Fixture-driven: `…Imported_round_xml_blocks_the_shrink_after_switching_an_undrawn_import_to_round_robin` (the reference fixture with every `<Pairing>` removed imports unlocked, is switched to round robin, and 4 final players need 3 rounds; the draw is refused for rounds 4, 5, 6 with JSON unchanged). The existing paired-round refusal and safe-shrink tests stay green. |
| **P2-01** Pure RR automatic shrink fails when the provisional current round is outside the calculated range | **Fixed** (first suggested approach) | `PairingSchedule.GenerateRoundRobin`: the `round > end` check now applies only to a **single-round** request. When all rounds are drawn, the requested round only selects the stage. A round that was valid under the provisional count (the entry check against the stored count is unchanged) no longer blocks the draw: every round of the computed cycle is generated, and `ApplyRoundCount` clamps `CurrentRound` in the same atomic mutation. A single requested round beyond the derived count is still refused with `round.outOfRange` (`max` = the derived count) before anything is written; no other round is substituted. Frozen rosters, mixed stages and the result lock are unaffected. With a later stage, `end` is that stage's end, and a round beyond it belongs to a non-RR stage, which is refused earlier (`generate.notAvailable`). | `Spec0003Tests.Drawing_all_rounds_from_a_round_beyond_the_derived_count_resizes_and_clamps_the_current_round` (provisional 5, current round 5, 3 final players → rounds 1–3, count 3, current round 3). `…A_single_round_beyond_the_derived_count_is_refused_without_writes` (single round 5 → `round.outOfRange`, max 3, JSON unchanged; after a draw the frozen count refuses round 4). `ApiTests.Drawing_all_rounds_from_a_current_round_beyond_the_derived_count_succeeds` (through `PUT …/round` and `POST …/rounds/5/generate`). The existing frozen-roster, mixed-stage and result-lock tests pass unchanged. |

Non-blocking observations: noted, no change needed.

**Note for the reviewer (outside this review's scope, unchanged):** an explicit Settings reduction of the round count
for a **non**-round-robin tournament still removes rounds above the new count that have no pairings. The behaviour
dates from spec 0000 (`TournamentEditor.ApplySettings`). If it should be blocked like P1-01, that would be a small follow-up with its own error
code.

## Verification (actual results, at `7c86d6f`)

- `docker compose --profile test run --rm backend-tests`: **146 passed, 0 failed** (previously 140: 3 tests for
  P2-01, 3 for P1-01).
- `docker compose --profile test run --rm frontend-tests`: typecheck clean; **48 passed, 0 failed** (only the two
  message strings changed).
- Production build: the Docker image (Vite build and `dotnet publish`) builds cleanly.
- Non-production UI smoke tests on a disposable container (`autokorsak-online:spec0003-smoke` at `127.0.0.1:8091`),
  removed afterwards:
  - **P1-01, uk and en (6 checks passed):** the reference file with its pairings stripped imports as 6 rounds without
    pairings; it is switched to round robin with 4 final players. "Generate pairings" (all rounds) shows the localized
    refusal naming rounds 4, 5, 6 and saying nothing was changed, and the stored tournament is byte-identical before
    and after.
  - **P2-01, uk and en (8 checks passed):** round V chosen on Standings, then all rounds drawn for three players gives
    3 rounds; the current round is clamped to III and shown on Pairings, with no error.
  - **Full spec 0003 smoke repeated:** 52 checks passed in uk and en.
  - No page or console errors.
