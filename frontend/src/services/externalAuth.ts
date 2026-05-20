import axios from 'axios';

/** Public-facing user profile returned from the API. */
export interface AuthUserResponse {
  /** Unique user identifier. */
  id: string;
  /** User's email address. */
  email: string;
  /** User's display name. */
  displayName: string;
  /** Whether the user's email has been verified. */
  emailVerified: boolean;
}

/** Auth response containing access token, expiry, and user info. */
export interface AuthResponse {
  /** JWT access token for API authorization. */
  accessToken: string;
  /** Date/time when the access token expires. */
  accessTokenExpiresAt: string;
  /** Authenticated user profile. */
  user: AuthUserResponse;
}

/**
 * Sends an external login request using an identity token from an OAuth provider.
 * @param provider - The OAuth provider name (e.g., "Google").
 * @param idToken - The ID token issued by the provider.
 * @returns The auth response with tokens and user info.
 */
export async function postExternalLogin(provider: string, idToken: string) {
  const resp = await axios.post<AuthResponse>('/api/auth/external',
    { provider, idToken },
    { withCredentials: true }
  );
  return resp.data;
}

/**
 * Exchanges an authorization code (PKCE flow) for tokens via the API.
 * @param provider - The OAuth provider name.
 * @param code - The authorization code from the provider's redirect.
 * @param codeVerifier - The PKCE code verifier used during the auth request.
 * @param redirectUri - The redirect URI registered with the provider.
 * @returns The auth response with tokens and user info.
 */
export async function postExternalCode(provider: string, code: string, codeVerifier: string, redirectUri: string) {
  const resp = await axios.post<AuthResponse>('/api/auth/external/code',
    { provider, code, codeVerifier, redirectUri },
    { withCredentials: true }
  );
  return resp.data;
}

/**
 * Attempts to refresh the auth session via the httpOnly refresh token cookie.
 * @returns The auth response if refresh succeeds, or null if no session is available.
 */
export async function refreshAuthToken(): Promise<AuthResponse | null> {
  try {
    const resp = await axios.post<AuthResponse>(
      '/api/auth/refresh',
      {},
      { withCredentials: true }
    );
    return resp.data;
  } catch {
    return null;
  }
}

/**
 * Logs the user out by calling the API logout endpoint (revokes refresh token cookie).
 */
export async function logout(): Promise<void> {
  try {
    await axios.post('/api/auth/logout', {}, { withCredentials: true });
  } catch {
    // Ignore errors during logout
  }
}
