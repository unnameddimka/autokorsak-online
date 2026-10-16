# 0000 — AutoKorsak Online: Functional UI Prototype

**Status:** Proposed canonical specification for initial implementation  
**Priority:** P0 — deliver a usable, containerized website for an early review with the tournament referee  
**Canonical specification language:** **English**  
**Product interface languages:** **Ukrainian (`uk-UA`) and English (`en`) only**  
**References:** AutoKorsak 1.38 screenshots, `legacy-autokorsak/А_11.12.21.xml`, related legacy TXT exports, the screenshot reference pack under `docs/reference/legacy-ui/`, and product decisions made with the tournament expert.

## 1. Goal and governing principles

Build a new web application for managing Go tournaments. It must run in Docker, open in a desktop browser, load a real legacy tournament, and support enough actual editing to let the tournament referee assess the proposed workflow and interface.

**This is not a port of AutoKorsak and not a recreation of its architecture.** Legacy software provides examples of existing workflows and a file format for compatibility. The referee's expertise and explicit product decisions determine the requirements. Do not reproduce legacy quirks merely because they exist.

**Definition of success for specification 0000:** The referee can import `legacy-autokorsak/А_11.12.21.xml`, inspect the tournament standings, participants, pairings, and rounds, edit a participant and a pairing, save those changes in the application, and download an updated XML file that can be imported again. A new tournament can also be created from scratch.

Optimize for a coherent, demonstrable workflow, not for comprehensive implementation of every tournament algorithm. Never present an inert control as a working feature.

## 2. Language and localization policy (mandatory)

### 2.1. Supported interface languages

- The complete user interface must support **Ukrainian (`uk-UA`) and English (`en`)**.
- **No Russian interface locale (`ru`, `ru-RU`, or equivalent), Russian translation bundle, Russian fallback, or Russian interface copy is permitted.** Do not add Russian as a third language or silently inherit Russian text from legacy screenshots.
- All visible product-owned strings must be localized: navigation, page titles, dialog titles, field labels, help text, placeholders, tooltips, table headings, status indicators, validation errors, warnings, confirmation messages, empty states, accessibility labels, and notices for unimplemented features.
- Provide an obvious, functional language selector in the application shell. Switching languages must update the current view without discarding unsaved edits. Persist the selected language across reloads (browser preference is sufficient for this prototype).
- Use Ukrainian as the initial UI default unless the user previously selected English. Treat this default as an easily changeable product setting, not a permanent domain rule.
- Every interface message must have a translation in both supported languages. Missing translations are a defect; use a deterministic **English** development fallback, not Russian.
- All canonical requirements, specification files, architecture notes, and implementation review documents are written in **English**. Use English for internal API identifiers, code identifiers, and comments where practical.

### 2.2. Language must not corrupt tournament data

- **Interface locale is not the language of imported user data.** Preserve proper names, tournament titles, club names, comments, and free-text content exactly as supplied, including legacy Cyrillic text. Do not transliterate, translate, reject, or rewrite user data merely because it is not Ukrainian or English.
- For example, the reference tournament title `Группа А` must remain unchanged after import and XML round-trip. This is source data, **not Russian UI localization**.
- Legacy XML field names and compatibility tokens remain as specified by the format; they are not user-facing interface labels.
- Date/number formatting may follow the selected interface locale where relevant; stored numeric values and import/export data must remain locale-independent.

### 2.3. Representative UI terminology

| Concept | Ukrainian UI | English UI |
|---|---|---|
| Tournaments | Турніри | Tournaments |
| New tournament | Новий турнір | New tournament |
| Import XML | Імпортувати XML | Import XML |
| Save as XML | Зберегти як XML | Save as XML |
| Standings | Турнірна таблиця | Standings |
| Pairings | Жеребкування | Pairings |
| Players | Гравці | Players |
| Tournament settings | Налаштування турніру | Tournament settings |
| Draw / generate pairings | Жеребкувати | Generate pairings |
| Manually protected pairing | Жеребкування власноруч | Manual pairing (protected) |
| Preliminary registration | Попередня реєстрація | Preliminary registration |
| Final registration | Остаточна реєстрація | Final registration |

These labels are illustrative, not a substitute for a complete two-language resource dictionary. Feature names visible only for later implementation must also have both translations.

## 3. Scope of the first implementation

### 3.1. Included (P0)

1. Docker-based website and persistent server-side storage across container restarts.
2. Tournament list, creation, opening, and editing tournament settings.
3. **Import of legacy AutoKorsak 1.38 tournament XML and export of the current tournament back to XML.**
4. Working tournament area with **Standings**, **Pairings**, and **Players** screens.
5. Player editor for creation and editing, registration status, and per-round availability.
6. Pairing editor: create manually, edit, delete, swap colors, record result, and protect a manually entered pairing.
7. Navigate rounds and include/exclude the current round from standings calculations.
8. Basic, calculated standings for regular completed games in the supplied reference tournament.
9. Persistent shared player catalog: search/select on tournament enrollment and explicit **upsert from a tournament**, with no separate catalog-management page.
10. Desktop-friendly UI, bilingual localization, validation, confirmation for destructive actions, and clear persistence feedback.

### 3.2. Explicitly excluded from P0

- Automatic pairing algorithms for any tournament system. The pairing generation action may be shown but must be clearly disabled and labeled as unavailable in both languages.
- Full handling of skipped rounds, unusual result types, advanced tie-break adjustments, handicap scoring, and other unverified competition calculations. Retain relevant source data where possible.
- Post-tournament rating change calculations, rating history, and rating charts.
- European Go Database (EGD) integration, imports/synchronization, and new-format EGD rating submission exports.
- Printable reports and TXT/CSV/PDF report export.
- Standalone management UI for the shared player catalog.
- Authentication, role-based permissions, public publishing, or exposing the prototype to the internet.
- Generalization for other board games. Go is the design priority.
- The legacy "Assistant" wizard.
- SORP-specific editable result-points field.

**Important distinction:** Exporting the tournament as **XML** is in P0. Delaying reports and EGD export does not delay XML save/download.

## 4. Primary user journeys

### S1 — Review an imported real tournament

1. Open the website, go to Tournaments, select Import XML.
2. Upload `legacy-autokorsak/А_11.12.21.xml` from the repository working tree.
3. See the `Группа А` tournament: 10 players, six rounds, and 30 completed games.
4. Inspect the five pairings in round VI; switch to round III and back.
5. Open a player, edit a comment or starting rating **for this tournament only**, and save.
6. Open a pairing, edit its result, and see the standings update.
7. Download the current tournament as XML; reimport it and verify that the changes survived.
8. Switch between Ukrainian and English on the same screens and continue editing without data loss.

### S2 — Create a tournament

1. Create a tournament and set its main configuration and ranking criteria.
2. Add players by entering their data or selecting from the shared catalog.
3. Set round availability, manually create pairings, assign colors, and record results.
4. Navigate rounds, inspect standings, refresh the page, and verify that saved changes remain.
5. Export the tournament as XML.

### S3 — Preliminary registration

- A preliminarily registered player appears in the tournament's player list and standings, but **must not be paired**.
- A finally registered player is eligible for a round only if their per-round participation flag is enabled.
- Provide batch actions to **finalize all preliminary registrations** and **delete all preliminarily registered players**. Deletion requires confirmation and must respect existing game data.

### S4 — Populate the shared player catalog

- Editing a player inside a tournament never silently updates the shared catalog.
- A referee explicitly invokes **Upsert players to catalog** from the tournament UI.
- Show the number of entries to create/update and an explicit confirmation before applying changes.
- Upsert inserts missing records and **overwrites all editable catalog fields** of matched records, including rating in P0.
- If a tournament player is linked to a catalog entry, match by that stable internal link. For unlinked players, show possible matches and require an explicit choice between linking/updating an existing record and creating a new entry. **Never silently merge namesakes.**
- Catalog updates do not retroactively alter existing tournaments. A future independently maintained rating system may restrict direct edits of canonical ratings; that restriction is not part of P0.

## 5. UI structure

### 5.1. Application shell and navigation

- Header: product name, current tournament name, link to tournament list, save status, and Ukrainian/English language selector.
- Tournament list: open, create, and import XML; show name, player count, number of rounds, and last modification.
- Within a tournament: **Standings**, **Pairings**, and **Players** screens, with round navigation near the relevant content.
- Editors may use dialogs or side panels. Do not copy the exact dimensions or density of the old WinForms UI.
- Include accessible controls, understandable empty states, and localized errors/warnings.
- Do not expose unsupported operations as if implemented.

### 5.2. Tournament settings

**General:** Name, description, board size (including 19×19), first-moving color, tournament system (**round-robin, Scheveningen, Swiss, McMahon**), number of rounds, handicap enablement, draw allowance, **derive rank from rating** flag, and calculation-customization flag.

**Ranking criteria:** An enabled/disabled and ordered list; change order by drag-and-drop or accessible move up/down buttons. In McMahon, treat **total points** and **number of wins** as distinct criteria; for the other systems, offer **number of wins** from this pair. Other legacy criteria may be displayed and stored without pretending they are all calculated at P0. Preserve ordering and enabled state.

**Standings layout:** Configurable visible columns and order: place, name, country, club, team, coach, grade, rank, rating, group, round results, criteria, and other mapped fields. Include **show player colors** and **hide empty columns** switches.

**Pairing settings:** Store soft constraints against same country, same club, same nationality, same team, and same coach. The automatic solver is outside P0. Established future priority: pair primarily by playing strength within the relevant score group, relaxing soft constraints when necessary.

**Calculations:** Show this settings tab only when the calculation-customization setting is enabled. Store skipped-round score handling choices (add 0.5 points; alternate 0, 1, 0, 1 starting with 0; add nothing), coefficient adjustment options, and **top group only**. The last switch applies to **all** options on this tab. Advanced calculations are not implemented in P0, so label those options appropriately.

A setting that affects implemented display behavior must work. A retained setting awaiting a later calculation engine must be explicitly identified as such in the interface.

### 5.3. Standings

- Show place, player name, rank, starting rating, per-round results, points/wins, and supported tie-break coefficients.
- A round cell may show opponent number, outcome (+/−), and optionally color or special status.
- Select a player to open their editor; selecting a round may navigate to its pairings.
- Compute standard completed game scores and, for the provided reference tournament, **SOS (Buchholz), SODOS (Berger), and SOSOS (secondary Buchholz)**, where applicable and verified.
- Recompute when results change or the current-round inclusion switch is toggled; no manual "refresh standings" action should be required.
- Do not invent ratings after a tournament or silently use unsupported tie-break formulas for special cases.
- In a McMahon tournament, distinguish total points from wins; defer advanced McMahon behavior to a later specification.

### 5.4. Player list and editor

Fields: local first name/surname (retaining the exact original full-name representation), English first name/surname, starting rating, rank, grade, grade shift, country, club, nationality, team, coach, preliminary/final registration state, participation by round, and comment.

- The same editor handles adding and changing a tournament player.
- A tournament participant may be copied from the shared catalog or created without a catalog entry.
- All editing affects only that tournament participant until explicit catalog upsert.
- The **derive rank from rating** tournament option must be represented. The exact rating-to-rank mapping still requires confirmation; do not invent one. Until defined, permit a clearly indicated manual rank field.
- EGD lookup and transliteration tools are deferred.
- Warn before deleting a player with existing games. P0 may block such deletion with a clear explanation instead of silently destroying games/results.
- The participant status and their per-round availability are independent conditions for pairing eligibility.

### 5.5. Pairings list and pairing editor

Pairings for a selected round: board number, black player, white player, result, handicap, and comment. A winner marker such as a star is **purely a UI presentation of the recorded result**; it is not a separate result field.

Editor fields: board number; fixed board-number flag; black/white participant selection and swap; delete pairing; handicap; additional komi; draw allowance; result (including unset); result by referee decision; comment; and **manual pairing protection**.

- A manually protected pairing must remain untouched when automatic pairing is implemented later, including its players and their colors.
- The eventual regenerate-pairings operation will replace all **unprotected** pairings and exclude players committed to protected ones. If replacing a pairing would discard an existing result, obtain confirmation. In P0, automatic regeneration is disabled.
- A player cannot appear in two pairings in the same round.
- A player with preliminary registration or disabled availability for that round cannot be assigned to a new pairing without an explicit registration/availability correction.
- Board numbers must be unique within a round.
- Store unsupported original XML attributes (including `ResultPoints`) transparently for round-trip, but do not show a SORP score input.

### 5.6. Round navigation

- Current round is within `1..NumberOfRounds`.
- Support previous/next round controls and direct round selection.
- **Include current round in calculations** is a working switch affecting standings; preserve the legacy `TakeCurrentRoundInAccount` setting when imported.
- Empty rounds are allowed before the referee creates pairings manually.

### 5.7. Legacy screenshot reference pack (required)

The repository includes **15 original screenshots** at `docs/reference/legacy-ui/` (see that folder's `README.md` for the complete file index). Inspect them before implementing the corresponding views:

- `01`–`05`: creation/settings tabs and calculation options.
- `06`–`09`: standings, round pairings, tournament players, and round navigation.
- `10`–`12`: player editing, legacy rating chart (deferred), and registration actions.
- `13`–`15`: pairing editing, round commands, and legacy report commands (reports deferred).

**The screenshots are functional references, not pixel-perfect design targets.** Build a modern browser interface according to the confirmed product decisions in this specification. When a screenshot conflicts with this specification, the specification takes precedence; when functionality is unclear, ask the tournament expert rather than assuming legacy behavior is desirable.

## 6. Legacy XML import and export (mandatory P0)

### 6.1. Supported input

**Required fixture placement (repository contract):**

- The real reference tournament XML **must be supplied in the repository working tree** at the exact relative path `legacy-autokorsak/А_11.12.21.xml` (relative to repository root). The first character of `А_11.12.21.xml` is **Cyrillic capital А (U+0410)**, not Latin `A`.
- The original binary may remain in the same directory as `legacy-autokorsak/AutoKorsak.exe`, but running or decompiling it is **not** a dependency for implementing specification 0000.
- Use this exact fixture for the importer, UI demo workflow, integration tests, and round-trip acceptance tests. Do not silently substitute generated mock data or another tournament when the file is missing. Tests that require it must fail with a clear message identifying the expected path.
- The file contains real player names and ratings. If the repository is public, the project owner must explicitly approve including these data or provide a sanitized fixture; otherwise keep the original file in a private working tree. Do not publish real participant data by accident.

- Import **AutoKorsak 1.38 XML**, with `Tournament typeversion="6"`, encoded as UTF-8. Use `legacy-autokorsak/А_11.12.21.xml` as the required reference fixture.
- Read tournament settings, players and nested `GoPlayer` objects, rounds (`TournamentRound`), pairings (`Pairing`), game results, standings layout, and criteria settings.
- `IndividualParticipant/Id` is an **identifier local to a single tournament**. `Pairing/Black` and `Pairing/White` refer to it. It is not the shared player catalog ID.
- The reference file often stores a complete local name in `LocalSurname`, leaving `LocalFirstName` empty. Show this name correctly **without guessing how to split it**.
- Large embedded legacy catalogs (`Country`/`Club`) and print settings are compatibility data, not a mandate to recreate the original application's domain architecture.

### 6.2. Persistence and XML round-trip

- Persist a normalized tournament model in the app database and retain the original XML document (or an equivalently complete representation) for legacy compatibility.
- Import XML as a new tournament by default. Invalid XML must produce a localized error and must not create a partial/corrupt tournament.
- **Save as XML** downloads the *current* state, including updated settings, players, rounds, pairings, and results; never return the unmodified original file after edits.
- For imported tournaments, preserve unknown and unsupported legacy XML fields/elements on export when not explicitly changed by the user. Update the mapped portions of the document without dropping unrelated legacy metadata.
- For newly created tournaments, generate a structurally valid AutoKorsak 1.38 XML document containing the supported fields. Never use copied participant data from the sample tournament to populate new documents.
- Our exported XML must be reimportable into our application without loss of supported fields. Successfully opening the export in original AutoKorsak 1.38 is desirable but requires an explicit legacy application test before claiming compatibility.
- Do not leak internal database identifiers, credentials, or arbitrary application metadata into legacy exports.
- XML parsing must disable external entities/DTDs (XXE), cap file size/depth, and handle invalid references safely.
- The chosen interface locale must never alter XML element names, IDs, result tokens, stored tournament text, or round-trip semantics.

### 6.3. XML acceptance tests

1. Import the reference: exact title `Группа А`, **10 participants, 6 rounds, 5 pairings per round, 30 completed results**.
2. XML → export without edits → import: tournament configuration, players, references, colors, games, and results remain equivalent.
3. Change one player rating, a comment, and one game result → export → import: changes persist and unrelated players/games are unchanged.
4. Unsupported source fields (`ResultPoints`, country/club catalogs, legacy print settings, etc.) survive a round-trip.
5. Malformed XML, duplicated participant IDs, and missing pairing references cause clear validation errors without database corruption.
6. Ukrainian text and all other imported Unicode data survive the round-trip exactly.
7. Export the same tournament while the UI is in Ukrainian and then English; the semantic XML data must be equivalent.

## 7. Domain model and invariants

Keep the **shared canonical player entry** separate from the **tournament-local player snapshot**:

- `PlayerCatalogEntry`: persistent ID, player data, optional external identifier. Modified only via explicit tournament-to-catalog upsert in P0.
- `Tournament`: ID, title, description, system, board size, round count, configuration, ordered criteria, layout, current round, current-round-in-calculations flag, and retained source XML metadata.
- `TournamentParticipant`: local ID, optional reference to `PlayerCatalogEntry`, copied name/rank/rating/affiliation data, registration status, round participation flags, comments, and compatibility fields.
- `Round`: round number and pairings, with effective inclusion in calculations.
- `Pairing`: ID, round, black/white participant references, board/fixed board number, result, manual protection, handicap/komi, comment, referee-decision marker, and retained legacy XML fields.
- `Standing`: **derived read model**, not the source of truth. Pairings and game results are authoritative.

Invariants: a tournament's historical data must not change when catalog entries are edited; tournament-local participants can differ from catalog records; editing a game result updates computed standings; every pairing participant belongs to the same tournament; pairings and board numbers are unique per round where required.

## 8. Implementation baseline

Suggested pragmatic stack for the first full-stack prototype:

- **Frontend:** React + TypeScript + Vite, with a lightweight localization solution and separate `uk-UA` and `en` resource sets.
- **Backend:** ASP.NET Core Minimal API; do not embed authoritative business logic in React components.
- **Database:** SQLite in a persistent Docker volume for tournaments and the player catalog.
- **Deployment:** Docker Compose. A single serving container for compiled static assets plus API is sufficient, with a mounted persistent SQLite data volume.
- One `docker compose up --build -d` must start the application; README documents the local address and start/stop commands.
- **No auth in P0 — local-only operation.** Bind the exposed port to `127.0.0.1` by default. Do not publicly deploy an unauthenticated instance containing player personal data.
- Persist changes through the API; `localStorage` may retain UI preferences such as language but must not be the system of record for tournament data.
- Keep XML import/export, domain/calculation logic, API, and UI in separate components. Do not create a generalized plugin framework.
- Keep the reference XML at its **single canonical path** `legacy-autokorsak/А_11.12.21.xml`; tests may read it from there but must not move or duplicate it to a different required fixture location. Import it explicitly for demonstrations rather than automatically seeding personal data in every new installation.
- The code and canonical specifications should use English identifiers/documentation. Localization is handled by interface resource bundles, not mixed-language hardcoded React strings.

### 8.1. Repository documentation and development workflow (mandatory)

Organize the repository around two deliberately different kinds of documentation:

1. **Historical, canonical development specifications:** store numbered, English-language specs in `docs/devel-specs/`. This initial spec lives at **`docs/devel-specs/0000-ui-prototype.md`**. Keep specs as a chronological record of what was agreed and implemented. After a spec is approved and published, do **not** silently rewrite it to reflect later product changes: write a new numbered spec for new scope or changed requirements, and link the superseding decision from the living documentation.
2. **Living project documentation:** create and maintain documents in the repository root and directly under `docs/`. They describe the **current** product, architecture, delivery workflow, and implementation status, and must be updated as development proceeds.

**The implementing agent (Claude) must create the following missing living documents as part of implementing 0000, not wait for the project owner to write them:**

| Path | Required contents |
|---|---|
| `README.md` | Product purpose, prerequisites, quick start (`docker compose up --build -d`), access URL, tests, and demo with the legacy XML fixture |
| `CLAUDE.md` | Concise, current agent/project working rules; English-only canonical specs; bilingual UI policy; documentation locations; safe edit, test, and review expectations |
| `docs/README.md` | Documentation index distinguishing current/living docs from the historical spec series and screenshot references |
| `docs/architecture.md` | The actual frontend/backend/data boundaries, persistence, XML import/export approach, and major model invariants |
| `docs/decisions.md` | Confirmed product and architectural decisions, open questions, and pointers to the numbered specs where they were established |
| `docs/status.md` | What works now, deferred features, test results/known limitations, next actions, and current implementation/spec progress |
| `docs/development-workflow.md` | Repeatable spec → implementation → commit/push → review → correction → deployment/demonstration workflow; clarify how to record review outcomes and version changes |

The agent must **create** these documents (where absent) and **maintain** them with meaningful, verified updates. Avoid fabricated progress, redundant dumps of the full specification, or documentation that contradicts the code. The initial versions can be concise; completeness grows with the project.

Keep **static evidence** separate from living docs: `docs/reference/legacy-ui/` contains the legacy screenshots and their index; `legacy-autokorsak/А_11.12.21.xml` is the single mandatory XML fixture outside `docs/`. Neither the original executable nor the screenshots are architectural mandates.

The implementing agent may choose the code directory layout, but it must preserve these documentation and fixture paths and make all required resources discoverable. Do not invent a Git remote, deployment endpoint, or existing CI workflow; document and use actual ones once configured.


## 9. Quality and acceptance criteria

### 9.1. Functional smoke tests

- With an empty database, create a tournament, a player, and a pairing; set a result; refresh and confirm persistence.
- Import the 2021 XML; verify 10 players, six rounds, 30 games, and the same five pairings/results in round VI as in AutoKorsak.
- For the reference tournament, confirm standard scores and tie-breaks: **Кобзар Гліб: 6 wins, SOS 21, SODOS 21, SOSOS 111; Єрємєєв Матвій: 5 wins, SOS 17, SODOS 11, SOSOS 113**.
- Edit the result in round VI, observe updated standings, export XML, reimport, and confirm the result survives.
- Change a tournament player's data and verify the shared catalog is unchanged; explicitly run upsert and verify the catalog changes while other existing tournament snapshots remain independent.
- A preliminarily registered player cannot be assigned to a new pairing; batch-finalization works.
- Double assignment in one round is rejected; destructive edits cannot silently discard completed games.
- Restart containers and verify tournament and catalog persistence.
- Unsupported buttons/actions are visibly disabled or clearly identified, not fake-working.

### 9.2. Localization smoke tests

- Language selector offers **exactly Ukrainian and English**.
- Switch `uk-UA` ↔ `en` on tournament list, settings, standings, pairing editor, player editor, XML import/export, and confirmation/error dialogs.
- No application-owned interface string appears in Russian in either locale. Do not ship a Russian resource file or locale option.
- No missing translation keys, mixed-language status text, or broken layouts due to longer translations in major workflows.
- Selected language survives refresh and does not reset an unsaved form.
- Imported tournament titles, player names, and comments remain unchanged when switching interface language.
- XML import/export works identically in both locales.

### 9.3. Automated test minimum

XML import/export round-trip; standard standings coefficients on the reference fixture; no duplicate pairing assignment; catalog-snapshot independence; explicit upsert; persistence/API smoke tests; and a check for translation key parity between Ukrainian and English resources (and the absence of a Russian locale).

**Delivery:** source code, Dockerfile/Compose, README, tests, the mandatory living documentation listed in §8.1, and a runnable local prototype where the environment permits. A set of static mockups alone does not satisfy this specification.

## 10. Known unknowns and intentional deferrals

1. **Automatic pairing** is a later specification. Established future principles: in McMahon, determine score groups first, then use rating to order players within a group; soft constraints cannot override the main strength/score-group priority; protected manual pairings remain unchanged.
2. The precise **rating-to-rank mapping** must be supplied or approved by the expert. Do not silently invent one.
3. Full rules for skipped rounds, handicap, draws, special outcomes, and nonstandard coefficients remain out of P0. Store original data without pretending unsupported calculations are correct.
4. **EGD export format has changed** since the legacy files. Do not implement an EGD submission exporter until the current format is supplied and confirmed.
5. Privacy, personal-data publishing permissions, and EGD data licensing must be resolved before a publicly accessible deployment or EGD synchronization. This prototype is local and does not import EGD data.
6. Scheveningen is included as a configurable tournament system despite infrequent local use, but no automatic pairing/calculation logic is promised for it in P0.
7. If further questions arise about workflow or business semantics, prefer the referee's explicit decision over the behavior of the legacy app.

## 11. Suggested implementation sequence

1. Minimal working full-stack app, bilingual UI foundation, Docker Compose, and persistent storage.
2. Legacy XML importer, reference fixture, normalized model, and **reverse XML export**.
3. Tournament list and the main Standings/Pairings/Players screens with the real reference tournament.
4. Editors, validations, and persistent changes.
5. Shared catalog lookup and tournament-to-catalog upsert; tournament settings.
6. Smoke tests, localization review in both languages, XML round-trip verification, and **first referee demo**.

If scope conflicts arise, prioritize **preservation of real tournament data and a coherent editable referee workflow**. Unsupported advanced features must be transparent to the user. Subsequent changes should be made under later specifications, not as undocumented additions to 0000.
