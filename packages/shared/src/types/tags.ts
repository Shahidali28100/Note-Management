// Tag DTOs (AB-1006). Mirror the backend's C# DTOs field-for-field — see
// apps/api/src/NoteManagement.Application/DTOs/Tags and delta-openapi.yaml under
// openspec/changes/ab-1006-tags-crud. The backend is the authoritative source of truth;
// these types are re-derived from the Zod schemas in ../schemas/tags.ts (see z.infer<> there)
// rather than hand-duplicated, so update the schema, not this file, when the contract changes.

export type {
  CreateTagRequest,
  UpdateTagRequest,
  TagResponse,
  TagListResponse,
  TagRef,
} from '../schemas/tags';
