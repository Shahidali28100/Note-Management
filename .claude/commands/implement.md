Implement: $ARGUMENTS

Before writing ONE line of code, read:
1. AGENTS.md
2. docs/FRS.md (business rules for this feature — cite requirement IDs)
3. docs/SDS.md (API contracts, DB schema, ADRs relevant to this feature)
4. Domain CLAUDE.md (backend/CLAUDE.md and/or frontend/CLAUDE.md)
5. openspec/changes/$ARGUMENTS/proposal.md
6. openspec/changes/$ARGUMENTS/plan.md
7. openspec/changes/$ARGUMENTS/tasks.md

Rules:
- Ask [y/n] before every file write
- Backend code shall use ASP.NET Core + EF Core + SQL Server only — no Prisma,
  no alternate ORMs, no alternate databases (per SDS ADR-003/ADR-004)
- Controllers stay thin — business logic goes in Application services (per SDS section 5.1)
- Domain layer shall not depend on ASP.NET Core infrastructure (per SDS section 5.3)
- After every phase: dotnet build → dotnet test (and pnpm build/lint/test if frontend touched)
- Write tests BEFORE or ALONGSIDE implementation
- Never skip a failing test
- At 60k tokens: save to session-context.md → /clear → resume
- When complete: openspec archive $ARGUMENTS

Output when done:
## Files Changed + why
## Spec Scenarios Covered (scenario → test name)
## FRS Requirements Covered (requirement ID → implementation)
## Assumptions Made
## Follow-up Tasks

Format: /implement AB-1002-authentication
