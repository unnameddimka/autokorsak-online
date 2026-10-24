# Status

**Current version:** spec 0003 (referee usability: automatic round-robin round count, pure round-robin participation,
Standings-only round control, transliteration, rating input, app-only explanation, result-first pairing editor with
the "both win" result), implemented 2026-10-24 on top of specs 0000–0002, **awaiting GPT-6 review**. Spec 0000 was
accepted after review (final version `8fd3898`), and so were spec 0001 (final version `b565ad20`) and spec 0002
(final version `6821a5b6`, accepted in `ea951a2`). The live container on `:8080` was not redeployed; it still runs
an earlier version until the owner asks to redeploy.

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
  - ~~A terminal round robin with the wrong round count offers a confirmed, atomic resize-and-draw.~~ Replaced by the
    automatic count of spec 0003.
  - "Clear round-robin draw" removes all stage pairings (byes included) when no game has a result, and keeps the
    numbers as drafts. If nothing else remains, Settings unlocks again.
  - Any entered game result locks round-robin generation and clearing until every result is removed.
- **Joining rounds (0001):** every participant has a "joins from round" value, with a sensible default and backfill
  to round 1. Rounds before it are neither pairable nor skipped rounds. Round-robin admission rules are enforced,
  and "finalize all" finalizes the eligible players and reports skipped ones with reasons.
- **Rounds:** previous/next/direct selection of the current round, and an "include current round in calculations"
  switch. Since spec 0003 this control is on the Standings page only; Pairings names the round in its heading.
- **Standings:** wins/points, SOS, SODOS, SOSOS, shared places, and per-round opponent/result cells with an
  optional color marker. A bye counts as one win and one point (0001). They recalculate after every change. Notices
  explain unsupported situations. The optional **"Sort by rating within score groups"** preparation view (0001)
  orders players by points from earlier rounds, then rating; the preference is remembered per tournament.
- **Spec 0003 (referee usability):**
  - **Pure round robin** (one round-robin stage from round 1): Settings shows the number of rounds read-only ("at the
    draw" before it); the first successful draw sets it from the final roster (N − 1 / N) with no confirmation, and the
    server ignores submitted counts. A frozen draw keeps roster and count until Clear. The tournament list shows "at
    the draw" too.
  - Joining-round and per-round availability controls are hidden in the player card and the Players list; the server
    ignores those fields for a pure round robin (stored values are kept but have no effect). Nonappearance is a normal
    referee-decided result.
  - **Transliterate** button in the player card (official Ukrainian table, KMU Resolution 55/2010), with confirmation
    before overwriting English names; unsupported characters are kept and reported.
  - Rating is typed as text (no forced leading zero) and validated on Save; rating 0 is shown as 0 everywhere.
  - **App only** fields have an accessible explanation (button, keyboard, touch) that says what is lost in an old-format
    XML round-trip and that the data stays in the app.
  - Pairing editor: players and the **Result** list come first; the list is ?-?, 1-0, 0-1, **1-1 (both win)**, 0-0, and
    ½-½ when draws are allowed. 1-1 gives each player one point and one win, counts as an entered result for all locks
    and confirmations, survives colour swaps, and imports/exports as the legacy token `1-1` (verified from the legacy
    serializer, decision T1).
- **Shared catalog:** search when adding a player, and an explicit upsert with create/update counts, confirmation,
  and a per-player decision for possible namesakes.

## Test results (2026-10-24, spec 0003)

- Backend (`docker compose --profile test run --rm backend-tests`): **140 passed, 0 failed** (125 before; 15 added,
  10 rewritten). `Spec0003Tests`: six players → 5 rounds with Settings unable to set or overwrite the count; three
  players with a stale count → 3 rounds with byes; preliminary players excluded and a one-player draw failing without
  writes; editable counts for other systems; an unsafe shrink failing atomically; roster and count stable until Clear,
  then recalculated; hidden join/skip flags ignored but kept (draw, roster, standings cells and notices); finalizing and
  numbering with stale joining rounds; a round robin followed by Swiss keeping the 0001 rules; a technical defeat as a
  referee result; 1-1 scoring (wins, SOS, SODOS, shared place, preparation view, no notice); 1-1 colour swap, discard
  protection and JSON persistence; legacy `1-1` import with an unchanged deep-equal export; a new 1-1 exporting as
  `1-1` without a limitation, an unknown token (`S-S`) staying opaque, and a draw still reported. API: forged Settings
  count ignored, list placeholder before the draw, server-derived count with a forged `roundCount`, `bothWin`
  persisted and scored. Rewritten 0001/0002 tests: the round-count prompt became an automatic resize, and the pure
  round-robin joining/availability refusals became "input ignored, nothing changes".
- Frontend (`docker compose --profile test run --rm frontend-tests`): typecheck clean; **48 passed, 0 failed** (24
  before; 24 added, the two round-count dialog tests rewritten): transliteration against the official table and the
  spec examples; card transliteration with confirm/cancel, empty source and a full name in one field; rating 0 → 50 and
  invalid/empty input; no join/skip controls in a pure round robin (kept for Swiss); the app-only explanation by button
  and Escape, with the input keeping its label; rating 0 in the Players and Standings tables; no join/availability
  columns; the round control on Standings only and the Pairings round heading; result list order, conditional ½-½, a
  kept recorded draw, 1-1 on colour swap and saving `bothWin`; the RR panel sending one request with no round-count
  dialog. The localization guard now allows Cyrillic only in `transliteration.ts` and its test.
- Production build: the Docker image (Vite build and `dotnet publish`) builds cleanly.
- Smoke (headless Chrome on a disposable container at `127.0.0.1:8091`, removed afterwards; the live `:8080` instance
  was not touched): **52 checks passed in uk and en, no page or console errors**: Settings ignoring a submitted count,
  "at the draw" in the list and Settings, no join/availability columns or card controls, rating 0 shown and edited to
  50 by typing, transliteration (Stetsenko Oleksii) and saving, the app-only explanation by keyboard with Escape
  keeping the card open, no round control on Players/Pairings, a draw of 3 final players (+1 preliminary) giving 3
  rounds with byes and no dialog, the result-first editor with ?-?/1-0/0-1/1-1/0-0, saving 1-1 (one win each), the
  round chosen on Standings driving Pairings, the 1-1 generation lock, and the drawn count read-only in Settings.

## Earlier test results (2026-10-16)

- Backend (`docker compose --profile test run --rm backend-tests`): **125 passed, 0 failed**. Coverage: reference
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
  - the API lifecycle: numbers → prompt → resize → lock → clear → unlock;
  - review corrections: a typed 0 refused by the batch API (domain and direct API request), null still clears.
- Frontend (`docker compose --profile test run --rm frontend-tests`): typecheck clean; **24 passed, 0 failed**
  (including participation markers before the joining round, locked roster rounds, and the pairing editor sending
  the confirmation flag when a credited bye is deleted). Spec 0002: the number-source dialog before the round-count
  dialog, a single final request, cancel sending nothing, and one batch request for several table edits. Review
  corrections:
  - player card: a new number, a changed number, an unrelated edit while the number is frozen, a typed 0 rejected,
    and an emptied field clearing;
  - table: a typed 0 rejected without a request; Clear blocked while edits are unsaved.
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
  correctly yet. They are flagged with notices (the owner kept 0-0 and ½-½ unchanged in spec 0003, decision T4). The
  calculation settings are stored but have no effect. "Both win" (1-1) is fully scored.
- The legacy draw token `½-½` is now evidenced by the legacy serializer (decision T2), but draws are still exported as
  `?-?` and reported, and an imported `½-½` stays an opaque result. A later spec can map it.
- A round robin followed by another system is set up by adding the stage while the count is still provisional;
  Settings edits the count again once the tournament has two stages.
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

1. GPT-6 review of the spec 0003 implementation.
2. Redeploy the live container when the owner asks (it does not include 0002 or 0003 yet).
3. Referee demo of the round-robin numbering, reset and stage-change flows. Collect answers to the open questions in
   `docs/decisions.md` (Kind codes, rating → rank, skipped-round scoring).
4. Next specs (likely candidates: automatic Swiss/McMahon pairing, skipped-round scoring).

## Review log

| Date | Reviewer | Commit | Findings | Resolution |
|---|---|---|---|---|
| 2026-10-16 | GPT-6 | `0cc16168` | 2× P1 (paired player → preliminary; game → bye kept its result), 4× P2 (false skipped-round notice, unsaved settings lost on navigation, Players list missing country/team/coach, invalid `NotPlayingInRound` silently dropped). Architecture accepted. | All six fixed with regression tests; see [review/0000-review-gpt6-0cc16168-response.md](review/0000-review-gpt6-0cc16168-response.md). Accepted on re-review ([review/0000-rereview-gpt6-bfe922c8.md](review/0000-rereview-gpt6-bfe922c8.md)). |
| 2026-10-16 | GPT-6 (re-review) | `bfe922c8` | All six previous findings closed. 1× P2: an explicit absence (`NotPlayingRounds`) in a counted round raised no skipped-round notice before any result existed. | Fixed with 4 regression tests; see [review/0000-rereview-gpt6-bfe922c8-response.md](review/0000-rereview-gpt6-bfe922c8-response.md). Awaiting final re-review. |
| 2026-10-16 | GPT-6 | `e7b57590` (spec 0001) | 1× P1 (frozen RR member could be made unavailable, blocking generation), 4× P2 (Settings lock lost after deleting pairings; unplayed colour swaps kept; credited bye not treated as an outcome; Players list showed pre-joining rounds as available). | All five fixed with regression tests; see [review/0001-review-gpt6-e7b57590-response.md](review/0001-review-gpt6-e7b57590-response.md). Closed on re-review. |
| 2026-10-16 | GPT-6 (re-review) | `87bf7a04` | All five previous findings closed. 2× P2: the pairing editor could not delete a credited bye (missing confirmation flag); stored pre-0001 tournaments could lose the Settings lock after deleting their last pairing. | Both fixed with regression tests; see [review/0001-rereview-gpt6-87bf7a04-response.md](review/0001-rereview-gpt6-87bf7a04-response.md). Accepted (final review `bf8d235`). |
| 2026-10-16 | GPT-6 | `4ecfd464` (spec 0002) | 1× P1 (the player card rejected every non-empty starting number, including unchanged frozen ones, because of a broken digit check), 2× P2 (a typed 0 silently cleared a draft; Clear could discard unsaved table edits). | All three fixed with regression tests; see [review/0002-review-gpt6-4ecfd464-response.md](review/0002-review-gpt6-4ecfd464-response.md). |
| 2026-10-16 | GPT-6 (re-review) | `6821a5b6` (spec 0002) | All three findings closed; no new blocking issue. | Accepted; see [review/0002-rereview-gpt6-6821a5b6.md](review/0002-rereview-gpt6-6821a5b6.md). |
