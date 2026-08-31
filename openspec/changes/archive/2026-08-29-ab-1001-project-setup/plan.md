# Plan: ab-1001-project-setup

Source: `proposal.md`, `specs/project-setup/spec.md`, `docs/SDS.md`, `AGENTS.md`, `apps/api/CLAUDE.md`, `apps/web/CLAUDE.md`, `packages/shared/CLAUDE.md`.

## 0. Environment facts gathered (this machine)

Verified before planning file paths, so the plan matches what will actually build here:

| Tool | Installed | Notes |
|---|---|---|
| .NET SDKs | 9.0.306, 3.1.426 | No .NET 8 SDK installed, **but** `Microsoft.NETCore.App 8.0.21` and `Microsoft.AspNetCore.App 8.0.21` **runtimes** are installed. The 9.0.306 SDK can build/run a `net8.0`-targeted project against those runtime packs — confirmed viable, no SDK install needed. |
| SQL Server LocalDB | Yes (`MSSQLLocalDB` instance present) | Matches the chosen dev-DB decision. |
| `dotnet-ef` (global tool) | 9.0.3 | Major-version mismatch vs. the EF Core 8.0.x packages this project will reference → **do not rely on the global tool**; pin a project-local tool instead (§4). |
| Node | v22.14.0 | Fine for Vite 7 / React 19 / TS 5.9 tooling (all require Node ≥ 18–20). |
| pnpm | Not installed, but `corepack` 0.31.0 is present | Activate via corepack rather than a separate global install (§6). |

## 1. Package/tool versions to pin (resolved via live lookup — none from memory)

No `@latest` anywhere. Where a source gave an unambiguous npm/NuGet "latest" tag, that exact version is pinned below. Where the live lookup was ambiguous, the plan says so explicitly rather than guessing a precise patch.

### NPM (root + apps/web + packages/shared)

| Package | Version | Role |
|---|---|---|
| pnpm | `11.24.0` | package manager (`packageManager` field) — this is the version npm's `latest` tag resolves to; pnpm 12 (Rust rewrite) is out but still on the `next-12` tag, not `latest`, so it's deliberately **not** used yet |
| node (engines) | `>=22.14.0` | matches this machine; document as the supported floor |
| vite | `7.3.3` | frontend build tool — deliberately one major behind the newest (`8.2.2`); see note below |
| @vitejs/plugin-react | latest compatible with vite 7 (resolve at install time via `pnpm add`) | React fast refresh plugin |
| react / react-dom | `19.2.8` | SDS-mandated major (React 19), latest patch |
| typescript | `5.9.3` | deliberately one major behind the newest (`7.0.2`) — see note below; last release of the mature 5.x line before the 6.0/7.0 compiler rewrite |
| @tanstack/react-query | `5.102.8` | server state |
| zustand | `5.0.15` | client UI state |
| @tiptap/core, @tiptap/react, @tiptap/starter-kit | `3.30.5` | editor (content format itself is an AB-1012 decision, not this ticket's) |
| tailwindcss, @tailwindcss/vite | `4.3.3` | shadcn/ui v4-generation default; Vite-native plugin, no PostCSS config needed |
| shadcn (CLI) | `4.13.1` | used via `pnpm dlx shadcn@4.13.1 init` — not a runtime dependency, a code-generator |
| vitest | `4.1.11` | test runner — peer-compatible with `vite@7.3.3` (Vitest 4.0 requires `vite >= 6.0.0`; 4.1 *added* Vite 8 support on top of that, it didn't drop 6/7 — confirmed via Vitest's own release notes/issue tracker, not assumed) |
| @testing-library/react | `16.3.3` | requires `@testing-library/dom` as an explicit peer (RTL 16+) |
| @testing-library/jest-dom, @testing-library/dom | latest compatible (resolve at install time) | RTL peer/matchers |
| jsdom | latest compatible (resolve at install time) | Vitest DOM environment |
| eslint + typescript-eslint + eslint-plugin-react-hooks + eslint-plugin-react-refresh | latest compatible (resolve at install time) | `pnpm lint --max-warnings 0` |
| husky | `9.1.7` | git hooks |
| @commitlint/cli, @commitlint/config-conventional | `21.2.2` | commit-msg enforcement |

**Revised per plan review**: the original draft pinned the absolute newest majors (`vite@8.2.2`, `typescript@7.0.2`) under a literal reading of "pin latest-stable." Per explicit reviewer direction, both are stepped back one major to their latest-stable *previous*-major patch instead — `vite@7.3.3`, `typescript@5.9.3` — trading a few months of freshness for an ecosystem (ESLint/`typescript-eslint`, Vite plugins) with fully-shaken-out support, appropriate for a foundation ticket everything else builds on. Confirmed `vitest@4.1.11` still peer-supports `vite@7.3.3` (see table above) so no other package needed to move.

### NuGet (apps/api, Central Package Management via `Directory.Packages.props`)

| Package | Version | Notes |
|---|---|---|
| Microsoft.EntityFrameworkCore | `8.0.x` (confirmed ≥ 8.0.20, saw an 8.0.25 reference too — **verify exact latest 8.0.x patch on nuget.org at implementation time**, all `Microsoft.EntityFrameworkCore.*` packages must use the same patch) | ORM |
| Microsoft.EntityFrameworkCore.SqlServer | same patch as above | SQL Server provider |
| Microsoft.EntityFrameworkCore.Design | same patch as above | needed for `dotnet ef` design-time |
| Microsoft.EntityFrameworkCore.Tools | same patch as above | PMC-style tooling (harmless to include even though we drive migrations via CLI) |
| Swashbuckle.AspNetCore | `10.2.3` | confirmed targeting net8.0; Swagger/OpenAPI generation + UI (.NET 8 has no built-in OpenAPI generator — that's a .NET 9 addition — so Swashbuckle is the correct choice for this TFM) |
| Microsoft.NET.Test.Sdk | `18.9.0` | test host |
| MSTest.TestFramework | `4.3.3` | classic MSTest packages — see **Architecture Decision** below on why not `MSTest.Sdk` |
| MSTest.TestAdapter | `4.3.3` | " |
| coverlet.collector | `10.0.1` | `dotnet test --collect:"XPlat Code Coverage"` (already documented in `apps/api/CLAUDE.md`) |
| dotnet-ef (local tool, `.config/dotnet-tools.json`) | `8.0.22` (confirmed to exist; **verify no newer 8.0.x patch at implementation time**) | pinned **per-project**, not the global 9.0.3 tool, to stay major-version-aligned with the EF Core 8.0.x packages |

## 2. File tree to create

```
/ (repo root)
├── package.json                          # root workspace manifest (NEW)
├── pnpm-workspace.yaml                   # packages: apps/web, packages/shared — NOT apps/api (NEW)
├── .npmrc                                # pnpm strictness settings (NEW)
├── commitlint.config.js                  # extends conventional + custom AB#ticket rule (NEW)
├── .commitlint/ab-ticket-rule.js          # custom commitlint plugin rule (NEW)
├── .husky/
│   └── commit-msg                        # runs `pnpm exec commitlint --edit "$1"` (NEW)
├── .mcp.json                              # Context7 MCP server declaration (NEW)
├── .github/
│   └── workflows/
│       └── ci.yml                        # frontend job (ubuntu-latest) + backend job (windows-latest) (NEW)
│
├── apps/
│   ├── api/
│   │   ├── CLAUDE.md                     # MODIFIED — fix `dotnet ef` examples to real multi-project paths (§5)
│   │   ├── NoteManagement.sln             (NEW)
│   │   ├── Directory.Build.props          (NEW — TargetFramework net8.0, Nullable, ImplicitUsings, analyzers)
│   │   ├── Directory.Packages.props       (NEW — Central Package Management, versions from §1)
│   │   ├── .config/
│   │   │   └── dotnet-tools.json          (NEW — local tool manifest pinning dotnet-ef 8.0.22)
│   │   ├── src/
│   │   │   ├── NoteManagement.Api/
│   │   │   │   ├── NoteManagement.Api.csproj
│   │   │   │   ├── Program.cs
│   │   │   │   ├── appsettings.json
│   │   │   │   ├── appsettings.Development.json.example   # committed template; real file gitignored
│   │   │   │   ├── Properties/launchSettings.json
│   │   │   │   └── Controllers/HealthController.cs
│   │   │   ├── NoteManagement.Application/
│   │   │   │   ├── NoteManagement.Application.csproj
│   │   │   │   ├── DependencyInjection.cs                 # AddApplication(this IServiceCollection)
│   │   │   │   ├── Interfaces/IHealthCheckService.cs
│   │   │   │   ├── Interfaces/IDatabaseHealthChecker.cs
│   │   │   │   ├── Services/HealthCheckService.cs
│   │   │   │   └── DTOs/Health/HealthCheckResultDto.cs
│   │   │   ├── NoteManagement.Domain/
│   │   │   │   ├── NoteManagement.Domain.csproj
│   │   │   │   ├── Entities/.gitkeep       # empty — first entities land in AB-1002/AB-1004
│   │   │   │   ├── Enums/.gitkeep
│   │   │   │   └── Interfaces/.gitkeep
│   │   │   └── NoteManagement.Infrastructure/
│   │   │       ├── NoteManagement.Infrastructure.csproj
│   │   │       ├── DependencyInjection.cs                 # AddInfrastructure(this IServiceCollection, IConfiguration)
│   │   │       ├── Data/ApplicationDbContext.cs            # zero DbSet<T> this ticket
│   │   │       ├── HealthChecks/DatabaseHealthChecker.cs
│   │   │       └── Migrations/
│   │   │           ├── <timestamp>_InitialCreate.cs         # empty model diff — creates DB + history table only
│   │   │           ├── <timestamp>_InitialCreate.Designer.cs
│   │   │           └── ApplicationDbContextModelSnapshot.cs
│   │   └── tests/
│   │       ├── NoteManagement.Tests.Unit/
│   │       │   ├── NoteManagement.Tests.Unit.csproj
│   │       │   └── Application/HealthCheckServiceTests.cs
│   │       └── NoteManagement.Tests.Integration/
│   │           ├── NoteManagement.Tests.Integration.csproj
│   │           ├── Api/HealthEndpointTests.cs
│   │           └── Infrastructure/ApplicationDbContextTests.cs
│   │
│   └── web/
│       ├── CLAUDE.md                     # unchanged — already accurate
│       ├── package.json                  # name: "web" (matches `pnpm --filter web ...` in CLAUDE.md)
│       ├── vite.config.ts                # includes vitest `test` block, @tailwindcss/vite plugin
│       ├── tsconfig.json / tsconfig.app.json / tsconfig.node.json
│       ├── index.html
│       ├── eslint.config.js              # flat config
│       ├── components.json               # shadcn/ui config (generated by `shadcn init`)
│       └── src/
│           ├── main.tsx
│           ├── App.tsx
│           ├── index.css                 # `@import "tailwindcss";`
│           ├── components/.gitkeep
│           ├── features/.gitkeep
│           ├── pages/HomePage.tsx        # placeholder page proving the scaffold renders
│           ├── hooks/.gitkeep
│           ├── services/.gitkeep
│           ├── stores/.gitkeep
│           ├── lib/.gitkeep
│           └── test/
│               ├── setupTests.ts         # jest-dom matchers
│               └── HomePage.test.tsx     # placeholder RTL test
│
└── packages/
    └── shared/
        ├── CLAUDE.md                     # unchanged — already accurate
        ├── package.json                  # name: "@repo/shared", no "build" script (raw source per decision)
        ├── tsconfig.json                 # referenced by apps/web via TS project references
        └── src/
            ├── index.ts                  # barrel export — empty pending AB-1002+ DTOs
            ├── types/.gitkeep
            ├── schemas/.gitkeep
            └── utils/.gitkeep
```

## 3. Backend architecture — layering and reasoning

Project reference graph (enforces SDS §5's one-directional dependency rule at compile time, not just by convention):

```
NoteManagement.Domain            (zero project references — no ASP.NET Core, no EF Core)
        ^
        |
NoteManagement.Application  ──requires──> Domain
        ^
        |
NoteManagement.Infrastructure ──requires──> Domain, Application
        ^
        |
NoteManagement.Api          ──requires──> Application, Infrastructure (composition root)

NoteManagement.Tests.Unit         ──requires──> Application, Domain (no EF Core/ASP.NET Core deps — fakes IDatabaseHealthChecker)
NoteManagement.Tests.Integration  ──requires──> Api, Infrastructure, Application (WebApplicationFactory<Program> + real LocalDB)
```

**Why a health check gets a full Application/Infrastructure split even though it's trivial**: AB-1001 is the template every later controller copies. `HealthController` (Api) → `IHealthCheckService` (Application interface) → `HealthCheckService` (Application impl, depends on `IDatabaseHealthChecker` abstraction) → `DatabaseHealthChecker` (Infrastructure impl, the only place that touches `ApplicationDbContext`). This is the exact dependency-inversion shape AB-1002's auth services and AB-1004's note services will replicate — proving the pattern now, on a low-stakes endpoint, is cheaper than discovering a layering mistake once real business logic depends on it.

**DTO shape** (`Application/DTOs/Health/HealthCheckResultDto.cs`), matching `delta-openapi.yaml`'s `HealthResponse` schema exactly:
```csharp
public sealed record HealthCheckResultDto(string Status, DateTime TimestampUtc);
```

**Interfaces**:
```csharp
// Application/Interfaces/IHealthCheckService.cs
public interface IHealthCheckService
{
    Task<HealthCheckResultDto> CheckAsync(CancellationToken cancellationToken);
}

// Application/Interfaces/IDatabaseHealthChecker.cs
public interface IDatabaseHealthChecker
{
    Task<bool> CanConnectAsync(CancellationToken cancellationToken);
}
```

**HealthCheckService** throws (rather than returning a "degraded" status) when the DB is unreachable, because `delta-openapi.yaml`'s `HealthResponse.status` enum only defines `healthy` — there's no `unhealthy` contract value. An unreachable DB is therefore a `500` (Problem Details), handled by the global exception middleware, not a `200` with a different body. This keeps the implementation honest to the contract instead of inventing an undocumented response shape.

**Controller** (`Api/Controllers/HealthController.cs`): thin — binds nothing, calls `IHealthCheckService.CheckAsync`, returns `Ok(result)`. `[AllowAnonymous]`, matching `delta-openapi.yaml`'s `security: []`.

**Program.cs composition** (minimal hosting model, `net8.0`):
- `AddControllers()`, `AddEndpointsApiExplorer()`, `AddSwaggerGen()` (dev-only `UseSwagger`/`UseSwaggerUI`)
- `AddProblemDetails()` + `UseExceptionHandler()` — satisfies SDS §39's error contract globally, not just for this endpoint; every future controller inherits it for free
- `AddCors("Frontend")` restricted to the configured frontend origin (SDS §65 — no wildcard CORS), read from config (`Cors:FrontendOrigin`, defaulting to `http://localhost:5173` for local dev)
- `builder.Services.AddApplication()` / `.AddInfrastructure(builder.Configuration)` — each layer self-registers via its own `DependencyInjection.cs`, composed only in `Program.cs`
- `public partial class Program { }` at the bottom — required so `Tests.Integration` can target it via `WebApplicationFactory<Program>`

**ApplicationDbContext**: zero `DbSet<T>` properties this ticket — intentionally empty per the proposal's scope (`Users`/`Notes`/etc. start at AB-1002/AB-1004). `Infrastructure/DependencyInjection.cs` registers it via `AddDbContext` reading `ConnectionStrings:DefaultConnection`, and throws a clear `InvalidOperationException` at startup if that key is missing — fails fast instead of a confusing later EF Core error.

## 4. EF Core migration

- **Migration name**: `InitialCreate`
- **Entity changes**: none — the migration's model diff is empty (no `DbSet<T>` exists yet). Its only effect is creating the target database (if absent) and the `__EFMigrationsHistory` table.
- **Why a migration at all, if there's nothing to migrate**: the spec's "DbContext resolves at startup" scenario requires `Database.CanConnectAsync()` to succeed, which needs the target database to physically exist on LocalDB. The AGENTS.md/SDS rule is "schema changes only through migrations, never manual/ad-hoc" (`apps/api/CLAUDE.md` anti-pattern list) — so the database gets created by running `dotnet ef database update`, not by `Database.EnsureCreated()` (which would bypass the migrations history table this project will rely on from AB-1002 onward). This keeps the health-check proof and the "migrations are the only schema-change mechanism" rule consistent from day one.
- **Backward compatible**: trivially yes — it's the first migration, nothing precedes it.
- **Real commands** (correcting `apps/api/CLAUDE.md`'s current single-path example, which predates this ticket's multi-project decision):
  ```bash
  dotnet ef migrations add InitialCreate \
    --project apps/api/src/NoteManagement.Infrastructure \
    --startup-project apps/api/src/NoteManagement.Api

  dotnet ef database update \
    --project apps/api/src/NoteManagement.Infrastructure \
    --startup-project apps/api/src/NoteManagement.Api
  ```
  `apps/api/CLAUDE.md`'s `dotnet ef migrations add <Name> --project apps/api` and `dotnet ef database update --project apps/api` lines will be updated to the two-flag form above, since `apps/api` alone is no longer a single project once the layered structure exists.

## 5. Frontend

- Scaffold via `pnpm create vite@7.3.3 apps/web -- --template react-ts`, then layer in the AGENTS.md §2 folders (`components/`, `features/`, `pages/`, `hooks/`, `services/`, `stores/`, `lib/`) that the default template doesn't create.
- `pnpm dlx shadcn@4.13.1 init` for the Tailwind v4 + shadcn/ui setup (`components.json`, `@tailwindcss/vite` plugin wired into `vite.config.ts`, base CSS variables).
- One placeholder route (`pages/HomePage.tsx`) and one RTL test (`test/HomePage.test.tsx`) — proves Vite dev server, build, and the Vitest+RTL pipeline all work end-to-end, per the spec's "Frontend dev server starts" scenario.
- `vite.config.ts` includes a `test: {...}` block (environment: `jsdom`, `setupFiles: ['./src/test/setupTests.ts']`, `globals: true`) rather than a separate `vitest.config.ts` — one config file, one source of truth for both dev/build and test.
- No TanStack Query/Zustand/TipTap *usage* yet (no data to fetch, no state to hold, no editor to render) — but all four packages install and a smoke-level "does it import without crashing" check isn't warranted as a dedicated test; the real proof comes from `pnpm build` succeeding with these packages present in `package.json` and, from AB-1010 onward, actually used.

## 6. Shared package (`packages/shared`)

- `package.json` has no `"build"` script (per the raw-source decision) — `apps/web`'s `tsconfig.json` references it via TypeScript path mapping / project references (`"@repo/shared/*": ["../../packages/shared/src/*"]`), so Vite and `tsc` both resolve it straight from `.ts` source with no compiled `dist/`.
- `src/index.ts` exists but exports nothing yet — first real DTOs/Zod schemas land in AB-1002. An empty barrel file (rather than no file) gives `apps/web` a stable import target (`import {} from '@repo/shared'`) to build the pattern against immediately.
- A "build" verification still exists for CI purposes: `tsc --noEmit` against `packages/shared` (see §8) — proves the raw source at least type-checks, without needing an emitted artifact.

## 7. Root tooling

- **`package.json`**: `"private": true`, `"packageManager": "pnpm@11.24.0"`, `"engines": {"node": ">=22.14.0"}`. Scripts scoped to the Node/TS side of the workspace (the .NET side has its own documented commands in `apps/api/CLAUDE.md` — the two ecosystems are invoked separately, matching how `AGENTS.md §4` already lists them as distinct commands, not one unified script):
  - `"build": "pnpm run build:shared && pnpm run build:web"` (`build:shared` = `tsc --noEmit`, `build:web` = `vite build`)
  - `"lint": "pnpm --filter web run lint"`
  - `"test": "pnpm --filter web run test"`
  - `"test:coverage": "pnpm --filter web run test -- --coverage"`
- **`pnpm-workspace.yaml`**: `packages: ["apps/web", "packages/shared"]` — `apps/api` deliberately excluded; it's a .NET solution, not an npm package.
- **`.husky/commit-msg`**: `pnpm exec commitlint --edit "$1"`.
- **`commitlint.config.js`**: extends `@commitlint/config-conventional` (gives the `type(scope): description` shape) plus one custom rule enforcing the trailing `AB#<ticket>` — `@commitlint/config-conventional` alone has no concept of that suffix, so a small local plugin rule (`.commitlint/ab-ticket-rule.js`) regex-checks the raw message ends in `AB#\d+`. This is the concrete mechanism behind CLAUDE.md's "Enforced by Husky + commitlint" claim — without it, `feat(auth): add jwt authentication` (missing the ticket suffix) would incorrectly pass.

## 8. CI (`.github/workflows/ci.yml`)

Two independent jobs, triggered on `push` and `pull_request`:

- **`frontend`** (`runs-on: ubuntu-latest`): `pnpm install --frozen-lockfile` → `pnpm lint --max-warnings 0` → `pnpm build` → `pnpm test`. Standard, fast, no OS constraint.
- **`backend`** (`runs-on: windows-latest`): `dotnet restore` → `dotnet tool restore` (picks up the local `dotnet-ef` manifest) → `dotnet ef database update` against the runner's pre-installed LocalDB → `dotnet build` → `dotnet test --collect:"XPlat Code Coverage"`.
  - **Architecture decision — why `windows-latest`, not `ubuntu-latest` + a SQL Server container**: GitHub's `windows-latest` hosted runners ship with SQL Server Express LocalDB preinstalled. Since the resolved dev-DB decision is specifically LocalDB (not "any SQL Server"), running CI on `windows-latest` means CI uses the *exact same* database engine as local dev — zero drift, zero extra service-container YAML. The tradeoff (slower/pricier Windows runner minutes vs. Linux) is accepted for a foundation ticket; if integration-test volume later makes that cost matter, switching to an `ubuntu-latest` runner + a `mssql/server:2022-latest` Linux container service is a drop-in swap for a future ticket, not a blocker now.

## 9. MCP configuration (`.mcp.json`, repo root)

```json
{
  "mcpServers": {
    "context7": {
      "command": "npx",
      "args": ["-y", "@upstash/context7-mcp"]
    }
  }
}
```
No API key committed (SDS §64 — no secrets in source control); Context7 works unauthenticated at a lower rate limit, which is sufficient for this project's scale. A contributor who wants a higher limit sets `CONTEXT7_API_KEY` locally and passes `--api-key` themselves — not something to bake into a committed file.

## 10. Reuse of existing shared code

None exists yet — this is the foundation ticket. `packages/shared` is being *created*, not extended. Nothing in `apps/api` or `apps/web` to reuse either (both directories currently hold only their `CLAUDE.md`). Every subsequent ticket (starting AB-1002) is what actually populates and reuses this scaffold.

## 11. Checkpoint commands

Run in this order; fix and re-run on first failure before moving to the next (per `CLAUDE.md` Quality Gates):

**Frontend/shared** (after §5–§7 file changes):
```bash
pnpm install
pnpm lint --max-warnings 0
pnpm build
pnpm test
```

**Backend** (after §3–§4 file changes):
```bash
dotnet tool restore
dotnet restore apps/api/NoteManagement.sln
dotnet ef database update --project apps/api/src/NoteManagement.Infrastructure --startup-project apps/api/src/NoteManagement.Api
dotnet build apps/api/NoteManagement.sln
dotnet test apps/api/NoteManagement.sln --collect:"XPlat Code Coverage"
```

**Full-repo gate before this ticket is considered done** (mirrors `CLAUDE.md`'s required sequence, run once both sides above are green):
```bash
pnpm lint --max-warnings 0
pnpm build
pnpm test --coverage
```
(the `dotnet build`/`dotnet test` pair above stands in for the .NET half, since it isn't wrapped in a pnpm script — see §7's reasoning.)

## 12. Explicitly not doing in this ticket

- No entities, DbSets, or business migrations beyond the empty `InitialCreate`.
- No JWT/auth wiring (AB-1002).
- No real usage of TanStack Query/Zustand/TipTap beyond installing them (AB-1010+).
- No pre-commit lint/test hook — only the `commit-msg` hook CLAUDE.md explicitly requires. Adding a pre-commit gate is a reasonable future idea but wasn't asked for here.
