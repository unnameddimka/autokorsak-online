# AutoKorsak Online

A web application for running Go tournaments: players, manual pairings, results, and standings. It is a new
application, not a port. Legacy AutoKorsak 1.38 serves as a source of workflows and as a file format to stay
compatible with: tournaments can be imported from its XML files and saved back to XML.

The current version is the **functional UI prototype** defined in
[docs/devel-specs/0000-ui-prototype.md](docs/devel-specs/0000-ui-prototype.md), built for review with the
tournament referee. The interface is available in **Ukrainian** (default) and **English**.

> **Local use only.** There is no authentication. The port is bound to `127.0.0.1` on purpose, and the
> instance must not be exposed to a network: tournament data includes personal data.

## Prerequisites

- Docker with Docker Compose v2 (Docker Desktop on Windows/macOS).

Nothing else is needed to run the app or the tests. For frontend development without Docker you need Node.js 24;
for backend development you need the .NET 10 SDK.

## Quick start

```sh
docker compose up --build -d
```

Open **http://127.0.0.1:8080**. To use another port, set `AUTOKORSAK_PORT` (for example
`AUTOKORSAK_PORT=8090 docker compose up -d`).

```sh
docker compose stop                 # stop, keep data
docker compose up -d                # start again
docker compose logs -f app          # follow logs
docker compose down                 # remove the container, keep data
docker compose down -v              # remove the container AND all data (tournaments, catalog)
```

Data is stored in SQLite inside the `autokorsak-data` Docker volume and survives restarts and rebuilds.

## Demo with the reference tournament

1. Open the app and choose **Імпортувати XML / Import XML** on the tournament list.
2. Select `legacy-autokorsak/А_11.12.21.xml` from this repository. The first character of the file name is the
   Cyrillic letter **А**.
3. The tournament `Группа А` opens: 10 players, 6 rounds, 30 games. Expected standings: Кобзар Гліб with
   6 wins, SOS 21, SODOS 21, SOSOS 111.
4. Try the journeys:
   - edit a player (rating, comment) or a pairing result, and watch the standings update;
   - switch rounds;
   - toggle **Include current round in calculations**;
   - switch the interface language (open forms keep their edits);
   - **Save as XML**, then import the downloaded file again.

Other legacy files in `legacy-autokorsak/` (`Б_12.12.2021.xml`, `13x13.xml`, `9х9 5ть турів ПІДСУМОК.xml`) contain
byes and per-round availability. They import, but byes and skipped rounds are not scored yet.

## Tests

```sh
docker compose --profile test run --rm backend-tests    # xUnit: domain, standings, XML round-trip, API
docker compose --profile test run --rm frontend-tests   # typecheck + localization tests (vitest)
```

Both runners mount the repository read-only, and the backend tests read the fixtures from `legacy-autokorsak/`.
Without Docker:

```sh
cd backend && dotnet test          # needs .NET 10 SDK
cd frontend && npm ci && npm test  # needs Node.js 24
```

## Repository layout

| Path | Contents |
|---|---|
| `backend/src/AutoKorsak.Domain` | Domain model, editing rules, standings calculation, catalog upsert |
| `backend/src/AutoKorsak.LegacyXml` | AutoKorsak 1.38 XML import/export and export-limitation report |
| `backend/src/AutoKorsak.Api` | ASP.NET Core Minimal API, SQLite storage, serves the built frontend |
| `backend/tests/AutoKorsak.Tests` | Automated backend tests |
| `frontend/` | React + TypeScript + Vite UI, Ukrainian/English resources in `src/i18n` |
| `docs/` | Living documentation, numbered specifications, legacy screenshots |
| `legacy-autokorsak/` | Legacy XML fixtures (real public tournaments, approved for this public repo) and the original executable |

See [docs/README.md](docs/README.md) for the documentation index and [docs/status.md](docs/status.md) for what
works now.
