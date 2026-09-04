// Zod schemas (AB-1007) — validation mirrors of the backend's Search DTOs, for frontend UX
// convenience only (packages/shared/CLAUDE.md). Consumed starting AB-1013 (frontend search UI).

import { z } from 'zod';
import { tagRefSchema } from './tags';

// Mirrors SearchQueryDto's [Required, TrimmedLength(1, 200)] — q is required, unlike
// noteListQuerySchema's optional page/pageSize/sortBy fields.
export const searchQuerySchema = z.object({
  q: z.string().trim().min(1).max(200),
  page: z.number().int().min(1).optional(),
  pageSize: z.number().int().min(1).optional(),
});
export type SearchQuery = z.infer<typeof searchQuerySchema>;

export const noteHighlightSchema = z.object({
  title: z.string(),
  content: z.string(),
});
export type NoteHighlight = z.infer<typeof noteHighlightSchema>;

export const searchResultSchema = z.object({
  id: z.string().uuid(),
  title: z.string(),
  content: z.string(),
  tags: z.array(tagRefSchema),
  createdAt: z.string(),
  updatedAt: z.string(),
  highlight: noteHighlightSchema,
});
export type SearchResult = z.infer<typeof searchResultSchema>;

// {items, page, pageSize, totalCount, totalPages} — the standard list envelope (AGENTS.md §6).
export const searchResponseSchema = z.object({
  items: z.array(searchResultSchema),
  page: z.number().int(),
  pageSize: z.number().int(),
  totalCount: z.number().int(),
  totalPages: z.number().int(),
});
export type SearchResponse = z.infer<typeof searchResponseSchema>;
