# project-setup Specification

## Purpose
Establishes the buildable, testable project skeleton — monorepo tooling, the ASP.NET Core backend solution, the React frontend scaffold, the shared TypeScript package, CI, and Claude Code / OpenSpec / MCP configuration — that every subsequent ticket (AB-1002 onward) builds on. Introduces no business entities, endpoints, or domain logic beyond a scaffold health check.

## Requirements

### Requirement: Monorepo Workspace
The system SHALL provide a pnpm workspace containing `apps/web`, `apps/api`, and `packages/shared`, installable and buildable through root-level pnpm scripts.

#### Scenario: Install and build succeed from repo root
- **WHEN** a developer runs `pnpm install` followed by `pnpm build` from the repository root
- **THEN** all workspace packages (`apps/web`, `packages/shared`) install and build successfully with zero errors

### Requirement: Backend Solution Builds
The system SHALL provide an ASP.NET Core 8 solution under `apps/api` following the layered structure (`Api`, `Application`, `Domain`, `Infrastructure`) defined in SDS §4.

#### Scenario: Backend builds and boots
- **WHEN** a developer runs `dotnet build` against the `apps/api` solution
- **THEN** the build succeeds with zero errors and the `Api` project starts and serves Swagger/OpenAPI documentation

### Requirement: Health Check Endpoint
The system SHALL expose a public, unauthenticated health-check endpoint proving the API is reachable and connected to its configured database provider.

#### Scenario: Health check responds
- **WHEN** a client sends `GET /api/health`
- **THEN** the API responds `200 OK` with a JSON body indicating the service is healthy

### Requirement: EF Core + SQL Server Wiring
The system SHALL configure an `ApplicationDbContext` using Entity Framework Core targeting SQL Server LocalDB, with connection settings externalized to configuration and never committed as plaintext secrets.

#### Scenario: DbContext resolves at startup
- **WHEN** the `apps/api` application starts in the Development environment
- **THEN** `ApplicationDbContext` successfully opens a connection to the configured SQL Server LocalDB instance without throwing

### Requirement: Frontend Scaffold
The system SHALL provide a Vite + React 19 + TypeScript application under `apps/web` with TanStack Query, Zustand, TipTap, and shadcn/ui installed, following the folder structure defined in AGENTS.md §2.

#### Scenario: Frontend dev server starts
- **WHEN** a developer runs `pnpm --filter web dev`
- **THEN** the Vite dev server starts and serves a placeholder page with zero console errors

### Requirement: Shared Package Consumption
The system SHALL provide a `packages/shared` TypeScript package consumed by `apps/web` via pnpm workspace linking and TypeScript project references, without a separate build step.

#### Scenario: Shared type import resolves
- **WHEN** `apps/web` imports a type or schema exported from `packages/shared`
- **THEN** both TypeScript compilation and the Vite dev/build process resolve the import without error

### Requirement: Quality Gate Automation
The system SHALL run the project's quality gates (lint, build, test for both frontend and backend) automatically on every push and pull request via CI.

#### Scenario: CI runs on pull request
- **WHEN** a pull request is opened against the repository
- **THEN** a CI workflow runs `pnpm lint --max-warnings 0`, `pnpm build`, `pnpm test` for the Node workspaces and `dotnet build`, `dotnet test` for `apps/api`, in that order, and blocks merge on failure

### Requirement: Git Commit Convention Enforcement
The system SHALL enforce the `type(scope): description AB#ticket` commit message convention via Husky + commitlint on every commit.

#### Scenario: Non-conforming commit is rejected
- **WHEN** a developer attempts to commit with a message that does not match the configured commitlint rule
- **THEN** the commit is rejected locally by the Husky `commit-msg` hook before it reaches source control

### Requirement: Context7 MCP Availability
The system SHALL declare the Context7 MCP server in project-level MCP configuration so it is available to any Claude Code session opened in this repository.

#### Scenario: MCP config present and valid
- **WHEN** a Claude Code session opens this repository
- **THEN** a project-level `.mcp.json` declaring the Context7 MCP server is present and loads without configuration errors
