// Zod schemas (AB-1006) — validation mirrors of the backend's Tag DTOs, for frontend UX
// convenience only (packages/shared/CLAUDE.md). The backend's CreateTagRequestDto/
// UpdateTagRequestDto remain the actual authority; these never replace server-side validation.
// Types are derived via z.infer<> per packages/shared/CLAUDE.md step 5.

import { z } from 'zod';

// Mirrors CreateTagRequestDto/UpdateTagRequestDto's TrimmedLength(1, 50) + #RRGGBB validation.
export const createTagRequestSchema = z.object({
  name: z.string().trim().min(1).max(50),
  color: z.string().regex(/^#[0-9A-Fa-f]{6}$/),
});
export type CreateTagRequest = z.infer<typeof createTagRequestSchema>;

export const updateTagRequestSchema = createTagRequestSchema;
export type UpdateTagRequest = z.infer<typeof updateTagRequestSchema>;

export const tagResponseSchema = z.object({
  id: z.string().uuid(),
  name: z.string(),
  color: z.string(),
  noteCount: z.number().int(),
  createdAt: z.string(),
  updatedAt: z.string(),
});
export type TagResponse = z.infer<typeof tagResponseSchema>;

// GET /api/tags returns a plain array — no pagination envelope (proposal.md: tags are
// low-cardinality per user, unlike notes).
export const tagListResponseSchema = z.array(tagResponseSchema);
export type TagListResponse = z.infer<typeof tagListResponseSchema>;

// The minimal shape embedded in a note's `tags` array — no noteCount, no timestamps.
export const tagRefSchema = z.object({
  id: z.string().uuid(),
  name: z.string(),
  color: z.string(),
});
export type TagRef = z.infer<typeof tagRefSchema>;
