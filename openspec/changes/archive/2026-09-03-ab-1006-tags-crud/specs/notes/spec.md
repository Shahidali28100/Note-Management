## MODIFIED Requirements

### Requirement: Note Creation
The system SHALL allow an authenticated user to create a note belonging to them by providing a title, content, and an optional list of tag ids.

The title SHALL be required and, after trimming leading/trailing whitespace, SHALL be between 1 and 200 characters. The content SHALL be required and, after trimming, SHALL be non-empty. The system SHALL treat content as an opaque string with no structural or format validation — the persisted representation is not interpreted, parsed, or constrained beyond the non-empty rule.

The request MAY include a `tagIds` array. A missing `tagIds` SHALL be treated as an empty array (no tags assigned). Every id in `tagIds` SHALL reference a tag owned by the authenticated user; duplicate ids within the array SHALL be de-duplicated server-side. There SHALL be no limit on the number of distinct tag ids that may be assigned to a note. If any id in `tagIds` does not exist or is not owned by the authenticated user, the entire request SHALL be rejected — no note SHALL be created and no tag assignment SHALL be partially applied.

A successful creation SHALL persist the note with the authenticated user as owner, SHALL associate the note with every tag id in `tagIds` (after de-duplication), SHALL set both the creation timestamp and the last-modification timestamp to the current UTC time, and SHALL respond with the created note's id, title, content, assigned tags (each as id/name/color), creation timestamp, and last-modification timestamp.

#### Scenario: Successful note creation
- **WHEN** an authenticated user submits a title of 1–200 characters and non-empty content, with no `tagIds`
- **THEN** the system creates a note owned by that user with no assigned tags and responds `201 Created` with the note's id, title, content, an empty `tags` array, createdAt, and updatedAt

#### Scenario: Successful note creation with tags
- **WHEN** an authenticated user submits a valid title and content along with `tagIds` referencing one or more tags they own
- **THEN** the system creates a note owned by that user, associates it with each referenced tag, and responds `201 Created` with the note including a `tags` array reflecting those assignments

#### Scenario: Duplicate tag ids de-duplicated
- **WHEN** an authenticated user submits `tagIds` containing the same tag id more than once
- **THEN** the system creates the note with that tag assigned exactly once, not duplicated

#### Scenario: Missing title rejected
- **WHEN** an authenticated user submits a request with no title, or a title that is empty or whitespace-only after trimming
- **THEN** the system rejects the request with `400 Bad Request` and does not create a note

#### Scenario: Title exceeding maximum length rejected
- **WHEN** an authenticated user submits a title longer than 200 characters after trimming
- **THEN** the system rejects the request with `400 Bad Request` and does not create a note

#### Scenario: Missing content rejected
- **WHEN** an authenticated user submits a request with no content, or content that is empty or whitespace-only after trimming
- **THEN** the system rejects the request with `400 Bad Request` and does not create a note

#### Scenario: Non-existent or unowned tag id rejected
- **WHEN** an authenticated user submits `tagIds` containing an id that does not exist or belongs to a different user, alongside an otherwise valid title and content
- **THEN** the system rejects the request with `400 Bad Request` and does not create a note

#### Scenario: Unauthenticated request rejected
- **WHEN** a request to create a note carries no valid access token
- **THEN** the system rejects the request with `401 Unauthorized` and does not create a note

### Requirement: Note Update
The system SHALL allow an authenticated user to replace the title, content, and tag assignment of their own active (non-deleted) note.

The same title/content validation used at creation SHALL apply. The request MAY include a `tagIds` array with the same rules as creation: missing means empty, every id must reference a tag owned by the caller, duplicates are de-duplicated, and any invalid id rejects the entire request with `400 Bad Request`. `tagIds` SHALL **fully replace** the note's existing tag assignment — a tag previously assigned but omitted from `tagIds` SHALL no longer be associated with the note after a successful update.

A successful update SHALL set the last-modification timestamp to the current UTC time and SHALL leave the creation timestamp unchanged.

A note that does not exist, is soft-deleted, or belongs to a different user SHALL be rejected identically to single note retrieval (`404 Not Found`, no existence/ownership disclosure).

#### Scenario: Owner successfully updates their note
- **WHEN** an authenticated user submits a valid title and content along with `tagIds` for a note id they own and that is not soft-deleted
- **THEN** the system updates the note's title, content, and tag assignment to exactly the referenced tags, sets updatedAt to the current UTC time, leaves createdAt unchanged, and responds `200 OK` with the updated note including its new `tags` array

#### Scenario: Omitted tag no longer associated after update
- **WHEN** an authenticated user updates a note that currently carries two tags, submitting `tagIds` containing only one of them
- **THEN** the system associates the note with only the submitted tag, removing the omitted one

#### Scenario: Empty tagIds clears all tag assignments
- **WHEN** an authenticated user updates a note that currently carries one or more tags, submitting an empty `tagIds` array (or omitting `tagIds`)
- **THEN** the system removes all of the note's tag assignments

#### Scenario: Invalid update rejected
- **WHEN** an authenticated user submits an update with a missing/oversized title or missing/empty content for a note they own
- **THEN** the system rejects the request with `400 Bad Request` and does not modify the note

#### Scenario: Non-existent or unowned tag id rejected
- **WHEN** an authenticated user submits an update with `tagIds` containing an id that does not exist or belongs to a different user, for a note they own
- **THEN** the system rejects the request with `400 Bad Request` and does not modify the note or its tag assignment

#### Scenario: Update to non-existent note rejected
- **WHEN** an authenticated user submits an update for a note id that does not exist
- **THEN** the system rejects the request with `404 Not Found`

#### Scenario: Update to another user's note rejected identically to not-found
- **WHEN** an authenticated user submits an update for a note id owned by a different user
- **THEN** the system rejects the request with `404 Not Found`, the same response used for a non-existent id, and does not modify the note

#### Scenario: Update to a soft-deleted note rejected
- **WHEN** an authenticated user submits an update for a note id they own that has been soft-deleted
- **THEN** the system rejects the request with `404 Not Found` and does not modify the note

### Requirement: Note Listing
The system SHALL allow an authenticated user to list their own active (non-deleted) notes.

The response SHALL use the standard list envelope: `items`, `page`, `pageSize`, `totalCount`, and `totalPages`. Each item in `items` SHALL include a `tags` array (each tag as id/name/color) reflecting the note's current tag assignment. The endpoint SHALL accept five optional query parameters — `page`, `pageSize`, `sortBy`, `sortDirection`, `tagId` — governing this listing. When none are supplied, the system SHALL behave exactly as the prior fixed default view: `page` `1`, `pageSize` `20`, sorted by last-modification timestamp descending (most recently updated first), no tag filter applied.

`page` SHALL be a positive integer (`>= 1`). A missing `page` SHALL default to `1`. A `page` value that is not a valid integer, or is less than `1`, SHALL be rejected with `400 Bad Request`.

`pageSize` SHALL be a positive integer (`>= 1`), capped at a maximum of `100`. A missing `pageSize` SHALL default to `20`. A `pageSize` value that is not a valid integer, or is less than `1`, SHALL be rejected with `400 Bad Request`. A valid integer `pageSize` greater than `100` SHALL be silently clamped to `100`.

`sortBy` SHALL be restricted to an explicit allowlist: `createdAt`, `updatedAt`, `title`. A missing `sortBy` SHALL default to `updatedAt`. A `sortBy` value outside the allowlist SHALL be rejected with `400 Bad Request`. Sorting SHALL be implemented using the allowlisted value to select a predetermined column expression — the raw query value SHALL NOT be concatenated into any query string.

`sortDirection` SHALL be restricted to `asc` or `desc`. A missing `sortDirection` SHALL default to `desc`. A `sortDirection` value outside this allowlist SHALL be rejected with `400 Bad Request`.

`tagId` SHALL be optional. When supplied, it SHALL reference a tag owned by the authenticated user, and the listing SHALL be restricted to the caller's active notes associated with that tag. A `tagId` that does not exist or is not owned by the authenticated user SHALL be rejected with `400 Bad Request` — the endpoint SHALL NOT silently return an empty page for an unowned or non-existent `tagId`, since doing so could be used to probe for the existence of another user's tag. This ticket supports exactly one `tagId` per request; filtering by multiple tags simultaneously is not supported.

A `page` value beyond the last available page SHALL NOT be treated as an error: the system SHALL respond `200 OK` with an empty `items` array and accurate `totalCount`/`totalPages` for the caller's current data (and current filter, when `tagId` is supplied).

Notes belonging to other users, and the authenticated user's own soft-deleted notes, SHALL NOT appear in the listing, regardless of the supplied pagination/sorting/filtering parameters.

#### Scenario: Lists only the caller's active notes
- **WHEN** an authenticated user requests their note list with no query parameters
- **THEN** the system responds `200 OK` with an envelope containing only that user's non-deleted notes, each including its `tags` array, sorted by updatedAt descending, with `page: 1` and `pageSize: 20`

#### Scenario: Soft-deleted notes excluded from the list
- **WHEN** an authenticated user has one or more soft-deleted notes
- **THEN** those notes do not appear in the listing and are not counted in `totalCount`

#### Scenario: Another user's notes excluded from the list
- **WHEN** an authenticated user requests their note list
- **THEN** notes owned by other users never appear in the response, regardless of how many other users' notes exist, or which pagination/sorting/filtering parameters are supplied

#### Scenario: Empty list for a user with no notes
- **WHEN** an authenticated user with no active notes requests their note list
- **THEN** the system responds `200 OK` with an empty `items` array and `totalCount: 0`

#### Scenario: Client requests a specific page and page size
- **WHEN** an authenticated user requests their note list with `page=2` and `pageSize=5`
- **THEN** the system responds `200 OK` with the second page of up to 5 of that user's notes, and the envelope reports `page: 2` and `pageSize: 5`

#### Scenario: Client sorts by an allowlisted field and direction
- **WHEN** an authenticated user requests their note list with `sortBy=title` and `sortDirection=asc`
- **THEN** the system responds `200 OK` with the caller's active notes ordered by title ascending

#### Scenario: Client filters by a tag they own
- **WHEN** an authenticated user requests their note list with `tagId` set to a tag id they own
- **THEN** the system responds `200 OK` with only that user's active notes carrying that tag, with accurate `totalCount`/`totalPages` for the filtered set

#### Scenario: Filtering by a tag with no matching notes returns an empty page
- **WHEN** an authenticated user requests their note list with `tagId` set to a tag id they own that is not currently assigned to any active note
- **THEN** the system responds `200 OK` with an empty `items` array and `totalCount: 0`

#### Scenario: Page beyond the last page returns an empty page, not an error
- **WHEN** an authenticated user requests a `page` number greater than the total number of available pages for their notes
- **THEN** the system responds `200 OK` with an empty `items` array and the correct `totalCount`/`totalPages` for their actual data

#### Scenario: Invalid page value rejected
- **WHEN** an authenticated user requests their note list with `page=0`, a negative `page`, or a non-integer `page`
- **THEN** the system rejects the request with `400 Bad Request`

#### Scenario: Oversized page size silently clamped
- **WHEN** an authenticated user requests their note list with `pageSize=500`
- **THEN** the system responds `200 OK` using `pageSize: 100` rather than rejecting the request

#### Scenario: Invalid page size value rejected
- **WHEN** an authenticated user requests their note list with `pageSize=0`, a negative `pageSize`, or a non-integer `pageSize`
- **THEN** the system rejects the request with `400 Bad Request`

#### Scenario: Unsupported sort field rejected
- **WHEN** an authenticated user requests their note list with a `sortBy` value outside `createdAt`, `updatedAt`, `title`
- **THEN** the system rejects the request with `400 Bad Request`

#### Scenario: Unsupported sort direction rejected
- **WHEN** an authenticated user requests their note list with a `sortDirection` value other than `asc` or `desc`
- **THEN** the system rejects the request with `400 Bad Request`

#### Scenario: Non-existent or unowned tagId rejected
- **WHEN** an authenticated user requests their note list with `tagId` set to an id that does not exist or belongs to a different user
- **THEN** the system rejects the request with `400 Bad Request`
