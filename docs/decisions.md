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
| I9 | Bye: `PairingWithBye=true`, the player in `Black`, no `White` element; `Black=-1` means no player. Byes are displayed but not scored. | Observed in `9х9…` and `Б…` fixtures; scoring deferred (spec 0000 §3.2). |
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
4. **Scoring rules** for byes, skipped rounds (the settings exist), draws, both-lose results, and McMahon start
   scores.
5. Whether the exported XML opens in the original AutoKorsak 1.38. It is reimportable here, but it has **not** been
   tested in the legacy application.
