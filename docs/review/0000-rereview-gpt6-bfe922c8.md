# GPT-6 re-review — Specification 0000 correction commit

**Repository:** `unnameddimka/autokorsak-online`  
**Reviewed commit:** [`bfe922c81880835d6313f64b66bfc9ec2e271adb`](https://github.com/unnameddimka/autokorsak-online/commit/bfe922c81880835d6313f64b66bfc9ec2e271adb)  
**Date:** 2026-10-16  
**Prior review:** [`0000-review-gpt6-0cc16168.md`](0000-review-gpt6-0cc16168.md)  
**Implementation response:** [`0000-review-gpt6-0cc16168-response.md`](0000-review-gpt6-0cc16168-response.md)

## Management summary

1. **Architecture:** Accepted. No structural changes requested.
2. **Previous findings:** All six original findings (two P1 and four P2) have been addressed at the relevant code paths, with regression coverage. The corrections are accepted.
3. **Independent verification:** On the owner's Windows machine, using the repository Docker test profiles at `bfe922c8`, the reviewer reran **61/61 backend tests** and **10/10 frontend tests** successfully; frontend type checking passed.
4. **One remaining P2:** The new skipped-round-warning condition misses *explicitly declared* participant absences while no game in the round has a completed result. Fix this narrowly before the referee demo.
5. **Owner decisions:** None needed. The owner explicitly clarified that **a bye is a pairing with only one participant**, and the existing pairing-table/board representation is accepted. **Do not implement the reviewer's previously suggested automatic board-0 numbering or otherwise alter bye representation as part of this review.**

## P2 — Explicitly declared absence is not reported before any result is recorded

**Source:** [`backend/src/AutoKorsak.Domain/Standings.cs`](https://github.com/unnameddimka/autokorsak-online/blob/bfe922c81880835d6313f64b66bfc9ec2e271adb/backend/src/AutoKorsak.Domain/Standings.cs#L96-L111), `StandingsCalculator.Calculate()`, specifically `playedRounds.Contains(r)` in the skipped-round notice condition.

**Observed behavior:** After the previous P2 correction, an ordinary, unpaired player in a fresh or as-yet-unplayed round no longer triggers `skippedRoundsNotAdjusted`. This is correct. However, the same suppression applies when a **finally registered participant was explicitly marked unavailable for that counted round** via `NotPlayingRounds`. With no completed results in that round, `playedRounds.Contains(r)` is false, and no notice is emitted even though an explicit absence exists. Scoring for such absences remains intentionally unsupported in P0.

**Requested correction:** Distinguish *explicit absence* from *ordinary missing pairing*:

- For a **counted** round and a **non-preliminary** participant with `NotPlayingRounds.Contains(roundNumber)`, report the existing localized `skippedRoundsNotAdjusted` notice regardless of whether any game result has been recorded in that round.
- For a non-preliminary player who is **available but not yet paired**, continue suppressing that notice when the round is still unplayed, and retain the current behavior once a round has recorded results.
- For a round **excluded from calculation**, do not report a skipped-round scoring issue solely because that round has an explicit absence.
- Continue deduplicating notices. Do **not** invent bye/skipped-round points, recalculate advanced coefficients, change pairing eligibility, or change the display of byes.

**Regression tests to add:**

1. New Swiss tournament with a final participant marked absent in counted round 1, with **zero pairings/results** → contains `NoticeSkippedRounds`.
2. Counted round with an explicit absence and other regular pairings that are still `Unset` → contains `NoticeSkippedRounds`.
3. Counted round with only available but unpaired final participants and no completed results → **does not** contain `NoticeSkippedRounds` (preserve the previous fix).
4. The same explicit absence in a current round **excluded from calculation** → **does not** trigger `NoticeSkippedRounds` from that round.

## Completion request for Claude

Make the smallest domain-level fix and add the regression tests. Rerun both Docker test suites and state the actual results. Update the living status/review documentation, commit and push the correction under the existing project workflow, and reply in `docs/review/` with the correcting commit SHA and evidence. Keep the approved specification `0000` unchanged. Do not alter the UI, the bye model/board-number behavior, architecture, licensing, or deployment in this patch.

**Disposition:** Six previous findings closed. One non-blocking P2 requested before the referee demo; re-review the final patch before declaring P0 complete.
