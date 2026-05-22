import { type FormEvent, useState } from 'react';
import { ExternalLoginButtons } from './ExternalLoginButtons';
import type { AuthMode, AuthView } from '../types';

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
  authView: AuthView;
  setAuthView: (view: AuthView) => void;
  onForgotPassword: (email: string) => Promise<void>;
  onVerifyEmail: (token: string) => Promise<void>;
  onResetPassword: (token: string, newPassword: string) => Promise<void>;
}

export function AuthPanel({
  authMode, setAuthMode, displayName, setDisplayName,
  email, setEmail, password, setPassword,
  authMessage, isAuthLoading, handleAuthSubmit,
  authView, setAuthView,
  onForgotPassword, onVerifyEmail, onResetPassword,
}: AuthPanelProps) {
  const [forgotEmail, setForgotEmail] = useState('');
  const [verifyToken, setVerifyToken] = useState('');
  const [resetToken, setResetToken] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');

  if (authView === 'forgot-password') {
    return (
      <section className="panel auth-panel" aria-label="Forgot password">
        <h2>Reset your password</h2>
        <form className="form-stack" onSubmit={async (e) => {
          e.preventDefault();
          await onForgotPassword(forgotEmail);
          setAuthView('email-sent');
        }}>
          <label>
            Email address
            <input
              type="email"
              value={forgotEmail}
              onChange={(e) => setForgotEmail(e.target.value)}
              required
            />
          </label>
          {authMessage && <p className="error-text">{authMessage}</p>}
          <button className="primary-button" type="submit" disabled={isAuthLoading}>
            {isAuthLoading ? 'Sending...' : 'Send reset link'}
          </button>
          <button className="secondary-button" type="button" onClick={() => setAuthView('login')}>
            Back to login
          </button>
        </form>
      </section>
    );
  }

  if (authView === 'email-sent') {
    return (
      <section className="panel auth-panel" aria-label="Email sent">
        <h2>Check your email</h2>
        <p>If the email exists, a reset link has been sent. Please check your inbox.</p>
        <button className="secondary-button" type="button" onClick={() => setAuthView('login')}>
          Back to login
        </button>
      </section>
    );
  }

  if (authView === 'verify-email') {
    return (
      <section className="panel auth-panel" aria-label="Verify email">
        <h2>Verify your email</h2>
        <p>Enter the verification code sent to your email address.</p>
        <form className="form-stack" onSubmit={async (e) => {
          e.preventDefault();
          await onVerifyEmail(verifyToken);
        }}>
          <label>
            Verification code
            <input
              value={verifyToken}
              onChange={(e) => setVerifyToken(e.target.value)}
              required
              placeholder="Paste your verification code here"
            />
          </label>
          {authMessage && <p className={authMessage.includes('successful') ? 'success-text' : 'error-text'}>{authMessage}</p>}
          <button className="primary-button" type="submit" disabled={isAuthLoading}>
            {isAuthLoading ? 'Verifying...' : 'Verify email'}
          </button>
        </form>
      </section>
    );
  }

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
        {authMode === 'login' && (
          <button
            type="button"
            className="link-button"
            onClick={() => setAuthView('forgot-password')}
          >
            Forgot password?
          </button>
        )}
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
