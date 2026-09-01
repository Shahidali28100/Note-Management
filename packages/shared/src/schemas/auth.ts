// Zod schemas (AB-1002) — validation mirrors of the backend's Auth DTOs, for frontend UX
// convenience only (packages/shared/CLAUDE.md). The backend's PasswordPolicyAttribute /
// DataAnnotations validators remain the actual authority; these never replace server-side
// validation. Types are derived via z.infer<> per packages/shared/CLAUDE.md step 5.

import { z } from 'zod';

export const registerRequestSchema = z.object({
  name: z.string().min(1).max(200),
  email: z.string().email().max(320),
  password: z
    .string()
    .min(8)
    .regex(/[A-Za-z]/, 'Password must contain at least one letter.')
    .regex(/[0-9]/, 'Password must contain at least one digit.'),
});
export type RegisterRequest = z.infer<typeof registerRequestSchema>;

export const userResponseSchema = z.object({
  id: z.string().uuid(),
  name: z.string(),
  email: z.string().email(),
});
export type UserResponse = z.infer<typeof userResponseSchema>;

export const loginRequestSchema = z.object({
  email: z.string().email(),
  password: z.string().min(1),
});
export type LoginRequest = z.infer<typeof loginRequestSchema>;

export const authTokensResponseSchema = z.object({
  accessToken: z.string(),
  refreshToken: z.string(),
  accessTokenExpiresAtUtc: z.string(),
  tokenType: z.literal('Bearer'),
});
export type AuthTokensResponse = z.infer<typeof authTokensResponseSchema>;

export const refreshRequestSchema = z.object({
  refreshToken: z.string().min(1),
});
export type RefreshRequest = z.infer<typeof refreshRequestSchema>;

export const logoutRequestSchema = z.object({
  refreshToken: z.string().min(1),
});
export type LogoutRequest = z.infer<typeof logoutRequestSchema>;

// AB-1003 — forgot-password / OTP-based reset (FRS-AUTH-005/006).

export const forgotPasswordRequestSchema = z.object({
  email: z.string().email().max(320),
});
export type ForgotPasswordRequest = z.infer<typeof forgotPasswordRequestSchema>;

export const resetPasswordRequestSchema = z.object({
  email: z.string().email().max(320),
  otp: z.string().regex(/^\d{6}$/, 'Code must be 6 digits.'),
  newPassword: z
    .string()
    .min(8)
    .regex(/[A-Za-z]/, 'Password must contain at least one letter.')
    .regex(/[0-9]/, 'Password must contain at least one digit.'),
});
export type ResetPasswordRequest = z.infer<typeof resetPasswordRequestSchema>;

export const messageResponseSchema = z.object({ message: z.string() });
export type MessageResponse = z.infer<typeof messageResponseSchema>;
