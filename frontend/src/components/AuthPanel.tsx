import { type FormEvent } from 'react';
import { ExternalLoginButtons } from './ExternalLoginButtons';
import type { AuthMode, AuthResponse } from '../types';

interface AuthPanelProps {
  authMode: AuthMode;
  setAuthMode: (mode: AuthMode) => void;
  displayName: string;
  setDisplayName: (name: string) => void;
  email: string;
  setEmail: (email: string) => void;
  password: string;
  setPassword: (password: string) => void;
  authMessage: string;
  isAuthLoading: boolean;
  handleAuthSubmit: (event: FormEvent<HTMLFormElement>) => Promise<void>;
}

export function AuthPanel({
  authMode, setAuthMode, displayName, setDisplayName,
  email, setEmail, password, setPassword,
  authMessage, isAuthLoading, handleAuthSubmit,
}: AuthPanelProps) {
  return (
    <section className="panel auth-panel" aria-label="Authentication">
      <div className="segmented-control">
        <button
          className={authMode === 'login' ? 'active' : ''}
          type="button"
          onClick={() => setAuthMode('login')}
        >
          Login
        </button>
        <button
          className={authMode === 'register' ? 'active' : ''}
          type="button"
          onClick={() => setAuthMode('register')}
        >
          Register
        </button>
      </div>

      <form className="form-stack" onSubmit={handleAuthSubmit}>
        {authMode === 'register' && (
          <label>
            Display name
            <input
              value={displayName}
              onChange={(event) => setDisplayName(event.target.value)}
              maxLength={120}
            />
          </label>
        )}
        <label>
          Email
          <input
            type="email"
            value={email}
            onChange={(event) => setEmail(event.target.value)}
            required
          />
        </label>
        <label>
          Password
          <input
            type="password"
            value={password}
            onChange={(event) => setPassword(event.target.value)}
            minLength={8}
            required
          />
        </label>
        {authMessage && <p className="error-text">{authMessage}</p>}
        <button className="primary-button" type="submit" disabled={isAuthLoading}>
          {isAuthLoading ? 'Working...' : authMode === 'login' ? 'Login' : 'Create account'}
        </button>
      </form>

      <div className="external-login-divider">
        <span>or</span>
      </div>

      <ExternalLoginButtons />
    </section>
  );
}
