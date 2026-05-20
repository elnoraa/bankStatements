import axios from 'axios';

export interface AuthUserResponse {
  id: string;
  email: string;
  displayName: string;
  emailVerified: boolean;
}

export interface AuthResponse {
  accessToken: string;
  accessTokenExpiresAt: string;
  user: AuthUserResponse;
}

export async function postExternalLogin(provider: string, idToken: string) {
  const resp = await axios.post<AuthResponse>('/api/auth/external',
    { provider, idToken },
    { withCredentials: true }
  );
  return resp.data;
}

export async function postExternalCode(provider: string, code: string, codeVerifier: string, redirectUri: string) {
  const resp = await axios.post<AuthResponse>('/api/auth/external/code',
    { provider, code, codeVerifier, redirectUri },
    { withCredentials: true }
  );
  return resp.data;
}

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

export async function logout(): Promise<void> {
  try {
    await axios.post('/api/auth/logout', {}, { withCredentials: true });
  } catch {
    // Ignore errors during logout
  }
}
