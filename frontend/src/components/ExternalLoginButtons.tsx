import React, { useCallback } from 'react';
import { postExternalLogin } from '../services/externalAuth';

function openPopup(url: string, name = 'oauth', w = 500, h = 700) {
  const left = window.screenX + (window.outerWidth - w) / 2;
  const top = window.screenY + (window.outerHeight - h) / 2;
  return window.open(url, name, `toolbar=0,location=0,status=0,menubar=0,scrollbars=1,resizable=1,width=${w},height=${h},top=${top},left=${left}`);
}

function makeNonce() {
  return Math.random().toString(36).slice(2) + Date.now().toString(36);
}

function base64UrlEncode(arrayBuffer: ArrayBuffer) {
  const bytes = new Uint8Array(arrayBuffer);
  let str = '';
  for (let i = 0; i < bytes.byteLength; i++) str += String.fromCharCode(bytes[i]);
  return btoa(str).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

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
        const provider = data.provider || 'unknown';
        const state = data.state;
        const key = `pkce:${state}`;
        const codeVerifier = sessionStorage.getItem(key);
        sessionStorage.removeItem(key);
        if (!codeVerifier) throw new Error('Missing code verifier');
        const redirectUri = `${window.location.origin}/auth-callback.html?provider=${encodeURIComponent(provider)}`;
        await import('../services/externalAuth').then(mod => mod.postExternalCode(provider, data.code, codeVerifier, redirectUri));
      } else if (data.id_token || data.access_token) {
        const provider = data.provider || 'unknown';
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

  const startAuth = useCallback(async (provider: 'Google' | 'Microsoft' | 'Auth0') => {
    const origin = window.location.origin;
    const redirectUri = `${origin}/auth-callback.html?provider=${encodeURIComponent(provider)}`;
    const state = makeNonce();
    const codeVerifier = makeNonce() + makeNonce();
    const codeChallenge = await sha256(codeVerifier);
    // store verifier keyed by state
    sessionStorage.setItem(`pkce:${state}`, codeVerifier);

    let url = '';
    if (provider === 'Google') {
      const clientId = import.meta.env.VITE_GOOGLE_CLIENT_ID;
      const scope = encodeURIComponent('openid email profile');
      url = `https://accounts.google.com/o/oauth2/v2/auth?client_id=${clientId}&redirect_uri=${encodeURIComponent(redirectUri)}&response_type=code&scope=${scope}&code_challenge=${codeChallenge}&code_challenge_method=S256&state=${state}&prompt=select_account`;
    } else if (provider === 'Microsoft') {
      const clientId = import.meta.env.VITE_MICROSOFT_CLIENT_ID;
      const scope = encodeURIComponent('openid email profile');
      url = `https://login.microsoftonline.com/common/oauth2/v2.0/authorize?client_id=${clientId}&redirect_uri=${encodeURIComponent(redirectUri)}&response_type=code&scope=${scope}&code_challenge=${codeChallenge}&code_challenge_method=S256&state=${state}`;
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
    <div>
      <button onClick={() => startAuth('Google')}>Sign in with Google</button>
      <button onClick={() => startAuth('Microsoft')}>Sign in with Microsoft</button>
      <button onClick={() => startAuth('Auth0')}>Sign in with Auth0</button>
    </div>
  );
};

export default ExternalLoginButtons;
