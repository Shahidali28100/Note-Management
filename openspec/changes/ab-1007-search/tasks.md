# Tasks: ab-1007-search

Source: `proposal.md`, `plan.md`. New `search` capability (`GET /api/search`): Application DTOs/interfaces, Infrastructure-only keyless projection type (`FullTextMatch`), one EF Core migration (raw SQL — full-text catalog + index on `Notes(Title, Content)`), a repository composing `FromSqlInterpolated`/`CONTAINSTABLE` with a LINQ join against `_dbContext.Notes`, and two pure helpers (`SearchTermTokenizer`, `SearchHighlighter`). No `Note`/`Tag` Domain entity changes. `packages/shared` gets a new `search` module (consumed starting AB-1013 — no `apps/web` diff, same precedent as AB-1006's `tags` module). Also carries the `project-setup` environment deviation (plan §0): local dev + CI move off SQL Server LocalDB (architecturally incapable of Full-Text Search) onto SQL Server Express / a `mssql/server` Linux container.

> **⚠ Environment deviation, recorded here for visibility (plan.md §0):** this ticket also modifies the already-archived `project-setup` capability. `openspec/specs/project-setup/spec.md` is **not** edited directly by this ticket — the delta at `specs/project-setup/spec.md` (MODIFIED Requirements) merges in only when `openspec archive ab-1007-search` runs, per this project's established OpenSpec workflow (same mechanism AB-1006 used for its `notes` delta).

## Phase 1: Foundation

Pure data shapes, interfaces, and infrastructure config only — no logic bodies yet. Unlike AB-1005/AB-1006, `ISearchRepository`/`ISearchService` are **brand-new** interfaces (nothing existing is being extended), so this phase is not expected to break the build — `dotnet build` should stay green throughout Phase 1.

**[PARALLEL] — 1.1 / 1.2 / 1.3 / 1.4 / 1.5 have no dependency on each other:**

- [x] 1.1 Application DTOs — plan §2:
  - `Application/DTOs/Search/SearchQueryDto.cs` (new): `[Required, TrimmedLength(1, 200)] Q`, optional `Page`/`PageSize` with `[Range(1, int.MaxValue)]` — matches `delta-openapi.yaml` exactly.
  - `Application/DTOs/Search/NoteHighlightDto.cs` (new): `Title`, `Content` — plain-text sentinel-delimited excerpts, never HTML.
  - `Application/DTOs/Search/SearchResultDto.cs` (new): standard note shape (`Id, Title, Content, Tags, CreatedAt, UpdatedAt`) plus `Highlight`.
  - `Application/DTOs/Search/SearchResponseDto.cs` (new): standard list envelope (`Items, Page, PageSize, TotalCount, TotalPages`).
- [x] 1.2 Application interfaces — plan §2:
  - `Application/Interfaces/ISearchRepository.cs` (new): `SearchAsync(userId, terms, page, pageSize, ct) -> (Items, TotalCount)`.
  - `Application/Interfaces/ISearchService.cs` (new): `SearchAsync(userId, SearchQueryDto, ct) -> SearchResponseDto`.
- [x] 1.3 Infrastructure — keyless projection type — plan §2:
  - `Infrastructure/Search/FullTextMatch.cs` (new, `internal sealed`): `Guid Key`, `int Rank` — maps `CONTAINSTABLE`'s `[KEY]`/`[RANK]` result shape.
  - `Infrastructure/Configurations/FullTextMatchConfiguration.cs` (new): `HasNoKey()`, `ToView(null)` (excludes it from migrations — no real table/view backs it), column-name mappings for `KEY`/`RANK`.
  - **Found and fixed a real bug** (not caught by plan review): plan.md's listing had this class `public`, which fails to build (`CS0051: Inconsistent accessibility`) against `internal sealed class FullTextMatch` — a public method can't take an internal type as a parameter. Changed `FullTextMatchConfiguration` to `internal sealed` (it only needs to be visible to `ApplyConfigurationsFromAssembly`'s reflection scan within this assembly, same as `FullTextMatch` itself).
- [x] 1.4 Shared package — plan §2:
  - `packages/shared/src/schemas/search.ts` (new): `searchQuerySchema`, `noteHighlightSchema`, `searchResultSchema`, `searchResponseSchema`.
  - `packages/shared/src/types/search.ts` (new): re-export types from `../schemas/search`, same idiom as `types/tags.ts`.
  - `packages/shared/src/index.ts` (modify): add the AB-1007 search export block (schema values + `export * from './types/search'`).
  - Verify: `tsc --noEmit` against `packages/shared` type-checks cleanly, independent of the backend build.
- [x] 1.5 Environment/CI — dev-database engine swap (plan §0/§2 deviation): **already completed in two prior commits on this branch** (`2ef97f0 chore(env): switch dev DB to SQL Server Express, CI to container AB#1007`, `12a1c99 fix(ci): derive integration test connection strings from CI env AB#1007`, both predating this `/implement` session) — verified against the current tree, matches plan.md exactly: `appsettings.Development.json.example` → `.\SQLEXPRESS`; every integration-test class now derives its connection string from a new `TestSupport/TestConnectionStringFactory` (reads `ConnectionStrings__DefaultConnection`, falls back to `.\SQLEXPRESS` locally) rather than a hardcoded string, fixing a real CI bug the first commit's naive substitution left behind (Windows named-instance syntax doesn't resolve on the Ubuntu container); `.github/workflows/ci.yml`'s `backend` job runs `ubuntu-latest` + `mssql/server:2022-latest`. No further action needed here.

**Checkpoint (Phase 1):**
```bash
dotnet build apps/api/NoteManagement.sln   # expected to PASS — new interfaces/DTOs/config are additive, nothing existing implements or consumes them yet
pnpm build                                 # expected to PASS — packages/shared type-checks independently
```
**Verified:** `dotnet build` — 0 errors after the `FullTextMatchConfiguration` accessibility fix above, 0 warnings, all 6 projects (confirmed PASS as predicted — no existing code was broken). `pnpm build` could not be invoked directly in this sandbox (`pnpm` is pinned via `corepack` but isn't resolvable on `PATH` for nested script-to-script calls — `corepack pnpm build` fails with `'pnpm' is not recognized`; an environment limitation, not a code issue). Ran the equivalent underlying command directly instead: `packages/shared`'s own `node_modules/.bin/tsc --noEmit` (exactly what `build:shared` invokes) — clean, 0 type errors.

## Phase 2: Core Implementation

Implements the Phase 1 interfaces. `SearchTermTokenizer`/`SearchHighlighter`/`SearchRepository` depend only on Phase 1 shapes, not on each other — `SearchService` depends on `SearchTermTokenizer`/`SearchHighlighter`'s finished bodies (it calls them directly) plus the `ISearchRepository`/`INoteRepository` interfaces (already available from Phase 1), so it goes last.

**[PARALLEL] — 2.1 / 2.2 / 2.4 have no dependency on each other; 2.3 depends on 2.1 and 2.2:**

- [x] 2.1 `Application/Services/SearchTermTokenizer.cs` (new, `internal static partial`, pure) — plan §2: `[GeneratedRegex(@"[^\p{L}\p{Nd}'-]+")]` strips FTS-special characters; splits `q` on whitespace, sanitizes each token, drops empty results, de-duplicates case-insensitively.
- [x] 2.2 `Application/Services/SearchHighlighter.cs` (new, `internal static`, pure) — plan §2: PUA sentinel characters (`` / ``) delimit matches; `ExtractExcerpt` (≤200 chars, centered on first case-insensitive term match, or start-of-content when only the title matched); `HighlightTerms` (case-insensitive literal-substring match via `Regex.Escape`d alternation, non-overlapping).
- [x] 2.3 `Application/Services/SearchService.cs` (new) — plan §2: implements `ISearchService`. Tokenizes `q` via `SearchTermTokenizer`; resolves `page`/`pageSize` defaults (1/20) and clamps `pageSize` to 100 (same constants/precedent as `NoteService.ListAsync`); short-circuits to an empty `SearchResponseDto` (still `200`, no repository call) when every term sanitizes away; otherwise calls `ISearchRepository.SearchAsync`, batches tag hydration via the existing `INoteRepository.GetTagsForNotesAsync`, and maps each result through `SearchHighlighter.Build`.
- [x] 2.4 `Infrastructure/Repositories/SearchRepository.cs` (new) — plan §2: implements `ISearchRepository`. Builds the `FORMSOF(INFLECTIONAL, "term")`-joined condition string from the already-sanitized terms (never raw `q`); issues `_dbContext.Set<FullTextMatch>().FromSqlInterpolated($"SELECT [KEY], [RANK] FROM CONTAINSTABLE(Notes, (Title, Content), {searchCondition})")` with the condition passed as a single parameter (never concatenated into command text — AGENTS.md §6/§11); joins against `_dbContext.Notes` (not the raw table) so `Note`'s existing `HasQueryFilter(DeletedAt == null)` + `UserId` scoping apply for free; orders by `Rank` descending, `UpdatedAt` descending as a stable tiebreak.

**Checkpoint (Phase 2):**
```bash
dotnet build apps/api/NoteManagement.sln   # 0 errors — SearchService/SearchRepository now satisfy the Phase 1 interfaces; Api project (controller/DI) not touched yet
dotnet test apps/api/NoteManagement.sln    # existing tests still green — nothing wired into DI/controller yet, no behavior change
```
**Verified:** `dotnet build` — 0 errors, 0 warnings, all 6 projects. `dotnet test` (unit only — integration requires a live SQL Server Express instance, exercised starting Phase 3) — 80/80 passed, no regressions.

## Phase 3: Integration

Wires the new capability into the HTTP pipeline and the database. Requires Phase 1.5's environment swap to already be in place (`.\SQLEXPRESS` with `IsFullTextInstalled = 1`) before the migration step below can succeed.

- [x] 3.1 `Api/Controllers/SearchController.cs` (new) — plan §2: `[Authorize]`, single `[HttpGet]` action calling `User.GetUserId()` + `ISearchService.SearchAsync`, `[ProducesResponseType]` attributes matching `delta-openapi.yaml` (`200/400/401`). No `ProblemDetailsExceptionHandler` change needed — every rejection is a `[Required]`/`[TrimmedLength]`/`[Range]` model-validation failure already turned into `400` by `[ApiController]`, and `[Authorize]` already produces `401`.
- [x] 3.2 `Application/DependencyInjection.cs` (modify): `services.AddScoped<ISearchService, SearchService>();`
- [x] 3.3 `Infrastructure/DependencyInjection.cs` (modify): `services.AddScoped<ISearchRepository, SearchRepository>();` (grouped under an "AB-1007: full-text search persistence" comment, matching the file's existing per-ticket comment convention).
- [x] 3.4 Generate the EF Core migration (solution now builds and wires cleanly end-to-end):
  ```bash
  dotnet ef migrations add AddNotesFullTextSearch --project apps/api/src/NoteManagement.Infrastructure --startup-project apps/api/src/NoteManagement.Api
  ```
  Generated with an empty `Up`/`Down` as expected — hand-edited per plan.md §2 to add the `IF NOT EXISTS`-guarded raw SQL. **Found and fixed a second real bug** (only surfaces when actually applying, not at `dotnet build`/`migrations add` time): `dotnet ef database update` failed with `Error Number:574` — `"CREATE FULLTEXT CATALOG statement cannot be used inside a user transaction"` — because EF Core wraps a migration's SQL in one transaction by default. Fixed by passing `suppressTransaction: true` to all four `migrationBuilder.Sql(...)` calls (catalog + index, `Up` and `Down`). Re-ran `migrations add`/edit cleanly after the fix; confirmed via `git diff` that `ApplicationDbContextModelSnapshot.cs`'s only change is the new keyless `FullTextMatch` entry (`ToView(null)`) — no real table/column touched.
- [x] 3.5 Apply the migration and manually verify via curl against the running dev API (per plan §0/§4, requires `.\SQLEXPRESS` with Full-Text Search available):
  ```bash
  dotnet ef database update --project apps/api/src/NoteManagement.Infrastructure --startup-project apps/api/src/NoteManagement.Api
  ```
  **Verified end-to-end against the real local `.\SQLEXPRESS` instance** (confirmed running, `SELECT SERVERPROPERTY('IsFullTextInstalled')` → `1`, before applying): migration applied cleanly; `sys.fulltext_indexes`/`sys.fulltext_catalogs` confirm `NotesFullTextCatalog` + a full-text index on `Notes` with `CHANGE_TRACKING AUTO`. Ran the dev API and walked through every scenario with two real registered/authenticated users and five real notes (one soft-deleted, one owned by the other user):
  - Single-term `q=elephant` → `200`, `totalCount: 3` (correctly excludes the soft-deleted note and the other user's note).
  - Multi-term `q=elephant party` → `200`, `totalCount: 1` — the note containing only "elephant" (not "party") is correctly excluded, confirming AND-of-terms.
  - No-match `q=zzzznomatch` → `200`, `items: []`, `totalCount: 0`.
  - Missing `q`, blank/whitespace-only `q`, and a 201-char `q` → `400` in all three cases.
  - No access token → `401`.
  - The other user's own search for `q=elephant` → `200`, `totalCount: 1` (only their own note) — cross-user isolation confirmed both directions.
  - Highlighting: response bodies show sentinel-wrapped (`...`) matches in both `highlight.title` and `highlight.content`, including both terms independently wrapped in the multi-term case.
  Full-text index population (`CHANGE_TRACKING AUTO`) resolved within the first 1-second poll in this run.

**Checkpoint (Phase 3):**
```bash
dotnet build apps/api/NoteManagement.sln
dotnet ef database update --project apps/api/src/NoteManagement.Infrastructure --startup-project apps/api/src/NoteManagement.Api
dotnet test apps/api/NoteManagement.sln    # still green — no new tests yet, nothing regressed
```
**Verified:** `dotnet build` — 0 errors, 0 warnings. `dotnet ef database update` — applied cleanly after the `suppressTransaction` fix (above). `dotnet test` (unit only at this point) — 80/80 passed, no regressions; the full unit+integration run is deferred to the Phase 4 checkpoint below, once the integration test project has search tests to actually exercise the new endpoint against a real database.

## Phase 4: Tests

One test per scenario in `specs/search/spec.md` (20 scenarios, all new) — split unit vs. integration per plan.md §3: pure logic (tokenization, highlighting, page/pageSize orchestration) → `Tests.Unit` (hand-rolled fakes, no mocking library); anything where the real full-text index, ranking, or the HTTP/auth pipeline matters → `Tests.Integration` (`WebApplicationFactory` + isolated SQL Server Express database, per `NotesControllerTests`'/`TagsControllerTests`' precedent).

**Found and fixed a third real bug before any test could be written**: `SearchTermTokenizer`/`SearchHighlighter` were `internal` per plan.md's own listing, but a `grep` for `InternalsVisibleTo` across the whole repo returns nothing — this codebase has no such grant anywhere, and its established precedent for this exact situation (`Infrastructure/Authentication/JwtOptions.cs`'s doc comment) is to make the type `public` instead so a test project can reference it directly, never to add `InternalsVisibleTo`. Changed both to `public`. While doing so, also replaced `SearchHighlighter`'s two literal Unicode Private-Use-Area sentinel characters (invisible in a diff/editor, and unreliable to reproduce byte-for-byte through several tool layers — confirmed by hand while making this exact edit) with named `public const char SentinelStart = (char)0xE000` / `SentinelEnd = (char)0xE001`, computed via hex cast rather than a `\uXXXX` escape or a literal character — both so the source file has no non-printable bytes and so tests can assert on the exact delimiter via the constant instead of duplicating the literal value.

- [x] 4.1 `Tests.Unit/Application/SearchTermTokenizerTests.cs` (new): `Tokenize_SplitsOnWhitespace_ReturnsEachTerm`, `Tokenize_StripsFtsSpecialCharacters`, `Tokenize_DropsTermsThatSanitizeToEmpty`, `Tokenize_DeduplicatesCaseInsensitively`.
- [x] 4.2 `Tests.Unit/Application/SearchHighlighterTests.cs` (new): `Build_TitleMatch_WrapsTermWithSentinels`, `Build_ContentMatch_ReturnsExcerptCenteredOnMatch`, `Build_ContentShorterThanExcerptLength_ReturnsWholeContent`, `Build_MultipleMatchingTerms_WrapsEachOne`, `Build_MarkupLikeContent_PassesThroughAsLiteralText` (input containing `<`, `>`, `&` near a match), `Build_NoContentMatch_ExcerptsFromStart`. One assertion in `Build_ContentMatch_ReturnsExcerptCenteredOnMatch` had to compare the *unwrapped* (sentinel-stripped) length against the 200-char bound, not the sentinel-wrapped result — the 200-char excerpt bound applies before highlighting adds 2 characters per match; this was caught by the test actually failing on first run, not by inspection.
- [x] 4.3 `Tests.Unit/Application/SearchServiceTests.cs` (new, `FakeSearchRepository` + a `FakeNoteRepository` implementing only `GetTagsForNotesAsync` — `NoteServiceTests`' own `FakeNoteRepository` is `private` to that file, so a same-shaped fake was written here rather than literally shared): `SearchAsync_WithDefaultPaging_UsesPage1PageSize20`, `SearchAsync_WithExplicitPaging_PassesThroughToRepository`, `SearchAsync_WithOversizedPageSize_ClampsTo100`, `SearchAsync_WithAllTermsSanitizedAway_ReturnsEmptyPageWithoutCallingRepository`, `SearchAsync_MapsResultsWithTagsAndHighlight`.
- [x] 4.4 `Tests.Integration/Api/SearchControllerTests.cs` (new) — plan §3 table, full scenario coverage:
  - `Search_WithSingleTermMatchingTitleOrContent_Returns200WithMatches`
  - `Search_WithMultiTermQuery_RequiresAllTerms` (also asserts the partial-match note is absent)
  - `Search_WithNoMatches_Returns200WithEmptyItems`
  - `Search_WithMissingQ_Returns400`
  - `Search_WithBlankQ_Returns400`
  - `Search_WithQOver200Chars_Returns400`
  - `Search_WithoutAccessToken_Returns401`
  - `Search_ExcludesOtherUsersMatchingNotes`
  - `Search_ExcludesSoftDeletedMatchingNotes`
  - `Search_TitleMatch_HighlightTitleContainsSentinelDelimitedTerm`
  - `Search_ContentMatch_HighlightContentIsBoundedExcerptWithSentinels`
  - `Search_MultiTermMatch_HighlightsEveryTerm`
  - `Search_NoteContentWithAngleBrackets_HighlightNeverContainsHtml`
  - `Search_WithNoPagingParams_UsesPage1PageSize20`
  - `Search_WithPageAndPageSize_ReturnsRequestedSlice`
  - `Search_WithInvalidPage_Returns400`
  - `Search_WithInvalidPageSize_Returns400`
  - `Search_WithPageSizeOver100_ClampsTo100`
  - `Search_WithPageBeyondLastPage_ReturnsEmptyItems`
  - Test-infrastructure addition: a `WaitForFullTextIndexAsync` polling helper (bounded retry, short delay — `CHANGE_TRACKING AUTO` population is asynchronous); every test above that creates/updates a note and immediately searches for it calls this helper instead of asserting on the first attempt. **Found and fixed during the real run** (not by inspection): the first full-suite run failed one test (`Search_NoteContentWithAngleBrackets_HighlightNeverContainsHtml`) with an `IndexOutOfRangeException` — the helper's original 10-attempt/500ms (5s) budget wasn't enough by the time that test ran ~15 notes deep into the shared per-class database, causing a genuine (not spurious) index-population race. Confirmed via a direct `CONTAINSTABLE` query against the leftover test database after the run that the note *was* indexed correctly — this was purely a timing budget issue, not a search-logic bug. Raised the default to 20 attempts (10s) and re-ran the full file clean.
- [ ] 4.5 CI verification (plan §0/§4): push this branch and watch the `backend` CI job go green end-to-end — restore, migrate, build, test.
  - **First real push (commit `8f5422b`) failed** at the "Apply migrations" step with `Error 7609`: `"Full-Text Search is not installed, or a full-text component cannot be loaded"`. Root cause, confirmed via Microsoft's own docs (`learn.microsoft.com/en-us/sql/linux/install-upgrade/setup-full-text-search`) and `microsoft/mssql-docker` issue #30/#665: **`mcr.microsoft.com/mssql/server:2022-latest` does not ship Full-Text Search at all** — it's a separate `mssql-server-fts` apt package, and it must be installed *before* `sqlservr`'s first start (no supported way to add it to an already-running instance). This is why GitHub Actions' `services:` block — which only pulls a pre-built image and starts it before any job step runs — can't host it: there's no opportunity to run an install step first.
  - Ruled out `MSSQL_PID` as a factor: Microsoft's SQL Server 2022 Linux edition/feature table confirms "Full-text and semantic search" is `Yes` on **every** edition including Express (unlike Windows, where Express needs the separate "Advanced Services" SKU) — the container's `MSSQL_PID: "Express"` was never the problem.
  - **Fix**: added `.github/docker/mssql-fts.Dockerfile` (`FROM mcr.microsoft.com/mssql/server:2022-latest`, installs `mssql-server-fts` via the Microsoft apt repo/key, confirmed against a working public recipe — `github.com/1nbuc/mssql-docker-fts`'s `Dockerfile`, fetched and matched near-verbatim). The `backend` job now `docker build`s this image and runs it via plain `docker run -d`/`docker exec` steps instead of a `services:` container, with a manual bounded health-check poll replacing the automatic one `services:` used to provide, and an `if: always()` cleanup step.
  - **Not yet confirmed** — about to be pushed as a second commit. This checkbox stays unchecked until a `backend` job is observed to pass "Apply migrations" (proving Full-Text Search is now actually present in the container) through to a green "Test" step.

**Checkpoint (Phase 4 — final gate for this ticket, per `CLAUDE.md` Quality Gates, run in order):**
```bash
pnpm lint --max-warnings 0
dotnet build apps/api/NoteManagement.sln
pnpm build
dotnet test apps/api/NoteManagement.sln --collect:"XPlat Code Coverage"
pnpm test --coverage
```
Fix and re-run on the first failure before proceeding to the next gate — never treat a later gate as informative once an earlier one has failed.

**Results (all commands actually executed and passing — `pnpm`/`corepack` itself isn't resolvable in this sandbox per Phase 1's note, so each gate's underlying local binary was invoked directly; behaviorally identical to what the `pnpm` script wraps):**
- Lint — `apps/web/node_modules/.bin/eslint . --max-warnings 0`: clean, 0 errors/warnings (this ticket has no `apps/web` diff; confirms nothing regressed).
- `dotnet build apps/api/NoteManagement.sln`: 0 errors, 0 warnings, all 6 projects.
- Build — `packages/shared`'s `node_modules/.bin/tsc --noEmit`: clean, 0 type errors. `apps/web/node_modules/.bin/vite build`: succeeds (30 modules, no diff from this ticket).
- `dotnet test apps/api/NoteManagement.sln --collect:"XPlat Code Coverage"`: **216/216 passed** (95 unit + 121 integration; 0 failed, 0 skipped) — up from 182/182 before this ticket (+15 unit, +19 integration, exactly matching tasks 4.1-4.4's planned test count). Per-file line coverage on every file this ticket added/touched (from the Cobertura reports): `SearchQueryDto.cs`/`NoteHighlightDto.cs`/`SearchResultDto.cs`/`SearchResponseDto.cs` 100%, `SearchTermTokenizer.cs` 100%, `SearchService.cs` 100% (its compiler-generated async state machine 84.6% in the integration run alone, 100% once combined with the unit run — both branches are exercised, just split across the two test projects), `SearchController.cs` 100%, `SearchRepository.cs` 100%, `SearchHighlighter.cs` 89.5% line / 66.7-75% branch (the one never-exercised branch is the "overlapping match — keep the earlier, already-emitted one" `continue`, which needs two search terms that are substrings of each other at the same position — no spec scenario requires it; same class of accepted gap as AB-1006's `GetOwnedIdsAsync` empty-guard branch), `FullTextMatch.cs` shows `0%` in the integration report specifically (a coverage-tool artifact — it's a 2-property keyless projection type materialized by EF Core's compiled-query pipeline rather than through instrumented property-getter call sites; the repository that queries it is itself 100% covered and the manual/integration verification above confirms real rows flow through it), and the migration's `Up`/`Down` raw-SQL guards are 52.9% (only the "not yet applied" branch runs in a fresh test database; the `IF EXISTS`/`Down` branches are inherently only exercised by rollback, never by `dotnet test`, same accepted gap as every other migration in this codebase). All well above the ≥80% new-code requirement (AGENTS.md §10/SDS §77) except the two explicitly-accepted, previously-precedented gaps above.
- `pnpm test --coverage` — `apps/web/node_modules/.bin/vitest run --pool=threads`: 1/1 passed, 100% coverage (unaffected `apps/web` placeholder suite — this ticket has zero `apps/web` diff, same as every prior backend-only ticket).

## Not in scope for this ticket (plan §5 / proposal.md)

`tagId` filtering combined with search; `sortBy`/`sortDirection` on this endpoint (relevance-only); prefix/wildcard or raw FTS boolean syntax in `q`; any frontend search UI (AB-1013); sharing (AB-1008); version history (AB-1009).
