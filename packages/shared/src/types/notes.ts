// Note DTOs (AB-1004). Mirror the backend's C# DTOs field-for-field — see
// apps/api/src/NoteManagement.Application/DTOs/Notes and delta-openapi.yaml under
// openspec/changes/ab-1004-notes-crud. The backend is the authoritative source of truth;
// these types are re-derived from the Zod schemas in ../schemas/notes.ts (see z.infer<> there)
// rather than hand-duplicated, so update the schema, not this file, when the contract changes.

export type {
  CreateNoteRequest,
  UpdateNoteRequest,
  NoteResponse,
  NoteListResponse,
} from '../schemas/notes';
