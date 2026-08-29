Create technical plan for: $ARGUMENTS

Steps:
1. Read: openspec/changes/$ARGUMENTS/proposal.md
2. Read: openspec/changes/$ARGUMENTS/specs/
3. Read: docs/SDS.md (backend architecture, DB schema, API contracts, ADRs relevant to this ticket)
4. Read: AGENTS.md + apps/api/CLAUDE.md (and apps/web/CLAUDE.md if this ticket touches the UI)
5. Scan existing codebase for reusable patterns (existing DbContext, existing services,
   existing DTOs/validators, existing repositories)
6. Generate plan covering:
   - Exact file paths to create/modify (Api/Controllers, Application/Services,
     Application/DTOs, Domain/Entities, Infrastructure/Data, Infrastructure/Migrations, etc.)
   - C# interfaces / DTO shapes (final shapes, matching the SDS API contracts exactly)
   - EF Core changes: entity changes, migration name, whether it's backward compatible
   - Architecture decisions with reasoning (which layer owns what, per SDS section 5)
   - Reuse of existing shared code (packages/shared, common middleware, common validators)
   - Build + test + lint checkpoint commands (see below)
7. Save to: openspec/changes/$ARGUMENTS/plan.md
8. Wait for approval before any implementation

Checkpoint commands to include in the plan:
- Backend: dotnet build, dotnet test (MSTest)
- Frontend (only if ticket touches frontend): pnpm build, pnpm lint --max-warnings 0, pnpm test (Vitest)

Format: /plan AB-1002-authentication