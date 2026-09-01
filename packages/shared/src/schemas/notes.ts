// Zod schemas (AB-1004) — validation mirrors of the backend's Note DTOs, for frontend UX
// convenience only (packages/shared/CLAUDE.md). The backend's TrimmedLengthAttribute remains the
// actual authority; these never replace server-side validation. Types are derived via z.infer<>
// per packages/shared/CLAUDE.md step 5.

import { z } from 'zod';

// Mirrors CreateNoteRequestDto/UpdateNoteRequestDto's TrimmedLength(1, 200)/TrimmedLength(1, ∞)
// validation — z.string().trim() normalizes before .min()/.max() check length, matching the
// backend's "after trimming" rule.
export const createNoteRequestSchema = z.object({
  title: z.string().trim().min(1).max(200),
  content: z.string().trim().min(1),
});
export type CreateNoteRequest = z.infer<typeof createNoteRequestSchema>;

export const updateNoteRequestSchema = createNoteRequestSchema;
export type UpdateNoteRequest = z.infer<typeof updateNoteRequestSchema>;

export const noteResponseSchema = z.object({
  id: z.string().uuid(),
  title: z.string(),
  content: z.string(),
  createdAt: z.string(),
  updatedAt: z.string(),
});
export type NoteResponse = z.infer<typeof noteResponseSchema>;

// {items, page, pageSize, totalCount, totalPages} — the standard list envelope (AGENTS.md §6).
export const noteListResponseSchema = z.object({
  items: z.array(noteResponseSchema),
  page: z.number().int(),
  pageSize: z.number().int(),
  totalCount: z.number().int(),
  totalPages: z.number().int(),
});
export type NoteListResponse = z.infer<typeof noteListResponseSchema>;
