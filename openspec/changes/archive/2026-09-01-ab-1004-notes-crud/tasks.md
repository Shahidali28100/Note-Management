# Tasks: ab-1004-notes-crud

Source: `proposal.md`, `plan.md`. Each task references the plan section it implements. `[PARALLEL]` tasks have no dependency on each other and may run in separate git worktrees per SDS §87. Introduces a new `notes` capability — new `NotesController`/`NoteService`/`Note` entity, parallel to (not touching) `AuthController`/`AuthService`/`authentication`.

## Phase 1: Foundation

Pure data shapes and the entity first — nothing here has business logic yet, so it can all land before any service/repository implementation exists.

**[PARALLEL] — 1.1 Domain vs. 1.2 Application DTOs/validation/exceptions/interfaces vs. 1.3 shared-package types:**

- [x] 1.1 Domain — plan §3: `Entities/Note.cs` (private setters + `Create` static factory, `IsDeleted`, `UpdateContent()`, `SoftDelete()`, `Restore()`)
- [x] 1.2 Application layer, data shapes only (no `NoteService`/`NoteRepository` implementations yet — plan §4):
  - `Validation/TrimmedLengthAttribute.cs`
  - `DTOs/Notes/CreateNoteRequestDto.cs`, `UpdateNoteRequestDto.cs`, `NoteResponseDto.cs`, `NoteListResponseDto.cs`
  - `Exceptions/NoteNotFoundException.cs`, `NoteNotDeletedException.cs`
  - `Interfaces/INoteRepository.cs`, `INoteService.cs`
- [x] 1.3 Shared package — plan §8: `src/schemas/notes.ts` (+`createNoteRequestSchema`, `updateNoteRequestSchema`, `noteResponseSchema`, `noteListResponseSchema`), `src/types/notes.ts` (+4 re-exported types), `src/index.ts` (+4 schema exports + type re-export)

**Checkpoint (Phase 1):**
```bash
dotnet build apps/api/NoteManagement.sln          # 0 errors — entity/DTOs/interfaces compile standalone
pnpm install && pnpm build                         # shared package (zod schemas) type-checks
```

## Phase 2: Core implementation

**[PARALLEL] — 2.1–2.4 Infrastructure vs. 2.5 Application service, once Phase 1 is done (the service only needs the *interfaces* from 1.2, not the Infrastructure implementations):**

- [x] 2.1 EF Core configuration — plan §5: `Configurations/NoteConfiguration.cs` (`Title` `HasMaxLength(200)`; `Content` no `HasMaxLength` — `nvarchar(max)`, opaque; composite `(UserId, DeletedAt, UpdatedAt)` index; FK to `Users` with `OnDelete(Cascade)`; `HasQueryFilter(n => n.DeletedAt == null)`)
- [x] 2.2 `ApplicationDbContext` — plan §5: add `DbSet<Note> Notes`
- [x] 2.3 `Repositories/NoteRepository.cs` — plan §5: `GetByIdAsync` (ownership + soft-delete filter applies), `GetByIdIncludingDeletedAsync` (`.IgnoreQueryFilters()`, ownership only), `GetPageForUserAsync` (ordered by `UpdatedAt` desc, `Skip`/`Take`, returns items + total count)
- [x] 2.4 `Infrastructure/DependencyInjection.cs` — plan §5: register `INoteRepository` (`AddScoped`)

- [x] 2.5 `Application/Services/NoteService.cs` — plan §4: ctor takes `INoteRepository`, `IUnitOfWork`; `DefaultPage`/`DefaultPageSize` constants (1/20); implement `CreateAsync` (trims Title/Content before persisting), `GetByIdAsync`, `ListAsync` (fixed default view), `UpdateAsync` (no version snapshot — AB-1009), `DeleteAsync`, `RestoreAsync` (`NoteNotFoundException` vs `NoteNotDeletedException` distinction); every write wrapped in `_unitOfWork.RunInTransactionAsync`. Also register `INoteService` (`AddScoped`) in `Application/DependencyInjection.cs`.

**Checkpoint (Phase 2):**
```bash
dotnet build apps/api/NoteManagement.sln          # 0 errors — Infrastructure + Application compile (Api not wired yet)
```

## Phase 3: Integration

Wires controller → service → repository → DbContext → SQL Server, the exception mapping, and the migration that makes the table exist.

- [x] 3.1 `Api/Extensions/ClaimsPrincipalExtensions.cs` — plan §7: `GetUserId()` extension on `ClaimsPrincipal` (factors out `AuthController.GetMe`'s inline `sub`-claim extraction; `AuthController` itself is left unmodified)
- [x] 3.2 `Api/Controllers/NotesController.cs` — plan §7: class-level `[Authorize]`, plain `Guid id` route params (no `{id:guid}` constraint); `Create` (201), `List` (200), `GetById` (200/404), `Update` (200/400/404), `Delete` (204/404), `Restore` (200/404/409)
- [x] 3.3 `Api/Middleware/ProblemDetailsExceptionHandler.cs` — plan §7: add `NoteNotFoundException` → 404, `NoteNotDeletedException` → 409 to the existing `switch` expression
- [x] 3.4 `Api/Program.cs` — plan §7a (user-requested): add `using Microsoft.OpenApi;`; replace `builder.Services.AddSwaggerGen();` with a configured call that adds a `Bearer` `AddSecurityDefinition` (HTTP, scheme `bearer`, `BearerFormat: "JWT"`) + a matching global `AddSecurityRequirement`, so Swagger UI gets an Authorize button — fixes a gap dating back to AB-1002 that has blocked manually testing every `[Authorize]` endpoint (AB-1002/AB-1003's, and now this ticket's 5) through Swagger UI. **Note**: Swashbuckle.AspNetCore 10.2.3 (pinned) resolves Microsoft.OpenApi 2.7.5, whose v10 API moved types to the `Microsoft.OpenApi` namespace and changed `AddSecurityRequirement` to `Func<OpenApiDocument, OpenApiSecurityRequirement>` — code updated to match (plan §7a), verified via Context7 against the official migration guide, confirmed correct by inspecting the generated `swagger.json`'s `components.securitySchemes`/`security` during manual verification (3.7)
- [x] 3.5 EF Core migration — plan §6:
  ```
  dotnet ef migrations add AddNotes --project apps/api/src/NoteManagement.Infrastructure --startup-project apps/api/src/NoteManagement.Api
  ```
- [x] 3.6 `dotnet ef database update` against LocalDB (creates `Notes` physically) — plan §6
- [x] 3.7 Manual verification: ran the API and drove it via curl (equivalent to the Swagger UI walkthrough — confirmed the Bearer security scheme + global security requirement are present in `swagger.json`, so the Authorize button is live): register/login a user → `POST /api/notes` without a token → 401 → create a note → 201 → `GET` it → 200 → list → envelope `{items:[1], page:1, pageSize:20, totalCount:1, totalPages:1}` → update it → 200, `updatedAt` changed, `createdAt` unchanged → create with a blank title → 400 → delete it → 204 → `GET` it again → 404 → delete again → 404 → restore it → 200 → `GET` it again → 200 → restore it again → 409 → register/login a **second** user → `GET`/`PUT`/`DELETE`/restore the first user's note as the second user → 404 for all four, second user's list stays empty, first user's note unmodified

**Checkpoint (Phase 3):**
```bash
dotnet ef database update --project apps/api/src/NoteManagement.Infrastructure --startup-project apps/api/src/NoteManagement.Api
dotnet build apps/api/NoteManagement.sln
pnpm build
```

## Phase 4: Tests

One test per `specs/notes/spec.md` scenario added by this ticket (29 scenarios), plus domain-rule tests. Plan §9 flags the one testing-strategy decision this phase must follow: `NotesControllerTests` registers/logs in a test user via the real `/api/auth/register` + `/api/auth/login` endpoints to obtain a bearer token (no auth stub) — and a **second** registered/logged-in user for every "another user's note" scenario.

| Spec scenario | Test |
|---|---|
| Successful note creation | `NoteServiceTests.CreateAsync_WithValidData_CreatesNote` + `NotesControllerTests.Create_WithValidData_Returns201WithNote` |
| Missing title rejected | `NotesControllerTests.Create_WithMissingOrBlankTitle_Returns400` |
| Title exceeding maximum length rejected | `NotesControllerTests.Create_WithTitleOver200Chars_Returns400` |
| Missing content rejected | `NotesControllerTests.Create_WithMissingOrBlankContent_Returns400` |
| Unauthenticated request rejected | `NotesControllerTests.Create_WithoutAccessToken_Returns401` |
| Owner retrieves their own note | `NoteServiceTests.GetByIdAsync_WithOwnedNote_ReturnsNote` + `NotesControllerTests.GetById_WithOwnedNote_Returns200` |
| Non-existent note rejected | `NoteServiceTests.GetByIdAsync_WithUnknownId_ThrowsNoteNotFoundException` + `NotesControllerTests.GetById_WithUnknownId_Returns404` |
| Another user's note rejected identically to not-found | `NotesControllerTests.GetById_WithAnotherUsersNote_Returns404` |
| Soft-deleted note rejected identically to not-found | `NotesControllerTests.GetById_AfterSoftDelete_Returns404` |
| Lists only the caller's active notes | `NoteServiceTests.ListAsync_ReturnsOnlyCallersActiveNotesSortedByUpdatedAtDesc` + `NotesControllerTests.List_ReturnsOwnedNotesWithPaginationEnvelope` |
| Soft-deleted notes excluded from the list | `NotesControllerTests.List_ExcludesSoftDeletedNotes` |
| Another user's notes excluded from the list | `NotesControllerTests.List_ExcludesOtherUsersNotes` |
| Empty list for a user with no notes | `NoteServiceTests.ListAsync_WithNoNotes_ReturnsEmptyEnvelope` |
| Owner successfully updates their note | `NoteServiceTests.UpdateAsync_WithValidData_UpdatesTitleContentAndUpdatedAt` + `NotesControllerTests.Update_WithValidData_Returns200WithUpdatedNote` |
| Invalid update rejected | `NotesControllerTests.Update_WithInvalidData_Returns400AndDoesNotModifyNote` |
| Update to non-existent note rejected | `NoteServiceTests.UpdateAsync_WithUnknownId_ThrowsNoteNotFoundException` + `NotesControllerTests.Update_WithUnknownId_Returns404` |
| Update to another user's note rejected identically to not-found | `NotesControllerTests.Update_WithAnotherUsersNote_Returns404` |
| Update to a soft-deleted note rejected | `NotesControllerTests.Update_AfterSoftDelete_Returns404` |
| Owner soft-deletes their note | `NoteServiceTests.DeleteAsync_WithOwnedNote_SetsDeletedAt` + `NotesControllerTests.Delete_WithOwnedNote_Returns204` |
| Soft-deleted note excluded from subsequent retrieval | `NotesControllerTests.Delete_ThenGetById_Returns404` |
| Delete of non-existent note rejected | `NoteServiceTests.DeleteAsync_WithUnknownId_ThrowsNoteNotFoundException` + `NotesControllerTests.Delete_WithUnknownId_Returns404` |
| Delete of another user's note rejected identically to not-found | `NotesControllerTests.Delete_WithAnotherUsersNote_Returns404AndNoteRemainsActive` |
| Delete of an already soft-deleted note rejected | `NotesControllerTests.Delete_CalledTwice_SecondCallReturns404` |
| Owner restores their soft-deleted note | `NoteServiceTests.RestoreAsync_WithDeletedNote_ClearsDeletedAt` + `NotesControllerTests.Restore_WithSoftDeletedNote_Returns200` |
| Restored note reappears in retrieval and listing | `NotesControllerTests.Restore_ThenGetByIdAndList_NoteIsActive` |
| Restore of non-existent note rejected | `NoteServiceTests.RestoreAsync_WithUnknownId_ThrowsNoteNotFoundException` + `NotesControllerTests.Restore_WithUnknownId_Returns404` |
| Restore of another user's note rejected identically to not-found | `NotesControllerTests.Restore_WithAnotherUsersNote_Returns404` |
| Restore of a not-deleted note rejected as a conflict | `NoteServiceTests.RestoreAsync_WithActiveNote_ThrowsNoteNotDeletedException` + `NotesControllerTests.Restore_WithActiveNote_Returns409` |
| Restore succeeds regardless of elapsed time since deletion | `NoteServiceTests.RestoreAsync_LongAfterDeletion_StillSucceeds` |
| `Note` domain rules (`IsDeleted`/`UpdateContent()`/`SoftDelete()`/`Restore()`) | `NoteTests`: `IsDeleted_WhenNotDeleted_ReturnsFalse`, `IsDeleted_AfterSoftDelete_ReturnsTrue`, `UpdateContent_SetsNewValuesAndBumpsUpdatedAt_LeavesCreatedAtUnchanged`, `SoftDelete_WhenCalledTwice_KeepsFirstDeletedAtTimestamp`, `Restore_ClearsDeletedAt` |

- [x] 4.1 `Tests.Unit/Domain/NoteTests.cs`: the 5 `IsDeleted_*`/`UpdateContent_*`/`SoftDelete_*`/`Restore_*` tests listed above
- [x] 4.2 `Tests.Unit/Application/NoteServiceTests.cs`: hand-rolled `FakeNoteRepository`/`FakeUnitOfWork` (`CreateSut` factory, same convention as `AuthServiceTests`); the 13 `CreateAsync_*`/`GetByIdAsync_*`/`ListAsync_*`/`UpdateAsync_*`/`DeleteAsync_*`/`RestoreAsync_*` tests listed above
- [x] 4.3 `Tests.Integration/Api/NotesControllerTests.cs`: `WebApplicationFactory<Program>` + its own isolated LocalDB database (`ClassInitialize`/`ClassCleanup`, same shape as `AuthControllerTests`); a helper that registers + logs in a user and returns a bearer token (used for both the primary caller and a second "other user" per test); the 27 `Create_*`/`GetById_*`/`List_*`/`Update_*`/`Delete_*`/`Restore_*` tests listed above

**Checkpoint (Phase 4 — final gate for this ticket, per `CLAUDE.md` Quality Gates, run in order):**
```bash
dotnet build apps/api/NoteManagement.sln
dotnet test apps/api/NoteManagement.sln --collect:"XPlat Code Coverage"
pnpm lint --max-warnings 0
pnpm build
pnpm test --coverage
```

**Results:**
- `dotnet build` — 0 errors, 0 warnings.
- `dotnet test --collect:"XPlat Code Coverage"` — **123/123 passed** (57 unit + 66 integration; 0 failed, 0 skipped). Line coverage on every file this ticket added/touched (`Note.cs`, `NoteService.cs`, `NoteRepository.cs`, `NoteConfiguration.cs`, `NotesController.cs`, `ClaimsPrincipalExtensions.cs`, all 4 Notes DTOs, both exceptions, `TrimmedLengthAttribute.cs`): **189/191 lines — 99.0%**, well above the 80% new-code requirement (AGENTS.md §10/SDS §77).
- `pnpm lint` (`--filter web run lint`) — clean, no output.
- `pnpm build` (`build:shared` tsc --noEmit + `--filter web run build`) — both succeed, 0 type errors.
- `pnpm test` (`--filter web run test`) — passes (1/1) **only under `--pool=threads`**; the default fork-based Vitest pool times out spawning a worker process in this sandboxed shell environment, unrelated to this ticket (`apps/web` has zero diff — `git status --short apps/web` is empty). Flagged as a pre-existing environment/CI-runner limitation, not a regression; see Follow-up Tasks in the `/implement` summary.

## Not in scope for this ticket (plan §12)

Tags on notes / `NoteTags` (AB-1006); client-driven pagination/sorting/tag-filtering query params on `GET /api/notes` (AB-1005); `NoteVersions` snapshot-on-save (AB-1009); search (AB-1007); sharing (AB-1008); any frontend notes UI (AB-1011/AB-1012); automatic purge of soft-deleted notes after any retention window (unowned gap, not silently assumed); any change to `Users`/`RefreshTokens`/`PasswordResetOtps`/`AuthController`/`AuthService` beyond the new, unrelated `ClaimsPrincipalExtensions` file.
