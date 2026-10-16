# Status

**Current version:** spec 0000 (functional UI prototype), 2026-10-16. The first implementation (`0cc16168`) has been
reviewed; this version contains the review corrections and awaits independent re-review before P0 is called
complete. The first referee demo follows.

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
  including referee decision, handicap/komi/comment, fixed board number, and manual protection. The UI lists the
  players without a pairing. Automatic pairing is visibly disabled.
- **Rounds:** previous/next/direct selection of the current round, and an "include current round in calculations"
  switch.
- **Standings:** wins/points, SOS, SODOS, SOSOS, shared places, and per-round opponent/result cells with an
  optional color marker. They recalculate after every change. Notices explain unsupported situations.
- **Shared catalog:** search when adding a player, and an explicit upsert with create/update counts, confirmation,
  and a per-player decision for possible namesakes.

## Test results (2026-10-16)

- Backend (`docker compose --profile test run --rm backend-tests`): **61 passed, 0 failed**. Coverage: reference
  fixture import shape; round VI pairings versus the legacy TXT export; the exact standings and tie-breaks from
  spec §9.1 (Кобзар Гліб 6/21/21/111, Єрємєєв Матвій 5/17/11/113) and the legacy table order; unedited export
  deep-equal to the source for all four fixtures; edit round-trip; preservation of unsupported fields; Unicode;
  byes and availability fixtures; new-tournament export without sample data; blocked export for unverified
  systems; export limitations; malformed XML, XXE, duplicate IDs, missing references, excessive depth; editing
  rules (double assignment, board uniqueness, preliminary players, availability, no silent game loss, bulk
  delete, round shrink, paired player cannot become preliminary, game → bye conversion, editing imported byes);
  standings notices for empty versus played rounds; rejected invalid `NotPlayingInRound` values; catalog upsert and snapshot independence; API persistence, the import → edit → export →
  reimport flow, the CSRF header, and that no legacy XML is leaked to the browser.
- Frontend (`docker compose --profile test run --rm frontend-tests`): typecheck clean; **10 passed, 0 failed**.
  Localization: exactly `uk`/`en`, key parity, placeholder parity, no empty strings, no Russian locale file, no
  Russian-only letters in Ukrainian strings, no hard-coded Cyrillic in components, English fallback. Component
  test (jsdom): the settings form prompts before in-app navigation discards unsaved changes, and does not prompt
  when the form is clean.
- Manual browser checks (headless Chrome against the container): the S1 flow on the reference fixture, the S2 flow
  (create → players → pairing → result → refresh → finalize → upsert → XML download), dialogs in both languages,
  no console errors. After the review fixes: the unsaved-settings prompt, the new Players columns, the disabled
  preliminary option for a paired player, and game → bye conversion with discard confirmation were all checked in
  the running app.

## Known limitations

- Byes, skipped rounds, draws, both-lose results, and McMahon start scores are not scored correctly yet. They are
  flagged with notices. The calculation settings are stored but have no effect.
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

1. Independent re-review of the corrections (see the review log and `docs/review/`).
2. First demo with the referee. Collect answers to the open questions in `docs/decisions.md` (Kind codes,
   rating → rank, scoring of byes and skipped rounds).
3. Spec 0001 for the next scope (likely candidates: bye and skipped-round scoring, automatic Swiss/McMahon
   pairing).

## Review log

| Date | Reviewer | Commit | Findings | Resolution |
|---|---|---|---|---|
| 2026-10-16 | GPT-6 | `0cc16168` | 2× P1 (paired player → preliminary; game → bye kept its result), 4× P2 (false skipped-round notice, unsaved settings lost on navigation, Players list missing country/team/coach, invalid `NotPlayingInRound` silently dropped). Architecture accepted. | All six fixed with regression tests; see [review/0000-review-gpt6-0cc16168-response.md](review/0000-review-gpt6-0cc16168-response.md). Awaiting re-review. |
