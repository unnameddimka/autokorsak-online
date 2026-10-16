# Status

**Current version:** spec 0001 (pairing stages, round robin, bye scoring, score-group view) on top of spec 0000,
2026-10-16. Spec 0000 was accepted after review (final version `8fd3898`); spec 0001 was accepted after review
(final version `b565ad20`). Spec 0002 (manual starting numbers, round-count adjustment, clear draw) is implemented
and awaits independent review. The live container still runs the 0001 version until the owner asks to redeploy.

## What works

- **Docker:** `docker compose up --build -d` starts a single container on `127.0.0.1:8080`, with SQLite in a
  persistent volume. Data survives container restarts (verified).
- **Bilingual UI:** Ukrainian (default) and English with a header selector. The choice persists in the browser.
  Switching keeps unsaved form edits (verified in a browser).
- **Tournaments:** list (name, player count, rounds, last modified), create, open, delete (with confirmation),
  and edit settings. The settings tabs are General, Ranking criteria (enable plus reorder by drag-and-drop or
  arrows), Standings columns (visibility, order, show colors, hide empty columns), Pairing constraints (stored),
  and Calculations (shown only when "customize calculations" is on; stored).
- **Legacy XML:** import of AutoKorsak 1.38 files (all four repository fixtures import). Save as XML downloads the
  current state, with a pre-download report of anything the legacy format cannot hold. Unedited exports are
  semantically identical to the source.
- **Players:** add (optionally copied from the shared catalog), edit, delete (blocked when the player has games),
  preliminary/final registration, per-round availability, batch finalize, and batch delete of preliminary players
  (players with games are kept and reported).
- **Pairings:** manual create, edit, delete, swap colors (the result follows the players), bye, result entry
  including referee decision, handicap/komi/comment, fixed board number, and manual protection (not offered in
  round-robin rounds). The UI lists the players without a pairing and the reason.
- **Pairing stages (0001):** the effective system per round; "Change pairing system starting from round…" (e.g.
  round robin, then Swiss), which preserves earlier rounds, is blocked by recorded results in affected rounds, and
  confirms before deleting unplayed pairings. One continuous tournament and standings across stages.
- **Round robin (0001):** standard FIDE Berger schedule for the selected round, or for all round-robin rounds.
  Starting numbers come from registration order, or from one random permutation chosen at the first draw; they are
  then frozen and shown. Odd rosters get one bye per round. Played games are never changed (a swapped-colour played
  game is accepted). Replacing unplayed pairings needs confirmation. Swiss, McMahon and Scheveningen generators
  remain disabled.
- **Round-robin numbering and reset (0002):**
  - Starting numbers are edited in the player card or in an editable table on Pairings (batch save). Inactive players
    keep a disabled, retained draft.
  - The draw uses either the entered numbers (exactly 1..N, with errors that name the players and numbers) or a random
    overwrite chosen in a two-action dialog.
  - A terminal round robin with the wrong round count offers a confirmed, atomic resize-and-draw.
  - "Clear round-robin draw" removes all stage pairings (byes included) when no game has a result, and keeps the
    numbers as drafts. If nothing else remains, Settings unlocks again.
  - Any entered game result locks round-robin generation and clearing until every result is removed.
- **Joining rounds (0001):** every participant has a "joins from round" value, with a sensible default and backfill
  to round 1. Rounds before it are neither pairable nor skipped rounds. Round-robin admission rules are enforced,
  and "finalize all" finalizes the eligible players and reports skipped ones with reasons.
- **Rounds:** previous/next/direct selection of the current round, and an "include current round in calculations"
  switch.
- **Standings:** wins/points, SOS, SODOS, SOSOS, shared places, and per-round opponent/result cells with an
  optional color marker. A bye counts as one win and one point (0001). They recalculate after every change. Notices
  explain unsupported situations. The optional **"Sort by rating within score groups"** preparation view (0001)
  orders players by points from earlier rounds, then rating; the preference is remembered per tournament.
- **Shared catalog:** search when adding a player, and an explicit upsert with create/update counts, confirmation,
  and a per-player decision for possible namesakes.

## Test results (2026-10-16)

- Backend (`docker compose --profile test run --rm backend-tests`): **124 passed, 0 failed**. Coverage: reference
  fixture import shape; round VI pairings versus the legacy TXT export; the exact standings and tie-breaks from
  spec §9.1 (Кобзар Гліб 6/21/21/111, Єрємєєв Матвій 5/17/11/113) and the legacy table order; unedited export
  deep-equal to the source for all four fixtures; edit round-trip; preservation of unsupported fields; Unicode;
  byes and availability fixtures; new-tournament export without sample data; blocked export for unverified
  systems; export limitations; malformed XML, XXE, duplicate IDs, missing references, excessive depth; editing
  rules (double assignment, board uniqueness, preliminary players, availability, no silent game loss, bulk
  delete, round shrink, paired player cannot become preliminary, game → bye conversion, editing imported byes);
  standings notices for explicit absences, empty, unplayed and played rounds, and excluded rounds; rejected invalid `NotPlayingInRound` values; catalog upsert and snapshot independence; API persistence, the import → edit → export →
  reimport flow, the CSRF header, and that no legacy XML is leaked to the browser. Spec 0001 (`Spec0001Tests` plus
  an API test): Berger tables for 4 and 6 players, full cycles for 3–12 players with one bye each and colour balance
  ≤ 1; registration-order and persisted random starting numbers; order-independent and idempotent generation;
  stage-length checks at generation; immutable played games (swapped colours accepted, conflicts block with zero
  changes); confirmation for replacements; no protection in round robin; withdrawal via referee-decision results;
  bye scoring and SOS; round robin → Swiss continuity; stage-change rules; late entrants without false absences;
  round-robin admission rules; XML export (pure round robin blocked, mixed exported as Kind=2 with reported
  losses); pre-0001 JSON compatibility; unidentified imported stage; preparation view (earlier rounds only, rating
  over coefficients); McMahon preparation with start scores and byes; Settings system lock; joining-round
  defaults; partial batch finalization. Review corrections: frozen-member availability refused (domain and API),
  the monotonic Settings lock after deleting all pairings and reloading, unplayed colour swaps restored only after
  confirmation, credited byes as outcomes (joining default, stage barrier, delete confirmation, generator
  conflict), and fixture byes kept on regeneration. Re-review: the lock is kept for a stored pre-0001 tournament after
  its last pairing is deleted (API test with an authentic pre-0001 JSON document). Spec 0002 (`Spec0002Tests` plus an
  API test):
  - field availability; partial and duplicate drafts with structured errors and zero writes;
  - batch atomicity and card/table rule parity; a manual permutation defining the frozen order;
  - random overwrite with inactive drafts untouched; frozen 0001 numbers and no auto-numbering;
  - the round-count prompt (8 players, 5 → 7 rounds) with atomic resize; safe and blocked shrinking; odd rosters and
    round-robin prefixes;
  - atomicity across confirmations; Clear of all stage pairings (manual, protected, byes) without touching later
    stages;
  - drafts kept after Clear; the result lock lifecycle (referee and unknown results, unlock after removal, byes never
    lock); the single-stage Settings unlock versus individual deletion and multi-stage Clear;
  - starting numbers in the export report;
  - the API lifecycle: numbers → prompt → resize → lock → clear → unlock.
- Frontend (`docker compose --profile test run --rm frontend-tests`): typecheck clean; **17 passed, 0 failed**
  (including participation markers before the joining round, locked roster rounds, and the pairing editor sending
  the confirmation flag when a credited bye is deleted). Spec 0002: the number-source dialog before the round-count
  dialog, a single final request, cancel sending nothing, and one batch request for several table edits.
  Localization: exactly `uk`/`en`, key parity, placeholder parity, no empty strings, no Russian locale file, no
  Russian-only letters in Ukrainian strings, no hard-coded Cyrillic in components, English fallback. Component
  test (jsdom): the settings form prompts before in-app navigation discards unsaved changes, and does not prompt
  when the form is clean.
- Manual browser checks (headless Chrome against the container): the S1 flow on the reference fixture, the S2 flow
  (create → players → pairing → result → refresh → finalize → upsert → XML download), dialogs in both languages,
  no console errors. After the review fixes: the unsaved-settings prompt, the new Players columns, the disabled
  preliminary option for a paired player, and game → bye conversion with discard confirmation were all checked in
  the running app. Spec 0001 (Ukrainian and English): random-number checkbox before the draw, all-round generation
  with frozen numbers shown, a five-player bye shown as +1 without a warning, no protection checkbox in round-robin
  rounds, a swapped and played game unchanged after regenerating, the referee-decision flag kept, the locked Settings
  system, round robin → Swiss from round IV with all-round generation stopping at round III, Swiss generation
  disabled, a late entrant defaulting to round IV and unavailable in round II, and the preparation view ordering by
  score group, then rating. No page errors. Review corrections were checked in the running app: a late entrant shows
  "not yet joined" markers, a roster member has locked availability in round-robin rounds, and the Settings system
  stays locked after all pairings are deleted. Re-review: deleting a credited bye in the pairing editor (uk and en)
  shows the bye-specific warning and succeeds after confirmation. Spec 0002, on a disposable instance (uk and en):
  - a disabled inactive row; Generate disabled while table edits are unsaved;
  - the error summary (duplicate, out of range, gaps) with highlighted rows; the "change to 7 rounds" dialog, then 7
    drawn rounds with read-only numbers;
  - a read-only card number; the lock after a result, its hint, and the unlock after removal;
  - Clear (28 pairings) with drafts kept, editable again, and Settings unlocked;
  - the random-overwrite choice: cancel keeps drafts, overwrite freezes a random order with the inactive draft
    untouched.

  No page errors.

## Known limitations

- Skipped rounds, draws, both-lose results, and McMahon start scores in the official standings are not scored
  correctly yet. They are flagged with notices. The calculation settings are stored but have no effect.
- Automatic Swiss, McMahon and Scheveningen pairing is not implemented. Moving or removing stage boundaries, and a
  round-robin stage after another system, are deferred (spec 0001 §1.1).
- A pure round-robin tournament cannot be exported to legacy XML (round-robin `Kind` code unverified). A round robin
  followed by Swiss exports as Swiss; stages, starting numbers and joining rounds are reported as lost.
- McMahon start scores cannot be edited in the UI (imported values only).
- Ranking criteria other than wins/points, SOS, SODOS, and SOSOS are stored but not calculated, and are excluded
  from ordering.
- "Derive rank from rating" is stored only; ranks are entered manually (mapping unconfirmed).
- XML export cannot carry team, coach, nationality, country, club, rank shift, non-numeric grade, "white moves
  first", draws, or unverified criteria. All are listed before download. New tournaments with a non-Swiss system
  cannot be exported (`Kind` code unverified).
- Compatibility of exported files with the original AutoKorsak 1.38 application has not been tested.
- Single-user assumption: there is no concurrent-edit detection between browser tabs (last write wins).
- No catalog management UI (by design in P0); catalog entries can only be created or updated by upsert.
- Desktop-first layout; mobile is not a target.

## Deferred (by spec 0000 §3.2)

Automatic pairing, rating recalculation and history, EGD integration and export, reports and printing, the
assistant wizard, authentication, other board games, SORP result points.

## Next actions

1. Independent review of the spec 0002 implementation.
2. Redeploy the live container when the owner asks.
3. Referee demo of the round-robin numbering, reset and stage-change flows. Collect answers to the open questions in
   `docs/decisions.md` (Kind codes, rating → rank, skipped-round scoring).
4. Next specs (likely candidates: automatic Swiss/McMahon pairing, skipped-round scoring).

## Review log

| Date | Reviewer | Commit | Findings | Resolution |
|---|---|---|---|---|
| 2026-10-16 | GPT-6 | `0cc16168` | 2× P1 (paired player → preliminary; game → bye kept its result), 4× P2 (false skipped-round notice, unsaved settings lost on navigation, Players list missing country/team/coach, invalid `NotPlayingInRound` silently dropped). Architecture accepted. | All six fixed with regression tests; see [review/0000-review-gpt6-0cc16168-response.md](review/0000-review-gpt6-0cc16168-response.md). Accepted on re-review ([review/0000-rereview-gpt6-bfe922c8.md](review/0000-rereview-gpt6-bfe922c8.md)). |
| 2026-10-16 | GPT-6 (re-review) | `bfe922c8` | All six previous findings closed. 1× P2: an explicit absence (`NotPlayingRounds`) in a counted round raised no skipped-round notice before any result existed. | Fixed with 4 regression tests; see [review/0000-rereview-gpt6-bfe922c8-response.md](review/0000-rereview-gpt6-bfe922c8-response.md). Awaiting final re-review. |
| 2026-10-16 | GPT-6 | `e7b57590` (spec 0001) | 1× P1 (frozen RR member could be made unavailable, blocking generation), 4× P2 (Settings lock lost after deleting pairings; unplayed colour swaps kept; credited bye not treated as an outcome; Players list showed pre-joining rounds as available). | All five fixed with regression tests; see [review/0001-review-gpt6-e7b57590-response.md](review/0001-review-gpt6-e7b57590-response.md). Closed on re-review. |
| 2026-10-16 | GPT-6 (re-review) | `87bf7a04` | All five previous findings closed. 2× P2: the pairing editor could not delete a credited bye (missing confirmation flag); stored pre-0001 tournaments could lose the Settings lock after deleting their last pairing. | Both fixed with regression tests; see [review/0001-rereview-gpt6-87bf7a04-response.md](review/0001-rereview-gpt6-87bf7a04-response.md). Awaiting final re-review. |
