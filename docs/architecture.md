# Architecture

Current state as of spec 0000. Keep this document in sync with the code.

## Components

```
browser ── React SPA (frontend/) ──HTTP/JSON──▶ ASP.NET Core Minimal API (AutoKorsak.Api)
                                                   │            │
                                    AutoKorsak.Domain     AutoKorsak.LegacyXml
                                    (model, rules,        (AutoKorsak 1.38 XML
                                     standings, catalog)   import/export/report)
                                                   │
                                           SQLite file (/data volume)
```

| Component | Responsibility | Must not |
|---|---|---|
| `frontend/` (React 19, TypeScript, Vite, react-router) | Screens, forms, localization (`src/i18n`), calls the API, shows the API's localized errors | Contain business rules or calculations |
| `AutoKorsak.Domain` | Aggregate model, all validation and editing rules (`TournamentEditor`), standings (`StandingsCalculator`), catalog upsert planning (`CatalogUpsert`) | Know about HTTP, SQLite, or XML |
| `AutoKorsak.LegacyXml` | Safe parsing, import into the domain model, export by patching, export limitation report | Contain business rules beyond format mapping |
| `AutoKorsak.Api` | HTTP endpoints, error mapping, SQLite persistence, write serialization, serving the SPA | Contain business rules |

In production, one container serves the compiled SPA (`wwwroot`) and the API on port 8080. Docker Compose binds it to
`127.0.0.1` only.

## API shape

- `GET/POST /api/tournaments`, `POST /api/tournaments/import` (multipart `file`), `GET/DELETE /api/tournaments/{id}`
- `PUT …/settings`, `PUT …/round` (current round + include flag), `GET …/standings`
- `POST/PUT/DELETE …/participants[/{pid}]`, `POST …/participants/finalize-preliminary`, `…/delete-preliminary`
- `POST …/rounds/{n}/pairings`, `PUT/DELETE …/pairings/{pid}[?confirmDiscardResult=true]`
- `GET …/export-report`, `GET …/export.xml`
- `GET /api/catalog?search=`, `GET/POST …/catalog-upsert`

Every mutation returns the full updated tournament view, so the UI simply replaces its state. Errors are
`{ code, args }` with status 400 (validation), 404, or 409 (conflict or confirmation needed). The UI translates
`error.<code>`.

**CSRF guard:** state-changing `/api` requests must carry the `X-AutoKorsak-Client` header. A cross-site page
cannot add it without a CORS preflight, and the server never approves preflights.

## Persistence

- SQLite (`Microsoft.Data.Sqlite`) at `Storage:DatabasePath` (`/data/autokorsak.db` in Docker), WAL mode,
  `PRAGMA user_version = 1`.
- `tournaments` table: one row per tournament. The **whole aggregate is stored as a JSON document** (`data`), and
  the list columns (`name`, `player_count`, …) are denormalized. Saves are atomic, and an API-level lock
  serializes writes (P0 assumes a single local referee).
- `catalog_entries` table: one JSON document per shared player entry.
- A catalog upsert writes the tournament (new links) and the catalog entries in one transaction.

## Domain model and invariants

- `Tournament`: settings, ordered `Criteria` (all known keys, each enabled or disabled), `Layout` columns,
  pairing constraints, calculation options, `CurrentRound`, `IncludeCurrentRound`, participants, rounds.
- `Participant`: tournament-local snapshot. `Id` is the local legacy ID that pairings reference. The optional
  `CatalogEntryId` points to the catalog. Catalog edits never change existing tournaments.
- `Round` → `Pairing` (black/white IDs, bye flag, board, result, protection, handicap/komi, comment).
- Standings are **derived** on every request from pairings and results; they are never stored.
- Enforced rules: a player appears at most once per round; board numbers are unique per round; a new assignment
  requires final registration and availability in that round; a pairing with a result cannot be deleted or
  re-assigned without confirmation; a player with games cannot be deleted; the round count cannot drop below the
  last paired round.

## Standings calculation (P0)

Only counted rounds take part: rounds `1..CurrentRound`, minus the current round when `IncludeCurrentRound` is off.

- win = 1, draw = ½ (simplified), both lose = 0;
- SOS = sum of opponents' points;
- SODOS = sum over games of own game score × opponent's points;
- SOSOS = sum of opponents' SOS.

Unset results, byes, unknown legacy results, skipped rounds, and McMahon start scores are **not** compensated.
Instead the API returns notices that the UI displays. Rows are ordered by the enabled, calculated criteria.
Players equal on all of them share a place; within a tie the order is by rating, then by name.

## Legacy XML import and export

- **Parsing:** DTDs are prohibited and no resolver is used (XXE-safe); input is capped at 10 MB and nesting depth
  at 32. Validation (root and version, duplicate IDs, dangling references, duplicate rounds, double assignment)
  runs before anything is stored, so an invalid file never creates a tournament.
- **Import:** keeps the full original document (`LegacySourceXml`) plus the original element of every participant,
  round, and pairing.
- **Export:** re-parses the original document, writes the **verified** mapped fields (see `LegacyFormat.cs`), and
  rebuilds the participant and round elements from the retained originals. Everything unmapped stays exactly as it
  was. Elements absent in the source (the legacy serializer omits defaults) stay absent while they hold a default
  value. A tournament created in the app starts from an embedded template: the reference file's settings and
  public country/club catalogs, **without participants**.
- **Report:** `ExportAnalyzer` exports, re-imports, and diffs. Every app value that would not survive appears in
  `GET …/export-report`, and the UI lists these before download. A system without a verified `Kind` code blocks the
  export rather than writing a guess.
- The interface locale never reaches the XML.

Tests show that an unedited export of all four legacy fixtures is semantically identical to the source document
(`LegacyXmlTests.Unedited_export_preserves_every_original_element`).
