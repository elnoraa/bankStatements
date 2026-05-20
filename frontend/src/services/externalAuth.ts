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
  refreshToken: string;
  refreshTokenExpiresAt: string;
  user: AuthUserResponse;
}

export async function postExternalLogin(provider: string, idToken: string) {
  const resp = await axios.post<AuthResponse>('/api/auth/external', { provider, idToken });
  localStorage.setItem('statements.auth', JSON.stringify(resp.data));
  return resp.data;
}

export async function postExternalCode(provider: string, code: string, codeVerifier: string, redirectUri: string) {
  const resp = await axios.post<AuthResponse>('/api/auth/external/code', { provider, code, codeVerifier, redirectUri });
  localStorage.setItem('statements.auth', JSON.stringify(resp.data));
  return resp.data;
}
