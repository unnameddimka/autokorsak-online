# Decisions and open questions

Confirmed decisions live here, with the spec or clarification that established them. Changes to approved
behavior require a new numbered spec.

## Product decisions (owner clarifications, 2026-10-16, for spec 0000)

| # | Decision | Source |
|---|---|---|
| P1 | Spec 0000 is approved. Further requirement changes go into numbered specs starting at 0001. | Owner answers to the 0000 questions |
| P2 | The reference tournament `А_11.12.21.xml` uses the **Swiss** system; legacy `Kind=2` = Swiss. Other `Kind` values are **not verified** and must not be invented. Unknown values are preserved on round-trip. | Owner answers §1 |
| P3 | The repository is public. The existing historical XML fixtures (real public tournaments from 2021) are **approved for the public repository**, development, tests, and demos. No anonymization is required. This approval does **not** cover future imports containing non-public personal data. | Owner answers §2 |
| P4 | No extra legacy files are created to reverse-engineer unknown XML fields. Fields without a verified representation (team, coach, nationality, country, club, rank shift, unverified criteria) are stored in the app only, and the export reports them. | Owner answers §3 |
| P5 | All four legacy XML files may be used for importer robustness tests. `А_11.12.21.xml` remains the mandatory P0 acceptance fixture. Byes and skipped rounds stay deferred. | Owner answers §4 |
| P6 | UI languages: Ukrainian (default) and English only. Russian is prohibited. | Spec 0000 §2 |
| P7 | Future automatic pairing priority (McMahon): score group first, rating within the group, then pairing decisions; soft constraints are secondary. Not implemented in P0. | Owner answers §1, spec 0000 §10 |

## Implementation decisions (spec 0000)

| # | Decision | Rationale |
|---|---|---|
| I1 | Stack: .NET 10 Minimal API + React 19/TypeScript/Vite + SQLite, in one container. | Spec 0000 §8 baseline; .NET 10 is the current LTS. |
| I2 | Each tournament aggregate is stored as **one JSON document** in SQLite, not as relational tables. | Atomic saves, simple schema evolution, no ORM. The model is normalized in the domain sense: it is the authoritative app model and the XML is only retained for compatibility. Revisit if multi-user editing or cross-tournament queries appear. |
| I3 | Legacy export **patches the original document** instead of regenerating it. | This is the only reliable way to preserve unknown elements, order, and omitted defaults. Verified by a deep-equality test on all fixtures. |
| I4 | The export report comes from export → re-import → diff. | It is truthful by construction: it cannot drift from the actual mapping. |
| I5 | A new tournament with an unverified system (round robin, Scheveningen, McMahon) **cannot be exported** to XML (clear message). An imported tournament keeps its original `Kind`. | Owner: document the limitation rather than generate incorrect XML. |
| I6 | Legacy criterion token `Score` maps to *wins* in non-McMahon systems and to *total points* in McMahon. `SOS`/`SODOS`/`SOSOS` map directly. Other criteria have no verified token: they are kept in the app, omitted from the export, and reported. Unknown imported tokens are kept as `legacy:<token>` and written back. | Tokens observed in fixtures; screenshot 02 describes "Очки" as McMahon points. |
| I7 | Standings-column tokens (`Place`, `Name`, `Country`, `Club`, `Team`, `Coach`, `Grade`, `Rank`, `Rating`, `Group`, `Tours`, `Criterias`, `NewRating`) are treated as verified. | All observed in the fixtures. |
| I8 | Result tokens: `1-0`, `0-1`, `0-0` (both lose), `?-?` (unset). A **draw has no verified token**: it is exported as `?-?` (never as a wrong winner) and reported. | Observed tokens only. |
| I9 | Bye: `PairingWithBye=true`, the player in `Black`, no `White` element; `Black=-1` means no player. Since spec 0001 a bye scores one win and one point. | Observed in `9х9…` and `Б…` fixtures; scoring per spec 0001 §4. |
| I10 | `FirstMoveColor` `0` = Black (all fixtures; screenshot default). "White first" is stored in the app; on export the original value is kept, otherwise it is reported. | No sample with white first. |
| I11 | `ForcedPairing` = manual pairing protection. `AllowJigo` (pairing level) = draw allowance. `BoardNumberFixed`, `ResultByReferee`, `Handicap`, and `AdditionalKomi` map by name. | Element names match the legacy pairing editor (screenshot 13). |
| I12 | Grade maps to the legacy integer `Grade` (`0` = none). Non-numeric grades are app-only. | Only `0` is observed. |
| I13 | Club is read from `GoPlayer/Club/Name` when present but **never written**. | The structure is unverified (all fixtures have an empty `<Club />`). |
| I14 | Rows tied on every enabled criterion share a place (`3–4`). Order within a tie: rating, then name. Round cells reference the opponent's **row number**. | Mirrors the legacy table (row numbers in round cells) without inventing tie-breaks. |
| I15 | The current round is one persisted tournament value (like the legacy `CurrentRoundNumber`). The navigator changes it, and it drives both standings and the pairings view. | Matches legacy semantics and spec §5.6. |
| I16 | A new pairing created in the UI is **protected** by default. | Manually entered pairings should survive a future automatic pairing unless the referee unprotects them. |
| I17 | Catalog upsert matches linked participants by the stored link. Unlinked participants are matched on an equal normalized display name or an equal non-zero EGD PIN, and each match needs an explicit decision (link / create / skip). | Spec 0000 S4: never silently merge namesakes. |
| I18 | State-changing requests require the `X-AutoKorsak-Client` header (CSRF guard for an unauthenticated local server). | Cheap protection against other websites driving `127.0.0.1`. |
| I19 | A finally registered player with pairings cannot be changed to preliminary (409 `participant.pairedCannotBePreliminary`). Imported preliminary players with games stay editable. | Review P1-01: preliminary players must not be paired; nothing historical is erased. |
| I20 | Converting a game to a bye clears its result and referee flag. If a result was recorded, the existing discard confirmation is required first. Editing an existing bye keeps its retained legacy data. | Review P1-02: a bye cannot carry a two-player result. |
| I21 | Legacy byes have board number 0. Board 0 is accepted for byes only and is excluded from the board-uniqueness check. | Fixtures store byes without a board, so they must stay editable. |
| I22 | `skippedRoundsNotAdjusted` is reported for a counted round when a final player is **explicitly absent** (`NotPlayingRounds`), whether or not results exist. It is also reported when an available final player has no game in a round that is being played (at least one game has a result). Available but unpaired players in an empty or unplayed round raise nothing (the round is awaiting pairings), and rounds excluded from calculation raise nothing. | Review P2-01 and re-review of `bfe922c8`. |
| I23 | `NotPlayingInRound` values that are malformed, out of range, or repeated make the import fail (`xml.invalidValue`) instead of being dropped. | Review P2-04: never lose supported data silently. |
| I24 | The bind address is configurable (`AUTOKORSAK_BIND`, default `127.0.0.1`). LAN exposure is an explicit, machine-local opt-in through a git-ignored `.env`. | The owner asked for LAN access to the demo machine (2026-10-16); the repository default stays local-only as spec 0000 §8 requires. |

## Spec 0001 decisions (2026-10-16)

The product rules are in spec 0001 (approved by the owner on 2026-10-16). These are implementation choices and
interpretations within it.

| # | Decision | Rationale |
|---|---|---|
| R1 | "Drawn" in the Settings lock means **any** pairing in the sole stage, including a manual one. | Safest reading of §2.1; agreed during spec review. |
| R2 | A finalized participant whose joining round falls inside a not-yet-drawn round robin, after its start, **blocks the first draw** with an error naming them. | Spec §2.3 "or the referee must explicitly change it"; nobody is silently left out of the roster. |
| R3 | Berger orientation: the standard FIDE tables, with the first-listed (first-moving) player as **Black**. | Spec §3.1 asks for a standard colour table; in Go, Black moves first. Tests verify the 4- and 6-player tables and colour balance ≤ 1. |
| R4 | ~~An unplayed pairing with the same two players is kept whatever its colours.~~ **Revised after review of `e7b57590` (P2-02):** only **played** games match in either colour (§3.4); since spec 0002 an entered result locks round-robin generation altogether. An unplayed game must have the exact Berger orientation; otherwise it is replaced after the usual confirmation. | The spec exempts played games only; the fixed Berger colours must be restorable. |
| R5 | Generated pairings take board numbers 1…n in fixture order, skipping boards used by kept pairings; the bye takes the next number. | Spec §3.1: existing board conventions. |
| R6 | A legacy bye with no player (`Black=-1`) awards nothing, raises no notice, and is not an awarded outcome. | There is no recipient; spec §4 scores the sole real participant. |
| R7 | The default joining round for a new player is the current round if it has no **awarded outcome**, otherwise the next round, adjusted for round-robin stages. An awarded outcome is a recorded result or a bye with a real player (revised after review P2-03). If no round is left, the API returns `participant.joinRoundRequired`, or the specific round-robin reason when one applies. No round is ever created automatically. | Spec §2.3 and test 20. |
| R8 | Joining-round constraints for frozen round robins apply to **finalized** participants. Preliminary players may be registered with any round; the constraints are checked when they are finalized (individually or in a batch). | Spec §2.4: finalization is the moment of admission. |
| R9 | The XML export does **not** write `NotPlayingInRound` for rounds before a late player's joining round; it only reports the joining round as lost. | "Not playing" could trigger legacy skipped-round scoring; spec §6 asks to report, not to invent. |
| R10 | The "sort by rating within score groups" preference is stored per tournament in browser `localStorage`. | Spec §7.1: a view preference, not tournament data. |
| R11 | **Awarded outcome** (`Pairing.HasAwardedOutcome`) = a recorded result **or** a credited bye. It governs joining-round defaults, the stage-change barrier (a credited bye blocks a change exactly like a played game), generator immutability (a credited bye that is not the fixture bye blocks generation), and the discard confirmation for deleting or reassigning a pairing. The "round is being played" condition for skipped-round notices still counts only two-player results, so a bye entered first does not flag the unpaired players. | Review P2-03; spec §1 (outcomes are never removed automatically) and §2.2. |
| R12 | The Settings system lock is **monotonic**. `Tournament.PairingsEverDrawn` is set on the first manual or generated pairing, on import when pairings exist, when data saved before 0001 that already contains pairings is loaded, and whenever a pairing is deleted. A frozen round-robin roster also locks. | Review P2-01; spec §2.1 "once any pairing has been drawn". |
| R13 | A frozen round-robin member cannot be marked unavailable in a round of that stage (409 `participant.rosterMemberUnavailable`); the editor disables those checkboxes. Withdrawals use referee-decision technical results (§5.3). Imported states that already contain such absences still stop generation with `generate.playerUnavailable`. | Review P1-01; spec §2.3 "cannot silently invalidate frozen schedules". |

## Spec 0002 decisions (2026-10-16)

Spec 0002 (approved by the owner on 2026-10-16) **overrides** three 0001 behaviours:
- implicit registration-order starting numbers are removed;
- round-robin regeneration around played games (0001 §3.4 / R4) is replaced by the result lock;
- the monotonic Settings lock (R12) may be reset, but only by a successful single-stage Clear that leaves no
  pairings.

The 0001 tests that relied on the old behaviour were rewritten (registration order became explicit numbering;
"played game kept on regeneration" became "generation locked").

| # | Decision | Rationale |
|---|---|---|
| S1 | Player card API: `startingNumber` null = unchanged, 0 = clear (internal sentinel sent only for an emptied field), positive = set. The card validates and sends the number only when it changed, so a read-only frozen number never blocks other edits. The Pairings table uses `PUT …/starting-numbers`, where null = clear and **0 is invalid** (revised after review of `4ecfd464`). In both UIs a typed literal 0 is rejected, not treated as clearing. Both share one server rule set (`CheckDraftEditable`). | Old clients and unrelated card edits never touch drafts; identical rules for both editors (§6). |
| S2 | Drafts are whole numbers 1–999. Edits are refused (409) when the stage is frozen, the player is not eligible (preliminary, or joining after the stage start), or the tournament has no round-robin stage. A batch with any bad row applies nothing. Gaps and duplicates are only checked at generation. | Spec §2.1–2.2; agreed during spec review. |
| S3 | The confirmation order is the number source first (a client-side choice dialog when "random" is ticked and drafts exist), then the server's `generate.roundCountAdjustment` 409, then one final request with `source` and `roundCount`. The server rechecks everything on that request. | Spec §4.2 fixed order; a 409 confirmation request writes nothing. |
| S4 | The server validates entered numbers **before** asking about the round count, so invalid numbers are reported without a pointless resize prompt. | Matches "numbers first". |
| S5 | Clear has a preflight (`confirm=false` → 409 `clear.confirm` with counts) so the dialog shows exact numbers; the confirmed request rechecks the lock. | Spec §5.1 confirmation contents; §6 revalidation. |
| S6 | The header save indicator ignores 409 confirmation requests (`clear.confirm`, `generate.roundCountAdjustment`, `generate.replaceUnplayed`, `pairing.resultWouldBeDiscarded`, `stage.unplayedPairingsWillBeDeleted`). | Nothing was attempted, so "last change not saved" would mislead. |
| S7 | After a clear that unlocks Settings and a switch to another system, stored drafts remain but are hidden, because the field only appears in tournaments with a round-robin stage. | Agreed during spec review; no data loss. |
| S9 | Unsaved Pairings-table edits block both Generate and Clear. | Review of `4ecfd464` (P2-02): a server update must never silently discard local edits. |
| S8 | The browser smoke test runs on a separate disposable container (`127.0.0.1:8091`); the live instance is not redeployed without the owner's instruction. | Spec §7.4 "do not deploy without the owner's separate instruction". |

## Open questions (for the referee / owner)

1. **Legacy `Kind` codes** for round robin, Scheveningen, and McMahon. The legacy settings dialog lists them in the
   order round robin, Scheveningen, Swiss, McMahon, and Swiss = 2 matches that order. This is a strong hint, but
   unverified. Note that `13x13.xml` and `9х9…` have `Kind=0` with 5 rounds for 10 and 9 players, which does not
   look like round robin.
2. **Rating → rank mapping** for "derive rank from rating". The stored ranks of all 10 players in `А_11.12.21.xml`
   fit `kyu = 21 − ⌊rating / 100⌋`; how this relates to `RatingLowestOneDanRating = 2050` is unclear. Not
   implemented; ranks are entered manually.
3. **XML representation** of team, coach, nationality, country, club, rank shift, and the criteria Перемоги
   (McMahon), Очки₂, Дод., Підйом, Зустріч. This needs a legacy sample with these filled in, if exact compatibility
   becomes important.
4. **Scoring rules** for skipped rounds (the settings exist), draws, both-lose results, and McMahon start scores in the
   official standings (byes are settled by spec 0001).
5. Whether the exported XML opens in the original AutoKorsak 1.38. It is reimportable here, but it has **not** been
   tested in the legacy application.
