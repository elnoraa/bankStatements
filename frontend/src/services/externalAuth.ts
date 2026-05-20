import axios from 'axios';

export interface AuthResponse {
  accessToken: string;
  expiresAt: string;
  refreshToken: string;
  refreshTokenExpiresAt: string;
}

export async function postExternalLogin(provider: string, idToken: string) {
  const resp = await axios.post<AuthResponse>('/api/auth/external', { provider, idToken });
  // store tokens (simple example)
  localStorage.setItem('accessToken', resp.data.accessToken);
  localStorage.setItem('refreshToken', resp.data.refreshToken);
  return resp.data;
}

export async function postExternalCode(provider: string, code: string, codeVerifier: string, redirectUri: string) {
  const resp = await axios.post<AuthResponse>('/api/auth/external/code', { provider, code, codeVerifier, redirectUri });
  localStorage.setItem('accessToken', resp.data.accessToken);
  localStorage.setItem('refreshToken', resp.data.refreshToken);
  return resp.data;
}
