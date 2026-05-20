import React, { useCallback } from 'react';
import { postExternalLogin } from '../services/externalAuth';

/**
 * Opens a centered popup window for OAuth authentication.
 * @param url - The OAuth authorization URL.
 * @param name - The window name (default "oauth").
 * @param w - The popup width in pixels (default 500).
 * @param h - The popup height in pixels (default 700).
 * @returns A reference to the opened window.
 */
function openPopup(url: string, name = 'oauth', w = 500, h = 700) {
  const left = window.screenX + (window.outerWidth - w) / 2;
  const top = window.screenY + (window.outerHeight - h) / 2;
  return window.open(url, name, `toolbar=0,location=0,status=0,menubar=0,scrollbars=1,resizable=1,width=${w},height=${h},top=${top},left=${left}`);
}

/**
 * Generates a cryptographically random PKCE code verifier (43 chars, base64url-encoded).
 * @returns A URL-safe base64-encoded random string.
 */
function generateCodeVerifier(): string {
  // 32 random bytes → base64url = 43 chars (meets PKCE 43–128 char spec)
  const bytes = new Uint8Array(32);
  crypto.getRandomValues(bytes);
  let str = '';
  for (let i = 0; i < bytes.byteLength; i++) str += String.fromCharCode(bytes[i]);
  return btoa(str).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

/** Generates a unique nonce string for OAuth state parameters. */
function makeNonce() {
  return Math.random().toString(36).slice(2) + Date.now().toString(36);
}

/**
 * Encodes an ArrayBuffer as a base64url string.
 * @param arrayBuffer - The binary data to encode.
 * @returns A URL-safe base64 string without padding.
 */
function base64UrlEncode(arrayBuffer: ArrayBuffer) {
  const bytes = new Uint8Array(arrayBuffer);
  let str = '';
  for (let i = 0; i < bytes.byteLength; i++) str += String.fromCharCode(bytes[i]);
  return btoa(str).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

/**
 * Computes the SHA-256 hash of a string, returning a base64url-encoded digest.
 * @param text - The input string to hash.
 * @returns The base64url-encoded SHA-256 digest.
 */
async function sha256(text: string) {
  const enc = new TextEncoder();
  const data = enc.encode(text);
  const hash = await window.crypto.subtle.digest('SHA-256', data);
  return base64UrlEncode(hash);
}

export const ExternalLoginButtons: React.FC = () => {
  const onMessage = useCallback(async (e: MessageEvent) => {
    try {
      if (e.origin !== window.location.origin) return;
      const data = e.data as any;
      if (!data) return;
      // PKCE flow returns code
      if (data.code) {
        const provider = (data.state && data.state.includes(':') ? data.state.split(':')[1] : null) || 'unknown';
        const state = data.state;
        const key = `pkce:${state}`;
        const codeVerifier = sessionStorage.getItem(key);
        sessionStorage.removeItem(key);
        if (!codeVerifier) throw new Error('Missing code verifier');
        const redirectUri = `${window.location.origin}/auth-callback.html`;
        await import('../services/externalAuth').then(mod => mod.postExternalCode(provider, data.code, codeVerifier, redirectUri));
      } else if (data.id_token || data.access_token) {
        const provider = (data.state && data.state.includes(':') ? data.state.split(':')[1] : null) || 'unknown';
        const idToken = data.id_token || data.access_token;
        await postExternalLogin(provider, idToken);
      }
      // optionally: refresh UI / redirect
      window.location.reload();
    } catch (err) {
      console.error('External login failed', err);
    }
  }, []);

  React.useEffect(() => {
    window.addEventListener('message', onMessage);
    return () => window.removeEventListener('message', onMessage);
  }, [onMessage]);

  const startAuth = useCallback(async (provider: 'Google' | 'Auth0') => {
    const origin = window.location.origin;
    const redirectUri = `${origin}/auth-callback.html`;
    const state = `${makeNonce()}:${provider}`;
    const codeVerifier = generateCodeVerifier();
    const codeChallenge = await sha256(codeVerifier);
    // store verifier keyed by state
    sessionStorage.setItem(`pkce:${state}`, codeVerifier);

    let url = '';
    if (provider === 'Google') {
      const clientId = import.meta.env.VITE_GOOGLE_CLIENT_ID;
      const scope = encodeURIComponent('openid email profile');
      url = `https://accounts.google.com/o/oauth2/v2/auth?client_id=${clientId}&redirect_uri=${encodeURIComponent(redirectUri)}&response_type=code&scope=${scope}&code_challenge=${codeChallenge}&code_challenge_method=S256&state=${state}&prompt=select_account`;
    } else if (provider === 'Auth0') {
      const domain = import.meta.env.VITE_AUTH0_DOMAIN;
      const clientId = import.meta.env.VITE_AUTH0_CLIENT_ID;
      const scope = encodeURIComponent('openid profile email');
      url = `https://${domain}/authorize?client_id=${clientId}&redirect_uri=${encodeURIComponent(redirectUri)}&response_type=code&scope=${scope}&code_challenge=${codeChallenge}&code_challenge_method=S256&state=${state}`;
    }

    const popup = openPopup(url);
    if (!popup) {
      alert('Popup blocked. Please allow popups for this site.');
      return;
    }
  }, []);

  return (
    <div className="external-login-buttons">
      <button onClick={() => startAuth('Google')}>Sign in with Google</button>
      <button onClick={() => startAuth('Auth0')}>Sign in with Auth0</button>
    </div>
  );
};

export default ExternalLoginButtons;
