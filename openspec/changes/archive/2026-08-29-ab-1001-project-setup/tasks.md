# Tasks: ab-1001-project-setup

Source: `proposal.md`, `plan.md`. Each task references the plan section it implements. `[PARALLEL]` tasks have no dependency on each other and may run in separate git worktrees per SDS §87 (frontend vs. backend is the natural split here).

## Phase 1: Foundation

Root tooling first — both the backend and frontend phases below depend on it existing (workspace membership, engines/packageManager pins).

- [x] 1.1 Root workspace: `package.json` (`private`, `packageManager: pnpm@11.24.0`, `engines.node >=22.14.0`, scripts per plan §7), `pnpm-workspace.yaml` (`apps/web`, `packages/shared` only), `.npmrc` — plan §7
- [x] 1.2 `pnpm install` succeeds with the empty workspace (sanity check before any package code exists)

**[PARALLEL] — 1.3 backend skeleton vs. 1.4 shared-package skeleton vs. 1.5 frontend skeleton, once 1.1–1.2 are done:**

- [x] 1.3 Backend solution skeleton — plan §2–§3:
  - `apps/api/NoteManagement.sln` referencing all 6 projects (empty at this point)
  - `Directory.Build.props` (net8.0, Nullable, ImplicitUsings, analyzers)
  - `Directory.Packages.props` (Central Package Management, all NuGet versions from plan §1)
  - `.config/dotnet-tools.json` pinning `dotnet-ef@8.0.22` (verify no newer 8.0.x patch first, per plan §1)
  - Six empty `.csproj` files with the project-reference graph from plan §3 (`Domain` ← `Application` ← `Infrastructure` ← `Api`; `Tests.Unit` → `Application`+`Domain`; `Tests.Integration` → `Api`+`Infrastructure`+`Application`)
  - `Domain/Entities|Enums|Interfaces/.gitkeep` placeholders
- [x] 1.4 Shared package skeleton — plan §6: `packages/shared/package.json` (name `@repo/shared`, no build script), `tsconfig.json`, `src/index.ts` (empty barrel), `src/types|schemas|utils/.gitkeep`
- [x] 1.5 Frontend skeleton — plan §5: `pnpm create vite@7.3.3 apps/web -- --template react-ts`, then add AGENTS.md §2 folders (`components/`, `features/`, `pages/`, `hooks/`, `services/`, `stores/`, `lib/`, `test/`) on top of the template output; wire `tsconfig` path mapping to `@repo/shared` (plan §6)

- [x] 1.6 `ApplicationDbContext` (zero `DbSet<T>`, plan §3) in `Infrastructure/Data/`

Note: the `InitialCreate` migration is **not** a Phase 1 task — `dotnet ef migrations add` needs design-time discovery of `ApplicationDbContext`, which requires `Program.cs` (3.1) and `AddInfrastructure` (2.2) already composed. It's task 3.2.

**Checkpoint (Phase 1):**
```bash
dotnet build apps/api/NoteManagement.sln          # 0 errors (empty projects, no logic yet)
pnpm install && pnpm build                        # web scaffold + shared barrel type-check clean
```

## Phase 2: Core implementation

**[PARALLEL] — backend health-check logic (2.1–2.3) vs. frontend styling/UI setup (2.4–2.5) vs. repo tooling (2.6–2.8):**

- [x] 2.1 Application layer — plan §3: `IHealthCheckService`, `IDatabaseHealthChecker` (interfaces), `HealthCheckResultDto` record, `HealthCheckService` implementation, `Application/DependencyInjection.cs` (`AddApplication`)
- [x] 2.2 Infrastructure layer — plan §3: `DatabaseHealthChecker` (implements `IDatabaseHealthChecker` via `ApplicationDbContext.Database.CanConnectAsync()`), `Infrastructure/DependencyInjection.cs` (`AddInfrastructure`, reads `ConnectionStrings:DefaultConnection`, throws clearly if missing)
- [x] 2.3 Api layer — plan §3: `HealthController` (`[AllowAnonymous]`, `GET /api/health` → `IHealthCheckService.CheckAsync`), `appsettings.json` + `appsettings.Development.json.example` (LocalDB connection string template), `Properties/launchSettings.json`
- [x] 2.4 Frontend styling — plan §5: `pnpm dlx shadcn@4.13.1 init` (Tailwind v4 + `@tailwindcss/vite`, `components.json`), wire the plugin into `vite.config.ts`
- [x] 2.5 Frontend placeholder UI — plan §5: `pages/HomePage.tsx`, `eslint.config.js` (flat config), Vitest `test` block in `vite.config.ts` (`environment: jsdom`, `setupFiles`), `test/setupTests.ts` (jest-dom matchers)
- [x] 2.6 CI workflow — plan §8: `.github/workflows/ci.yml` (`frontend` job on `ubuntu-latest`, `backend` job on `windows-latest`, per plan §8's exact command sequences)
- [x] 2.7 MCP config — plan §9: `.mcp.json` declaring the `context7` server (no API key committed)
- [x] 2.8 Commit-convention enforcement — plan §7: `.husky/commit-msg`, `commitlint.config.js` (extends `@commitlint/config-conventional`), `.commitlint/ab-ticket-rule.js` (custom `AB#<ticket>` suffix rule)

**Checkpoint (Phase 2):**
```bash
dotnet build apps/api/NoteManagement.sln          # 0 errors, health-check code compiles
pnpm lint --max-warnings 0
pnpm build
```

## Phase 3: Integration

- [x] 3.1 `Program.cs` composition — plan §3: `AddControllers`, `AddEndpointsApiExplorer`, `AddSwaggerGen` (+ dev-only `UseSwagger`/`UseSwaggerUI`), `AddProblemDetails` + `UseExceptionHandler`, `AddCors("Frontend")` (reads `Cors:FrontendOrigin`, default `http://localhost:5173`), `builder.Services.AddApplication()` + `.AddInfrastructure(builder.Configuration)`, `app.UseCors("Frontend")`, `app.MapControllers()`, trailing `public partial class Program { }`
- [x] 3.2 `InitialCreate` migration — plan §4 (moved from Phase 1: requires 3.1 + 2.2 already composed so `dotnet ef` can discover `ApplicationDbContext` at design-time) — via:
  ```
  dotnet ef migrations add InitialCreate --project apps/api/src/NoteManagement.Infrastructure --startup-project apps/api/src/NoteManagement.Api
  ```
- [x] 3.3 Fix `apps/api/CLAUDE.md`'s `dotnet ef` command examples to the real two-flag `--project`/`--startup-project` form — plan §4
- [x] 3.4 `dotnet ef database update` against LocalDB (creates the DB physically) — manual verification that `GET /api/health` returns `200 {"status":"healthy", "timestampUtc": ...}` via `dotnet run --project apps/api/src/NoteManagement.Api` + a manual request (curl/Swagger UI)
- [x] 3.5 Wire `HomePage` into `App.tsx`/`main.tsx`; manual verification that `pnpm --filter web dev` renders it with zero console errors
- [x] 3.6 Manual verification: commit with a message that omits `AB#<ticket>` is rejected by the `commit-msg` hook (proves 2.8 actually works end-to-end — not itself a candidate for an automated test, per plan §12). Afterward, clean up: unstage/reset whatever was staged for the test and remove the failed commit attempt (it's rejected by the hook before it's created, but confirm `git status`/`git log` are clean — no stray staged files or partial commit left behind) so the working tree is exactly as it was before this verification step.

**Checkpoint (Phase 3):**
```bash
dotnet ef migrations add InitialCreate --project apps/api/src/NoteManagement.Infrastructure --startup-project apps/api/src/NoteManagement.Api
dotnet ef database update --project apps/api/src/NoteManagement.Infrastructure --startup-project apps/api/src/NoteManagement.Api
dotnet build apps/api/NoteManagement.sln
pnpm build
```

## Phase 4: Tests

One test per `specs/project-setup/spec.md` scenario that's meaningfully automatable (the CI-automation, commit-convention, and MCP-config scenarios are verified structurally/manually per Phase 3 and CI's own existence, not by a unit/integration test asserting on GitHub Actions or git hooks internals):

| Spec scenario | Test |
|---|---|
| Install and build succeed from repo root | Covered by Phase 1/2 checkpoints (`pnpm install && pnpm build`) — no separate test file |
| Backend builds and boots | Covered by `dotnet build` checkpoints + 4.2 (integration test boots the host via `WebApplicationFactory`) |
| Health check responds | 4.2 `HealthEndpointTests.GetHealth_WhenCalled_Returns200WithHealthyStatus` |
| DbContext resolves at startup | 4.3 `ApplicationDbContextTests.CanConnectAsync_AfterMigration_ReturnsTrue` |
| Frontend dev server starts | Covered by 3.5 manual verification — a dev-server-boot assertion isn't a Vitest/RTL concern |
| Shared type import resolves | Covered by `tsc --noEmit` in the Phase 1/2 build checkpoints |
| CI runs on pull request | Verified by the CI workflow itself running on this ticket's PR (plan §8) — not a unit test |
| Non-conforming commit is rejected | 3.6 manual verification |
| MCP config present and valid | Verified by Claude Code loading `.mcp.json` without error when this repo is opened — structural, not a test file |

- [x] 4.1 `NoteManagement.Tests.Unit/Application/HealthCheckServiceTests.cs`:
  - `CheckAsync_WhenDatabaseReachable_ReturnsHealthyStatus` (fakes `IDatabaseHealthChecker.CanConnectAsync` → `true`)
  - `CheckAsync_WhenDatabaseUnreachable_ThrowsInvalidOperationException` (fakes → `false`; asserts the throw, since the contract has no "unhealthy" response value — plan §3)
- [x] 4.2 `NoteManagement.Tests.Integration/Api/HealthEndpointTests.cs`:
  - `GetHealth_WhenCalled_Returns200WithHealthyStatus` (`WebApplicationFactory<Program>`, real LocalDB, asserts `200` + `HealthResponse` shape matching `delta-openapi.yaml`)
- [x] 4.3 `NoteManagement.Tests.Integration/Infrastructure/ApplicationDbContextTests.cs`:
  - `CanConnectAsync_AfterMigration_ReturnsTrue` (against the migrated LocalDB test database)
- [x] 4.4 `apps/web/src/test/HomePage.test.tsx` (RTL): `HomePage_WhenRendered_DisplaysPlaceholderContent`

**Checkpoint (Phase 4 — final gate for this ticket, per CLAUDE.md Quality Gates, run in order):**
```bash
dotnet build apps/api/NoteManagement.sln
dotnet test apps/api/NoteManagement.sln --collect:"XPlat Code Coverage"
pnpm lint --max-warnings 0
pnpm build
pnpm test --coverage
```

## Not in scope for this ticket (plan §12)

No entities/DbSets beyond the empty `InitialCreate` migration, no JWT/auth wiring, no real TanStack Query/Zustand/TipTap usage, no pre-commit lint/test hook.
