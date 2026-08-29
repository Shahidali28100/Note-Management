Prepare PR for: $ARGUMENTS

Steps:
1. Run:
   dotnet build
   dotnet test
   pnpm lint --max-warnings 0   (skip if this ticket has no frontend changes)
   pnpm test                    (skip if this ticket has no frontend changes)
   npx commitlint --from HEAD~1
   (Fix any failures before proceeding)
2. Run: git diff main --stat
3. Read: openspec/archive/$ARGUMENTS/proposal.md
4. Generate commit:
   feat(scope): description AB#ticket
   - bullet 1
   - bullet 2
   Relates to AB#XXXX
5. Ask: "Run git add . && git commit? [y/n]"
6. Generate PR description:
   ## What
   ## FRS Requirements Covered
   ## Spec Artifacts
   ## Checklist
   ## Test Coverage
7. Ask: "Run git push? [y/n]"

Format: /pr AB-1002-authentication
