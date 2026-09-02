# Tasks: ab-1005-notes-pagination

Source: `proposal.md`, `plan.md`. Each task references the plan section it implements. Extends the existing `notes` capability's `GET /api/notes` in place — no new entity, no new controller, no migration (plan.md §2 "DB / EF Core Migrations: None").

## Phase 1: Foundation

Pure data shapes and interface contracts first — the query DTO and the two interface signature changes compile standalone; nothing here touches `NoteService`/`NoteRepository`'s bodies yet, so the solution still builds even though those two classes won't satisfy their interfaces until Phase 2.

**[PARALLEL] — 1.1 Application DTO vs. 1.2 shared-package schema (no dependency on each other):**

- [x] 1.1 Application layer, data shape only — plan §2:
  - `Application/DTOs/Notes/NoteListQueryDto.cs` (new record: `Page`/`PageSize` with `[Range(1, int.MaxValue)]`, `SortBy`/`SortDirection` with `[AllowedValues(...)]`, all four optional with `= null` defaults)
  - `Interfaces/INoteService.cs` — change `ListAsync(Guid userId, CancellationToken ct)` → `ListAsync(Guid userId, NoteListQueryDto query, CancellationToken ct)`
  - `Interfaces/INoteRepository.cs` — change `GetPageForUserAsync(Guid userId, int page, int pageSize, CancellationToken ct)` → add `string sortBy, string sortDirection` params; update the XML doc remark per plan §2
  - Verify: `dotnet build apps/api/NoteManagement.sln` fails at this point with exactly the expected errors (`NoteService`/`NoteRepository` no longer satisfy their interfaces) — confirms the signature change is isolated to these three files before Phase 2 fixes the implementations
- [x] 1.2 Shared package — plan §2:
  - `packages/shared/src/schemas/notes.ts` — add `noteListQuerySchema` (`page`/`pageSize` positive-int-optional, `sortBy`/`sortDirection` `z.enum(...).optional()`)
  - `packages/shared/src/index.ts` — export `noteListQuerySchema` alongside the existing notes schema exports
  - Verify: `pnpm build` (shared package `tsc --noEmit`) type-checks cleanly on its own (independent of the backend build)

**Checkpoint (Phase 1):**
```bash
pnpm build                                          # packages/shared type-checks (expected to pass)
dotnet build apps/api/NoteManagement.sln            # expected to FAIL — NoteService/NoteRepository don't yet implement the new interface signatures; confirms scope of 1.1 before Phase 2
```

## Phase 2: Core implementation

**[PARALLEL] — 2.1 Infrastructure vs. 2.2 Application, once Phase 1 is done (each only needs the *interfaces* from 1.1, not the other's implementation):**

- [x] 2.1 `Infrastructure/Repositories/NoteRepository.cs` — plan §2: extend `GetPageForUserAsync` to the new 5-parameter signature; replace the hardcoded `OrderByDescending(n => n.UpdatedAt)` with the explicit `(sortBy, sortDirection) switch` mapping all 6 valid combinations (`createdAt`/`updatedAt`/`title` × `asc`/`desc`) to a fixed `OrderBy`/`OrderByDescending` expression, default arm falls back to `updatedAt desc`
- [x] 2.2 `Application/Services/NoteService.cs` — plan §2: add `MaxPageSize = 100`, `DefaultSortBy = "updatedAt"`, `DefaultSortDirection = "desc"` constants; rewrite `ListAsync` to accept `NoteListQueryDto`, resolve defaults (`query.Page ?? DefaultPage`, etc.), clamp `pageSize` down to `MaxPageSize` via `Math.Min`, and pass the resolved `page/pageSize/sortBy/sortDirection` through to `GetPageForUserAsync`

**Checkpoint (Phase 2):**
```bash
dotnet build apps/api/NoteManagement.sln            # 0 errors — NoteRepository/NoteService now satisfy the Phase 1 interfaces; Api project not touched yet
```

## Phase 3: Integration

Wires the controller to the new query DTO — the only remaining call site.

- [x] 3.1 `Api/Controllers/NotesController.cs` — plan §2: change `List` to `List([FromQuery] NoteListQueryDto query, CancellationToken cancellationToken)`, pass `query` through to `_noteService.ListAsync`; add `[ProducesResponseType(StatusCodes.Status400BadRequest)]` to the action's attributes (the envelope/200/401 attributes are unchanged)
- [x] 3.2 Manual verification via curl against the running dev API (no migration/DB change to apply first — existing LocalDB schema is untouched). **Found and fixed a real bug**: the first pass (using the built-in `[AllowedValues]`) showed `GET /api/notes` with *no query string at all* returning `400` — `AllowedValuesAttribute.IsValid(null)` returns `false` (confirmed via an isolated console probe, see plan.md's corrected architecture-decision entry), rejecting the exact "missing value defaults" case the spec requires. Replaced with `OptionalAllowedValuesAttribute`; re-verified `IsValid(null) == true` / `IsValid("title") == true` / `IsValid("bogus") == false` in the same isolated probe. Re-running the full curl matrix against the live API to confirm end-to-end was blocked by intermittent SQL LocalDB named-pipe connectivity in this sandbox (`sqlcmd` itself failed to connect, independent of the app — an environment limitation, not a code defect; see Follow-up Tasks in the `/implement` summary). Phase 4's integration test suite (own isolated LocalDB database per test class, already-established `WebApplicationFactory` pattern) is the authoritative re-verification for all scenarios this step intended to cover.

**Checkpoint (Phase 3):**
```bash
dotnet build apps/api/NoteManagement.sln
pnpm build
```

## Phase 4: Tests

One test per new/changed scenario in `specs/notes/spec.md`'s "Note Listing" requirement (11 scenarios total: 4 carried over unchanged from AB-1004, 7 new), split unit vs. integration per plan §3 — `[ApiController]`'s automatic ModelState→400 only fires through the real MVC pipeline, so every rejection scenario (invalid `page`/`pageSize`/`sortBy`/`sortDirection`) is an integration test, matching AB-1004's `Create_With..._Returns400` precedent.

| Spec scenario | Test |
|---|---|
| Lists only the caller's active notes | `NoteServiceTests.ListAsync_ReturnsOnlyCallersActiveNotesSortedByUpdatedAtDesc` (updated call site) + `NotesControllerTests.List_ReturnsOwnedNotesWithPaginationEnvelope` (unchanged) |
| Soft-deleted notes excluded from the list | `NotesControllerTests.List_ExcludesSoftDeletedNotes` (unchanged) |
| Another user's notes excluded from the list | `NotesControllerTests.List_ExcludesOtherUsersNotes` (unchanged) |
| Empty list for a user with no notes | `NoteServiceTests.ListAsync_WithNoNotes_ReturnsEmptyEnvelope` (updated call site) |
| Client requests a specific page and page size | `NoteServiceTests.ListAsync_WithExplicitPageAndPageSize_UsesRequestedValues` + `NotesControllerTests.List_WithPageAndPageSize_ReturnsRequestedPage` |
| Client sorts by an allowlisted field and direction | `NoteServiceTests.ListAsync_WithSortByTitleAscending_OrdersByTitleAscending` + `NotesControllerTests.List_WithSortByTitleAscending_ReturnsNotesOrderedByTitle` |
| Page beyond the last page returns an empty page, not an error | `NotesControllerTests.List_WithPageBeyondLastPage_ReturnsEmptyItemsNotError` |
| Invalid page value rejected | `NotesControllerTests.List_WithInvalidPage_Returns400` (`page=0`, `page=-1`, `page=abc`) |
| Oversized page size silently clamped | `NoteServiceTests.ListAsync_WithPageSizeOver100_ClampsTo100` + `NotesControllerTests.List_WithPageSizeOver100_ClampsTo100` |
| Invalid page size value rejected | `NotesControllerTests.List_WithInvalidPageSize_Returns400` (`pageSize=0`, `pageSize=-1`, `pageSize=abc`) |
| Unsupported sort field rejected | `NotesControllerTests.List_WithUnsupportedSortBy_Returns400` |
| Unsupported sort direction rejected | `NotesControllerTests.List_WithUnsupportedSortDirection_Returns400` |

- [x] 4.1 `Tests.Unit/Application/NoteServiceTests.cs` — plan §3:
  - Update the 2 existing `ListAsync(...)` call sites to pass `new NoteListQueryDto()` instead of nothing
  - Update `FakeNoteRepository.GetPageForUserAsync` to the new 5-parameter signature, with its own mirrored `(sortBy, sortDirection) switch` so sort-order assertions are meaningful
  - Add `ListAsync_WithExplicitPageAndPageSize_UsesRequestedValues`, `ListAsync_WithPageSizeOver100_ClampsTo100`, `ListAsync_WithSortByTitleAscending_OrdersByTitleAscending`
  - Verified: `dotnet test tests/NoteManagement.Tests.Unit` — 60/60 passed
- [x] 4.2 `Tests.Integration/Api/NotesControllerTests.cs` — plan §3: add `List_WithPageAndPageSize_ReturnsRequestedPage`, `List_WithSortByTitleAscending_ReturnsNotesOrderedByTitle`, `List_WithPageBeyondLastPage_ReturnsEmptyItemsNotError`, `List_WithInvalidPage_Returns400`, `List_WithInvalidPageSize_Returns400`, `List_WithPageSizeOver100_ClampsTo100`, `List_WithUnsupportedSortBy_Returns400`, `List_WithUnsupportedSortDirection_Returns400`; existing `List_*` tests need no changes (no query string = prior defaults, unchanged behavior)

**Checkpoint (Phase 4 — final gate for this ticket, per `CLAUDE.md` Quality Gates, run in order):**
```bash
pnpm lint --max-warnings 0
dotnet build apps/api/NoteManagement.sln
pnpm build
dotnet test apps/api/NoteManagement.sln --collect:"XPlat Code Coverage"
pnpm test --coverage
```

**Results:**
- `pnpm lint` — clean, 0 errors/warnings.
- `dotnet build apps/api/NoteManagement.sln` — 0 errors, 0 warnings, all 6 projects.
- `pnpm build` (`build:shared` `tsc --noEmit` + `--filter web run build`) — both succeed, 0 type errors.
- `dotnet test --collect:"XPlat Code Coverage"` — **134/134 passed** (60 unit + 74 integration; 0 failed, 0 skipped). Environment note: the LocalDB instance in this sandbox needed a full stop/delete/recreate plus removal of stale `.mdf`/`.ldf` files left in the user profile from prior sessions' test runs before it would accept connections reliably — a one-time environment repair unrelated to this ticket's code, not a regression (see Follow-up Tasks in the `/implement` summary). Line coverage on every file this ticket added/touched: `NoteService.cs` 100%, `NoteRepository.cs` 100%, `NotesController.cs` 100%, `NoteListQueryDto.cs` 100%, `OptionalAllowedValuesAttribute.cs` 100% lines / 75% branches — all well above the 80% new-code requirement (AGENTS.md §10/SDS §77).
- `pnpm test --coverage` (`--filter web run test`) — 1/1 passed, 100% coverage (unaffected `apps/web` placeholder suite — this ticket has zero `apps/web` diff).

## Not in scope for this ticket (plan §5 / proposal.md)

Tag filtering (FRS-NOTE-008 / AB-1006); search (AB-1007); any change to `POST /api/notes`, `GET /api/notes/{id}`, `PUT /api/notes/{id}`, `DELETE /api/notes/{id}`, or `POST /api/notes/{id}/restore`; any frontend notes UI (AB-1011/AB-1012); any new index or migration (no schema change in this ticket).
