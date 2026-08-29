# Functional Requirements Specification (FRS)

**Project:** Note Taking Application
**Document:** Functional Requirements Specification
**Version:** 1.0
**Status:** Draft
**Date:** 2026-08-26

---

## 1. Purpose

This document defines the functional requirements for the Note Taking Application.

The application allows authenticated users to create, organize, search, share, and maintain version history for their notes.

The application will provide:

* User authentication
* Note management
* Tags
* Full-text search
* Public note sharing
* Note version history
* Web-based frontend
* End-to-end user journey testing

The system is intended to be built using a spec-driven development workflow where each ticket has an approved specification before implementation begins.

---

# 2. Scope

## 2.1 In Scope

The application shall support:

1. User registration
2. User login
3. User logout
4. JWT access-token authentication
5. Refresh-token authentication
6. Forgot-password flow using OTP
7. Password reset using OTP
8. Note creation
9. Note retrieval
10. Note update
11. Note soft deletion
12. Note recovery within the recovery period
13. Pagination
14. Sorting
15. Tag filtering
16. User-scoped tags
17. Tag colors
18. Note count per tag
19. SQL Server full-text search
20. Search result highlighting
21. Search pagination
22. Public share links
23. Share-link expiry
24. Share-link revocation
25. Public read-only note access
26. Atomic share-link view counting
27. Note version snapshots
28. Version listing
29. Version viewing
30. Version restoration
31. Automatic version cleanup
32. React frontend
33. Note editor using TipTap
34. Autosave
35. Playwright end-to-end testing

---

# 3. Out of Scope

The following functionality shall not be implemented:

* Real-time collaborative editing
* File attachments
* Image attachments
* Mobile applications
* OAuth authentication
* Social login
* Note folders
* Nested notes
* Actual email delivery
* External search services

Forgot-password emails shall be simulated by logging the OTP to the application console/logging system.

---

# 4. User Roles

## 4.1 Authenticated User

An authenticated user can:

* Manage their account
* Create notes
* View their notes
* Update their notes
* Soft-delete notes
* Search their notes
* Create and manage tags
* Assign tags to notes
* Create public share links
* Revoke share links
* View note versions
* Restore previous versions

## 4.2 Public User

A public user is an unauthenticated visitor accessing a valid share link.

A public user can:

* View a shared note

A public user cannot:

* Modify the note
* Delete the note
* View private notes
* View the owner's other notes
* View version history
* Manage tags
* Create or revoke share links

---

# 5. Authentication Requirements

## FRS-AUTH-001 — Registration

The system shall allow a new user to register with:

* Name
* Email
* Password

The email address shall be unique.

Passwords shall satisfy the application's configured password policy.

### Success

A valid registration shall create a new user account.

### Errors

The system shall reject:

* Invalid email
* Invalid password
* Missing required fields
* Duplicate email

---

## FRS-AUTH-002 — Login

The system shall allow registered users to log in using:

* Email
* Password

A successful login shall return:

* JWT access token
* Refresh token
* Access-token expiration information

The access token shall expire after 15 minutes.

The refresh token shall expire after 7 days.

---

## FRS-AUTH-003 — Refresh Token

The system shall allow an authenticated client to obtain a new access token using a valid refresh token.

Refresh tokens shall be stored in the database.

Invalid, expired, revoked, or otherwise unusable refresh tokens shall be rejected.

---

## FRS-AUTH-004 — Logout

The system shall allow a user to log out.

Logout shall invalidate the associated refresh token.

An invalidated refresh token shall not be usable to obtain another access token.

---

## FRS-AUTH-005 — Forgot Password

The system shall provide a forgot-password flow.

The user shall provide their email address.

The system shall generate a time-limited OTP.

The OTP shall be logged to the application console/logging system instead of being sent through an actual email provider.

---

## FRS-AUTH-006 — Password Reset

The user shall be able to reset their password using:

* Email
* OTP
* New password

The OTP shall be:

* Time-limited
* Single-use
* Invalid after successful password reset

The system shall not reveal whether an email address exists during the initial forgot-password request.

---

# 6. Notes Requirements

## FRS-NOTE-001 — Create Note

Authenticated users shall be able to create notes.

A note shall contain at minimum:

* Title
* Content
* Tags
* Creation timestamp
* Last modification timestamp

The note shall belong to the authenticated user.

---

## FRS-NOTE-002 — View Note

A user shall be able to retrieve their own notes.

A user shall not be able to access another user's private notes.

---

## FRS-NOTE-003 — Update Note

A user shall be able to update their own note.

An update shall modify the note's last-modified timestamp.

Each successful save shall create a version-history snapshot as defined by the version-history requirements.

---

## FRS-NOTE-004 — Soft Delete

A user shall be able to delete their own note.

Deleting a note shall set its `deletedAt` timestamp.

The note shall not be physically deleted during the 30-day recovery window.

Deleted notes shall not appear in normal note listings or search results.

---

## FRS-NOTE-005 — Note Recovery

A soft-deleted note shall remain recoverable for 30 days.

Recovery shall clear the `deletedAt` timestamp.

After the recovery window expires, the system may permanently purge the note according to the application's retention process.

---

# 7. Pagination, Sorting and Filtering

## FRS-NOTE-006 — Pagination

The note list shall support pagination.

The response shall provide sufficient metadata for the client to determine:

* Current page
* Page size
* Total records
* Total pages

---

## FRS-NOTE-007 — Sorting

The note list shall support sorting using approved sortable fields.

At minimum, sorting shall support:

* Created date
* Updated date
* Title

The API shall use a safe allowlist of sortable fields.

---

## FRS-NOTE-008 — Tag Filtering

Users shall be able to filter notes by tag.

Only tags belonging to the authenticated user shall be accepted.

---

# 8. Tags

## FRS-TAG-001 — Create Tag

Authenticated users shall be able to create tags.

A tag shall contain:

* Name
* Color
* Owner

Tag names shall be unique within the user's scope.

---

## FRS-TAG-002 — Update Tag

Users shall be able to update their own tags.

Updating a tag shall not change the ownership of the tag.

---

## FRS-TAG-003 — Delete Tag

Users shall be able to remove their own tags.

Deleting a tag shall not delete associated notes.

The relationship between the deleted tag and notes shall be removed.

---

## FRS-TAG-004 — Tag Note Count

The system shall return the number of notes associated with each tag.

Deleted notes shall not be included in the active note count.

---

# 9. Search

## FRS-SEARCH-001 — Full-Text Search

Authenticated users shall be able to search their notes using keywords.

Search shall use SQL Server Full-Text Search.

No external search service shall be used.

---

## FRS-SEARCH-002 — User Isolation

Search results shall only contain notes owned by the authenticated user.

Deleted notes shall not appear in search results.

---

## FRS-SEARCH-003 — Highlighting

Search results shall identify matching keywords using highlighting.

The frontend shall render the highlighted search result safely without introducing executable HTML.

---

## FRS-SEARCH-004 — Search Pagination

Search results shall support pagination.

The API shall return pagination metadata.

---

# 10. Sharing

## FRS-SHARE-001 — Generate Share Link

A note owner shall be able to generate a public read-only share link.

The link shall contain an unguessable share token.

---

## FRS-SHARE-002 — Share Expiry

A share link shall support an expiry time.

Once expired, the share link shall no longer provide access to the note.

---

## FRS-SHARE-003 — Public Access

Anyone possessing a valid share link shall be able to view the shared note without authentication.

Public access shall be read-only.

---

## FRS-SHARE-004 — Revoke Share Link

The note owner shall be able to revoke an active share link.

A revoked link shall immediately stop providing access.

---

## FRS-SHARE-005 — View Count

Each successful public access shall increment the share link's view count.

The view count update shall be atomic to prevent lost updates under concurrent requests.

---

# 11. Version History

## FRS-VERSION-001 — Snapshot Per Save

A version snapshot shall be created whenever a note is successfully saved.

A version shall preserve the note content at that point in time.

---

## FRS-VERSION-002 — List Versions

A note owner shall be able to list versions belonging to their note.

The list shall include:

* Version identifier
* Version number
* Creation timestamp

---

## FRS-VERSION-003 — View Version

A user shall be able to view the complete content of an existing version.

---

## FRS-VERSION-004 — Restore Version

A user shall be able to restore a previous version.

Restoring a version shall create a **new version** rather than overwriting or deleting existing history.

---

## FRS-VERSION-005 — Automatic Purge

Old versions shall be automatically purged according to the configured retention policy.

The purge process shall not remove the current note content.

---

# 12. Frontend Requirements

## FRS-FE-001 — Authentication Pages

The frontend shall provide:

* Registration
* Login
* Forgot password
* OTP reset

---

## FRS-FE-002 — Notes List

The notes page shall provide:

* Note listing
* Pagination
* Sorting
* Tag filtering
* Search
* Create-note action
* Delete-note action

---

## FRS-FE-003 — Note Editor

The note editor shall use TipTap.

It shall support:

* Title editing
* Rich-text content editing
* Tags
* Save
* Autosave

---

## FRS-FE-004 — Search UI

The frontend shall display:

* Search input
* Search results
* Pagination
* Highlighted matching keywords

---

## FRS-FE-005 — Sharing UI

The frontend shall provide a share modal allowing the owner to:

* Generate a link
* View active links
* View expiry
* View view count
* Revoke a link

---

## FRS-FE-006 — Version History UI

The frontend shall provide a version-history drawer allowing the user to:

* View versions
* Open a version
* Restore a version

---

# 13. End-to-End Journey

The application shall support the following complete user journey:

1. Register
2. Login
3. Create a note
4. Add tags
5. Edit the note
6. Autosave changes
7. Search for the note
8. View highlighted search results
9. Generate a public share link
10. Open the public link
11. Verify the view count
12. Update the note
13. View version history
14. Restore a previous version
15. Verify the restored content
16. Logout

---

# 14. Ticket Traceability

| Ticket  | Functional Area                |
| ------- | ------------------------------ |
| AB-1001 | Project setup                  |
| AB-1002 | Authentication                 |
| AB-1003 | Password reset                 |
| AB-1004 | Notes CRUD                     |
| AB-1005 | Pagination, sorting, filtering |
| AB-1006 | Tags                           |
| AB-1007 | Search                         |
| AB-1008 | Sharing                        |
| AB-1009 | Version history                |
| AB-1010 | Frontend authentication        |
| AB-1011 | Notes list                     |
| AB-1012 | Note editor                    |
| AB-1013 | Search UI                      |
| AB-1014 | Share UI                       |
| AB-1015 | Version UI                     |
| AB-1016 | E2E                            |

---

# 15. Global Acceptance Criteria

A feature is functionally complete only when:

* Happy-path behavior works.
* Validation errors are handled.
* Authorization rules are enforced.
* User data isolation is maintained.
* Every approved specification scenario has a named test.
* API status codes match the SDS contract.
* Manual smoke testing has been completed.
* Automated tests pass.
* `openspec validate` passes.
* The implementation passes the fresh-terminal review.
* The change is archived before PR creation.

---

# 16. Explicit Non-Functional Boundaries

The implementation shall not introduce:

* Real-time collaboration
* Attachments
* OAuth
* Social login
* Folders
* Nested notes
* External search infrastructure
* Actual email delivery

Any requirement not explicitly included in this FRS or an approved ticket specification shall be treated as out of scope until formally approved.
