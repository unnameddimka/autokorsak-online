# Response to the GPT-6 review of spec 0001 (`e7b57590`)

**Responder:** Claude (implementing agent) · **Date:** 2026-10-16
**Correcting commit:** [`87bf7a049c8432825583e42368e37322fadaa11c`](https://github.com/unnameddimka/autokorsak-online/commit/87bf7a049c8432825583e42368e37322fadaa11c) on `main`
**Scope:** corrections within approved spec 0001. No spec change, no new owner decisions. Spec 0000 untouched.
Bye = +1 scoring and the immutable played-game contract are unchanged. No referee data was modified.

## Dispositions

| Finding | Disposition | Change | Regression tests |
|---|---|---|---|
| **P1-01** Frozen RR member could be made unavailable in an undrawn RR round | **Fixed** (reject at save) | `TournamentEditor.Apply`: newly unavailable rounds covered by a frozen RR stage that contains the player are refused with 409 `participant.rosterMemberUnavailable` (uk/en text points to referee-decision technical results). The player editor disables those checkboxes and explains why. Availability after the RR stage stays editable. Imported states that already contain such absences still stop generation with `generate.playerUnavailable`. | `Spec0001Tests.Frozen_roster_member_cannot_become_unavailable_in_a_round_robin_round` (round 1 drawn only; edit refused; zero changes; generate-all still works), `…Availability_after_a_round_robin_stage_stays_editable_for_roster_members`, `ApiTests.Frozen_round_robin_member_availability_edit_is_refused_and_nothing_changes` |
| **P2-01** Settings lock forgotten after deleting pairings | **Fixed** | New monotonic `Tournament.PairingsEverDrawn`, set by the first manual or generated pairing (and on import when pairings exist). `IsSystemLocked` = second stage, ever drawn, frozen roster, or pairings present. Enforced in the backend (`settings.systemLocked`) and reflected in the UI through `systemLocked`. | `…Settings_system_lock_is_remembered_after_all_pairings_are_deleted` (RR: delete all → reload → update refused; manual Swiss: draw → delete → reload → locked) |
| **P2-02** Unplayed RR games with swapped colours silently kept | **Fixed** (as recommended; decision R4 revised) | `PlanRound`/`Matches`: only games **with a recorded result** match in either colour. Unplayed games must match the exact Berger orientation, otherwise `generate.replaceUnplayed` asks for confirmation before restoring it. | `…Unplayed_pairing_with_swapped_colours_is_restored_only_after_confirmation` (refusal is a no-op; the confirmed request restores the fixture). The existing played-swap test still passes. |
| **P2-03** Credited bye not treated as an outcome | **Fixed** | New `Pairing.HasAwardedOutcome` = recorded result **or** bye with a real player (decision R11). It is used for: joining-round defaults (`RoundHasResults`); the stage-change barrier (a credited bye blocks a change exactly like a played game, `stage.roundsHaveResults`); generator immutability (a credited bye that is not the fixture bye fails with `generate.playedPairingConflicts`, zero changes; the fixture bye is kept); and the discard confirmation for deleting or reassigning a pairing. A playerless legacy bye is not an outcome (R6). | `…A_credited_bye_counts_as_an_outcome_for_joining_defaults` (manual Swiss bye as the only outcome → default round 2), `…A_credited_bye_blocks_a_stage_change_and_needs_confirmation_to_delete`, `…A_credited_bye_that_conflicts_with_the_schedule_blocks_generation` (odd RR), `…The_fixture_bye_is_kept_when_regenerating_an_odd_round_robin` |
| **P2-04** Players list showed pre-joining rounds as available | **Fixed** | A new `roundParticipation()` helper gives "not joined" / "not playing" / "plays". The Players list shows a distinct marker and a "not yet joined" tooltip (uk/en) for rounds before the joining round. | `frontend/src/format.test.ts` (rounds 1–5 of a round-6 entrant are never "plays"; roster-round locking). Browser check: markers `·····●●`, first tooltip "Round I: not yet joined". |

One note on P2-03: the "round is being played" condition for **skipped-round notices** still counts only two-player
results. That way a referee who enters a Swiss bye before the other pairings doesn't trigger false absence warnings
for players who haven't been paired yet. This is documented in R11.

## Verification (actual results, at `87bf7a04`)

- `docker compose --profile test run --rm backend-tests`: **106 passed, 0 failed** (previously 97), no compiler
  warnings.
- `docker compose --profile test run --rm frontend-tests`: typecheck clean; **12 passed, 0 failed** (previously 10).
- `docker compose up --build -d` plus a browser check on a temporary tournament, deleted afterwards (the referee's
  data was untouched):
  - the late entrant's markers show "not yet joined";
  - roster member availability is locked in RR rounds I–III and free in IV–VII, with an explanatory hint;
  - the Settings system stays read-only after all pairings are deleted;
  - no page errors.

Decisions R4 and R7 were revised and R11–R13 added in `docs/decisions.md`; `docs/architecture.md`, `docs/status.md`
and its review log are updated. Ready for re-review.
