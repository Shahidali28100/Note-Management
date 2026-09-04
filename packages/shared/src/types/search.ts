// Search DTOs (AB-1007). Mirror the backend's C# DTOs field-for-field — see
// apps/api/src/NoteManagement.Application/DTOs/Search and delta-openapi.yaml under
// openspec/changes/ab-1007-search. Re-derived from ../schemas/search.ts (z.infer<>), not
// hand-duplicated.

export type {
  SearchQuery,
  NoteHighlight,
  SearchResult,
  SearchResponse,
} from '../schemas/search';
