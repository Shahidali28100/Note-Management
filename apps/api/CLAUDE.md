# CLAUDE.md — apps/api

Scoped to the ASP.NET Core backend. Inherits root `AGENTS.md`/`CLAUDE.md` — this file adds only backend-local specifics.

## Commands

```bash
dotnet build                                   # build the API project
dotnet run --project apps/api                  # run dev server
dotnet test                                    # run MSTest unit + integration tests
dotnet test --collect:"XPlat Code Coverage"    # with coverage
dotnet ef migrations add <Name> --project apps/api
dotnet ef database update --project apps/api
dotnet format                                  # apply formatting before commit
```

## Framework Patterns

- Follow the layered flow: `Controller → Application Service → Domain → EF Core (Infrastructure) → SQL Server`. Controllers only bind requests, call one service method, and map the result to an HTTP response.
- One `ApplicationDbContext` exposes entities via `DbSet<T>`; never query `SqlConnection` directly outside Infrastructure.
- Entity configuration lives in `IEntityTypeConfiguration<T>` classes (e.g. `NoteConfiguration`), never inline in the entity class.
- Wrap multi-write operations in an explicit EF Core transaction: note-save+version-snapshot, version-restore+new-version, auth writes, share-link updates.
- Full-text search and the atomic `ViewCount` increment use parameterized EF Core SQL when LINQ can't express them — never string-built SQL.
- New tables/columns arrive only through an EF Core migration, committed alongside the code that needs them.
- DTOs (Application layer) are the only shapes that cross the controller boundary — never return EF entities from an endpoint.

## Anti-Patterns

- No Prisma, no raw ADO.NET/Dapper as a replacement for EF Core.
- No business logic inside controllers or entity configuration classes.
- No string-concatenated SQL, anywhere, for any reason.
- No read-modify-write on `ShareLinks.ViewCount` — atomic update only.
- No returning `PasswordHash`, raw refresh tokens, or raw share tokens from any endpoint or log line.
- No skipping the transaction on note-save+version-snapshot, even for "just a small edit."
- No manual/ad-hoc schema edits against the database — migrations only.
