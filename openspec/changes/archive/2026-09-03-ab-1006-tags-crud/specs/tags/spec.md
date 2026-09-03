## Purpose
User-scoped tags that a user can create, list, rename/recolor, and delete, and assign to their own notes for later filtering.

## ADDED Requirements

### Requirement: Tag Creation
The system SHALL allow an authenticated user to create a tag belonging to them by providing a name and a color.

The name SHALL be required and, after trimming leading/trailing whitespace, SHALL be between 1 and 50 characters. The color SHALL be required and SHALL match the hex format `#RRGGBB` (a `#` followed by exactly six hexadecimal digits).

Tag names SHALL be unique within the owning user's scope, compared **case-insensitively** (e.g. `Work` and `work` collide for the same user). Different users MAY have tags with the same name.

A successful creation SHALL persist the tag with the authenticated user as owner, SHALL set both the creation timestamp and the last-modification timestamp to the current UTC time, and SHALL respond with the created tag's id, name, color, a `noteCount` of `0`, creation timestamp, and last-modification timestamp.

#### Scenario: Successful tag creation
- **WHEN** an authenticated user submits a name of 1–50 characters and a valid `#RRGGBB` color, with no existing tag of the same name (case-insensitive) for that user
- **THEN** the system creates a tag owned by that user and responds `201 Created` with the tag's id, name, color, `noteCount: 0`, createdAt, and updatedAt

#### Scenario: Missing name rejected
- **WHEN** an authenticated user submits a request with no name, or a name that is empty or whitespace-only after trimming
- **THEN** the system rejects the request with `400 Bad Request` and does not create a tag

#### Scenario: Name exceeding maximum length rejected
- **WHEN** an authenticated user submits a name longer than 50 characters after trimming
- **THEN** the system rejects the request with `400 Bad Request` and does not create a tag

#### Scenario: Missing color rejected
- **WHEN** an authenticated user submits a request with no color
- **THEN** the system rejects the request with `400 Bad Request` and does not create a tag

#### Scenario: Invalid color format rejected
- **WHEN** an authenticated user submits a color that does not match `#RRGGBB` (e.g. missing `#`, wrong digit count, non-hex characters, or a named color like `red`)
- **THEN** the system rejects the request with `400 Bad Request` and does not create a tag

#### Scenario: Duplicate name for the same user rejected
- **WHEN** an authenticated user submits a name that matches an existing tag of theirs case-insensitively (e.g. they already own `Work` and submit `work`)
- **THEN** the system rejects the request with `409 Conflict` and does not create a second tag

#### Scenario: Same name allowed across different users
- **WHEN** two different authenticated users each create a tag with the same name
- **THEN** the system creates a tag for each user, since uniqueness is scoped per user

#### Scenario: Unauthenticated request rejected
- **WHEN** a request to create a tag carries no valid access token
- **THEN** the system rejects the request with `401 Unauthorized` and does not create a tag

### Requirement: Tag Listing
The system SHALL allow an authenticated user to list all tags they own.

The response SHALL be a plain array (no pagination envelope) containing every tag owned by the caller. Each item SHALL include the tag's id, name, color, `noteCount`, creation timestamp, and last-modification timestamp.

`noteCount` SHALL reflect the number of the owner's currently **active** (non-deleted) notes carrying that tag. A note that is soft-deleted SHALL NOT be included in any tag's `noteCount`.

Tags belonging to other users SHALL NOT appear in the listing.

#### Scenario: Lists only the caller's own tags
- **WHEN** an authenticated user requests their tag list
- **THEN** the system responds `200 OK` with an array containing only tags owned by that user

#### Scenario: Empty list for a user with no tags
- **WHEN** an authenticated user with no tags requests their tag list
- **THEN** the system responds `200 OK` with an empty array

#### Scenario: Note count reflects only active notes
- **WHEN** an authenticated user has a tag assigned to two active notes and one soft-deleted note
- **THEN** that tag's `noteCount` in the listing is `2`

#### Scenario: Another user's tags excluded from the list
- **WHEN** an authenticated user requests their tag list
- **THEN** tags owned by other users never appear in the response, regardless of how many other users' tags exist

#### Scenario: Unauthenticated request rejected
- **WHEN** a request to list tags carries no valid access token
- **THEN** the system rejects the request with `401 Unauthorized`

### Requirement: Tag Update
The system SHALL allow an authenticated user to update the name and color of their own tag.

The same validation used at creation SHALL apply: name required, 1–50 characters after trimming; color required, matching `#RRGGBB`. The same case-insensitive per-user uniqueness check SHALL apply, excluding the tag being updated (so a tag may be "updated" to its own current name unchanged). Updating a tag SHALL NOT change its ownership.

A tag that does not exist or belongs to a different user SHALL be rejected identically (`404 Not Found`, no existence/ownership disclosure), matching the notes capability's established pattern.

#### Scenario: Owner successfully updates their tag
- **WHEN** an authenticated user submits a valid name and color for a tag id they own
- **THEN** the system updates the tag's name and color, sets updatedAt to the current UTC time, leaves the owner unchanged, and responds `200 OK` with the updated tag

#### Scenario: Update to unchanged name allowed
- **WHEN** an authenticated user submits an update for their own tag using the tag's current name (same case) and a new color
- **THEN** the system updates the color and responds `200 OK`, without rejecting the name as a duplicate of itself

#### Scenario: Invalid update rejected
- **WHEN** an authenticated user submits an update with a missing/oversized name or an invalid color format for a tag they own
- **THEN** the system rejects the request with `400 Bad Request` and does not modify the tag

#### Scenario: Update to a duplicate name rejected
- **WHEN** an authenticated user submits an update renaming a tag to match another tag of theirs case-insensitively
- **THEN** the system rejects the request with `409 Conflict` and does not modify the tag

#### Scenario: Update to non-existent tag rejected
- **WHEN** an authenticated user submits an update for a tag id that does not exist
- **THEN** the system rejects the request with `404 Not Found`

#### Scenario: Update to another user's tag rejected identically to not-found
- **WHEN** an authenticated user submits an update for a tag id owned by a different user
- **THEN** the system rejects the request with `404 Not Found`, the same response used for a non-existent id, and does not modify the tag

### Requirement: Tag Deletion
The system SHALL allow an authenticated user to delete their own tag.

Deleting a tag SHALL NOT delete any note that carries it. Deleting a tag SHALL remove the association between that tag and every note that carried it (the corresponding `NoteTags` rows), so those notes no longer report the deleted tag.

A tag that does not exist or belongs to a different user SHALL be rejected identically (`404 Not Found`).

#### Scenario: Owner deletes their tag
- **WHEN** an authenticated user deletes a tag id they own
- **THEN** the system removes the tag and responds `204 No Content`

#### Scenario: Deleting a tag preserves its notes
- **WHEN** an authenticated user deletes a tag that is assigned to one or more of their notes
- **THEN** those notes remain unchanged and retrievable, but no longer carry the deleted tag

#### Scenario: Deleted tag excluded from subsequent listing
- **WHEN** an authenticated user lists their tags immediately after deleting one
- **THEN** the deleted tag does not appear in the response

#### Scenario: Delete of non-existent tag rejected
- **WHEN** an authenticated user attempts to delete a tag id that does not exist
- **THEN** the system rejects the request with `404 Not Found`

#### Scenario: Delete of another user's tag rejected identically to not-found
- **WHEN** an authenticated user attempts to delete a tag id owned by a different user
- **THEN** the system rejects the request with `404 Not Found`, the same response used for a non-existent id, and the other user's tag remains intact
