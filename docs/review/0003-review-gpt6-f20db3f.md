# SPEC 0003 — GPT-6 review of implementation `f20db3f`

**Review date:** 2026-10-24  
**Implementation:** [`f20db3ff9b8aa093d2b98e602eee580b6d05aed6`](https://github.com/unnameddimka/autokorsak-online/commit/f20db3ff9b8aa093d2b98e602eee580b6d05aed6)  
**Status update:** `d0965a66fc7656ed41d4550fbba0a17c8e1ba575`  
**Reference:** `docs/devel-specs/0003-referee-usability-round-robin-and-results.md`  
**Verdict:** **CHANGES REQUESTED — one P1 data-safety defect and one P2 correctness defect.** Core architectural direction accepted; do not deploy on the strength of this review.

## Summary

The implementation covers the principal agreed scope: server-derived pure round-robin round count; participation fields hidden and disregarded without deleting historical metadata; navigator moved to Standings; deliberate Ukrainian transliteration with confirmation; correct editable and displayed rating zero; app-only XML explanations; result-first pairing card; real BothWin result (1-1) in scoring, pairing locks, UI and verified legacy mapping. Relevant tests have been added or updated.

Review was based on GitHub source/diff at the specified commit and its accompanying status/test record. **Independent backend/frontend execution was not possible in this session**: the previously used Windows development machine is offline; the only available remote machine is an unrelated online Ubuntu device, which was not used to check out, mutate, or run this application. The implementation author reports **140 backend and 48 frontend tests passing**, production build and 52 disposable-instance smoke checks. These are *author-reported*, not reviewer-verified results.

## P1-01 — Automatic round-count shrink silently drops retained metadata-only rounds

**Code:** `backend/src/AutoKorsak.Domain/PairingSchedule.cs`, `ValidateRoundCountChange` (about lines 288–305) and `ApplyRoundCount` (307–312); `backend/src/AutoKorsak.Domain/Model.cs`, `Round.LegacyXml`; `backend/src/AutoKorsak.LegacyXml/LegacyXmlExporter.cs`, `WriteRound`.

**Reproduction:** Take an undrawn pure RR tournament with three finalized players and provisional `NumberOfRounds = 6`. Preserve a round 5 entity with **zero pairings** but nonempty `LegacyXml` (a `TournamentRound` subtree containing an additional historical XML field); capture the JSON before generation. Generate the full cycle; the correct target is three rounds. Currently `ValidateRoundCountChange` only blocks truncated round entities with `Pairings.Count > 0`, so it approves this shrink. `ApplyRoundCount` then calls `t.Rounds.RemoveAll(r => r.Number > target)` and permanently deletes the retained round XML from the stored aggregate. `LegacyXmlExporter.WriteRound` proves that this field holds original metadata to be retained, not an irrelevant cache.

**Required behavior:** Treat `Round.LegacyXml` and any other retained round metadata beyond the proposed end as unsafe to drop, **even if the round has no pairings**; return a localized actionable conflict (e.g. `generate.roundCountShrinkBlocked`) without changing any tournament JSON. Truly empty, metadata-free rounds may still shrink automatically. This is required by spec 0003 §2.2/§8 and spec 0002's no-historical-metadata-loss invariant. Do **not** introduce a force-discard option.

**Regression tests:** Domain test that inserts a metadata-only round above the new target, asserts the generation is refused and JSON is exactly unchanged; paired-round refusal and safe shrink remain green. If practical, add a fixture-driven old-XML round test.

**Severity:** P1 — irreversible data loss from an automatic operation.

## P2-01 — Pure RR automatic shrink fails when the provisional current round is outside the calculated range

**Code:** `backend/src/AutoKorsak.Domain/PairingSchedule.cs`, approximately lines 110–149.

**Reproduction:**
1. Create a pure round-robin tournament (the default internal `NumberOfRounds` is 5).
2. Before drawing, use the current-round control to select round 4 or 5 (supported while the pre-draw number is provisional).
3. Finalize **three** actual players; give them numbers 1, 2 and 3 or select random numbering.
4. Request **Generate all round-robin rounds** from the Pairings page.
5. Server computes a three-round fixture, but then compares the *old* requested round (4 or 5) to the newly derived end (3) and throws `round.outOfRange` before committing the draw.

The relevant sequence is:
```cs
var roundCount = t.NumberOfRounds;
// required is 3, safely shrunk from 5 ...
var end = t.HasLaterStage(stage) ? t.StageEnd(stage) : roundCount;
if (round > end) throw new DomainException("round.outOfRange", ...);
var targets = allRounds ? Enumerable.Range(start, length).ToList() : new List<int> { round };
```

The validated resize would already clamp `CurrentRound` in `ApplyRoundCount`, but this check aborts **before** reaching the clamp. In `allRounds == true` mode, the selected old round is not actually needed to determine targets. This contravenes 0003 §2.2 and acceptance scenario 2: safe automatic resizing should not need user editing of a provisional count or selected round.

**Required fix:** When `allRounds == true`, allow a now-out-of-range *pre-resize* requested current round (within the original stored range) and generate every round of the computed cycle, with `CurrentRound` clamped on successful atomic commit. Keep round validation for `allRounds == false` and existing explicit out-of-range API requests. Another safe approach is to normalize the selected round appropriately at the generation entry point before validating the computed count; **do not silently choose a different single round**. Ensure no partial writes if later validations fail.

**Tests:** Add domain + API regression with provisional 5 rounds, current/route round 5, three finalized players, `allRounds = true`; expect 3 generated rounds and clamped current round 3. Also cover `allRounds = false`: return a clear error without changing anything. Verify no regression for frozen rosters, mixed stages, or already recorded results.

## Non-blocking observations

- `LegacyFormat.ResultToken` maps BothWin to `1-1`, and importer maps the same token back. The implementation reports serializer evidence under `docs/decisions.md`; no unexplained coercion of unknown result tokens was observed.
- The new `Tournament.IsPureRoundRobin`, `Plays` and `EffectiveJoinRound` helpers correctly avoid making hidden historical skip flags authoritative in pure RR, without erasing them; mixed stages continue to use the existing rules.
- `SettingsPage` renders a pending read-only RR count and a frozen count after the draw. `TournamentEditor.ApplySettings` ignores submitted round counts for single-stage RR. Other systems retain editing.
- The player card rating field now keeps editable text; Players and Standings explicitly render numeric zero. Transliteration is an explicit action, and the pairing editor places players/result first.
- Leave the originally approved specs 0000–0002 unchanged. No product decision is required to fix P2-01.

## Requested implementer reply

Fix P1-01 and P2-01 with regression tests, run all backend/frontend checks and disposable-instance smoke, and document the correction in `docs/review/0003-review-gpt6-f20db3f-response.md`. Do not deploy or mutate the live referee tournament data. Request re-review at the new commit.
