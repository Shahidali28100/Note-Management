## Why

Users can currently only browse their notes via the paginated list (AB-1005) and manual tag filtering (AB-1006) — there is no way to find a note by its actual content or title text. FRS-SEARCH-001..004 require a real keyword search over a user's own notes, using SQL Server Full-Text Search (per AGENTS.md/SDS ADR-005 — no external search service), with safe result highlighting and pagination. AB-1007 is next in the strict ticket dependency sequence (SDS §92) and unblocks the AB-1013 Search UI ticket.

## What Changes

- Add `GET /api/search` — authenticated, user-scoped SQL Server Full-Text Search over each note's Title + Content.
- Query syntax: the `q` parameter is tokenized on whitespace; a note matches only when it contains **all** tokens (AND-of-terms), each matched via `FORMSOF(INFLECTIONAL, ...)`. Tokens are passed to `CONTAINSTABLE` as parameters — never concatenated into the FTS query string.
- `q` is required, 1–200 characters after trimming; missing/empty/whitespace-only or over-length `q` is rejected with `400 Bad Request`.
- Results are ordered strictly by SQL Server FTS relevance rank (descending) — no `sortBy`/`sortDirection` params on this endpoint.
- Response reuses the standard list envelope (`items`, `page`, `pageSize`, `totalCount`, `totalPages`); `pageSize` follows the same default(20)/cap(100) rule as note listing. Each item carries the standard note shape (id, title, content, tags, createdAt, updatedAt) plus a `highlight: { title, content }` object: plain-text excerpts with matched tokens wrapped in a non-HTML sentinel pair (``/``) so the frontend can HTML-escape the excerpt first and only then turn sentinels into `<mark>` — matched text can never smuggle in executable markup (SDS §44/§60, FRS-SEARCH-003).
- Search is scoped to the authenticated user's own notes; soft-deleted notes are excluded (FRS-SEARCH-002), enforced the same way the existing global query filter + `UserId` scoping is enforced on note listing.
- No `tagId` filter on this endpoint (out of scope for AB-1007 — SDS §35 only calls for keywords, pagination, and user scoping here).
- New EF Core migration adding a SQL Server full-text catalog and a full-text index on `Notes(Title, Content)`, keyed on `Notes.Id` — created via raw SQL in the migration (EF Core has no native full-text-index API), never touching application-layer query construction.

## Environment Deviation from AB-1001 (discovered during /plan review, before implementation)

AB-1001 chose **SQL Server Express LocalDB** as the local/CI dev-database engine. Verifying this ticket's core dependency — SQL Server Full-Text Search — against a real LocalDB instance found:

- `SELECT SERVERPROPERTY('IsFullTextInstalled')` returns `0` on LocalDB, **even after reinstalling LocalDB with the Full-Text feature explicitly selected**.
- This is not a missing-feature/installer problem: **LocalDB cannot host Full-Text Search at all**, because LocalDB runs as a per-user process, and SQL Server's Full-Text daemon depends on a Windows service — an architecture LocalDB does not have. No reinstall, feature flag, or configuration change can add it.
- A full SQL Server Express instance (`SQLEXPRESS`, installed as a Windows service) on the same machine reports `IsFullTextInstalled = 1` and works correctly.

Since Full-Text Search is this ticket's entire purpose (FRS-SEARCH-001, ADR-005), and is a hard requirement AGENTS.md/SDS forbid working around with an external search service, **the dev-database engine decision must change**:

- **Local dev**: connection strings move from `(localdb)\MSSQLLocalDB` to a full SQL Server Express instance (`.\SQLEXPRESS`) for every developer, not just this machine.
- **CI**: `windows-latest`'s preinstalled LocalDB has the identical architectural limitation (this is inherent to LocalDB, not something a particular runner image could fix), so the existing `backend` CI job cannot apply this ticket's migration either. CI adopts the swap AB-1001's plan.md explicitly pre-flagged for this situation: `ubuntu-latest` + an `mssql/server:2022-latest` service container (which does support Full-Text Search), replacing the `windows-latest` + preinstalled-LocalDB job. See `plan.md` §0 for the full reasoning and §2 for the exact CI/config changes.

This reverses AB-1001's "SQL Server LocalDB" dev-environment decision (archived `openspec/specs/project-setup/spec.md`, "EF Core + SQL Server Wiring"). `project-setup` is therefore a **Modified Capability** of this change, alongside the new `search` capability — an approved, documented deviation, not a silent config change.

## Capabilities

### New Capabilities
- `search`: authenticated, user-scoped SQL Server full-text keyword search over notes' title/content, with paginated, relevance-ranked, safely-highlighted results.

### Modified Capabilities
- `project-setup`: "EF Core + SQL Server Wiring" requirement changes from targeting SQL Server LocalDB to targeting a full local SQL Server Express instance, because LocalDB architecturally cannot support Full-Text Search (see "Environment Deviation" above). CI's dev-database engine changes correspondingly (`windows-latest`+LocalDB → `ubuntu-latest`+SQL Server container).

## Impact

- **API**: new `GET /api/search` endpoint (`apps/api/src/NoteManagement.Api/Controllers/SearchController.cs`).
- **Application**: new `ISearchService`/`SearchService`, `SearchQueryDto`, `SearchResultDto`/`NoteHighlightDto`.
- **Infrastructure**: new `ISearchRepository`/`SearchRepository` issuing a parameterized `CONTAINSTABLE` query via EF Core raw SQL; new migration creating the full-text catalog + index on `Notes`.
- **Domain**: no entity changes — `Note` already carries `Title`/`Content`/`UserId`/`DeletedAt`.
- **Tests**: MSTest unit tests (query tokenization, validation, highlight-sentinel construction) and integration tests (real full-text index behavior, user isolation, soft-delete exclusion, pagination) per SDS §69.
- **Dependencies**: none added — SQL Server Full-Text Search is a database feature, not a package.
- **Environment/CI** (deviation above): `apps/api/src/NoteManagement.Api/appsettings.Development.json.example` and every hardcoded integration-test connection string move from `(localdb)\MSSQLLocalDB` to `.\SQLEXPRESS`; `.github/workflows/ci.yml`'s `backend` job moves from `windows-latest` (preinstalled LocalDB) to `ubuntu-latest` + an `mssql/server:2022-latest` service container.
