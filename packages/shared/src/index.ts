// Shared TypeScript contracts (DTOs, Zod schemas, pagination/error types) consumed by apps/web.
// See packages/shared/CLAUDE.md — this package never redeclares a type apps/web already has,
// and the backend (apps/api, C#) holds the authoritative implementation these types mirror.

// AB-1002 — Auth DTOs + Zod schemas (SDS §55/§81). Consumed starting AB-1010 (frontend auth).
// Only the schema *values* are re-exported here — their types already flow through
// ./types/auth (which re-derives them via z.infer<>), so wildcard-exporting both would
// duplicate every type name and fail to compile.
export {
  registerRequestSchema,
  userResponseSchema,
  loginRequestSchema,
  authTokensResponseSchema,
  refreshRequestSchema,
  logoutRequestSchema,
  forgotPasswordRequestSchema,
  resetPasswordRequestSchema,
  messageResponseSchema,
} from './schemas/auth';
export * from './types/auth';

// AB-1004 — Note DTOs + Zod schemas (SDS §55/§81). Consumed starting AB-1011/AB-1012 (frontend notes UI).
// AB-1005 adds noteListQuerySchema (client-driven pagination/sorting query params).
// AB-1006 adds tagIds/tags/tagId to the existing shapes (see schemas/notes.ts).
export {
  createNoteRequestSchema,
  updateNoteRequestSchema,
  noteResponseSchema,
  noteListResponseSchema,
  noteListQuerySchema,
} from './schemas/notes';
export * from './types/notes';

// AB-1006 — Tag DTOs + Zod schemas (SDS §55/§81). Consumed starting AB-1011/AB-1012.
export {
  createTagRequestSchema,
  updateTagRequestSchema,
  tagResponseSchema,
  tagListResponseSchema,
  tagRefSchema,
} from './schemas/tags';
export * from './types/tags';

