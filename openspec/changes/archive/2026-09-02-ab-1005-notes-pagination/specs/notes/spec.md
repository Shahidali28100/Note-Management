## MODIFIED Requirements

### Requirement: Note Listing
The system SHALL allow an authenticated user to list their own active (non-deleted) notes.

The response SHALL use the standard list envelope: `items`, `page`, `pageSize`, `totalCount`, and `totalPages`. The endpoint SHALL accept four optional query parameters — `page`, `pageSize`, `sortBy`, `sortDirection` — governing this listing. When none are supplied, the system SHALL behave exactly as the prior fixed default view: `page` `1`, `pageSize` `20`, sorted by last-modification timestamp descending (most recently updated first).

`page` SHALL be a positive integer (`>= 1`). A missing `page` SHALL default to `1`. A `page` value that is not a valid integer, or is less than `1`, SHALL be rejected with `400 Bad Request`.

`pageSize` SHALL be a positive integer (`>= 1`), capped at a maximum of `100`. A missing `pageSize` SHALL default to `20`. A `pageSize` value that is not a valid integer, or is less than `1`, SHALL be rejected with `400 Bad Request` — consistent with how `page` less than `1` is handled, since a non-positive value is invalid rather than a magnitude to clamp. A valid integer `pageSize` greater than `100` SHALL be silently clamped to `100`.

`sortBy` SHALL be restricted to an explicit allowlist: `createdAt`, `updatedAt`, `title`. A missing `sortBy` SHALL default to `updatedAt`. A `sortBy` value outside the allowlist SHALL be rejected with `400 Bad Request`. Sorting SHALL be implemented using the allowlisted value to select a predetermined column expression — the raw query value SHALL NOT be concatenated into any query string.

`sortDirection` SHALL be restricted to `asc` or `desc`. A missing `sortDirection` SHALL default to `desc`. A `sortDirection` value outside this allowlist SHALL be rejected with `400 Bad Request`.

A `page` value beyond the last available page SHALL NOT be treated as an error: the system SHALL respond `200 OK` with an empty `items` array and accurate `totalCount`/`totalPages` for the caller's current data.

Tag filtering is explicitly out of scope of this requirement — filtering the list by tag is governed by a separate requirement introduced once tags exist.

Notes belonging to other users, and the authenticated user's own soft-deleted notes, SHALL NOT appear in the listing, regardless of the supplied pagination/sorting parameters.

#### Scenario: Lists only the caller's active notes
- **WHEN** an authenticated user requests their note list with no query parameters
- **THEN** the system responds `200 OK` with an envelope containing only that user's non-deleted notes, sorted by updatedAt descending, with `page: 1` and `pageSize: 20`

#### Scenario: Soft-deleted notes excluded from the list
- **WHEN** an authenticated user has one or more soft-deleted notes
- **THEN** those notes do not appear in the listing and are not counted in `totalCount`

#### Scenario: Another user's notes excluded from the list
- **WHEN** an authenticated user requests their note list
- **THEN** notes owned by other users never appear in the response, regardless of how many other users' notes exist, or which pagination/sorting parameters are supplied

#### Scenario: Empty list for a user with no notes
- **WHEN** an authenticated user with no active notes requests their note list
- **THEN** the system responds `200 OK` with an empty `items` array and `totalCount: 0`

#### Scenario: Client requests a specific page and page size
- **WHEN** an authenticated user requests their note list with `page=2` and `pageSize=5`
- **THEN** the system responds `200 OK` with the second page of up to 5 of that user's notes, and the envelope reports `page: 2` and `pageSize: 5`

#### Scenario: Client sorts by an allowlisted field and direction
- **WHEN** an authenticated user requests their note list with `sortBy=title` and `sortDirection=asc`
- **THEN** the system responds `200 OK` with the caller's active notes ordered by title ascending

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
