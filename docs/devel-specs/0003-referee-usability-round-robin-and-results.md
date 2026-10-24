# SPEC 0003 — Referee usability: round-robin setup, player cards, and game results

**Status:** Approved by project owner, 2026-10-24. Ready for implementation by Claude.
**Origin:** Referee usability feedback from Yulia, clarified and approved by the owner.
**Baseline:** Implemented and review-accepted specs 0000, 0001, and 0002. This spec is additive and overrides only the specific behaviors identified below.
**Target:** Current .NET 10 backend, React/TypeScript frontend, SQLite tournament JSON model.
**Product locales:** Ukrainian (default) and English. Never add Russian UI or modify original historical screenshots.
**Primary references:** docs/reference/legacy-ui/10-player-editor-data.png and 13-pairing-editor.png; docs/devel-specs/0000-ui-prototype.md, 0001-pairing-stages-round-robin-and-score-group-view.md, and 0002-round-robin-starting-numbers-and-draw-reset.md.

## 1. Executive contract

Implement the following referee-facing changes, not merely a cosmetic redesign:

1. For a **pure single-stage round-robin** tournament, the number of rounds is derived from the actual finalized participant roster **when generating the draw**. The referee does not enter a round count in Settings. Do not introduce a separate expected-player-count setting.
2. For this pure RR workflow, hide the per-player joining-round and round-by-round availability/skip controls. Actual nonappearance is handled by a referee-entered result on the scheduled game, not by removing the player from its Berger schedule.
3. Show the shared **Current round** navigation/control only on the **Standings** page, not above the Players or Pairings pages.
4. Restore the deliberate **Transliterate** action in the player editor, using the Ukrainian government transliteration table approved by Cabinet of Ministers Resolution No. 55 (2010).
5. Fix the controlled rating input that inserts an unavoidable leading zero. Display actual rating zero as **0**, not a blank, wherever ratings appear.
6. Replace opaque legacy-XML-only labels/tooltips with an understandable explanation of app-only fields and their loss in legacy XML round-trips.
7. Make the two players and the **Result** dropdown the first visual priority of the pairing editor. Use the original list of results, including **1-1 = both players win**; integrate it into backend scoring and safe legacy XML handling.

Preserve pairing identities/colors, recorded results, existing referee/manual protections, normal confirmation behavior, data safety, catalog isolation, and Ukrainian/English localization unless explicitly changed here. Do not redesign Swiss, McMahon, or Scheveningen pairing algorithms.

## 2. Round count and round-robin participation

### 2.1 Scope and authoritative roster

The new rules in this section apply to a tournament with **one stage, starting at round 1, whose system is RoundRobin**. A mixed/multi-stage tournament, a later RR stage, and switching systems after games/stage history exist remain governed by accepted 0001/0002 behavior. Do not expand the scope of system switching in this spec.

For an undrawn pure RR tournament, the draw roster consists of the **actual tournament participants who are finally registered**, not a configured target/headcount, their numeric local IDs, their ratings, or fictional empty slots from a Settings value. Preliminary registration continues to mean ineligible for drawing, as already defined by 0000/0001. The existing entered-vs-random starting-number choice and permutation validation from 0002 remain intact.

Example: a referee has actually registered 3 final players, even if 6 were expected informally. Draw 3 players and produce 3 rounds, including a bye according to the accepted Berger algorithm; do not wait for nonexistent players or use the previously configured 5-round default.

### 2.2 Settings behavior and calculation lifecycle

When the selected initial tournament system is RoundRobin:

- Do not accept manual editing of **Number of rounds** in Settings. It can be displayed read-only; before the first draw, show a localized explanation such as “Calculated automatically when the round-robin draw is generated” rather than presenting the old provisional stored value as an authoritative count.
- The actual number of rounds is calculated **at the first successful generation** of the pure RR draw: for N final entrants, N−1 rounds if N is even; N rounds if N is odd. At least two eligible entrants are required. Use the final roster eligible for the draw.
- Save the calculated count and schedule transactionally with the frozen numbered roster and generated pairings. Other views then display the persisted real round count.
- The 0002 **round-count-adjustment confirmation dialog** is **removed for this pure RR workflow**: no user approval of an automatically computed round count is required. The server performs the calculation authoritatively even for direct API calls or stale clients.
- Existing 0002 data-integrity checks for resizing remain mandatory. An impossible or unsafe resize (out-of-range count, later-stage boundary, existing pairings, results, retained metadata, joining-round constraints, etc.) must **fail atomically with a localized actionable error**; it must not silently discard anything or ask for a “force” override. A mismatch alone, when safely resolvable, is not an error or confirmation prompt.
- A pure RR draw that is already frozen retains its **frozen roster and saved round count** when remaining unplayed rounds are generated again. Merely adding/removing players does not silently reconfigure an existing draw. Only the approved explicit Clear RR Draw lifecycle releases a frozen roster; the next successful new draw recalculates from the then-current final roster.
- The current-round value must remain within the new range. The already-approved safeguard preventing generation after any RR game result, and the Clear result lock, remain in force.
- A non-RR system retains its existing editable Number of rounds behavior and validation. If a pre-draw RR selection is changed to another system while permitted by 0001/0002, the field becomes editable again. More complicated switching and multi-stage effects are **out of scope**.

Implementation note: Tournament.NumberOfRounds may still need a provisional valid value for an undrawn RR aggregate in the existing JSON schema and round navigator. This is an **internal implementation detail**, not an alternative number-of-rounds input or a promise to the referee. Updating unrelated Settings in RR mode must never inadvertently overwrite the real drawn count with a stale provisional number.

### 2.3 Remove irrelevant round-participation UI for pure RR

For pure RR tournaments:

- Hide **First eligible / joining round** and the per-round **Plays / skips / availability checkboxes** from the player editor. Their purpose is superseded by the canonical RR schedule; do not replace them with another skip selector.
- Do not show misleading joining-round or round-by-round availability controls/columns on the Players page in this pure RR mode. Registration state and the existing editable draft starting number remain visible.
- For newly added pure-RR entrants before the draw, provide server-safe defaults equivalent to joining at round 1 with no per-round exclusions. Do not require the referee to supply hidden fields.
- Do not let obsolete/invisible per-round skip flags accidentally exclude finalized players from a **new pure RR draw** or cause the draw to fail with a reason that can only be repaired by a hidden checkbox. Adjust domain eligibility/validation consistently for the pure-RR case; do not merely hide the controls.
- Preserve old saved participation metadata and existing fixtures/results on load and export. Do not mass-delete legacy fields to get around the hidden UI. For imported/historical or mixed-stage tournaments, preserve established explicit participation semantics as applicable. Any genuinely incompatible historical state must receive an intelligible, non-destructive error; never rewrite a played fixture.
- For a frozen pure RR roster, nonattendance is handled by keeping the two-player pairing and having the referee enter the correct outcome, optionally marked **Result by referee**. Technical defeats do **not** remove the player from the schedule. Do not add automatic forfeits, extra points, or an additional technical-result category.

The underlying data model may continue to carry FirstEligibleRound and NotPlayingRounds for compatibility with other systems and older saves. The pure-RR rule is about referee workflow and effective eligibility, not retroactively erasing data.

## 3. Current-round navigator placement

The existing current-round selector, previous/next navigation, and the “Include current round” calculation toggle currently rendered from TournamentLayout belong **only on the Standings page**.

- Move or conditionally render the whole control there; avoid duplicated navigators.
- Pairings and Players do not display a separate Current round strip or redundant round selector at the top.
- Preserve the single persisted Tournament.CurrentRound / IncludeCurrentRound state. A change on Standings determines which round the Pairings screen shows when opened afterward.
- Keep a visible, localized round identification in the Pairings page heading or content so the referee knows which round is being edited. This is a label, **not** a second navigator.
- Behavior of score inclusion and persistence remains unchanged.
- Switching pages must not silently reset the round.

## 4. Player editor transliteration

Restore the **“Транслітерувати” / “Transliterate”** action seen between the Ukrainian and English name fields on legacy screenshot 10-player-editor-data.png.

**Normative source:** Cabinet of Ministers of Ukraine Resolution No. 55 dated 27 January 2010, “Про впорядкування транслітерації українського алфавіту латиницею”, current official text and its attached mapping table: https://zakon.rada.gov.ua/laws/show/55-2010-%D0%BF .

- Implement the **official Ukrainian-to-Latin table**, including initial-versus-non-initial position rules (e.g. Є, Ї, Й, Ю, Я), the special Зг sequence, capitalization, and omission/handling of soft sign and apostrophes according to the table and notes. Do not substitute ad-hoc Russian transliteration or a general-purpose locale guess.
- On explicit click, convert LocalSurname to Surname and LocalFirstName to FirstName. Do not auto-transliterate on typing, save, catalog selection, or import. English values stay freely editable.
- If **either destination English field would be overwritten**, obtain confirmation before writing **either** field. Cancel means no changes. The action updates the in-memory form; the participant is persisted only by the normal Save button.
- Preserve field boundaries. Legacy data sometimes contains a whole name in LocalSurname and an empty LocalFirstName; transliterate that field as entered instead of heuristically splitting or rearranging it.
- Handle spaces, hyphens, apostrophes, and common Ukrainian personal-name capitalization correctly. If source names are absent or contain characters outside the supported Ukrainian transliteration mapping, do not silently destroy an existing English value or invent phonetic translations; show a localized explanatory message or safely preserve unsupported characters without corrupting the original text.
- Provide deterministic, offline, pure-function tests against the official table (at minimum: Олексій → Oleksii, Стеценко → Stetsenko, Євген → Yevhen, Юлія → Yuliia, Київ → Kyiv, Згурський → Zghurskyi), plus existing-target confirmation/cancel, empty source, hyphen/apostrophe, and legacy full-name-in-one-field cases.
- The original Ukrainian names must remain unchanged unless explicitly edited by the referee.

## 5. Rating input and zero display

### 5.1 Editable rating

The player card currently uses Number(input.value) || 0 on every keystroke. When the referee clears the field to type 50, it immediately reinserts 0, producing 050.

- Keep a temporary **string representation while editing**, allowing an empty intermediate input. Validate and convert to a numeric rating at save/commit time (or blur if it does not disrupt typing).
- Retain existing backend range checks (currently -1000 through 4000); do not reinterpret the integer zero as missing.
- Submitting an empty/invalid rating must have deliberate, understandable handling (for example, an inline validation error), never silent coercion to an unintended value.
- A normal edit from 0 to 50 must visibly produce exactly 50, and reload as 50. Existing editable positive/negative ratings must not regress.
- No change to the isolated tournament-versus-shared-catalog save semantics.

### 5.2 Read-only rating displays

- Render numeric rating **0** as literal **0**, not as an empty cell, on the Players list and Standings table; also correct other visible player/catalog rating formatting that uses a truthiness check on a non-null numeric rating.
- Do not replace 0 with a blank for sorting, filtering, exports, or derived data. Keep true “no value” distinct where a field actually is nullable.
- Regression tests cover both the edited field and display of 0 in both primary tables.

## 6. Clear explanation of app-only / legacy XML fields

The current technical text roughly saying a field “has no verified place in the original XML” is opaque to referees.

- Keep a short Ukrainian label such as **“Лише в застосунку”** (English: **“App only”**) for affected fields, but provide accessible explanation through an information icon/control that works with mouse, touch, and keyboard; a hover-only title is insufficient.
- Recommended clear Ukrainian text:

  “Це поле зберігається в Автокорсаку, але не переноситься до XML-файлу старої версії програми. Якщо експортувати турнір у старий формат і повторно імпортувати, значення цього поля може бути втрачено.”

  Supply equivalent natural English copy. Wording may be polished without changing the meaning.
- **Do not claim that merely downloading an XML file deletes anything from the application's database.** Existing tournament data stays saved in the app. The risk is loss **in that exported file / on round-trip reimport**.
- Apply consistently to the currently app-only player fields (including rank shift, country, club, nationality, team, coach as flagged by the current UI) and any reused app-only indicator; do not invent old-XML mappings.
- Preserve the current export limitations report and original-XML preservation rules.

## 7. Pairing editor: player/result-first design

### 7.1 Hierarchy, not a fixed visual design

The referee opens a pairing primarily to **enter or edit its result**.

- At the **beginning of the form**, place the two player names/selectors, clearly associated with their colors, followed immediately by the **Result** dropdown. Make these visibly the primary part of the form by grouping, modest styling, a subdued secondary section, or another designer-chosen treatment.
- No requirement for huge typography, large winner buttons, exact colors, a mandatory collapsible panel, or a pixel-for-pixel copy of the old application.
- Board number, fixed-board flag, handicap, additional komi, comments, protected/manual flag, referee-result flag, bye conversion, and other settings remain accessible but **secondary**. Do not remove existing editing functionality or its validations and confirmations.
- Existing pairing identity, color-swap semantics, player-eligibility checks, and confirmation before destroying a result must still work.
- Keep the primary result selector visible without scrolling in a normal desktop-sized editor as far as practical.

### 7.2 Required result dropdown

Use a standard dropdown, modeled on the legacy screenshot provided by the owner. Its user-visible result codes and semantics are:

| Label | Meaning | Points credited | Wins credited |
| --- | --- | --- | --- |
| ?-? | no result entered | 0, not a completed game | 0 |
| 1-0 | Black wins | Black 1, White 0 | Black 1, White 0 |
| 0-1 | White wins | Black 0, White 1 | Black 0, White 1 |
| 1-1 | **Both players win** | Black 1, White 1 | Black 1, White 1 |
| 0-0 | Both players lose | Black 0, White 0 | Black 0, White 0 |
| ½-½ | Jigo / draw | Black 0.5, White 0.5 | 0 wins each |

**The meaning of 1-1 is explicitly approved by the project owner.** This is not a draw, not two half-points, and not the same as a scheduled bye.

- Provide localized accessible text identifying winners/colors if needed, but retain the recognizable numerical code in each option.
- The **½-½** option is available only when draws are permitted by the applicable tournament/pairing rules; use the existing Tournament.AllowDraws and Pairing.AllowDraw semantics rather than introducing a new permission. If a saved pairing already has a draw, keep it visibly selectable even if current settings later disallow new draws; do not silently change historical results.
- All other result codes, including **1-1**, remain available irrespective of the draw permission.
- Preserve existing unknown imported legacy results visibly and unmodified until the referee deliberately changes them.
- A bye remains a bye and must not be assigned a two-player result.

### 7.3 Domain, scoring, and compatibility for 1-1

Implement a genuine persisted domain outcome for **both win** (for example, GameResult.BothWin, named consistently with the existing enum), not a presentation-only code or a special string in the UI.

- Both players receive one **point and one win** in all relevant official standings and score-group/preparation calculations. Apply existing opponent/coefficient calculation conventions to each player's completed game, just as for other recorded two-player outcomes; do not count it as two separate pairings.
- Round cells, result badges/labels, result-related sorting, results-by-referee indicators, saved JSON, editing, reload, and color swapping must support it. Swapping colors leaves 1-1 unchanged.
- It counts as an **entered result** for the 0002 RR generation/Clear locks, result-discard confirmation, and round-completion/scoring logic. Replacing or clearing it must not silently bypass result protections.
- Import a legacy XML Result token literally equal to **1-1** as both-win if observed. Importing other unknown result tokens must remain lossless/opaque.
- The legacy screenshot demonstrates the **UI result code**, but is not by itself proof of the **serialized legacy XML token**. Do **not** claim verified AutoKorsak 1.38 XML export compatibility solely from that screenshot. Verify the underlying XML token using original evidence/legacy software if possible. If confirmed, implement and test the exact old-XML export mapping. If not confirmed, preserve any unchanged imported source token and explicitly report a potential loss/unsupported export for a newly entered 1-1 instead of silently substituting a different result. Existing export of an unverified jigo/draw must continue to report limitations rather than invent a token.
- Do not weaken existing deep-equivalence tests for unedited legacy XML, unknown token preservation, or app-only export warnings.

## 8. Data integrity, localization, and backward compatibility

- Existing persisted JSON aggregates from specs 0000–0002 load without migrations that alter pairings, results, IDs, frozen RR rosters, historical join/availability metadata, starting numbers, or saved round counts.
- Changing the RR UI must not silently modify tournament records merely by viewing Settings or a player card.
- Server remains authoritative for round count, roster choice, results, and eligibility. Never rely on disabled/hidden UI elements for protection; direct API requests must meet the new pure-RR rules.
- Keep current 0002 starting-number source prompt (random overwrite versus entered/manual versus cancel), other safe pairing-replacement confirmations, and stage-clear confirmations. Only the now-unnecessary **pure-RR round-count approval step** is removed.
- Do not unintentionally enable later RR stages or unfinished Swiss/McMahon/Scheveningen generators.
- Both locales must contain complete, idiomatic labels, errors, confirmations, and help text; proper names and imported data remain untouched and untranslated.
- Frontend and backend tests must cover any changed serialization contracts and old-persistence compatibility.

## 9. Acceptance checklist / test scenarios

Implement tests at the appropriate level (domain, API, frontend, and at least a disposable-instance referee-facing smoke). Minimum scenarios:

1. New pure RR with 6 final players: Settings round count cannot be manually edited; draw assigns **5 rounds** automatically and persists 5.
2. New pure RR with 3 final players (even if a stale internal Settings value says 5 or an informal expected roster was 6): draw assigns **3 rounds**, creates the approved byes and persists 3.
3. Newly finalized actual roster determines the schedule; preliminary-only players do not inflate it. Fewer than 2 eligible final players yields a localized failure without partial edits.
4. Pure RR Settings may be saved before the draw and after the draw without accidentally replacing the auto-derived count from a stale form value; attempts to forge a manual RR round count via API do not defeat this rule.
5. A safe implicit resize takes effect with **no round-count confirmation**; an unsafe shrink (existing affected pairings, later-stage metadata, joining-round artifacts or other relevant history) fails atomically, with a meaningful explanation and no lost data.
6. After first draw, roster and count remain stable; a successful explicit Clear (where permitted) releases roster and permits redrawing from the new actual roster, recalculating count. Entered RR results still block regeneration and Clear.
7. Player card in pure RR shows no join/skip controls; Players list does not expose meaningless join/availability columns; new participants work with defaults; imported stale skip flags do not silently delete data or invisibly sabotage a new pure RR draw.
8. Technical defeat is recorded as a normal referee-decided result on the scheduled pairing; no automatic exclusion/forfeit.
9. Current-round control is present on Standings only; using it changes the persisted selected round used by Pairings; pairings show the active round as a heading/label without a second navigator.
10. Transliteration matches official mapping for accented/position-dependent letters, Зг, and punctuation; existing English values require confirmation before either is overwritten; cancel leaves both untouched; originals and catalog remain intact.
11. Rating edit from 0 to 50 produces exactly 50 (not 050), saves and reloads; blank/invalid edit is handled deliberately; zero appears as 0 in Players and Standings (and other rating displays).
12. App-only information is understandable and accessible without mouse hover; exporting legacy XML neither erases app-side values nor falsely promises that those values will be in the downloaded file.
13. Pairing editor opens with both color-labelled players and result selector up front; secondary settings remain operable.
14. Dropdown includes ?-?, 1-0, 0-1, **1-1**, 0-0, and conditional ½-½. No oversized winner buttons are required. Already saved draws and opaque unknown outcomes are preserved correctly.
15. Recording 1-1 credits **one win and one point each**; results feed standings, preparation order and coefficient calculations; swapping colors keeps 1-1; 1-1 locks RR Generate/Clear, survives reload, and triggers existing destructive-edit protections.
16. Legacy XML import of literal 1-1 recognizes the result, ordinary known results remain unchanged, and unknown tokens retain exact round-trip identity; old-XML export of newly entered 1-1 is verified before being claimed lossless, or is explicitly reported as a limitation.
17. Existing 0000/0001/0002 backend and frontend suites remain green; test the updated UI in **uk** and **en** on disposable data only.

## 10. Implementation and delivery instructions for Claude

- Implement code, focused regression tests, and any required user-facing documentation/localization. Treat this document as the complete authorized scope. Do not rewrite earlier approved specs or edit the historical screenshot reference pack.
- Respect the existing project architecture, domain/API validation, transactional tournament saves, and current coding conventions. Make changes only necessary to this spec.
- Use disposable test instances/volumes for smoke checks. **Do not restart, redeploy, overwrite, or mutate the live localhost:8080 referee application, its database, or its volumes.**
- Run and report backend tests, frontend tests/typecheck, frontend production build, and smoke results. Note any not-yet-verified legacy XML serialization mapping explicitly.
- Record implementation commit(s), test counts, scope deviations, and outstanding limitations in docs/status.md and/or a review response as appropriate. Request GPT-6 review after implementation. Owner approval is required separately for deployment.
