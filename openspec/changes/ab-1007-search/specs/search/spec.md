## Purpose
User-scoped SQL Server full-text keyword search over a user's own notes (title and content), returning paginated, relevance-ranked results with safely-renderable match highlighting.

## ADDED Requirements

### Requirement: Full-Text Keyword Search
The system SHALL allow an authenticated user to search their own notes by keyword using SQL Server Full-Text Search over each note's title and content.

The search keyword SHALL be supplied as a required `q` parameter. `q` SHALL, after trimming leading/trailing whitespace, be between 1 and 200 characters. A missing `q`, a `q` that is empty or whitespace-only after trimming, or a `q` exceeding 200 characters after trimming SHALL be rejected with `400 Bad Request`.

`q` SHALL be tokenized into one or more whitespace-separated search terms. A note SHALL match only when its title or content contains **every** term (an AND of terms) — a note containing only some of the terms SHALL NOT match. Matching SHALL also recognize close natural-language word variants of a term (e.g. plural/singular, verb tense), consistent with SQL Server Full-Text Search's inflectional matching. Search terms SHALL be treated as data, never concatenated into a query string — malformed or unusual characters in `q` SHALL NOT cause a search-syntax error to be exposed to the caller.

Matching results SHALL be ordered by full-text search relevance rank, descending (best match first). This endpoint SHALL NOT accept a `sortBy` or `sortDirection` parameter.

#### Scenario: Successful single-term search
- **WHEN** an authenticated user searches with a single-word `q` that appears in the title or content of one or more of their active notes
- **THEN** the system responds `200 OK` with those notes, ordered by relevance rank descending

#### Scenario: Multi-term search requires every term
- **WHEN** an authenticated user searches with a multi-word `q`
- **THEN** only their active notes containing every term (in title and/or content, in any order) are returned

#### Scenario: Note matching only some terms is excluded
- **WHEN** an authenticated user searches with a multi-word `q` and one of their notes contains only some of the terms
- **THEN** that note is not included in the results

#### Scenario: No matching notes returns an empty page, not an error
- **WHEN** an authenticated user searches with a valid `q` that matches none of their active notes
- **THEN** the system responds `200 OK` with an empty `items` array and `totalCount: 0`

#### Scenario: Missing q rejected
- **WHEN** an authenticated user calls search with no `q` parameter
- **THEN** the system rejects the request with `400 Bad Request`

#### Scenario: Empty or whitespace-only q rejected
- **WHEN** an authenticated user calls search with `q` that is empty or contains only whitespace
- **THEN** the system rejects the request with `400 Bad Request`

#### Scenario: Oversized q rejected
- **WHEN** an authenticated user calls search with a `q` longer than 200 characters after trimming
- **THEN** the system rejects the request with `400 Bad Request`

#### Scenario: Unauthenticated request rejected
- **WHEN** a search request carries no valid access token
- **THEN** the system rejects the request with `401 Unauthorized`

### Requirement: Search Result User Isolation
Search results SHALL only contain active (non-deleted) notes owned by the authenticated user, regardless of whether another user's note, or the caller's own soft-deleted note, would otherwise match the search terms.

#### Scenario: Only the caller's own notes are searched
- **WHEN** an authenticated user searches with a `q` that would match notes owned by other users
- **THEN** the system's results contain only notes owned by the searching user

#### Scenario: Soft-deleted notes excluded from search results
- **WHEN** an authenticated user has a soft-deleted note whose title or content matches the search terms
- **THEN** that note does not appear in the search results and is not counted in `totalCount`

### Requirement: Search Result Highlighting
Each search result SHALL include a `highlight` object identifying where the search terms matched, so the frontend can visually emphasize them without being given executable markup (FRS-SEARCH-003, SDS §44/§60).

`highlight` SHALL contain a `title` field and a `content` field, each plain text (no HTML). Within each field, every matched search term SHALL be delimited by a pair of non-HTML sentinel markers surrounding just that term, with all other text left exactly as it appears in the note. The system SHALL NOT emit HTML tags, attributes, or other markup of any kind inside `highlight.title` or `highlight.content` — content already present in the note (including characters that look like markup) SHALL pass through as inert literal text, never interpreted.

`highlight.content` SHALL be a bounded-length excerpt of the note's content (at most 200 characters) rather than the full content: centered on the first matching term when content matches, or taken from the start of the content when only the title matched.

#### Scenario: Matching term highlighted in title
- **WHEN** a search term matches text in a note's title
- **THEN** the response's `highlight.title` for that note contains the title text with the matching term delimited by sentinel markers, and no HTML tags

#### Scenario: Matching term highlighted in a content excerpt
- **WHEN** a search term matches text in a note's content
- **THEN** the response's `highlight.content` for that note is an excerpt of at most 200 characters centered on the match, with the matching term delimited by sentinel markers

#### Scenario: Multiple matching terms all highlighted
- **WHEN** a multi-term search matches more than one term within the same field
- **THEN** every matching term within that field is individually delimited by sentinel markers

#### Scenario: Markup-like note content is never rendered as markup
- **WHEN** a note's title or content contains characters that resemble HTML (e.g. `<`, `>`, `&`) at or near a matched term
- **THEN** the system's `highlight` output preserves those characters as literal text alongside the sentinel markers and introduces no HTML of its own

### Requirement: Search Pagination
Search results SHALL be paginated using the same envelope, defaults, and validation rules as note listing (FRS-SEARCH-004).

The response SHALL use the standard list envelope: `items`, `page`, `pageSize`, `totalCount`, `totalPages`. `page` SHALL be a positive integer (`>= 1`) defaulting to `1`; an invalid (non-integer or `< 1`) `page` SHALL be rejected with `400 Bad Request`. `pageSize` SHALL be a positive integer (`>= 1`) defaulting to `20` and capped at `100`; an invalid (non-integer or `< 1`) `pageSize` SHALL be rejected with `400 Bad Request`, while a valid `pageSize` greater than `100` SHALL be silently clamped to `100`. A `page` beyond the last available page for the caller's matching results SHALL NOT be treated as an error — it SHALL return `200 OK` with an empty `items` array and accurate `totalCount`/`totalPages`.

#### Scenario: Default pagination applied
- **WHEN** an authenticated user searches with no `page` or `pageSize`
- **THEN** the system responds `200 OK` with `page: 1` and `pageSize: 20`

#### Scenario: Client requests a specific page and page size
- **WHEN** an authenticated user searches with `page=2` and `pageSize=5`
- **THEN** the system responds `200 OK` with the second page of up to 5 matching results, and the envelope reports `page: 2` and `pageSize: 5`

#### Scenario: Invalid page value rejected
- **WHEN** an authenticated user searches with `page=0`, a negative `page`, or a non-integer `page`
- **THEN** the system rejects the request with `400 Bad Request`

#### Scenario: Invalid page size value rejected
- **WHEN** an authenticated user searches with `pageSize=0`, a negative `pageSize`, or a non-integer `pageSize`
- **THEN** the system rejects the request with `400 Bad Request`

#### Scenario: Oversized page size silently clamped
- **WHEN** an authenticated user searches with `pageSize=500`
- **THEN** the system responds `200 OK` using `pageSize: 100` rather than rejecting the request

#### Scenario: Page beyond the last page returns an empty page, not an error
- **WHEN** an authenticated user requests a `page` number greater than the total number of pages for their matching results
- **THEN** the system responds `200 OK` with an empty `items` array and the correct `totalCount`/`totalPages`
