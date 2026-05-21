import { type FormEvent, useCallback, useEffect, useMemo, useRef, useState } from 'react';
import './App.css';
import { ExternalLoginButtons } from './components/ExternalLoginButtons';
import { refreshAuthToken, logout as apiLogout } from './services/externalAuth';

/** Authentication mode — either login or registration. */
type AuthMode = 'login' | 'register';

/** Authenticated user profile returned from the API. */
type AuthUser = {
  id: string;
  email: string;
  displayName: string;
  emailVerified: boolean;
};

/** Auth response containing access token, expiry, and user info. */
type AuthResponse = {
  accessToken: string;
  accessTokenExpiresAt: string;
  user: AuthUser;
};

/** A bank account belonging to the user. */
type BankAccount = {
  id: string;
  userId: string;
  bankName: string;
  accountName: string;
  accountMask: string | null;
  currency: string;
  createdAt: string;
  updatedAt: string;
};

/** Response after uploading a bank statement. */
type StatementUploadResponse = {
  id: string;
  originalFileName: string;
  storedFileName: string;
  status: string;
  uploadedAt: string;
  parsedTransactionCount: number;
  processedAt?: string | null;
  errorMessage?: string | null;
};

/** Spending aggregated by category. */
type CategorySpending = {
  category: string;
  totalDebit: number;
  transactionCount: number;
};

/** A single recent transaction. */
type RecentTransaction = {
  id: string;
  transactionDate: string;
  description: string;
  amount: number;
  transactionType: 'credit' | 'debit';
  category?: string | null;
};

/** Spending analysis summary for a period. */
type SpendingSummary = {
  periodStart?: string | null;
  periodEnd?: string | null;
  totalCredit: number;
  totalDebit: number;
  netCashflow: number;
  isCashflowPositive: boolean;
  spendingByCategory: CategorySpending[];
  recentTransactions: RecentTransaction[];
};

const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5213';

const TOTAL_ID = '__total__';

/** Main application component with auth flow, account management, statement upload, and spending analysis. */
function App() {
  const [authMode, setAuthMode] = useState<AuthMode>('login');
  const [displayName, setDisplayName] = useState('');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [auth, setAuth] = useState<AuthResponse | null>(null);
  const [isInitialLoading, setIsInitialLoading] = useState(true);
  const [authMessage, setAuthMessage] = useState('');

  // Bank account state
  const [accounts, setAccounts] = useState<BankAccount[]>([]);
  const [selectedAccountId, setSelectedAccountId] = useState<string>(TOTAL_ID);
  const [editingAccountId, setEditingAccountId] = useState<string | null>(null);
  const [editingAccountName, setEditingAccountName] = useState('');
  const [isAccountsLoading, setIsAccountsLoading] = useState(false);
  const [accountsMessage, setAccountsMessage] = useState('');

  // Upload state
  const [selectedFile, setSelectedFile] = useState<File | null>(null);
  const [upload, setUpload] = useState<StatementUploadResponse | null>(null);
  const [pendingStatementId, setPendingStatementId] = useState<string | null>(null);
  const [statementStatus, setStatementStatus] = useState<string | null>(null);
  const [parsedTransactionCount, setParsedTransactionCount] = useState(0);

  // Analysis state
  const [summary, setSummary] = useState<SpendingSummary | null>(null);
  const [isAuthLoading, setIsAuthLoading] = useState(false);
  const [isUploadLoading, setIsUploadLoading] = useState(false);
  const [isSummaryLoading, setIsSummaryLoading] = useState(false);
  const [appMessage, setAppMessage] = useState('');

  // Track current access token for API calls (stored in memory only — never localStorage)
  const accessTokenRef = useRef<string | null>(null);
  const editInputRef = useRef<HTMLInputElement | null>(null);

  const currency = useMemo(
    () => new Intl.NumberFormat('en-AU', { style: 'currency', currency: 'AUD' }),
    []
  );

  // Focus the edit input when entering rename mode
  useEffect(() => {
    if (editingAccountId && editInputRef.current) {
      editInputRef.current.focus();
      editInputRef.current.select();
    }
  }, [editingAccountId]);

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

  // When auth changes, load accounts and summary
  useEffect(() => {
    if (auth) {
      accessTokenRef.current = auth.accessToken;
      void loadAccounts(auth.accessToken);
      void loadSummary(auth.accessToken);
    } else {
      accessTokenRef.current = null;
      setAccounts([]);
      setSelectedAccountId(TOTAL_ID);
      setSummary(null);
      setUpload(null);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [auth]);

  /** Fetches the user's bank accounts from the API. */
  async function loadAccounts(accessToken?: string) {
    const token = accessToken ?? accessTokenRef.current;
    if (!token) return;

    setIsAccountsLoading(true);
    try {
      const response = await authedFetch(`${apiBaseUrl}/api/v1/bank-accounts`);
      if (!response.ok) throw new Error(await response.text());
      const fetched: BankAccount[] = await response.json();
      setAccounts(fetched);

      // If selected account is still TOTAL_ID, keep it; otherwise ensure it still exists
      setSelectedAccountId((prev) => {
        if (prev === TOTAL_ID) return TOTAL_ID;
        if (fetched.some((a) => a.id === prev)) return prev;
        return TOTAL_ID;
      });
    } catch {
      setAccountsMessage('Could not load accounts.');
    } finally {
      setIsAccountsLoading(false);
    }
  }

  /** Adds a new "Untitled" bank account. */
  async function handleAddAccount() {
    if (!auth) return;
    try {
      const response = await authedFetch(`${apiBaseUrl}/api/v1/bank-accounts`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({}),
      });
      if (!response.ok) throw new Error(await response.text());
      const created: BankAccount = await response.json();
      setAccounts((prev) => [...prev, created]);
      setSelectedAccountId(created.id);
      setAccountsMessage('');
    } catch {
      setAccountsMessage('Could not create account.');
    }
  }

  /** Starts inline rename for an account. */
  function handleStartRename(account: BankAccount) {
    setEditingAccountId(account.id);
    setEditingAccountName(account.accountName);
  }

  /** Saves the renamed account. */
  async function handleSaveRename(accountId: string) {
    const name = editingAccountName.trim();
    if (!name || !auth) {
      cancelRename();
      return;
    }

    try {
      const response = await authedFetch(`${apiBaseUrl}/api/v1/bank-accounts/${accountId}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ accountName: name }),
      });
      if (!response.ok) throw new Error(await response.text());
      const updated: BankAccount = await response.json();
      setAccounts((prev) => prev.map((a) => (a.id === accountId ? updated : a)));
      cancelRename();
    } catch {
      setAccountsMessage('Could not rename account.');
      cancelRename();
    }
  }

  function cancelRename() {
    setEditingAccountId(null);
    setEditingAccountName('');
  }

  /** Deletes a bank account. */
  async function handleDeleteAccount(accountId: string) {
    if (!auth) return;
    try {
      const response = await authedFetch(`${apiBaseUrl}/api/v1/bank-accounts/${accountId}`, {
        method: 'DELETE',
      });
      if (!response.ok) throw new Error(await response.text());
      setAccounts((prev) => prev.filter((a) => a.id !== accountId));
      if (selectedAccountId === accountId) {
        setSelectedAccountId(TOTAL_ID);
      }
    } catch {
      setAccountsMessage('Could not delete account.');
    }
  }

  /**
   * Handles authentication form submission (login or register).
   */
  async function handleAuthSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setIsAuthLoading(true);
    setAuthMessage('');

    const path = authMode === 'login' ? '/api/v1/auth/login' : '/api/v1/auth/register';
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
      setAuth(null);
      accessTokenRef.current = null;
      throw new Error('Session expired. Please sign in again.');
    }

    return response;
  }, []);

  /** Handles statement file upload form submission. */
  async function handleUpload(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!auth || !selectedFile || selectedAccountId === TOTAL_ID) return;

    setIsUploadLoading(true);
    setAppMessage('');

    const formData = new FormData();
    formData.append('file', selectedFile);
    formData.append('bankAccountId', selectedAccountId);

    try {
      const response = await authedFetch(`${apiBaseUrl}/api/v1/statements/upload`, {
        method: 'POST',
        body: formData,
      });

      if (!response.ok) {
        throw new Error(await response.text());
      }

      const result = await response.json() as StatementUploadResponse;
      setUpload({ ...result });
      setPendingStatementId(result.id);
      setStatementStatus('uploaded');
      setSelectedFile(null);
    } catch (error) {
      setAppMessage(error instanceof Error ? error.message : 'Statement upload failed.');
    } finally {
      setIsUploadLoading(false);
    }
  }

  // Poll for statement processing status after upload
  useEffect(() => {
    if (!pendingStatementId) return;

    const poll = async () => {
      try {
        const response = await authedFetch(`${apiBaseUrl}/api/v1/statements/${pendingStatementId}`);
        if (!response.ok) throw new Error('Poll failed');
        const data: StatementUploadResponse = await response.json();

        setStatementStatus(data.status);
        setParsedTransactionCount(data.parsedTransactionCount ?? 0);
        setUpload({
          id: data.id,
          originalFileName: data.originalFileName,
          storedFileName: data.storedFileName,
          status: data.status,
          uploadedAt: data.uploadedAt,
          parsedTransactionCount: data.parsedTransactionCount ?? 0,
        });

        if (data.status === 'processed') {
          setPendingStatementId(null);
          void loadSummary();
        } else if (data.status === 'failed') {
          setPendingStatementId(null);
          setAppMessage(data.errorMessage ?? 'Statement processing failed.');
        }
      } catch {
        // Silently retry on next interval
      }
    };

    const interval = setInterval(poll, 2000);
    return () => clearInterval(interval);
  }, [pendingStatementId]);

  /**
   * Loads the spending analysis summary from the API.
   */
  async function loadSummary(accessToken?: string) {
    const token = accessToken ?? accessTokenRef.current;
    if (!token) return;

    setIsSummaryLoading(true);

    try {
      const params = new URLSearchParams();
      if (selectedAccountId !== TOTAL_ID) {
        params.set('bankAccountId', selectedAccountId);
      }

      const query = params.toString();
      const url = `${apiBaseUrl}/api/v1/analysis/summary${query ? `?${query}` : ''}`;
      const response = await authedFetch(url);

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

  /** Reload summary when selected account changes */
  useEffect(() => {
    if (auth) {
      void loadSummary();
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selectedAccountId, auth]);

  /** Signs the user out. */
  async function signOut() {
    await apiLogout();
    setAuth(null);
    accessTokenRef.current = null;
    setEmail('');
    setPassword('');
    setDisplayName('');
    setAppMessage('');
    setAuthMessage('');
    setAccounts([]);
    setSelectedAccountId(TOTAL_ID);
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

  const selectedAccount = selectedAccountId === TOTAL_ID
    ? null
    : accounts.find((a) => a.id === selectedAccountId) ?? null;

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

      {/* Account toolbar */}
      <div className="account-bar">
        <label className="account-bar-label">Account:</label>
        <div className="account-select-wrapper">
          <select
            className="account-select"
            value={selectedAccountId}
            onChange={(e) => setSelectedAccountId(e.target.value)}
          >
            <option value={TOTAL_ID}>Total (all accounts)</option>
            {accounts.length > 0 && <option disabled>──────────</option>}
            {accounts.map((account) => (
              <option key={account.id} value={account.id}>
                {account.accountName}
              </option>
            ))}
          </select>
        </div>
        <div className="account-list">
          {accounts.map((account) => (
            <div className="account-item" key={account.id}>
              {editingAccountId === account.id ? (
                <input
                  ref={editInputRef}
                  className="account-name-edit"
                  value={editingAccountName}
                  onChange={(e) => setEditingAccountName(e.target.value)}
                  onKeyDown={(e) => {
                    if (e.key === 'Enter') void handleSaveRename(account.id);
                    if (e.key === 'Escape') cancelRename();
                  }}
                  onBlur={() => void handleSaveRename(account.id)}
                  maxLength={120}
                />
              ) : (
                <>
                  <span
                    className="account-name-clickable"
                    onClick={() => handleStartRename(account)}
                    title="Click to rename"
                  >
                    {account.accountName}
                  </span>
                  <div className="account-actions">
                    <button
                      className="account-action-btn"
                      type="button"
                      title="Rename"
                      onClick={() => handleStartRename(account)}
                    >
                      ✎
                    </button>
                    <button
                      className="account-action-btn account-action-delete"
                      type="button"
                      title="Delete account and all its statements"
                      onClick={() => {
                        if (window.confirm(`Delete "${account.accountName}" and all its statements?`)) {
                          void handleDeleteAccount(account.id);
                        }
                      }}
                    >
                      ×
                    </button>
                  </div>
                </>
              )}
            </div>
          ))}
        </div>
        <button
          className="account-add-btn"
          type="button"
          onClick={() => void handleAddAccount()}
          disabled={isAccountsLoading}
          title="Add account"
        >
          + Add account
        </button>
        {accountsMessage && <p className="error-text account-message">{accountsMessage}</p>}
      </div>

      <section className="dashboard-grid">
        <form className="panel upload-panel" onSubmit={handleUpload}>
          <div>
            <p className="panel-label">PDF upload</p>
            <h2>Parse a bank statement</h2>
          </div>

          {selectedAccountId === TOTAL_ID ? (
            <p className="empty-state upload-hint">
              Select a specific account above to upload a statement.
            </p>
          ) : (
            <>
              <p className="upload-context">
                Uploading to: <strong>{selectedAccount?.accountName ?? 'Unknown'}</strong>
              </p>
              <label className="file-input">
                <span>{selectedFile ? selectedFile.name : 'Choose a PDF statement'}</span>
                <input
                  type="file"
                  accept="application/pdf,.pdf"
                  onChange={(event) => setSelectedFile(event.target.files?.[0] ?? null)}
                />
              </label>
              <button
                className="primary-button"
                type="submit"
                disabled={!selectedFile || isUploadLoading}
              >
                {isUploadLoading ? 'Uploading...' : 'Upload and analyse'}
              </button>
            </>
          )}

          {upload && (
            <p className={statementStatus === 'failed' ? 'error-text' : 'success-text'}>
              {statementStatus === 'uploaded' && `${upload.originalFileName} uploaded — processing...`}
              {statementStatus === 'processing' && `${upload.originalFileName} processing...`}
              {statementStatus === 'processed' && `${upload.originalFileName} processed with ${parsedTransactionCount} transactions.`}
              {statementStatus === 'failed' && `${upload.originalFileName} processing failed.`}
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
            <button className="secondary-button" type="button" onClick={() => void loadSummary()} disabled={isSummaryLoading}>
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
