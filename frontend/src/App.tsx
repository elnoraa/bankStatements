import { type FormEvent, useCallback, useEffect, useMemo, useRef, useState } from 'react';
import './App.css';
import { ExternalLoginButtons } from './components/ExternalLoginButtons';
import { refreshAuthToken, logout as apiLogout } from './services/externalAuth';

/** Authentication mode — either login or registration. */
type AuthMode = 'login' | 'register';

/** Authenticated user profile returned from the API. */
type AuthUser = {
  /** Unique user identifier. */
  id: string;
  /** User's email address. */
  email: string;
  /** User's display name. */
  displayName: string;
  /** Whether the user's email has been verified. */
  emailVerified: boolean;
};

/** Auth response containing access token, expiry, and user info. */
type AuthResponse = {
  /** JWT access token for API authorization. */
  accessToken: string;
  /** Date/time when the access token expires. */
  accessTokenExpiresAt: string;
  /** Authenticated user profile. */
  user: AuthUser;
};

/** Response after uploading a bank statement. */
type StatementUploadResponse = {
  /** Unique statement identifier. */
  id: string;
  /** Original file name as provided by the user. */
  originalFileName: string;
  /** Unique file name used for server storage. */
  storedFileName: string;
  /** Processing status (e.g., "uploaded", "processing", "completed"). */
  status: string;
  /** ISO timestamp of when the statement was uploaded. */
  uploadedAt: string;
  /** Number of transactions parsed from the statement. */
  parsedTransactionCount: number;
};

/** Spending aggregated by category. */
type CategorySpending = {
  /** The spending category name. */
  category: string;
  /** Total debit amount in this category. */
  totalDebit: number;
  /** Number of transactions in this category. */
  transactionCount: number;
};

/** A single recent transaction. */
type RecentTransaction = {
  /** Unique transaction identifier. */
  id: string;
  /** ISO date of the transaction. */
  transactionDate: string;
  /** Transaction description or merchant name. */
  description: string;
  /** Transaction amount. */
  amount: number;
  /** "credit" or "debit". */
  transactionType: 'credit' | 'debit';
  /** Assigned spending category, if any. */
  category?: string | null;
};

/** Spending analysis summary for a period. */
type SpendingSummary = {
  /** Optional start date of the analysis period. */
  periodStart?: string | null;
  /** Optional end date of the analysis period. */
  periodEnd?: string | null;
  /** Total credit (income) amount. */
  totalCredit: number;
  /** Total debit (expense) amount. */
  totalDebit: number;
  /** Net cash flow (credit - debit). */
  netCashflow: number;
  /** Whether net cash flow is positive. */
  isCashflowPositive: boolean;
  /** Spending breakdown by category. */
  spendingByCategory: CategorySpending[];
  /** Recent individual transactions. */
  recentTransactions: RecentTransaction[];
};

const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5213';

/** Main application component with auth flow, statement upload, and spending analysis. */
function App() {
  const [authMode, setAuthMode] = useState<AuthMode>('login');
  const [displayName, setDisplayName] = useState('');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [auth, setAuth] = useState<AuthResponse | null>(null);
  const [isInitialLoading, setIsInitialLoading] = useState(true);
  const [authMessage, setAuthMessage] = useState('');
  const [selectedFile, setSelectedFile] = useState<File | null>(null);
  const [upload, setUpload] = useState<StatementUploadResponse | null>(null);
  const [summary, setSummary] = useState<SpendingSummary | null>(null);
  const [isAuthLoading, setIsAuthLoading] = useState(false);
  const [isUploadLoading, setIsUploadLoading] = useState(false);
  const [isSummaryLoading, setIsSummaryLoading] = useState(false);
  const [appMessage, setAppMessage] = useState('');

  // Track current access token for API calls (stored in memory only — never localStorage)
  const accessTokenRef = useRef<string | null>(null);

  const currency = useMemo(
    () => new Intl.NumberFormat('en-AU', { style: 'currency', currency: 'AUD' }),
    []
  );

  // On mount, try to refresh the auth session via httpOnly cookie
  useEffect(() => {
    const init = async () => {
      const result = await refreshAuthToken();
      if (result) {
        accessTokenRef.current = result.accessToken;
        setAuth(result);
      }
      setIsInitialLoading(false);
    };
    void init();
  }, []);

  // When auth changes, load summary or clear state
  useEffect(() => {
    if (auth) {
      accessTokenRef.current = auth.accessToken;
      void loadSummary(auth.accessToken);
    } else {
      accessTokenRef.current = null;
      setSummary(null);
      setUpload(null);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [auth]);

  /**
   * Handles authentication form submission (login or register).
   * On success, stores the auth response and clears the password field.
   */
  async function handleAuthSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setIsAuthLoading(true);
    setAuthMessage('');

    const path = authMode === 'login' ? '/api/auth/login' : '/api/auth/register';
    const body = authMode === 'login'
      ? { email, password }
      : { email, password, ...(displayName ? { displayName } : {}) };

    try {
      const response = await fetch(`${apiBaseUrl}${path}`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
        credentials: 'include',
      });

      if (!response.ok) {
        throw new Error(await response.text());
      }

      const data = await response.json() as AuthResponse;
      accessTokenRef.current = data.accessToken;
      setAuth(data);
      setPassword('');
    } catch (error) {
      setAuthMessage(error instanceof Error ? error.message : 'Authentication failed.');
    } finally {
      setIsAuthLoading(false);
    }
  }

  /**
   * Makes an authenticated API call, automatically retrying with a refreshed token on 401.
   * If token refresh fails, clears auth state and throws.
   */
  const authedFetch = useCallback(async (url: string, options: RequestInit = {}): Promise<Response> => {
    const token = accessTokenRef.current;
    if (!token) {
      throw new Error('Not authenticated');
    }

    const response = await fetch(url, {
      ...options,
      headers: {
        ...options.headers,
        'Authorization': `Bearer ${token}`,
      },
    });

    // If 401, try to refresh the token and retry once
    if (response.status === 401) {
      const refreshed = await refreshAuthToken();
      if (refreshed) {
        accessTokenRef.current = refreshed.accessToken;
        setAuth(refreshed);
        const retryResponse = await fetch(url, {
          ...options,
          headers: {
            ...options.headers,
            'Authorization': `Bearer ${refreshed.accessToken}`,
          },
        });
        return retryResponse;
      }
      // Refresh failed — clear auth state
      setAuth(null);
      accessTokenRef.current = null;
      throw new Error('Session expired. Please sign in again.');
    }

    return response;
  }, []);

  /** Handles statement file upload form submission. */
  async function handleUpload(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    if (!auth || !selectedFile) {
      return;
    }

    setIsUploadLoading(true);
    setAppMessage('');

    const formData = new FormData();
    formData.append('file', selectedFile);

    try {
      const response = await authedFetch(`${apiBaseUrl}/api/statements/upload`, {
        method: 'POST',
        body: formData,
      });

      if (!response.ok) {
        throw new Error(await response.text());
      }

      setUpload(await response.json() as StatementUploadResponse);
      setSelectedFile(null);
      await loadSummary();
    } catch (error) {
      setAppMessage(error instanceof Error ? error.message : 'Statement upload failed.');
    } finally {
      setIsUploadLoading(false);
    }
  }

  /**
   * Loads the spending analysis summary from the API.
   * @param accessToken - Optional explicit access token to use. Falls back to the ref-stored token.
   */
  async function loadSummary(accessToken?: string) {
    const token = accessToken ?? accessTokenRef.current;
    if (!token) {
      return;
    }

    setIsSummaryLoading(true);

    try {
      const response = await authedFetch(`${apiBaseUrl}/api/analysis/summary`);

      if (!response.ok) {
        throw new Error(await response.text());
      }

      setSummary(await response.json() as SpendingSummary);
    } catch (error) {
      setAppMessage(error instanceof Error ? error.message : 'Could not load analysis.');
    } finally {
      setIsSummaryLoading(false);
    }
  }

  /** Signs the user out: calls the logout API and clears all auth state. */
  async function signOut() {
    await apiLogout();
    setAuth(null);
    accessTokenRef.current = null;
    setEmail('');
    setPassword('');
    setDisplayName('');
    setAppMessage('');
    setAuthMessage('');
  }

  // Show loading state while checking for existing session
  if (isInitialLoading) {
    return (
      <main className="app-shell auth-layout">
        <section className="auth-copy">
          <p className="eyebrow">Bank statement analysis</p>
          <h1>Loading...</h1>
        </section>
      </main>
    );
  }

  if (!auth) {
    return (
      <main className="app-shell auth-layout">
        <section className="auth-copy">
          <p className="eyebrow">Bank statement analysis</p>
          <h1>Upload PDF statements and see your cashflow clearly.</h1>
          <p>
            Sign in to parse statement PDFs, total credits and debits, and review spending by category.
          </p>
        </section>

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
      </main>
    );
  }

  return (
    <main className="app-shell">
      <header className="topbar">
        <div>
          <p className="eyebrow">Statement workspace</p>
          <h1>{auth.user.displayName}</h1>
        </div>
        <button className="secondary-button" type="button" onClick={signOut}>
          Sign out
        </button>
      </header>

      <section className="dashboard-grid">
        <form className="panel upload-panel" onSubmit={handleUpload}>
          <div>
            <p className="panel-label">PDF upload</p>
            <h2>Parse a bank statement</h2>
          </div>
          <label className="file-input">
            <span>{selectedFile ? selectedFile.name : 'Choose a PDF statement'}</span>
            <input
              type="file"
              accept="application/pdf,.pdf"
              onChange={(event) => setSelectedFile(event.target.files?.[0] ?? null)}
            />
          </label>
          <button className="primary-button" type="submit" disabled={!selectedFile || isUploadLoading}>
            {isUploadLoading ? 'Uploading...' : 'Upload and analyse'}
          </button>
          {upload && (
            <p className="success-text">
              {upload.originalFileName} processed with {upload.parsedTransactionCount} transactions.
            </p>
          )}
          {appMessage && <p className="error-text">{appMessage}</p>}
        </form>

        <section className="metric-strip">
          <article className="metric">
            <span>Total credit</span>
            <strong>{currency.format(summary?.totalCredit ?? 0)}</strong>
          </article>
          <article className="metric">
            <span>Total debit</span>
            <strong>{currency.format(summary?.totalDebit ?? 0)}</strong>
          </article>
          <article className={summary?.isCashflowPositive ? 'metric positive' : 'metric negative'}>
            <span>Net cashflow</span>
            <strong>{currency.format(summary?.netCashflow ?? 0)}</strong>
          </article>
        </section>
      </section>

      <section className="content-grid">
        <section className="panel">
          <div className="section-heading">
            <div>
              <p className="panel-label">Categories</p>
              <h2>Spending breakdown</h2>
            </div>
            <button className="secondary-button" type="button" onClick={() => loadSummary()} disabled={isSummaryLoading}>
              Refresh
            </button>
          </div>
          <div className="category-list">
            {(summary?.spendingByCategory.length ?? 0) === 0 && (
              <p className="empty-state">Upload a PDF statement to see category totals.</p>
            )}
            {summary?.spendingByCategory.map((category) => (
              <div className="category-row" key={category.category}>
                <div>
                  <strong>{category.category}</strong>
                  <span>{category.transactionCount} transactions</span>
                </div>
                <b>{currency.format(category.totalDebit)}</b>
              </div>
            ))}
          </div>
        </section>

        <section className="panel">
          <div className="section-heading">
            <div>
              <p className="panel-label">Transactions</p>
              <h2>Recent activity</h2>
            </div>
            <span className="date-range">
              {summary?.periodStart && summary?.periodEnd
                ? `${summary.periodStart} to ${summary.periodEnd}`
                : 'No period yet'}
            </span>
          </div>
          <div className="transaction-list">
            {(summary?.recentTransactions.length ?? 0) === 0 && (
              <p className="empty-state">Parsed transactions will appear here.</p>
            )}
            {summary?.recentTransactions.map((transaction) => (
              <div className="transaction-row" key={transaction.id}>
                <div>
                  <strong>{transaction.description}</strong>
                  <span>{transaction.transactionDate} | {transaction.category ?? 'Uncategorised'}</span>
                </div>
                <b className={transaction.transactionType}>
                  {transaction.transactionType === 'credit' ? '+' : '-'}
                  {currency.format(transaction.amount)}
                </b>
              </div>
            ))}
          </div>
        </section>
      </section>
    </main>
  );
}

export default App;
