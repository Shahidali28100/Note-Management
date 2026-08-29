Review implementation for: $ARGUMENTS

Read-only mode — do NOT modify any files.

Steps:
1. Read: openspec/changes/archive/$ARGUMENTS/
2. Read: docs/FRS.md (original requirements) and docs/SDS.md (contracts/ADRs)
3. Compare implementation against spec scenarios AND FRS criteria, including:
   - Server-side authorization enforced (SDS section 95, item 1–2)
   - Passwords never stored in plaintext
   - Refresh tokens / OTPs protected
   - Share tokens unguessable
   - SQL injection prevention (parameterized EF Core queries only)
   - Search highlighting is XSS-safe
   - Soft-deleted notes excluded from listings/search
   - IDOR / horizontal privilege escalation prevented on user-owned resources
4. Output:
   ✅ Implemented: [scenario]
   ❌ Missing: [scenario]
   ⚠️ Drifted: [scenario — spec says X, code does Y]
   🔒 Security: [concern]
   📋 FRS gap: [requirement not addressed]
5. No style feedback — compliance only

Format: /review AB-1002-authentication
