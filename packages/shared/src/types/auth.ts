// Auth DTOs (AB-1002). Mirror the backend's C# DTOs field-for-field — see
// apps/api/src/NoteManagement.Application/DTOs/Auth and delta-openapi.yaml under
// openspec/changes/ab-1002-authentication. The backend is the authoritative source of truth;
// these types are re-derived from the Zod schemas in ../schemas/auth.ts (see z.infer<> there)
// rather than hand-duplicated, so update the schema, not this file, when the contract changes.

export type {
  RegisterRequest,
  UserResponse,
  LoginRequest,
  AuthTokensResponse,
  RefreshRequest,
  LogoutRequest,
  ForgotPasswordRequest,
  ResetPasswordRequest,
  MessageResponse,
} from '../schemas/auth';
