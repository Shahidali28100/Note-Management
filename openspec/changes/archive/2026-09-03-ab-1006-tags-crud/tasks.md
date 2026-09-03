# Tasks: ab-1006-tags-crud

Source: `proposal.md`, `plan.md`. New `tags` capability (Domain/Application/Infrastructure/Api, full CRUD) plus an additive extension of the existing `notes` capability (tag assignment on create/update, tag filter on list, `tags` field on every note response). One new EF Core migration (`AddTagsAndNoteTags`). `packages/shared` gets a new `tags` module and an additive `notes` module change. No `apps/web` diff.

> **⚠ Deviation from the approved plan, recorded here for visibility:** the approved `plan.md` specified `Cascade`/`Cascade` for `NoteTags`'s two foreign keys. What shipped in task 3.5 is `Note → NoteTags` = **Restrict** (not Cascade), `Tag → NoteTags` = Cascade (unchanged). This was forced by a SQL Server error (`Error Number:1785`, "may cause cycles or multiple cascade paths") raised by `dotnet ef database update` — not by `dotnet ef migrations add`, which only generates code and never touches a database — and was not something `/plan`/`/tasks` review or `dotnet build` could have caught, since none of those execute real DDL. **A future ticket that hard-deletes/purges `Note` rows must explicitly delete that note's `NoteTags` rows first — it cannot rely on cascade.** Full account: `plan.md` §0.

## Phase 1: Foundation

Pure data shapes and interface contracts first — new DTOs/entities/exceptions compile standalone. `INoteRepository`'s signature change (extending `GetPageForUserAsync`, adding three tag-related members) is introduced here but **not implemented** until Phase 2 — this is expected to break `NoteRepository`/`NoteService`/`NoteServiceTests`'s `FakeNoteRepository` compilation until then, mirroring AB-1005's Phase 1 precedent for this same method.

**[PARALLEL] — 1.1 / 1.2 / 1.3 / 1.5 / 1.6 have no dependency on each other; 1.4 depends only on the Domain entities from 1.1:**

- [x] 1.1 Domain entities — plan §2:
  - `Domain/Entities/Tag.cs` (new): `Id, UserId, Name, Color, CreatedAt, UpdatedAt`, private setters, static `Create(userId, name, color)`, `Rename(name, color)` (bumps `UpdatedAt`, never touches `UserId`) — mirrors `Note.cs`'s shape exactly.
  - `Domain/Entities/NoteTag.cs` (new): `NoteId, TagId` only, private setters, static `Create(noteId, tagId)` — no surrogate id, no timestamps (SDS §16).
- [x] 1.2 Application DTOs — plan §2:
  - `Application/DTOs/Tags/CreateTagRequestDto.cs`, `UpdateTagRequestDto.cs` (new): `[Required, TrimmedLength(1, 50)] Name`, `[Required, RegularExpression(@"^#[0-9A-Fa-f]{6}$")] Color`.
  - `Application/DTOs/Tags/TagResponseDto.cs` (new): `Id, Name, Color, NoteCount, CreatedAt, UpdatedAt`.
  - `Application/DTOs/Tags/TagRefDto.cs` (new): `Id, Name, Color` — the shape embedded in a note's `tags` array.
  - `Application/DTOs/Notes/CreateNoteRequestDto.cs`, `UpdateNoteRequestDto.cs` (modify): add trailing `IReadOnlyList<Guid>? TagIds = null`.
  - `Application/DTOs/Notes/NoteResponseDto.cs` (modify): add `IReadOnlyList<TagRefDto> Tags` (positioned before `CreatedAt`, matching `delta-openapi.yaml`'s `Note` schema field order).
  - `Application/DTOs/Notes/NoteListQueryDto.cs` (modify): add trailing `Guid? TagId = null` (no DataAnnotations needed — a malformed GUID query string is already rejected `400` by `[ApiController]`'s automatic model-binding-failure handling).
- [x] 1.3 Application exceptions — plan §2:
  - `Application/Exceptions/TagNotFoundException.cs` (new) — mirrors `NoteNotFoundException` exactly; mapped to `404`.
  - `Application/Exceptions/DuplicateTagNameException.cs` (new) — mirrors `DuplicateEmailException`; mapped to `409`.
  - `Application/Exceptions/InvalidTagReferenceException.cs` (new) — takes the invalid id set; mapped to `400` (never `404`/`403` — see plan.md architecture decisions).
- [x] 1.4 Application interfaces — plan §2 (depends on 1.1's `Tag`/`NoteTag` types):
  - `Application/Interfaces/ITagRepository.cs` (new): `Add`, `Remove`, `GetByIdAsync(id, userId, ct)`, `GetAllForUserAsync(userId, ct)`, `GetActiveNoteCountsAsync(userId, ct)`, `GetOwnedIdsAsync(userId, tagIds, ct)`.
  - `Application/Interfaces/ITagService.cs` (new): `CreateAsync`, `ListAsync`, `UpdateAsync`, `DeleteAsync`.
  - `Application/Interfaces/INoteRepository.cs` (modify): add `GetTagsForNoteAsync(noteId, ct)`, `GetTagsForNotesAsync(noteIds, ct)`, `ReplaceTagsForNoteAsync(noteId, tagIds, ct)`; extend `GetPageForUserAsync(...)` with a sixth parameter `Guid? tagId` (inserted before `CancellationToken`).
  - Verify: `dotnet build apps/api/NoteManagement.sln` fails with exactly the expected errors — `NoteRepository`/`NoteService` no longer satisfy `INoteRepository`, and `NoteServiceTests`'s `FakeNoteRepository` no longer satisfies it either — confirming the interface change's full blast radius before Phase 2 fixes it.
- [x] 1.5 Infrastructure configurations — plan §2:
  - `Infrastructure/Configurations/TagConfiguration.cs` (new): `Name` maxlength 50 required, `Color` maxlength 7 required, unique index on `(UserId, Name)` (relies on default case-insensitive collation, same precedent as `Users.Email` — no `COLLATE` clause, no shadow column), FK to `Users` cascade.
  - `Infrastructure/Configurations/NoteTagConfiguration.cs` (new): composite key `(NoteId, TagId)`, FK to `Notes` cascade, FK to `Tags` cascade, secondary index on `TagId`, no query filter (association rows must survive a note's soft delete).
  - `Infrastructure/Data/ApplicationDbContext.cs` (modify): add `DbSet<Tag> Tags`, `DbSet<NoteTag> NoteTags`.
- [x] 1.6 Shared package — plan §2:
  - `packages/shared/src/schemas/tags.ts` (new): `createTagRequestSchema`, `updateTagRequestSchema`, `tagResponseSchema`, `tagListResponseSchema` (plain array, no envelope), `tagRefSchema`.
  - `packages/shared/src/types/tags.ts` (new): re-export types from `../schemas/tags`, same idiom as `types/notes.ts`.
  - `packages/shared/src/schemas/notes.ts` (modify): import `tagRefSchema`; add `tagIds: z.array(z.string().uuid()).optional()` to `createNoteRequestSchema`; add `tags: z.array(tagRefSchema)` to `noteResponseSchema`; add `tagId: z.string().uuid().optional()` to `noteListQuerySchema`.
  - `packages/shared/src/index.ts` (modify): add the AB-1006 tags export block (schema values + `export * from './types/tags'`), mirroring the existing notes block.
  - Verify: `pnpm build` (shared package `tsc --noEmit`) type-checks cleanly, independent of the backend build.

**Checkpoint (Phase 1):**
```bash
dotnet build apps/api/NoteManagement.sln   # expected to FAIL — NoteRepository/NoteService/FakeNoteRepository don't yet
                                            # implement the new INoteRepository members; confirms scope of 1.4 before Phase 2
pnpm build                                 # expected to PASS — packages/shared type-checks independently
```
**Verified:** `dotnet build` failed with exactly the predicted 4 errors (`NoteService.cs` — missing `cancellationToken` arg, 2 deconstruction-inference errors, missing `NoteResponseDto.UpdatedAt` arg). `pnpm build` passed cleanly (shared `tsc --noEmit` + `web` vite build).

## Phase 2: Core Implementation

Implements the Phase 1 interfaces. All four production tasks depend only on Phase 1's interfaces/DTOs, not on each other's implementations, so they can proceed independently; 2.5 (test-fixture compile fix) depends on 2.2/2.4's final method shapes.

**[PARALLEL] — 2.1 / 2.2 / 2.3 / 2.4 (each only needs the Phase 1 interfaces):**

- [x] 2.1 `Infrastructure/Repositories/TagRepository.cs` (new) — plan §2: implement `ITagRepository`. `GetActiveNoteCountsAsync` uses a per-tag correlated subquery through `_dbContext.Notes.Any(...)` so the existing soft-delete query filter excludes deleted notes automatically (FRS-TAG-004) — no duplicated `DeletedAt == null` predicate.
- [x] 2.2 `Infrastructure/Repositories/NoteRepository.cs` (modify) — plan §2: implement `GetTagsForNoteAsync` (join `NoteTags`→`Tags`), `GetTagsForNotesAsync` (batched, grouped by `NoteId`), `ReplaceTagsForNoteAsync` (delete existing `NoteTags` rows for the note, insert one per submitted id — full-replace, no diffing); extend `GetPageForUserAsync` with the `tagId` filter, applied as an extra `.Where(...)` only when `tagId is Guid t` so the no-filter SQL is unchanged from AB-1005.
- [x] 2.3 `Application/Services/TagService.cs` (new) — plan §2: implement `ITagService`. `CreateAsync`/`UpdateAsync` catch `UniqueConstraintViolationException` from `SaveChangesAsync` and rethrow as `DuplicateTagNameException` (same pattern as `AuthService.RegisterAsync` → `DuplicateEmailException` — no pre-check race). `DeleteAsync` relies on `NoteTagConfiguration`'s FK cascade to remove associations — no manual cleanup.
- [x] 2.4 `Application/Services/NoteService.cs` (modify) — plan §2: add `ITagRepository` constructor dependency; add private `ResolveTagIdsAsync` (empty/missing → `Array.Empty<Guid>()`; de-duplicates; throws `InvalidTagReferenceException` for any id `GetOwnedIdsAsync` doesn't confirm) and `MapWithTagsAsync`/`Map(note, tags)` helpers; wire `ResolveTagIdsAsync` + `ReplaceTagsForNoteAsync` into `CreateAsync`/`UpdateAsync`; wire `MapWithTagsAsync` into `CreateAsync`/`GetByIdAsync`/`UpdateAsync`/`RestoreAsync`; wire the `TagId` ownership check + `GetTagsForNotesAsync` batching into `ListAsync`.
- [x] 2.5 Test-fixture compile fix (structural only — new test *methods* are Phase 4's job): `Tests.Unit/Application/NoteServiceTests.cs`'s `FakeNoteRepository` — implement the three new `INoteRepository` members and the extended `GetPageForUserAsync` signature using an in-memory `Dictionary<Guid, List<Guid>>` of `noteId`→`tagIds`; add a `FakeTagRepository` (implementing `ITagRepository`) that both `NoteServiceTests` and the new `TagServiceTests` (Phase 4) will use; update the `CreateSut` helper to accept/construct the new `ITagRepository` dependency so every existing test method compiles against `NoteService`'s new constructor signature (not just the `ListAsync(...)` call sites — `NoteResponseDto` itself now has a *required* `Tags` member, so every existing test that reads a `CreateAsync`/`GetByIdAsync`/`UpdateAsync`/`RestoreAsync`/`ListAsync` result is touching a value that now carries `Tags`, even where no assertion on it existed before). Sweep every existing test method in this file — not only the ones already named for Phase 4 — and add a minimal `Tags` assertion (e.g. `CollectionAssert.AreEqual(Array.Empty<TagRefDto>(), ...)` or `Assert.AreEqual(0, result.Tags.Count)`) wherever the note under test has no tags assigned, so a regression that silently populates or drops tags on the untagged path is still caught. Existing test *behavior* must be unchanged (all-null/empty `tagIds`/`tagId` reproduces AB-1005's behavior exactly).

**Checkpoint (Phase 2):**
```bash
dotnet build apps/api/NoteManagement.sln   # 0 errors — TagRepository/TagService/NoteRepository/NoteService now satisfy
                                            # the Phase 1 interfaces; Api project (controllers/DI) not touched yet
dotnet test apps/api/NoteManagement.sln    # existing tests still green — no behavior change for the null/empty tagIds/tagId path
```
**Verified:** `dotnet build` — 0 errors, 0 warnings, all 6 projects. `dotnet test` (unit only) — 60/60 passed, including the 5 existing tests extended in-place with a new `Assert.AreEqual(0, result.Tags.Count)`/`Assert.IsTrue(result.Items.All(i => i.Tags.Count == 0))` assertion per task 2.5's sweep (Create/GetById/List/Update/Restore canonical happy-path tests).

## Phase 3: Integration

Wires the new capability into the HTTP pipeline and the database.

- [x] 3.1 `Api/Controllers/TagsController.cs` (new) — plan §2: `[Authorize]`, four actions (`Create` → `201`, `List` → `200`, `Update` → `200`, `Delete` → `204`), each calling `User.GetUserId()` + the matching `ITagService` method, with `[ProducesResponseType]` attributes matching `delta-openapi.yaml` exactly (`Create`: `400/401/409`; `List`: `401`; `Update`: `400/401/404/409`; `Delete`: `401/404`).
- [x] 3.2 `Api/Middleware/ProblemDetailsExceptionHandler.cs` (modify) — plan §2: add `TagNotFoundException → 404`, `DuplicateTagNameException → 409`, `InvalidTagReferenceException → 400` to the exception-type `switch`.
- [x] 3.3 `Application/DependencyInjection.cs` (modify): `services.AddScoped<ITagService, TagService>();`
- [x] 3.4 `Infrastructure/DependencyInjection.cs` (modify): `services.AddScoped<ITagRepository, TagRepository>();` (grouped under an "AB-1006: tags persistence" comment, matching the file's existing per-ticket comment convention).
- [x] 3.5 Generate the EF Core migration (now that the solution builds cleanly end-to-end):
  ```bash
  dotnet ef migrations add AddTagsAndNoteTags --project apps/api/src/NoteManagement.Infrastructure --startup-project apps/api/src/NoteManagement.Api
  ```
  **Found and fixed a real bug during `dotnet ef database update`** (not caught by static review): SQL Server rejected the originally-planned `Cascade`/`Cascade` pair on `NoteTags`'s two FKs with "may cause cycles or multiple cascade paths" — `Users → Notes → NoteTags` and `Users → Tags → NoteTags` both cascading to the same table from the same ancestor is disallowed. Fixed by changing `NoteTagConfiguration`'s `Note → NoteTags` FK to `DeleteBehavior.Restrict` (keeping `Tag → NoteTags` as `Cascade`, since FRS-TAG-003 requires it and nothing hard-deletes a Note yet — soft delete is an UPDATE). Removed the first migration (`dotnet ef migrations remove`) and regenerated after the fix; `plan.md`'s `NoteTagConfiguration.cs` listing updated to match. Reviewed the regenerated `Up()`/`Down()`: `CreateTable "Tags"` (+ unique index `IX_Tags_UserId_Name`), `CreateTable "NoteTags"` (`FK_NoteTags_Notes_NoteId` now `ON DELETE NO ACTION`, `FK_NoteTags_Tags_TagId` still `ON DELETE CASCADE`, + index `IX_NoteTags_TagId`) — purely additive, no existing table/column altered.
- [x] 3.6 Apply the migration and manually verify via curl against the running dev API: create a tag, list tags (`noteCount: 0`), create a note with `tagIds`, confirm its `tags` array, attempt an invalid `tagIds` entry (`400`), list notes with `?tagId=` (matches), delete the tag, confirm the note's `tags` array no longer includes it (note itself untouched), and confirm the now-deleted `tagId` is rejected `400` on a subsequent list-filter attempt.
  ```bash
  dotnet ef database update --project apps/api/src/NoteManagement.Infrastructure --startup-project apps/api/src/NoteManagement.Api
  ```

**Checkpoint (Phase 3):**
```bash
dotnet build apps/api/NoteManagement.sln
dotnet ef database update --project apps/api/src/NoteManagement.Infrastructure --startup-project apps/api/src/NoteManagement.Api
dotnet test apps/api/NoteManagement.sln    # still green — no new tests yet, nothing regressed
```
**Verified:** Migration applied cleanly after the FK fix. Full manual curl walkthrough passed every scenario listed in 3.6, including the case-insensitive duplicate-name `409` and invalid-color-format `400` (spot-checked while the server was up). `dotnet build` — 0 errors. `dotnet test` — 60 unit + 74 integration passed (no new tests yet; confirms the wiring introduced no regression).

## Phase 4: Tests

One test per new/changed scenario in `specs/tags/spec.md` (24 scenarios, all new) and `specs/notes/spec.md`'s deltas (12 new/changed scenarios), per plan.md §3's full mapping table. Split unit vs. integration the same way AB-1004/1005 did: pure business-rule/mapping logic → `Tests.Unit`; anything depending on `[ApiController]`'s automatic ModelState→`400` or the real auth/HTTP pipeline → `Tests.Integration`.

- [x] 4.1 `Tests.Unit/Domain/TagTests.cs` (new): `Create_SetsAllFields`, `Rename_UpdatesNameColorAndUpdatedAt_LeavesOwnerUnchanged`.
- [x] 4.2 `Tests.Unit/Domain/NoteTagTests.cs` (new): `Create_SetsNoteIdAndTagId`.
- [x] 4.3 `Tests.Unit/Application/TagServiceTests.cs` (new) — plan §3 table: `CreateAsync_WithValidData_CreatesTag`, `CreateAsync_WithCaseInsensitiveDuplicateName_ThrowsDuplicateTagNameException`, `ListAsync_WithNoTags_ReturnsEmptyArray`, `UpdateAsync_WithValidData_UpdatesNameAndColor`, `UpdateAsync_WithDuplicateName_ThrowsDuplicateTagNameException`, `UpdateAsync_WithUnknownId_ThrowsTagNotFoundException`, `DeleteAsync_WithOwnedTag_RemovesTag`, `DeleteAsync_WithUnknownId_ThrowsTagNotFoundException`.
- [x] 4.4 `Tests.Unit/Application/NoteServiceTests.cs` (modify) — plan §3 table, new methods: `CreateAsync_WithTagIds_AssociatesNoteWithTags`, `CreateAsync_WithDuplicateTagIds_AssignsTagExactlyOnce`, `CreateAsync_WithInvalidTagId_ThrowsInvalidTagReferenceException`, `UpdateAsync_WithTagIds_ReplacesTagAssignment`, `UpdateAsync_OmittingPreviouslyAssignedTag_RemovesAssociation`, `UpdateAsync_WithEmptyTagIds_ClearsAllAssignments`, `UpdateAsync_WithInvalidTagId_ThrowsInvalidTagReferenceException`, `ListAsync_WithTagIdFilter_ReturnsOnlyNotesCarryingThatTag`, `ListAsync_WithInvalidTagId_ThrowsInvalidTagReferenceException`.
- [x] 4.5 `Tests.Integration/Api/TagsControllerTests.cs` (new) — plan §3 table, full CRUD + validation + cross-user isolation: `Create_WithValidData_Returns201WithTag`, `Create_WithMissingOrBlankName_Returns400`, `Create_WithNameOver50Chars_Returns400`, `Create_WithMissingColor_Returns400`, `Create_WithInvalidColorFormat_Returns400`, `Create_WithDuplicateName_Returns409`, `Create_SameNameDifferentUsers_BothSucceed`, `Create_WithoutAccessToken_Returns401`, `List_ReturnsOnlyCallersTags`, `List_NoteCountExcludesSoftDeletedNotes`, `List_ExcludesOtherUsersTags`, `List_WithoutAccessToken_Returns401`, `Update_WithValidData_Returns200`, `Update_WithSameNameNewColor_Returns200`, `Update_WithInvalidNameOrColor_Returns400`, `Update_WithDuplicateName_Returns409`, `Update_OtherUsersTag_Returns404`, `Delete_WithOwnedTag_Returns204`, `Delete_PreservesAssociatedNotesButRemovesAssociation`, `Delete_ThenList_ExcludesDeletedTag`, `Delete_OtherUsersTag_Returns404`.
- [x] 4.6 `Tests.Integration/Api/NotesControllerTests.cs` (modify) — plan §3 table: new — `Create_WithTagIds_Returns201WithTags`, `Create_WithInvalidTagId_Returns400`, `Update_WithTagIds_Returns200WithUpdatedTags`, `Update_WithInvalidTagId_Returns400`, `List_WithTagIdFilter_ReturnsFilteredNotes`, `List_WithTagIdFilterNoMatches_ReturnsEmptyItems`, `List_WithInvalidTagId_Returns400`. `NoteResponseDto`'s `Tags` member is required (not optional/nullable), so every existing test that deserializes one — directly via `ReadFromJsonAsync<NoteResponseDto>` or through the `CreateNoteAsync` helper — is now handling a value with a real `Tags` field even where the test never asserted on it before: sweep `Create_WithValidData_Returns201WithNote`, `GetById_WithOwnedNote_Returns200`, `Update_WithValidData_Returns200WithUpdatedNote` (and its "unchanged after invalid update" counterpart), `Restore_WithDeletedNote_Returns200`, and `List_ReturnsOwnedNotesWithPaginationEnvelope`, adding an explicit `CollectionAssert.AreEqual(Array.Empty<TagRefDto>(), ...)`/`Assert.AreEqual(0, ....Tags.Count)` assertion to each so the untagged path is verified, not just left to compile silently.

**Checkpoint (Phase 4 — final gate for this ticket, per `CLAUDE.md` Quality Gates, run in order):**
```bash
pnpm lint --max-warnings 0
dotnet build apps/api/NoteManagement.sln
pnpm build
dotnet test apps/api/NoteManagement.sln --collect:"XPlat Code Coverage"
pnpm test --coverage
```
Fix and re-run on the first failure before proceeding to the next gate — never treat a later gate as informative once an earlier one has failed.

**Results:**
- `pnpm lint --max-warnings 0` — clean, 0 errors/warnings.
- `dotnet build apps/api/NoteManagement.sln` — 0 errors, 0 warnings, all 6 projects.
- `pnpm build` (`build:shared` `tsc --noEmit` + `--filter web run build`) — both succeed, 0 type errors.
- `dotnet test --collect:"XPlat Code Coverage"` — **182/182 passed** (80 unit + 102 integration; 0 failed, 0 skipped). Line coverage on every file this ticket added/touched: `TagService.cs` 100%, `TagRepository.cs` 100% (one compiler-generated async-state-machine segment for `GetOwnedIdsAsync`'s empty-input guard at 77.8% — that branch is genuinely unreachable from any current call site, since every caller already guards against an empty id set before calling it; left as defensive code, not a gap), `NoteService.cs` 100%, `NoteRepository.cs` 100% except the pre-existing `GetPageForUserAsync` state machine (81.8%, same shape/cause as AB-1005's own unexercised sort/filter combinations), `Tag.cs`/`NoteTag.cs`/all new DTOs/exceptions/configurations 100%. All well above the 80% new-code requirement (AGENTS.md §10/SDS §77).
- `pnpm test --coverage` (`--filter web run test`) — 1/1 passed, 100% coverage (unaffected `apps/web` placeholder suite — this ticket has zero `apps/web` diff). **Environment note**: the default `forks` worker pool timed out spawning a child process in this sandbox on the first attempt (`[vitest-pool-runner]: Timeout waiting for worker to respond`); re-run with `--pool=threads` passed cleanly in 26s, confirming the timeout was a sandbox/child-process-spawn restriction, not a regression — see Follow-up Tasks in the `/implement` summary.

## Not in scope for this ticket (plan §5 / proposal.md)

Multi-tag filtering (AND/OR across several `tagId` values); tag colors beyond `#RRGGBB` hex; any frontend tag UI (AB-1011/AB-1012); search (AB-1007); sharing (AB-1008); version history capturing tag assignments (AB-1009).
