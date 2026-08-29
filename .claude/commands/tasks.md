Break down into tasks for: $ARGUMENTS

Steps:
1. Read: openspec/changes/$ARGUMENTS/proposal.md
2. Read: openspec/changes/$ARGUMENTS/plan.md
3. Generate sequenced task checklist:
   - Phase 1: Foundation (EF Core entities/configurations, migrations, shared DTOs)
   - Phase 2: Core implementation [mark PARALLEL tasks — e.g. service logic vs. controller wiring]
   - Phase 3: Integration (wire controller → service → repository → DbContext, middleware, auth)
   - Phase 4: Tests (one MSTest test per spec scenario; one Vitest/RTL test per frontend
     scenario if the ticket touches the UI)
   - Checkpoint after each phase:
     * dotnet build → 0 errors
     * dotnet test → all green
     * (frontend tickets only) pnpm build / pnpm lint --max-warnings 0 / pnpm test
4. Save to: openspec/changes/$ARGUMENTS/tasks.md
5. Wait for approval

Format: /tasks AB-1002-authentication
