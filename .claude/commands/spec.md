Run OpenSpec proposal creation for: $ARGUMENTS

Steps:
1. Run: openspec changes list
2. Read: openspec/specs/ (current system state)
3. Read: docs/FRS.md → find the relevant requirement IDs for this ticket
   (e.g. FRS-AUTH-*, FRS-NOTE-*, FRS-TAG-*, FRS-SEARCH-*, FRS-SHARE-*, FRS-VERSION-*, FRS-FE-*)
4. Read: docs/SDS.md → find the relevant design decisions for this ticket
   (DB schema section, API contract section, ADRs, "Open Technical Decisions" section for this ticket)
5. Read: AGENTS.md (constraints — ASP.NET Core / EF Core / SQL Server only, no Prisma,
   no external search, no OAuth, no attachments, no real-time collab)
6. Ask clarifying questions — minimum 3, maximum 8. Clarifying questions should resolve
   anything the SDS marks as "Open Technical Decisions" for this ticket, plus any FRS
   ambiguity (e.g. exact validation rules, exact error codes).
7. Run: openspec proposal $ARGUMENTS
8. Show generated proposal.md and spec delta
9. Do NOT proceed to implementation

Format: /spec AB-1002-authentication
