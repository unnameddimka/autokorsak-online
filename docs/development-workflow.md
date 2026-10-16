# Development workflow

## Cycle

1. **Spec.** The owner and the referee agree on scope, written as a numbered English spec in `docs/devel-specs/`
   (`NNNN-short-name.md`, starting `Status: Proposed`). Questions are resolved before implementation. Answers that
   do not change scope go to `docs/decisions.md`. The owner then marks the spec `Approved` (only the status line
   changes).
2. **Implementation.** Code, tests, and the living docs change together. Business rules go in
   `AutoKorsak.Domain`, XML format knowledge in `AutoKorsak.LegacyXml`, and UI strings in both dictionaries.
3. **Verification.** Before committing:
   ```sh
   docker compose --profile test run --rm backend-tests
   docker compose --profile test run --rm frontend-tests
   docker compose up --build -d     # check the affected screens at http://127.0.0.1:8080 in both languages
   ```
4. **Commit and push.** Write small, descriptive commits that reference the spec (`spec 0000: …`). The remote is
   `origin` → `https://github.com/unnameddimka/autokorsak-online` (public). Push only what the owner has approved
   to publish: the repository is public, and no new personal data may be committed without approval
   (decision P3).
5. **Review.** An independent review is done against the spec. Record its outcome in `docs/status.md` under
   *Review log*, with the date, reviewer, commit, findings, and their resolution.
6. **Correction.** Fixes for review findings that stay within the approved spec are committed as follow-ups and
   noted in the review log. Findings that need new or changed behavior become a new numbered spec.
7. **Deployment / demonstration.** Run locally with `docker compose up --build -d`, and import the reference
   fixture for the demo (it is never seeded automatically). Write the referee's feedback down as decisions or as
   input for the next spec.

## Versioning

- The product version is the latest **implemented spec** plus its follow-up commits; `docs/status.md` states it.
- Any change to the persisted data format must stay readable by the previous version's data, or come with a
  migration. The SQLite `user_version` pragma tracks the schema (currently 1). The tournament JSON document is
  read with tolerant defaults for missing fields.
- Breaking changes to the API or XML mapping are listed in `docs/status.md` and `docs/decisions.md`.

## CI

The only workflow is `.github/workflows/snapshot.yml`, which uploads a repository snapshot artifact on every
push. It **does not run tests**. Until a test workflow is added, run the test commands above locally.
