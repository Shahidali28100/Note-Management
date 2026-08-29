## Why

The repository currently contains only specs and tooling config (`docs/FRS.md`, `docs/SDS.md`, `AGENTS.md`, `CLAUDE.md`, `openspec/`, `.claude/`) — no application code exists yet. Per SDS §93, AB-1001 is the foundation ticket: monorepo tooling, ASP.NET Core + EF Core + SQL Server wiring, frontend scaffold, and Claude/OpenSpec/CI configuration. Every later ticket (AB-1002 → AB-1016) depends on this skeleton existing and building green.

## What Changes

- **Monorepo tooling**: root `package.json` + `pnpm-workspace.yaml` wiring `apps/web`, `apps/api` (via passthrough scripts), and `packages/shared` into `pnpm install` / `pnpm build` / `pnpm lint --max-warnings 0` / `pnpm test` (`--coverage` variant). Husky + commitlint enforcing `type(scope): description AB#ticket`.
- **Backend (`apps/api`)**: .NET 8 (LTS) solution, full layered structure — separate projects `Api`, `Application`, `Domain`, `Infrastructure`, `Tests.Unit`, `Tests.Integration` (SDS §4). ASP.NET Core Web API with Swagger/OpenAPI enabled, CORS configured for the frontend origin, structured logging. `ApplicationDbContext` (EF Core 8.0.x) registered in `Infrastructure`, targeting **SQL Server LocalDB** for local dev, connection string externalized to `appsettings.Development.json` (gitignored) with a committed `.example` template — no entities/DbSets yet, since AB-1001 introduces no FRS-mapped domain entities (those start at AB-1002/AB-1004). One scaffold-only endpoint, `GET /api/health`, proves the API boots and the DB connection resolves. `coverlet.collector` wired for MSTest coverage.
- **Frontend (`apps/web`)**: Vite + React 19 + TypeScript scaffold with TanStack Query, Zustand, TipTap, shadcn/ui, Vitest, and React Testing Library installed at pinned latest-stable versions compatible with React 19 (never `@latest`). Folder structure per AGENTS.md §2: `components/`, `features/`, `pages/`, `hooks/`, `services/`, `stores/`, `lib/`.
- **Shared package (`packages/shared`)**: TypeScript package consumed as raw source via pnpm workspace linking + TS project references — no build step. Empty placeholder structure (folders for DTOs, Zod schemas, pagination/error types) ready for AB-1002+ to populate.
- **OpenSpec**: `openspec/config.yaml` (`schema: spec-driven`) and `openspec/project.md` already exist from the initial setup commit — unchanged by this ticket.
- **Claude Code config**: `.claude/commands`, `.claude/skills`, `.claude/agents` already exist from the initial setup commit — unchanged by this ticket.
- **MCP**: add a project-level `.mcp.json` declaring the Context7 MCP server, so it's available to any Claude Code session opened in this repo (SDS §85 requires Context7 active throughout development).
- **CI**: add `.github/workflows/ci.yml` running the quality gates in order — `pnpm lint --max-warnings 0` → `pnpm build` → `pnpm test` for the Node workspaces, and `dotnet build` → `dotnet test` for `apps/api` — on push and pull request.

## Capabilities

### New Capabilities
- `project-setup`: the buildable, testable project skeleton — monorepo tooling, backend solution boot + health check, EF Core/SQL Server LocalDB wiring, frontend scaffold, shared-package consumption, CI quality-gate automation, commit-convention enforcement, and Context7 MCP availability. No business entities or endpoints beyond the scaffold health check.

### Modified Capabilities
_None — this is the first capability in the repo; no existing specs exist to modify._

## Impact

- **Affected specs**: new capability `project-setup` (`openspec/specs/project-setup/spec.md` does not exist yet — this is the first spec in the repo).
- **Affected code**: entire repo skeleton (`apps/api`, `apps/web`, `packages/shared`, root tooling, `.github/workflows`, `.mcp.json`). No business logic, no domain entities beyond the scaffold, no authentication — those begin at AB-1002.
- **API surface**: no prior API exists, so nothing breaks. Exactly one endpoint is added, `GET /api/health`, covered by this ticket's `delta-openapi.yaml`, purely to validate the scaffold.
- **Explicitly out of scope** (re-affirming AGENTS.md §11 / FRS §3 / SDS §96 for this ticket): Users/Notes/Tags/RefreshTokens/etc. entities and their EF Core configurations/migrations, JWT auth, any business endpoint, real email delivery, external search, OAuth, attachments, folders, nested notes, real-time collaboration.

## Resolved Open Technical Decisions (SDS §97, AB-1001)

| Decision | Resolution |
|---|---|
| .NET version | .NET 8 (LTS) |
| Solution/project structure | Full layered — separate `Api` / `Application` / `Domain` / `Infrastructure` / `Tests.Unit` / `Tests.Integration` projects, one `.sln` |
| SQL Server dev environment | SQL Server LocalDB |
| EF Core version | 8.0.x (matching .NET 8) |
| Frontend package versions | Pinned to latest-stable releases compatible with React 19 as of this proposal; exact numbers finalized in `plan.md` / `package.json` (never `@latest`) |
| Shared package structure | Raw TypeScript source via pnpm workspace + TS project references, no build step |
| OpenSpec configuration | Already established (`openspec/config.yaml`, `project.md`) — no change |
| Claude Code commands | Already established (`.claude/commands`, `.claude/skills`, `.claude/agents`) — no change |
| MCP configuration | Add project-level `.mcp.json` configuring the Context7 MCP server |
| CI pipeline | Included in this ticket — GitHub Actions workflow enforcing all quality gates on push/PR |
