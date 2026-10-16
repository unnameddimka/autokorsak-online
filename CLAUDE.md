# Working rules for agents (and humans)

## Product rules that are easy to break

- **Interface languages: Ukrainian (`uk`) and English (`en`) only.** Never add a Russian locale, a Russian resource
  file, or Russian interface text. All product-owned strings go into `frontend/src/i18n/en.ts` and
  `frontend/src/i18n/uk.ts`, with identical keys. Never hard-code UI text in components. The frontend tests
  enforce this.
- **User data is not UI text.** Imported names and titles (e.g. `Группа А`) must be preserved byte-for-byte.
  Never transliterate, translate, or "fix" them.
- **Never fake a feature.** Unsupported actions are visibly disabled or labelled. Unsupported calculations
  produce notices, not invented numbers.
- **Never lose games silently.** Destructive changes to pairings or results need explicit confirmation (HTTP 409 +
  `confirmDiscardResult=true`). Players with games cannot be deleted.
- **Legacy XML:** map a field only when its representation is verified in real legacy files. Never invent tags.
  Unknown elements must survive a round-trip, because export patches the original document. Anything that cannot
  be exported must show up in the export report (`ExportAnalyzer`).
- **Do not guess business rules.** Examples: the rating→rank mapping, legacy `Kind` codes other than Swiss=2,
  bye scoring. Implement a safe default, document it in `docs/decisions.md`, and move on.

## Documentation

- `docs/devel-specs/NNNN-*.md`: numbered, English, historical. **Never rewrite an approved spec.** New or changed
  requirements go into the next number (0001, …).
- Living docs: `README.md`, `docs/architecture.md`, `docs/decisions.md`, `docs/status.md`,
  `docs/development-workflow.md`. Update them in the same change as the code they describe. Record verified
  facts only.
- All specs, docs, code identifiers, and comments are in English.
- `docs/reference/legacy-ui/` (screenshots) and `legacy-autokorsak/` (fixtures) are evidence. Do not modify them.
  The primary fixture path `legacy-autokorsak/А_11.12.21.xml` (Cyrillic А) is a contract.

## Code layout and boundaries

- Business rules live in `backend/src/AutoKorsak.Domain`, never in React components or API handlers.
- XML format knowledge lives only in `backend/src/AutoKorsak.LegacyXml` (`LegacyFormat.cs` lists the verified facts).
- The API returns errors as `{ code, args }`. Every code needs `error.<code>` keys in both dictionaries.

## Before you commit

```sh
docker compose --profile test run --rm backend-tests
docker compose --profile test run --rm frontend-tests
docker compose up --build -d    # if the UI or API changed: check the real app at http://127.0.0.1:8080
```

- Add or adjust tests together with behavior changes (domain rules → `DomainTests`, XML → `LegacyXmlTests`,
  endpoints → `ApiTests`).
- Update `docs/status.md` with the actual test results and the known limitations.
- Commit on a branch or on `main` only as the owner directs. See `docs/development-workflow.md`.
