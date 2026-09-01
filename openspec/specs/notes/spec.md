# notes Specification

## Purpose
TBD - created by archiving change ab-1004-notes-crud. Update Purpose after archive.

## Requirements

### Requirement: Note Creation
The system SHALL allow an authenticated user to create a note belonging to them by providing a title and content.

The title SHALL be required and, after trimming leading/trailing whitespace, SHALL be between 1 and 200 characters. The content SHALL be required and, after trimming, SHALL be non-empty. The system SHALL treat content as an opaque string with no structural or format validation — the persisted representation is not interpreted, parsed, or constrained beyond the non-empty rule.

A successful creation SHALL persist the note with the authenticated user as owner, SHALL set both the creation timestamp and the last-modification timestamp to the current UTC time, and SHALL respond with the created note's id, title, content, creation timestamp, and last-modification timestamp.

#### Scenario: Successful note creation
- **WHEN** an authenticated user submits a title of 1–200 characters and non-empty content
- **THEN** the system creates a note owned by that user and responds `201 Created` with the note's id, title, content, createdAt, and updatedAt

#### Scenario: Missing title rejected
- **WHEN** an authenticated user submits a request with no title, or a title that is empty or whitespace-only after trimming
- **THEN** the system rejects the request with `400 Bad Request` and does not create a note

#### Scenario: Title exceeding maximum length rejected
- **WHEN** an authenticated user submits a title longer than 200 characters after trimming
- **THEN** the system rejects the request with `400 Bad Request` and does not create a note

#### Scenario: Missing content rejected
- **WHEN** an authenticated user submits a request with no content, or content that is empty or whitespace-only after trimming
- **THEN** the system rejects the request with `400 Bad Request` and does not create a note

#### Scenario: Unauthenticated request rejected
- **WHEN** a request to create a note carries no valid access token
- **THEN** the system rejects the request with `401 Unauthorized` and does not create a note

### Requirement: Single Note Retrieval
The system SHALL allow an authenticated user to retrieve one of their own active (non-deleted) notes by id.

A note that does not exist, that has been soft-deleted, or that belongs to a different user SHALL be rejected identically — the response SHALL NOT reveal whether the id exists or who owns it.

#### Scenario: Owner retrieves their own note
- **WHEN** an authenticated user requests a note by id that they own and that is not soft-deleted
- **THEN** the system responds `200 OK` with that note's id, title, content, createdAt, and updatedAt

#### Scenario: Non-existent note rejected
- **WHEN** an authenticated user requests a note id that does not exist
- **THEN** the system rejects the request with `404 Not Found`

#### Scenario: Another user's note rejected identically to not-found
- **WHEN** an authenticated user requests a note id that exists but is owned by a different user
- **THEN** the system rejects the request with `404 Not Found`, the same response used for a non-existent id

#### Scenario: Soft-deleted note rejected identically to not-found
- **WHEN** an authenticated user requests a note id that they own but that has been soft-deleted
- **THEN** the system rejects the request with `404 Not Found`

### Requirement: Note Listing
The system SHALL allow an authenticated user to list their own active (non-deleted) notes.

The response SHALL use the standard list envelope: `items`, `page`, `pageSize`, `totalCount`, and `totalPages`. This requirement covers a fixed default view only: `page` SHALL be `1`, `pageSize` SHALL be `20`, and items SHALL be sorted by last-modification timestamp descending (most recently updated first). The endpoint SHALL NOT accept page, page-size, sort, or tag-filter query parameters in this requirement — client-driven pagination, sorting, and tag filtering are governed by a separate requirement.

Notes belonging to other users, and the authenticated user's own soft-deleted notes, SHALL NOT appear in the listing.

#### Scenario: Lists only the caller's active notes
- **WHEN** an authenticated user requests their note list
- **THEN** the system responds `200 OK` with an envelope containing only that user's non-deleted notes, sorted by updatedAt descending, with `page: 1` and `pageSize: 20`

#### Scenario: Soft-deleted notes excluded from the list
- **WHEN** an authenticated user has one or more soft-deleted notes
- **THEN** those notes do not appear in the listing and are not counted in `totalCount`

#### Scenario: Another user's notes excluded from the list
- **WHEN** an authenticated user requests their note list
- **THEN** notes owned by other users never appear in the response, regardless of how many other users' notes exist

#### Scenario: Empty list for a user with no notes
- **WHEN** an authenticated user with no active notes requests their note list
- **THEN** the system responds `200 OK` with an empty `items` array and `totalCount: 0`

### Requirement: Note Update
The system SHALL allow an authenticated user to replace the title and content of their own active (non-deleted) note.

The same validation used at creation SHALL apply: title required, 1–200 characters after trimming; content required, non-empty after trimming, treated as an opaque string. A successful update SHALL set the last-modification timestamp to the current UTC time and SHALL leave the creation timestamp unchanged.

A note that does not exist, is soft-deleted, or belongs to a different user SHALL be rejected identically to single note retrieval (`404 Not Found`, no existence/ownership disclosure).

#### Scenario: Owner successfully updates their note
- **WHEN** an authenticated user submits a valid title and content for a note id they own and that is not soft-deleted
- **THEN** the system updates the note's title and content, sets updatedAt to the current UTC time, leaves createdAt unchanged, and responds `200 OK` with the updated note

#### Scenario: Invalid update rejected
- **WHEN** an authenticated user submits an update with a missing/oversized title or missing/empty content for a note they own
- **THEN** the system rejects the request with `400 Bad Request` and does not modify the note

#### Scenario: Update to non-existent note rejected
- **WHEN** an authenticated user submits an update for a note id that does not exist
- **THEN** the system rejects the request with `404 Not Found`

#### Scenario: Update to another user's note rejected identically to not-found
- **WHEN** an authenticated user submits an update for a note id owned by a different user
- **THEN** the system rejects the request with `404 Not Found`, the same response used for a non-existent id, and does not modify the note

#### Scenario: Update to a soft-deleted note rejected
- **WHEN** an authenticated user submits an update for a note id they own that has been soft-deleted
- **THEN** the system rejects the request with `404 Not Found` and does not modify the note

### Requirement: Note Soft Deletion
The system SHALL allow an authenticated user to soft-delete their own active (non-deleted) note.

A successful deletion SHALL set the note's `deletedAt` timestamp to the current UTC time. The note SHALL NOT be physically removed from the database. Once soft-deleted, the note SHALL NOT appear in single-note retrieval or listing responses.

A note that does not exist, is already soft-deleted, or belongs to a different user SHALL be rejected identically (`404 Not Found`).

#### Scenario: Owner soft-deletes their note
- **WHEN** an authenticated user deletes a note id they own that is not already soft-deleted
- **THEN** the system sets that note's deletedAt to the current UTC time, does not physically remove the row, and responds `204 No Content`

#### Scenario: Soft-deleted note excluded from subsequent retrieval
- **WHEN** an authenticated user retrieves a note id immediately after soft-deleting it
- **THEN** the system responds `404 Not Found` for that id

#### Scenario: Delete of non-existent note rejected
- **WHEN** an authenticated user attempts to delete a note id that does not exist
- **THEN** the system rejects the request with `404 Not Found`

#### Scenario: Delete of another user's note rejected identically to not-found
- **WHEN** an authenticated user attempts to delete a note id owned by a different user
- **THEN** the system rejects the request with `404 Not Found`, the same response used for a non-existent id, and the other user's note remains active

#### Scenario: Delete of an already soft-deleted note rejected
- **WHEN** an authenticated user attempts to delete a note id they own that is already soft-deleted
- **THEN** the system rejects the request with `404 Not Found`

### Requirement: Note Recovery
The system SHALL allow an authenticated user to restore their own soft-deleted note, clearing its `deletedAt` timestamp so it becomes active again.

A note that does not exist or belongs to a different user SHALL be rejected with `404 Not Found` (no existence/ownership disclosure). A note that exists, is owned by the caller, but is **not currently soft-deleted** SHALL be rejected with `409 Conflict` (nothing to restore).

Automatic permanent purging of soft-deleted notes after any retention window is explicitly out of scope for this requirement — no such purge mechanism exists yet, so restore SHALL succeed for any soft-deleted note owned by the caller regardless of how long ago it was deleted.

#### Scenario: Owner restores their soft-deleted note
- **WHEN** an authenticated user restores a note id they own that is currently soft-deleted
- **THEN** the system clears that note's deletedAt, and responds `200 OK` with the restored note's id, title, content, createdAt, and updatedAt

#### Scenario: Restored note reappears in retrieval and listing
- **WHEN** an authenticated user retrieves a note or requests their note list immediately after restoring it
- **THEN** that note is present in the response as an active note

#### Scenario: Restore of non-existent note rejected
- **WHEN** an authenticated user attempts to restore a note id that does not exist
- **THEN** the system rejects the request with `404 Not Found`

#### Scenario: Restore of another user's note rejected identically to not-found
- **WHEN** an authenticated user attempts to restore a note id owned by a different user
- **THEN** the system rejects the request with `404 Not Found`, the same response used for a non-existent id

#### Scenario: Restore of a not-deleted note rejected as a conflict
- **WHEN** an authenticated user attempts to restore a note id they own that is currently active (not soft-deleted)
- **THEN** the system rejects the request with `409 Conflict` and the note remains unchanged

#### Scenario: Restore succeeds regardless of elapsed time since deletion
- **WHEN** an authenticated user restores a note they own that was soft-deleted more than 30 days ago
- **THEN** the system restores the note successfully, since no automatic purge process exists in this ticket
