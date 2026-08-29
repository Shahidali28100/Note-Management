# Software Design Specification (SDS)

**Project:** Note Taking Application
**Document:** Software Design Specification
**Version:** 1.0
**Status:** Draft
**Date:** 2026-08-26

---

# 1. Purpose

This document defines the technical architecture, database design, API contracts, security requirements, testing strategy, development workflow, and implementation standards for the Note Taking Application.

The application is a full-stack web application consisting of:

* React 19 frontend
* TypeScript
* Vite
* TanStack Query
* Zustand
* TipTap
* shadcn/ui
* ASP.NET Core MVC/Web API backend
* Entity Framework Core
* SQL Server
* JWT authentication
* SQL Server Full-Text Search
* MSTest
* Vitest
* React Testing Library
* Playwright
* pnpm workspaces

The backend is primarily a **.NET application**.

All backend persistence and database operations shall use:

> **Entity Framework Core + SQL Server**

Prisma is not part of the backend architecture.

---

# 2. Technology Stack

| Layer               | Technology                       |
| ------------------- | -------------------------------- |
| Frontend            | React 19                         |
| Frontend Language   | TypeScript                       |
| Frontend Build Tool | Vite                             |
| Server State        | TanStack Query                   |
| Client State        | Zustand                          |
| Rich Text Editor    | TipTap                           |
| UI Components       | shadcn/ui                        |
| Backend             | ASP.NET Core MVC / Web API       |
| Backend Language    | C#                               |
| ORM / Data Access   | Entity Framework Core            |
| Database            | Microsoft SQL Server             |
| Authentication      | JWT Access Token + Refresh Token |
| Search              | SQL Server Full-Text Search      |
| Backend Testing     | MSTest                           |
| Frontend Testing    | Vitest + React Testing Library   |
| E2E Testing         | Playwright                       |
| Monorepo            | pnpm workspaces                  |
| API Documentation   | OpenAPI / Swagger                |
| Git Hooks           | Husky                            |
| Commit Validation   | commitlint                       |
| Specification       | OpenSpec                         |
| AI Development      | Claude Code + Context7 MCP       |

---

# 3. Architecture Overview

The application shall follow a layered full-stack architecture.

```text
┌──────────────────────────────────────────────┐
│                  Browser                     │
│                                              │
│ React 19 + TypeScript                        │
│ TanStack Query + Zustand                     │
│ TipTap + shadcn/ui                           │
└──────────────────────┬───────────────────────┘
                       │
                       │ HTTPS / REST
                       ▼
┌──────────────────────────────────────────────┐
│             ASP.NET Core API                 │
│                                              │
│ Controllers                                  │
│     ↓                                        │
│ Application Services                         │
│     ↓                                        │
│ Business / Domain Logic                      │
│     ↓                                        │
│ Entity Framework Core                        │
│     ↓                                        │
│ SQL Server                                   │
└──────────────────────────────────────────────┘
```

The frontend shall communicate with the backend through REST APIs.

The backend shall be responsible for:

* Authentication
* Authorization
* Business rules
* Validation
* CRUD operations
* Search
* Sharing
* Version management
* Database access
* API contracts

The frontend shall be responsible for:

* User interface
* Client-side state
* API communication
* Form handling
* Rich-text editing
* Autosave experience
* Displaying validation and API errors

---

# 4. Backend Architecture

The backend shall use ASP.NET Core MVC/Web API.

The application shall follow separation of concerns.

Recommended logical structure:

```text
src/
├── Api/
│   ├── Controllers/
│   ├── Middleware/
│   ├── Filters/
│   └── Configuration/
│
├── Application/
│   ├── Services/
│   ├── DTOs/
│   ├── Validators/
│   └── Interfaces/
│
├── Domain/
│   ├── Entities/
│   ├── Enums/
│   └── Interfaces/
│
├── Infrastructure/
│   ├── Data/
│   ├── Configurations/
│   ├── Repositories/
│   └── Migrations/
│
└── Tests/
```

The exact physical project structure shall be finalized during AB-1001.

---

# 5. Backend Layer Responsibilities

## 5.1 API Layer

Responsible for:

* HTTP request handling
* Model binding
* Authentication/authorization integration
* Calling application services
* Returning appropriate HTTP responses

Controllers shall remain thin.

Business logic shall not be unnecessarily placed directly inside controllers.

---

## 5.2 Application Layer

Responsible for:

* Use cases
* Business workflows
* Validation coordination
* Transaction coordination
* Calling persistence abstractions
* Mapping between domain entities and DTOs

---

## 5.3 Domain Layer

Responsible for:

* Core entities
* Domain rules
* Domain-specific abstractions

The domain layer shall not depend on ASP.NET Core infrastructure.

---

## 5.4 Infrastructure Layer

Responsible for:

* EF Core
* SQL Server
* DbContext
* Entity configurations
* Repositories where required
* Database migrations
* Persistence-specific implementations

---

# 6. Entity Framework Core

**Entity Framework Core is the primary ORM and database-access technology.**

All normal backend database operations shall use EF Core.

EF Core shall be responsible for:

* Entity mapping
* Relationships
* LINQ queries
* Insert operations
* Update operations
* Transactions
* Database migrations
* Change tracking
* Concurrency handling where required

The application shall use an `ApplicationDbContext` or equivalent DbContext.

Example:

```text
ApplicationDbContext
```

The DbContext shall expose the application's entities through `DbSet<T>` properties.

---

# 7. SQL Server

Microsoft SQL Server is the authoritative application database.

All persistent application data shall be stored in SQL Server.

SQL Server shall provide:

* Relational data storage
* Foreign-key relationships
* Unique constraints
* Indexes
* Transactions
* Full-Text Search
* Atomic database operations
* Data integrity

Database schema changes shall be managed through EF Core migrations.

---

# 8. Prisma

**Prisma shall not be used.**

Although the original AB-1001 description mentions Prisma, the actual backend stack is:

```text
ASP.NET Core
      ↓
Entity Framework Core
      ↓
SQL Server
```

There shall be:

* No Prisma Client
* No Prisma schema
* No Prisma migrations
* No Prisma database access layer

The authoritative persistence workflow is:

```text
C# Entities
     ↓
EF Core Configuration
     ↓
EF Core Migrations
     ↓
SQL Server
```

---

# 9. Database Design

The initial database shall contain these logical entities:

```text
Users
RefreshTokens
PasswordResetOtps
Notes
Tags
NoteTags
ShareLinks
NoteVersions
```

Additional entities shall only be introduced through an approved specification change.

---

# 10. Users

Logical schema:

```text
Users
-----
Id
Name
Email
PasswordHash
CreatedAt
UpdatedAt
```

Requirements:

* `Id` shall be the primary key.
* `Email` shall be unique.
* `PasswordHash` shall never contain plaintext passwords.
* Timestamps shall be stored in UTC.

---

# 11. RefreshTokens

Logical schema:

```text
RefreshTokens
-------------
Id
UserId
TokenHash
ExpiresAt
RevokedAt
CreatedAt
```

Relationship:

```text
User
  │
  └───< RefreshTokens
```

Requirements:

* Every refresh token belongs to one user.
* Expired tokens shall not be accepted.
* Revoked tokens shall not be accepted.
* Tokens shall be generated using cryptographically secure randomness.
* Token hashes should be stored instead of raw refresh tokens.

---

# 12. PasswordResetOtps

Logical schema:

```text
PasswordResetOtps
-----------------
Id
UserId
OtpHash
ExpiresAt
UsedAt
CreatedAt
```

Requirements:

* OTPs shall be time-limited.
* OTPs shall be single-use.
* Used OTPs shall not be accepted.
* OTPs should be stored as hashes.
* Actual OTP delivery shall be simulated through application logging.

No actual email provider shall be integrated.

---

# 13. Notes

Logical schema:

```text
Notes
-----
Id
UserId
Title
Content
CreatedAt
UpdatedAt
DeletedAt
```

Relationship:

```text
User
  │
  └───< Notes
```

Requirements:

* Every note belongs to exactly one user.
* Notes support soft deletion.
* `DeletedAt = NULL` means active.
* `DeletedAt != NULL` means deleted.
* Normal note queries shall exclude deleted notes.

---

# 14. Soft Delete

Notes shall use timestamp-based soft deletion.

Delete operation:

```text
DeletedAt = current UTC timestamp
```

The application shall not physically delete a note during the 30-day recovery window.

Normal queries shall exclude:

```text
DeletedAt IS NOT NULL
```

An EF Core global query filter may be used where appropriate.

Recovery shall set:

```text
DeletedAt = NULL
```

Permanent deletion after the recovery window shall be handled by an approved retention/purge process.

---

# 15. Tags

Logical schema:

```text
Tags
----
Id
UserId
Name
Color
CreatedAt
UpdatedAt
```

Relationship:

```text
User
  │
  └───< Tags
```

Requirements:

* Tags are user-scoped.
* Different users may have tags with the same name.
* A user shall not have duplicate tag names.
* Tags support a color value.

A unique constraint shall be applied to the appropriate user/name combination.

---

# 16. NoteTags

Notes and tags have a many-to-many relationship.

```text
Notes ────────< NoteTags >──────── Tags
```

Logical schema:

```text
NoteTags
--------
NoteId
TagId
```

Composite primary key:

```text
(NoteId, TagId)
```

Foreign keys:

```text
NoteId → Notes.Id
TagId  → Tags.Id
```

The backend shall ensure that a user cannot associate a note with another user's tag.

---

# 17. ShareLinks

Logical schema:

```text
ShareLinks
----------
Id
NoteId
TokenHash
ExpiresAt
RevokedAt
ViewCount
CreatedAt
```

Relationship:

```text
Note
  │
  └───< ShareLinks
```

Requirements:

* Each share link belongs to one note.
* Share tokens shall be cryptographically secure.
* Share tokens shall be unguessable.
* Token hashes should be stored instead of raw tokens.
* Links support expiration.
* Links support revocation.
* View count shall be updated atomically.

---

# 18. NoteVersions

Logical schema:

```text
NoteVersions
------------
Id
NoteId
VersionNumber
Title
Content
CreatedAt
```

Relationship:

```text
Note
  │
  └───< NoteVersions
```

Requirements:

* Versions are immutable.
* Every successful note save creates a snapshot.
* Version numbers are sequential per note.
* `(NoteId, VersionNumber)` shall be unique.

---

# 19. Entity Relationships

The primary database relationships are:

```text
Users
 │
 ├──────────< Notes
 │              │
 │              ├──────────< NoteVersions
 │              │
 │              ├──────────< ShareLinks
 │              │
 │              └──────────< NoteTags >────────── Tags
 │
 ├──────────< RefreshTokens
 │
 └──────────< PasswordResetOtps
```

---

# 20. EF Core Entity Configuration

Entity configuration shall preferably be separated from entity classes.

Examples:

```text
UserConfiguration
RefreshTokenConfiguration
PasswordResetOtpConfiguration
NoteConfiguration
TagConfiguration
NoteTagConfiguration
ShareLinkConfiguration
NoteVersionConfiguration
```

Configuration shall define:

* Primary keys
* Foreign keys
* Required fields
* Maximum lengths
* Unique constraints
* Indexes
* Relationships
* Delete behaviors
* Query filters where required

---

# 21. Database Indexes

Indexes shall be created for frequently queried fields.

Potential indexes include:

```text
Users.Email

Notes.UserId
Notes.DeletedAt
Notes.UpdatedAt

Tags.UserId
Tags.UserId + Tags.Name

NoteTags.NoteId
NoteTags.TagId

ShareLinks.TokenHash
ShareLinks.NoteId
ShareLinks.ExpiresAt

NoteVersions.NoteId
NoteVersions.NoteId + VersionNumber
```

The final indexes shall be validated against actual query patterns.

---

# 22. EF Core Migrations

Database schema changes shall be managed through EF Core migrations.

Migrations shall be committed to source control.

Workflow:

```text
Entity / Configuration Change
          ↓
EF Core Migration
          ↓
Migration Review
          ↓
Database Update
```

Manual database schema changes shall not be the normal development mechanism.

---

# 23. Transactions

EF Core transactions shall be used when multiple database operations must succeed or fail together.

Important transactional operations include:

* Note update + version creation
* Version restore + new version creation
* Authentication operations requiring multiple writes
* Share operations requiring multiple updates

---

# 24. Note Save Transaction

Saving a note and creating its version snapshot shall be one logical operation.

```text
Begin Transaction
       ↓
Update Note
       ↓
Create NoteVersion
       ↓
Commit
```

If version creation fails, the note update shall not be committed.

---

# 25. Version Restore Transaction

Restoring a version shall create a new version.

Example:

```text
Version 1
Version 2
Version 3
```

Restoring Version 1 produces:

```text
Version 1
Version 2
Version 3
Version 4 ← content restored from Version 1
```

Existing versions remain unchanged.

---

# 26. Authentication Architecture

Authentication shall use:

```text
JWT Access Token
+
Database-backed Refresh Token
```

Access token lifetime:

```text
15 minutes
```

Refresh token lifetime:

```text
7 days
```

---

# 27. JWT Access Token

The JWT shall identify the authenticated user.

At minimum:

```text
sub = UserId
```

The backend shall validate:

* Signature
* Expiration
* Issuer where configured
* Audience where configured
* Required claims

JWT secrets shall not be committed to source control.

---

# 28. Refresh Token

Refresh tokens shall:

* Be cryptographically random.
* Be associated with a user.
* Be persisted in SQL Server.
* Expire after 7 days.
* Support revocation.
* Be rejected after expiration.
* Be rejected after logout.

The exact token rotation policy shall be defined in AB-1002.

---

# 29. Authorization

Authentication identifies the user.

Authorization determines whether the user can access a resource.

For private notes:

```text
Authenticated User ID
        =
Note.UserId
```

must be true.

The backend shall enforce resource ownership.

The frontend shall never be treated as an authorization boundary.

---

# 30. Public Sharing Authorization

Public shared-note access does not require authentication.

Authorization is based on the share token.

A share link is valid only when:

```text
Token is valid
AND
Token is not revoked
AND
Token is not expired
AND
Underlying note is accessible
```

The exact behavior for deleted notes shall be defined in AB-1008.

---

# 31. API Architecture

The backend shall expose REST APIs.

Base path:

```text
/api
```

Controllers shall be responsible for:

* HTTP concerns
* Request binding
* Authentication/authorization
* Calling application services
* Returning HTTP responses

Controllers shall not contain unnecessary business logic.

---

# 32. Authentication APIs

```text
POST /api/auth/register
POST /api/auth/login
POST /api/auth/refresh
POST /api/auth/logout
POST /api/auth/forgot-password
POST /api/auth/reset-password
```

Exact request/response schemas shall be defined by AB-1002 and AB-1003 OpenSpec changes.

---

# 33. Notes APIs

```text
GET    /api/notes
POST   /api/notes
GET    /api/notes/{id}
PUT    /api/notes/{id}
DELETE /api/notes/{id}
POST   /api/notes/{id}/restore
```

All private note endpoints require authentication.

---

# 34. Tags APIs

```text
GET    /api/tags
POST   /api/tags
PUT    /api/tags/{id}
DELETE /api/tags/{id}
```

All tag-management endpoints require authentication.

---

# 35. Search API

```text
GET /api/search
```

The endpoint shall support:

* Search keywords
* Pagination
* User scoping

Deleted notes shall not appear in search results.

---

# 36. Sharing APIs

```text
POST   /api/notes/{id}/shares
GET    /api/notes/{id}/shares
DELETE /api/notes/{id}/shares/{shareId}

GET /api/shared/{token}
```

The public endpoint does not require authentication.

Share-management endpoints require authentication and ownership validation.

---

# 37. Version APIs

```text
GET  /api/notes/{id}/versions
GET  /api/notes/{id}/versions/{versionId}
POST /api/notes/{id}/versions/{versionId}/restore
```

Version endpoints require authentication and note ownership.

---

# 38. HTTP Status Codes

The API shall follow the approved OpenAPI contract.

Default mappings:

| Scenario                | HTTP Status |
| ----------------------- | ----------: |
| Successful GET          |         200 |
| Successful POST         |         201 |
| Successful PUT          |         200 |
| Successful DELETE       |         204 |
| Invalid request         |         400 |
| Authentication required |         401 |
| Access denied           |         403 |
| Resource not found      |         404 |
| Resource conflict       |         409 |
| Unexpected server error |         500 |

Ticket-specific OpenSpec contracts take precedence.

---

# 39. API Error Contract

The API shall use a consistent error response.

ASP.NET Core Problem Details is recommended.

Example:

```json
{
  "type": "https://example.com/errors/validation",
  "title": "Validation failed",
  "status": 400,
  "detail": "One or more validation errors occurred."
}
```

The final schema shall be defined in OpenAPI.

---

# 40. Pagination

Paginated endpoints shall support:

```text
page
pageSize
```

The backend shall enforce a maximum page size.

Example:

```json
{
  "items": [],
  "page": 1,
  "pageSize": 20,
  "totalCount": 100,
  "totalPages": 5
}
```

Exact response structures shall be defined by the corresponding ticket specification.

---

# 41. Sorting

Sorting shall use an explicit allowlist.

Example:

```text
sortBy=updatedAt
sortDirection=desc
```

User-provided sorting values shall never be directly concatenated into SQL.

---

# 42. Tag Filtering

Notes may be filtered by tag.

The backend shall verify that the supplied tag belongs to the authenticated user.

User A shall never be able to use User B's tag to access or discover User B's notes.

---

# 43. SQL Server Full-Text Search

SQL Server Full-Text Search shall be used for note searching.

No external search service shall be introduced.

Architecture:

```text
Search Request
      ↓
ASP.NET Core API
      ↓
Application Service
      ↓
EF Core / Parameterized SQL
      ↓
SQL Server Full-Text Search
      ↓
User Ownership Filter
      ↓
DeletedAt Filter
      ↓
Pagination
      ↓
API Response
```

If EF Core cannot directly express the required Full-Text Search query, parameterized SQL may be executed through EF Core.

Unsafe SQL string concatenation is prohibited.

---

# 44. Search Highlighting

Search results shall provide enough information for the frontend to display matching keywords.

Highlighting shall be generated safely.

Search input shall be treated as untrusted data.

The frontend shall not blindly inject arbitrary HTML returned by the API.

---

# 45. Share Link Generation

A share token shall be generated using a cryptographically secure random generator.

Logical flow:

```text
Generate Secure Token
        ↓
Hash Token
        ↓
Store Hash in SQL Server
        ↓
Return Share Link to Owner
```

The raw share token should not be permanently stored in the database.

---

# 46. Share Link Expiration

A share link becomes invalid when:

```text
Current UTC time >= ExpiresAt
```

or:

```text
RevokedAt IS NOT NULL
```

The exact public endpoint response shall be defined in AB-1008.

---

# 47. Atomic Share View Count

Share-link view count shall be incremented atomically.

The implementation shall avoid an unsafe read-modify-write pattern:

```text
SELECT ViewCount
UPDATE ViewCount
```

Instead, SQL Server shall perform an atomic operation equivalent to:

```text
ViewCount = ViewCount + 1
```

Concurrent requests shall not cause lost increments.

---

# 48. Note Versioning

A version snapshot shall preserve:

```text
Title
Content
VersionNumber
CreatedAt
```

Versions are immutable.

Every successful save creates a version according to the approved versioning specification.

---

# 49. Automatic Version Purging

Old versions shall be purged according to the retention policy defined in AB-1009.

The purge operation shall:

* Be safe to execute repeatedly.
* Preserve the current note content.
* Respect the configured retention policy.
* Avoid corrupting version numbering.

The exact retention period shall be defined in AB-1009.

---

# 50. Frontend Architecture

The frontend shall use:

```text
React 19
TypeScript
Vite
TanStack Query
Zustand
TipTap
shadcn/ui
```

Recommended logical structure:

```text
apps/web/
├── src/
│   ├── components/
│   ├── features/
│   ├── pages/
│   ├── hooks/
│   ├── services/
│   ├── stores/
│   ├── lib/
│   └── ...
```

The exact structure shall be finalized during frontend tickets.

---

# 51. TanStack Query

TanStack Query shall manage server state including:

* Notes
* Tags
* Search results
* Share links
* Versions
* Authentication-related server requests

Mutations shall invalidate or update relevant queries.

---

# 52. Zustand

Zustand shall manage client-side state where appropriate.

Examples:

* Modal state
* Editor UI state
* Temporary client preferences
* UI-specific state

Server state shall not unnecessarily be duplicated in Zustand.

---

# 53. TipTap

TipTap shall provide the note editing experience.

The final persistence format shall be explicitly defined in AB-1012.

The frontend and backend shall use a single canonical content representation.

---

# 54. Autosave

The editor shall implement debounced autosave.

The application shall not send an API request for every keystroke.

Logical flow:

```text
User edits
    ↓
Debounce
    ↓
Save Request
    ↓
Backend Validation
    ↓
Database Transaction
    ↓
Note Updated
    ↓
Version Created
```

The exact debounce interval shall be defined in AB-1012.

---

# 55. Shared TypeScript Package

All shared TypeScript types and Zod schemas shall reside exclusively in:

```text
packages/shared
```

The frontend shall import shared types instead of duplicating them.

Examples:

```text
Auth DTOs
Note DTOs
Tag DTOs
Search DTOs
Share DTOs
Version DTOs
Pagination types
API error types
```

---

# 56. Frontend Validation

Frontend validation shall provide immediate user feedback.

However, frontend validation shall never replace backend validation.

Shared Zod schemas shall be stored in:

```text
packages/shared
```

---

# 57. Backend Validation

The backend shall validate every externally supplied request.

Validation shall cover:

* Required fields
* String lengths
* Email format
* Password requirements
* Tag values
* Pagination limits
* Sorting values
* Resource identifiers
* Share expiry
* OTP values

---

# 58. Data Ownership

All user-owned resources shall be scoped to the authenticated user.

Ownership applies to:

* Notes
* Tags
* Share links
* Versions
* Refresh tokens
* Password reset records

The backend shall prevent ID-based horizontal privilege escalation.

---

# 59. SQL Injection Protection

EF Core parameterization shall be the default database-access mechanism.

When raw SQL is necessary, parameterized EF Core APIs shall be used.

Unsafe SQL construction is prohibited.

This is especially important for:

* Search
* Sorting
* Filtering
* Public share-token lookup

---

# 60. XSS Protection

Note content shall be considered untrusted.

Search-highlight content shall be considered untrusted.

The frontend shall safely render rich-text content.

Publicly accessible notes shall receive the same content-safety treatment as authenticated notes.

---

# 61. Password Security

Passwords shall never be stored as plaintext.

A secure password hashing mechanism appropriate for ASP.NET Core shall be used.

Password verification shall happen server-side.

Passwords shall never be returned in API responses.

---

# 62. OTP Security

OTP requirements shall include:

* Expiration
* Single-use behavior
* Secure storage
* Attempt protection where required

For this assignment, OTP delivery shall be simulated through application logging.

No external email provider shall be implemented.

---

# 63. Logging

The backend shall use structured application logging.

Logs shall not expose:

* Passwords
* Password hashes
* Refresh tokens
* Share tokens
* Authentication credentials

The assignment explicitly allows password-reset OTPs to be logged for development/testing.

---

# 64. Configuration and Secrets

Application configuration shall be externalized.

Sensitive values shall not be committed to source control.

Examples:

```text
Database connection string
JWT signing key
JWT issuer
JWT audience
```

Environment-specific configuration shall be supported.

---

# 65. CORS

The backend shall configure CORS for approved frontend origins.

Production-equivalent environments shall not use unrestricted wildcard CORS unless explicitly required.

---

# 66. API Documentation

The backend shall expose OpenAPI/Swagger documentation.

The OpenAPI contract shall serve as the API contract for ticket-level specification changes.

Every ticket that modifies an API shall provide the corresponding:

```text
delta-openapi.yaml
```

before implementation.

---

# 67. Testing Strategy

Testing shall be divided into four primary layers:

```text
┌─────────────────────────────┐
│       Playwright E2E        │
│ Complete browser journeys   │
└──────────────┬──────────────┘
               │
┌──────────────▼──────────────┐
│      API Integration        │
│          MSTest             │
└──────────────┬──────────────┘
               │
┌──────────────▼──────────────┐
│ Frontend Unit/Component     │
│ Vitest + React Testing Lib  │
└──────────────┬──────────────┘
               │
┌──────────────▼──────────────┐
│      Backend Unit Tests     │
│           MSTest            │
└─────────────────────────────┘
```

---

# 68. Backend Unit Testing — MSTest

MSTest shall be used for backend unit tests.

Tests shall cover:

* Business logic
* Validation
* Authentication
* Authorization
* Token behavior
* Note operations
* Tag operations
* Search logic
* Sharing logic
* Version logic
* Error scenarios

Unit tests shall avoid unnecessary infrastructure dependencies.

---

# 69. Backend Integration Testing — MSTest

MSTest shall also be used for backend integration tests.

Integration tests shall verify interaction between:

```text
ASP.NET Core
      ↓
EF Core
      ↓
SQL Server
```

Important integration scenarios include:

* Database constraints
* Soft delete
* Pagination
* Sorting
* Tag filtering
* Full-Text Search
* Atomic view count
* Version creation
* Version restoration
* Authorization

---

# 70. Frontend Testing — Vitest

Vitest shall be the primary frontend test runner.

Vitest shall be used for:

* Unit tests
* Hook tests
* Utility tests
* Mocking
* Assertions
* Coverage
* Component test execution

Frontend tests shall be runnable through the repository's pnpm scripts.

---

# 71. React Testing Library

React Testing Library shall be used with Vitest for React component testing.

Tests shall focus on user-visible behavior rather than implementation details.

Tests should interact with components through:

* Accessible roles
* Labels
* Visible text
* User interactions
* Form controls

Tests should avoid testing internal implementation details such as:

* Private component state
* Internal function calls
* Component implementation structure

---

# 72. Frontend Unit and Component Test Coverage

Frontend tests shall cover:

### Authentication

* Registration form
* Login form
* Forgot-password form
* OTP reset flow
* Validation
* Loading states
* Error states

### Notes

* Note list rendering
* Pagination
* Sorting
* Tag filtering
* Create-note flow
* Delete-note behavior

### Editor

* TipTap editor rendering
* User input
* Save behavior
* Autosave behavior
* Save failure handling

### Search

* Search input
* Search results
* Highlighting
* Pagination
* Empty results
* Error states

### Sharing

* Share modal
* Generate link
* Active links
* Expiry display
* View count
* Revoke behavior

### Versions

* Version drawer
* Version listing
* Version viewing
* Restore action
* Restore success/error states

---

# 73. Frontend Test Mocking

Frontend API calls shall be mocked in unit/component tests.

Tests shall not require the real backend for normal component tests.

The mocking strategy shall be finalized during AB-1010/AB-1011 based on the selected frontend testing architecture.

---

# 74. Playwright E2E

Playwright shall be used for complete browser-level user journeys.

The E2E test shall verify the integration of:

```text
Frontend
   ↓
ASP.NET Core API
   ↓
SQL Server
```

where applicable.

---

# 75. E2E User Journey

The primary E2E journey shall be:

```text
Register
   ↓
Login
   ↓
Create Note
   ↓
Add Tag
   ↓
Edit Note
   ↓
Autosave
   ↓
Search
   ↓
View Highlighted Result
   ↓
Generate Share Link
   ↓
Open Public Link
   ↓
Verify View Count
   ↓
Edit Note
   ↓
View Version History
   ↓
Restore Version
   ↓
Verify Restored Content
   ↓
Logout
```

---

# 76. Test Naming

Every approved specification scenario shall have exactly one named test.

Test names shall clearly describe behavior.

Examples:

```text
Register_WithValidData_CreatesUser
Login_WithInvalidPassword_ReturnsUnauthorized
DeleteNote_SetsDeletedAt
Search_ReturnsOnlyCurrentUsersNotes
CreateShareLink_IncrementsViewCountAtomically
RestoreVersion_CreatesNewVersion
```

Frontend examples:

```text
LoginForm_SubmitsValidCredentials
NoteList_DisplaysPagination
SearchResults_HighlightMatchingText
ShareModal_RevokesActiveLink
VersionDrawer_RestoresSelectedVersion
```

---

# 77. Coverage Requirement

Every ticket shall achieve:

```text
≥ 80% coverage on new code
```

Coverage shall be meaningful.

Trivial tests shall not be used to artificially inflate coverage.

The repository's coverage configuration shall define the exact included/excluded paths.

---

# 78. Build and Quality Gates

At every phase checkpoint:

```bash
pnpm build
pnpm lint --max-warnings 0
pnpm test
```

Before ticket completion:

```bash
pnpm test --coverage
```

All commands must pass.

A failing checkpoint blocks progression.

---

# 79. Package Version Pinning

All tool and package versions shall be pinned in `package.json`.

The repository shall not use:

```text
@latest
```

in installation commands.

Example:

```bash
pnpm add package-name@1.2.3
```

rather than:

```bash
pnpm add package-name@latest
```

---

# 80. Monorepo

The project shall use pnpm workspaces.

Logical structure:

```text
/
├── apps/
│   ├── web/
│   └── api/
│
├── packages/
│   └── shared/
│
├── openspec/
├── .claude/
├── AGENTS.md
├── CLAUDE.md
├── FRS.md
├── SDS.md
├── package.json
└── pnpm-workspace.yaml
```

The .NET backend may use its native solution/project structure inside `apps/api`.

---

# 81. Shared Package Responsibility

`packages/shared` shall contain shared frontend TypeScript contracts.

Examples:

```text
Types
DTOs
Zod Schemas
API Response Types
Pagination Types
```

The frontend shall not duplicate shared API models.

The backend shall maintain the authoritative C# implementation of its API contracts.

---

# 82. Specification-Driven Development

Every ticket shall follow:

```text
/spec AB-xxxx
      ↓
Human Review
      ↓
delta-openapi.yaml Approval
      ↓
/plan AB-xxxx
      ↓
Human Review
      ↓
/tasks AB-xxxx
      ↓
Human Review
      ↓
/implement AB-xxxx
      ↓
Validation
      ↓
Fresh Terminal /review AB-xxxx
      ↓
openspec archive AB-xxxx
      ↓
/pr AB-xxxx
```

Implementation shall not begin before the required specification approvals.

---

# 83. Ticket Isolation

Only one ticket shall be implemented in a Claude Code session.

After completing a ticket:

```text
/clear
```

shall be executed before starting another ticket.

---

# 84. Context Management

When context usage reaches approximately 70%:

```text
/compact
```

shall be used.

The workflow shall not wait until context is exhausted.

Tasks estimated above 45 minutes shall be delegated to a subagent according to the assignment rules.

---

# 85. Context7 MCP

Context7 MCP shall remain active throughout development.

Library APIs shall be verified against current documentation before implementation.

This applies to:

* ASP.NET Core
* Entity Framework Core
* React
* TanStack Query
* Zustand
* TipTap
* shadcn/ui
* Vitest
* React Testing Library
* Playwright
* MSTest

No library API shall be invented based solely on model memory.

---

# 86. File Write Approval

Claude Code shall ask for explicit confirmation before every file write:

```text
[y/n]
```

Claude shall not silently create or modify files.

---

# 87. Git Worktrees

Git worktrees shall be used when two tasks are explicitly marked `[PARALLEL]`.

Frontend and backend work shall run in separate worktrees when developed simultaneously.

Changes shall only be merged after their respective validation gates pass.

---

# 88. Model Selection

Model selection shall follow the assignment:

| Work                    | Model             |
| ----------------------- | ----------------- |
| Boilerplate             | Haiku             |
| Standard implementation | Sonnet            |
| Architecture decisions  | Opus + ultrathink |

Architecture decisions shall use the strongest reasoning model.

---

# 89. Review Process

Before raising a PR:

1. Open a fresh terminal.
2. Start a new Claude instance.
3. Run:

```text
/review AB-xxxx
```

The reviewer agent shall be read-only.

The review must show:

```text
✅
```

for every requirement.

The following are blocking:

```text
❌ Missing
⚠️ Drifted
🔴 Security
📋 FRS gap
```

No PR shall be raised while any blocking finding remains.

---

# 90. OpenSpec Archive

After successful review:

```bash
openspec archive AB-xxxx
```

The change shall move from:

```text
openspec/changes/
```

to:

```text
openspec/archive/
```

before the PR is raised.

---

# 91. Git Commit Convention

All commits shall follow:

```text
type(scope): description AB#ticket
```

Examples:

```text
feat(auth): add user registration AB#1002
feat(auth): add jwt authentication AB#1002
feat(notes): add note crud AB#1004
feat(search): add sql full text search AB#1007
feat(sharing): add public share links AB#1008
feat(versions): add note version history AB#1009
```

Husky and commitlint shall enforce the convention.

---

# 92. Ticket Dependency Sequence

Tickets shall be implemented strictly in this order:

```text
AB-1001
   ↓
AB-1002
   ↓
AB-1003
   ↓
AB-1004
   ↓
AB-1005
   ↓
AB-1006
   ↓
AB-1007
   ↓
AB-1008
   ↓
AB-1009
   ↓
AB-1010
   ↓
AB-1011
   ↓
AB-1012
   ↓
AB-1013
   ↓
AB-1014
   ↓
AB-1015
   ↓
AB-1016
```

No ticket shall be skipped or reordered.

---

# 93. Ticket-to-Technical-Area Mapping

| Ticket  | Primary Technical Area                                                     |
| ------- | -------------------------------------------------------------------------- |
| AB-1001 | Monorepo, ASP.NET Core, EF Core, SQL Server, Claude/OpenSpec configuration |
| AB-1002 | JWT, refresh tokens, authentication                                        |
| AB-1003 | OTP and password reset                                                     |
| AB-1004 | EF Core notes CRUD and soft delete                                         |
| AB-1005 | EF Core pagination, sorting, filtering                                     |
| AB-1006 | EF Core tags and many-to-many relationships                                |
| AB-1007 | SQL Server Full-Text Search                                                |
| AB-1008 | Share tokens, public API, atomic SQL Server counter                        |
| AB-1009 | EF Core version snapshots and retention                                    |
| AB-1010 | React authentication + Vitest + RTL                                        |
| AB-1011 | React notes list + Vitest + RTL                                            |
| AB-1012 | TipTap editor + autosave + Vitest + RTL                                    |
| AB-1013 | Search UI + Vitest + RTL                                                   |
| AB-1014 | Share UI + Vitest + RTL                                                    |
| AB-1015 | Version UI + Vitest + RTL                                                  |
| AB-1016 | Playwright E2E                                                             |

---

# 94. Architecture Decisions

## ADR-001 — Backend Framework

**Decision:** ASP.NET Core MVC/Web API

**Reason:** The project is a .NET backend application and requires REST APIs, authentication, authorization, validation, and integration with EF Core.

---

## ADR-002 — Database

**Decision:** Microsoft SQL Server

**Reason:** SQL Server is explicitly specified and provides the relational database features, transactions, constraints, indexes, and Full-Text Search required by the application.

---

## ADR-003 — ORM

**Decision:** Entity Framework Core

**Reason:** EF Core is the native and appropriate ORM for the ASP.NET Core backend and provides LINQ, SQL Server integration, migrations, transactions, relationships, and change tracking.

---

## ADR-004 — Prisma

**Decision:** Not used.

**Reason:** The backend is ASP.NET Core/C#. EF Core is the selected persistence technology. Prisma is not required and shall not be introduced.

---

## ADR-005 — Search

**Decision:** SQL Server Full-Text Search

**Reason:** The assignment explicitly requires SQL Full-Text Search and prohibits external search services.

---

## ADR-006 — Authentication

**Decision:** JWT access token + database-backed refresh token

**Reason:** This provides short-lived access tokens and server-side refresh-token management/revocation.

---

## ADR-007 — Note Deletion

**Decision:** Soft delete

**Reason:** Notes must remain recoverable for 30 days.

---

## ADR-008 — Version Restore

**Decision:** Restore as a new version

**Reason:** Historical versions must remain immutable.

---

## ADR-009 — Frontend Testing

**Decision:** Vitest + React Testing Library

**Reason:** Vitest provides a fast TypeScript/JavaScript test runner, while React Testing Library provides user-focused React component testing.

---

## ADR-010 — End-to-End Testing

**Decision:** Playwright

**Reason:** Playwright provides browser-level testing for the complete application journey.

---

# 95. Security Principles

The implementation shall follow these principles:

1. Server-side authorization is mandatory.
2. Users can only access their own private resources.
3. Passwords are never stored in plaintext.
4. Refresh tokens are protected.
5. OTPs are protected.
6. Share tokens are unguessable.
7. SQL injection must be prevented.
8. Search highlighting must not create XSS vulnerabilities.
9. Secrets must not be committed to source control.
10. Logs must not expose authentication secrets.
11. Public share links must be independently validated.
12. Soft-deleted notes must not leak through normal APIs or search.
13. User-owned resources must be protected against IDOR/horizontal privilege escalation.

---

# 96. Out-of-Scope Technical Features

The implementation shall not introduce:

* Real-time collaborative editing
* File attachments
* Image attachments
* Mobile applications
* OAuth
* Social login
* Note folders
* Nested notes
* Actual email delivery
* Elasticsearch
* Azure Cognitive Search
* Algolia
* Other external search services
* Prisma
* MongoDB
* PostgreSQL

---

# 97. Open Technical Decisions

The following shall be finalized through the corresponding ticket specifications.

## AB-1001

* Exact .NET version
* Exact solution/project structure
* SQL Server development environment
* EF Core version
* Frontend package versions
* Shared package structure
* OpenSpec configuration
* Claude Code commands
* MCP configuration

## AB-1002

* JWT signing algorithm
* Refresh-token rotation strategy
* Refresh-token reuse policy
* Token storage details

## AB-1003

* OTP expiration
* OTP attempt limits
* Password policy
* Reset behavior

## AB-1004

* Note content persistence format
* Note validation rules
* Recovery behavior

## AB-1007

* SQL Server Full-Text Search catalog/index configuration
* Supported search syntax
* Highlighting implementation

## AB-1008

* Share-link expiry rules
* Deleted-note behavior
* Public endpoint error contract
* View-count semantics

## AB-1009

* Version retention period
* Purge mechanism
* Version creation semantics during autosave

## AB-1012

* TipTap content format
* Autosave debounce interval
* Concurrent-save behavior
* Failed autosave handling

---

# 98. Final Architecture

The final backend architecture is:

```text
                    ┌──────────────────────┐
                    │      React 19        │
                    │     TypeScript       │
                    └──────────┬───────────┘
                               │
                               │ REST / HTTPS
                               ▼
                    ┌──────────────────────┐
                    │   ASP.NET Core API   │
                    │      C# / MVC        │
                    └──────────┬───────────┘
                               │
                               ▼
                    ┌──────────────────────┐
                    │ Application Services │
                    │   Business Logic     │
                    └──────────┬───────────┘
                               │
                               ▼
                    ┌──────────────────────┐
                    │   Entity Framework   │
                    │        Core          │
                    └──────────┬───────────┘
                               │
                               ▼
                    ┌──────────────────────┐
                    │     SQL Server       │
                    │                      │
                    │ CRUD                 │
                    │ Transactions         │
                    │ Constraints          │
                    │ Full-Text Search     │
                    └──────────────────────┘
```

Frontend testing:

```text
React Components
       ↓
React Testing Library
       ↓
Vitest
```

Backend testing:

```text
ASP.NET Core
       ↓
MSTest
       ↓
EF Core
       ↓
SQL Server
```

End-to-end testing:

```text
Browser
   ↓
Playwright
   ↓
React
   ↓
ASP.NET Core
   ↓
EF Core
   ↓
SQL Server
```

The authoritative technical stack is therefore:

```text
Frontend:
React 19 + TypeScript + Vite
+ TanStack Query
+ Zustand
+ TipTap
+ shadcn/ui
+ Vitest
+ React Testing Library

Backend:
ASP.NET Core MVC/Web API
+ C#
+ Entity Framework Core
+ SQL Server
+ MSTest

E2E:
Playwright

Monorepo:
pnpm workspaces

Specification:
OpenSpec

AI Development:
Claude Code + Context7 MCP
```

The project shall favor a **simple, maintainable, testable, secure .NET architecture** and shall not introduce additional databases, ORMs, search engines, or infrastructure without an approved specification change.
