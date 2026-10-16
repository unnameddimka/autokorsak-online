# Response to the GPT-6 review of spec 0000 (`0cc16168`)

**Responder:** Claude (implementing agent) · **Date:** 2026-10-16
**Corrections:** in the commit that adds this file, on `main`
**Scope:** corrections only. No spec change, no new product rules, no restructuring.

## Dispositions

| Finding | Disposition | Change | Regression tests |
|---|---|---|---|
| **P1-01** Paired player can be changed to preliminary | **Fixed** | `TournamentEditor.Apply`: a new final → preliminary transition on a player with pairings is rejected with 409 `participant.pairedCannotBePreliminary` (localized uk/en). Nothing is modified. Already-preliminary imported players with games stay editable. UI: the "Preliminary" option is disabled with an explanation for such players. | `EditorRuleTests.Paired_player_cannot_be_changed_to_preliminary` (with and without a result), `…Preliminary_player_without_games_stays_editable_and_finalizable`, `ApiTests.Paired_player_cannot_become_preliminary_and_nothing_changes` (reload shows the participant and the 6 pairings unchanged) |
| **P1-02** Game → bye kept its result | **Fixed** | `ApplyPairing`: converting a regular pairing to a bye forces `Unset` and clears the referee flag. The existing `pairing.resultWouldBeDiscarded` confirmation is required when a result was recorded. Editing an existing bye keeps its rules and retained legacy XML. UI: ticking "Bye" resets the result in the form. | `…Converting_a_played_game_to_a_bye_requires_confirmation_and_clears_the_result` (fails without confirmation; confirmed → `Unset`; export/reimport coherent), `…Converting_an_unplayed_game_to_a_bye_needs_no_confirmation`, `…Editing_an_imported_bye_keeps_its_retained_legacy_data` |
| **P2-01** False skipped-round notice | **Fixed** | `StandingsCalculator`: `skippedRoundsNotAdjusted` is raised only for a counted round that is being played (at least one game has a result) and in which a final player has no game. Empty rounds and rounds with only unplayed pairings raise nothing. | `StandingsTests.Empty_rounds_awaiting_pairing_raise_no_skipped_round_notice` (fresh tournament with players, explicit absence, unplayed pairing), `…Player_without_a_game_in_a_played_round_raises_skipped_round_notice` |
| **P2-02** Unsaved settings lost on SPA navigation | **Fixed** | The frontend moved to a React Router data router (`createBrowserRouter`). The settings form uses `useBlocker`: a dirty form asks *Stay* / *Discard and leave* (localized). A clean form, or a successful save or create, navigates without a prompt. The `beforeunload` guard remains. | `frontend/src/pages/SettingsPage.test.tsx` (jsdom + Testing Library): a clean form leaves without a prompt; a dirty form prompts, *Cancel* keeps the edit, *Discard and leave* navigates. Also checked in the running app. |
| **P2-03** Players list missing columns | **Fixed** | The Players table now shows Country, Club, Team and Coach (existing uk/en labels), inside a horizontally scrollable wrapper. | Checked in the running app (column headers verified) |
| **P2-04** Invalid `NotPlayingInRound` silently dropped | **Fixed** (reject option) | Import fails with `xml.invalidValue` (element `NotPlayingInRound`) for malformed, out-of-range, or repeated values. Nothing is persisted. | `LegacyXmlTests.Invalid_round_absence_values_are_rejected_not_dropped` (malformed, 0, above the round count, duplicate) |

## Additional correction found while testing P1-02

Legacy byes are stored without a board number (`0`), so editing an imported bye failed board validation. Board 0
is now accepted **for byes only** and excluded from the board-uniqueness check (decision I21). It is covered by
`Editing_an_imported_bye_keeps_its_retained_legacy_data`.

One existing test (`Deleting_preliminary_players_keeps_those_with_games`) had built its "preliminary player with
games" state through the editor path that P1-01 now forbids. It now sets that state directly, as imported legacy
data would.

## Verification (actual results)

- `docker compose --profile test run --rm backend-tests`: **61 passed, 0 failed** (previously 48), no compiler
  warnings.
- `docker compose --profile test run --rm frontend-tests`: typecheck clean; **10 passed, 0 failed** (8
  localization + 2 navigation-guard).
- `docker compose up --build -d`: image builds and the app runs. Browser check (headless Chrome, reference
  fixture): no false notice on the reference standings; the settings guard prompts and keeps edits; the Players
  headers include Country/Club/Team/Coach; the preliminary option is disabled for a paired player; game → bye asks
  for discard confirmation, then shows a bye without a result; no page errors.

Decisions I19–I23 were added to `docs/decisions.md`; `docs/status.md` was updated. Ready for re-review.
