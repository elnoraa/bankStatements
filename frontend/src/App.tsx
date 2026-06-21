import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import './App.css';
import { refreshAuthToken, logout as apiLogout } from './services/externalAuth';
import { AuthPanel } from './components/AuthPanel';
import { AccountToolbar } from './components/AccountToolbar';
import { UploadPanel } from './components/UploadPanel';
import { MetricStrip } from './components/MetricStrip';
import { SpendingBreakdown } from './components/SpendingBreakdown';
import { RecentActivity } from './components/RecentActivity';
import { StatementManager } from './components/StatementManager';
import { BudgetManager } from './components/BudgetManager';
import { BasiqPanel } from './components/BasiqPanel';
import { useStatementHub, type StatementStatusUpdate } from './hooks/useStatementHub';
import type { AuthMode, AuthResponse, AuthView, BankAccount, StatementUploadResponse, SpendingSummary } from './types';
import { apiBaseUrl, TOTAL_ID } from './types';
import type { AuthResponse as ExternalAuthResponse } from './services/externalAuth';

/** Main application component with auth flow, account management, statement upload, and spending analysis. */
function App() {
  const [authMode, setAuthMode] = useState<AuthMode>('login');
  const [authView, setAuthView] = useState<AuthView>('login');
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

  // Category options for transaction editing
  const [categories, setCategories] = useState<{ id: string; name: string; transactionType: string }[]>([]);

  // Budget progress state
  const [budgetProgress, setBudgetProgress] = useState<{ categoryName: string; budgetAmount: number; actualSpending: number; remaining: number; isOverBudget: boolean; percentageUsed: number }[]>([]);

  // Analysis state
  const [summary, setSummary] = useState<SpendingSummary | null>(null);
  const [statementRefreshKey, setStatementRefreshKey] = useState(0);
  const [isAuthLoading, setIsAuthLoading] = useState(false);
  const [isUploadLoading, setIsUploadLoading] = useState(false);
  const [isSummaryLoading, setIsSummaryLoading] = useState(false);
  const [appMessage, setAppMessage] = useState('');

  // Refresh counter to trigger transaction list reload after upload/delete
  const [transactionRefreshKey, setTransactionRefreshKey] = useState(0);

  // SignalR state: becomes true when hub connection is established
  const [signalRReady, setSignalRReady] = useState(false);

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
      void loadCategories();
      void loadBudgetProgress();
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

      setSelectedAccountId((prev) => {
        if (prev === TOTAL_ID) return TOTAL_ID;
        if (fetched.some((a) => a.id === prev)) return prev;
        return TOTAL_ID;
      });
    } catch {
      // Silently ignore load failures — empty state handles it
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
  async function handleAuthSubmit(event: React.FormEvent<HTMLFormElement>) {
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

  /** Sends a password reset email for the given address. */
  async function handleForgotPassword(forgotEmail: string) {
    setIsAuthLoading(true);
    setAuthMessage('');
    try {
      const response = await fetch(`${apiBaseUrl}/api/v1/auth/forgot-password`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ email: forgotEmail }),
        credentials: 'include',
      });
      if (!response.ok) throw new Error(await response.text());
    } catch (error) {
      setAuthMessage(error instanceof Error ? error.message : 'Request failed.');
    } finally {
      setIsAuthLoading(false);
    }
  }

  /** Verifies email using a verification token. */
  async function handleVerifyEmail(token: string) {
    setIsAuthLoading(true);
    setAuthMessage('');
    try {
      const response = await fetch(`${apiBaseUrl}/api/v1/auth/verify-email`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ token }),
        credentials: 'include',
      });
      if (!response.ok) throw new Error(await response.text());
      const data = await response.json();
      setAuthMessage(data.message ?? 'Email verified successfully.');
      // Refresh auth to reflect verified status
      if (auth) {
        setAuth({ ...auth, user: { ...auth.user, emailVerified: true } });
      }
    } catch (error) {
      setAuthMessage(error instanceof Error ? error.message : 'Verification failed.');
    } finally {
      setIsAuthLoading(false);
    }
  }

  /** Resets password using a reset token. */
  async function handleResetPassword(resetToken: string, newPassword: string) {
    setIsAuthLoading(true);
    setAuthMessage('');
    try {
      const response = await fetch(`${apiBaseUrl}/api/v1/auth/reset-password`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ token: resetToken, newPassword }),
        credentials: 'include',
      });
      if (!response.ok) throw new Error(await response.text());
      const data = await response.json();
      setAuthMessage(data.message ?? 'Password reset successfully.');
      setAuthView('login');
    } catch (error) {
      setAuthMessage(error instanceof Error ? error.message : 'Reset failed.');
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
  async function handleUpload(event: React.FormEvent<HTMLFormElement>) {
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
      setStatementStatus(result.status);
      setSelectedFile(null);
    } catch (error) {
      setAppMessage(error instanceof Error ? error.message : 'Statement upload failed.');
    } finally {
      setIsUploadLoading(false);
    }
  }

  // Poll for statement processing status after upload (fallback when SignalR is not connected)
  useEffect(() => {
    if (!pendingStatementId || signalRReady) return;

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

        setStatementRefreshKey((k) => k + 1);

        if (data.status === 'processed') {
          setPendingStatementId(null);
          void loadSummary();
          setTransactionRefreshKey((k) => k + 1);
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
      void loadBudgetProgress();
    } catch (error) {
      setAppMessage(error instanceof Error ? error.message : 'Could not load analysis.');
    } finally {
      setIsSummaryLoading(false);
    }
  }

  /** Loads budget progress for the given month and account. Defaults to current month. */
  async function loadBudgetProgress(year?: number, month?: number) {
    if (!year || !month) {
      const now = new Date();
      year = now.getFullYear();
      month = now.getMonth() + 1;
    }
    const params = new URLSearchParams({ year: year.toString(), month: month.toString() });
    if (selectedAccountId !== TOTAL_ID) {
      params.set('bankAccountId', selectedAccountId);
    }
    try {
      const response = await authedFetch(`${apiBaseUrl}/api/v1/budgets/progress?${params.toString()}`);
      if (!response.ok) return;
      setBudgetProgress(await response.json());
    } catch {
      // Non-critical
    }
  }

  /** Loads available categories for transaction editing. */
  async function loadCategories() {
    try {
      const response = await authedFetch(`${apiBaseUrl}/api/v1/analysis/categories`);
      if (!response.ok) return;
      const data = await response.json();
      setCategories(data);
    } catch {
      // Categories are non-critical; silently fail
    }
  }

  /** Downloads transactions as CSV for the current filters. */
  async function handleDownloadCsv() {
    const token = accessTokenRef.current;
    if (!token) return;

    const params = new URLSearchParams();
    if (selectedAccountId !== TOTAL_ID) {
      params.set('bankAccountId', selectedAccountId);
    }

    try {
      const response = await fetch(
        `${apiBaseUrl}/api/v1/analysis/export?${params.toString()}`,
        { headers: { 'Authorization': `Bearer ${token}` } }
      );
      if (!response.ok) return;

      const blob = await response.blob();
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `transactions-${new Date().toISOString().slice(0, 10)}.csv`;
      a.click();
      URL.revokeObjectURL(url);
    } catch {
      // Silently fail
    }
  }

  /** Updates local state after a transaction edit succeeds. */
  function handleTransactionUpdated(id: string, updates: Record<string, unknown>) {
    setSummary((prev) => {
      if (!prev) return prev;
      return {
        ...prev,
        recentTransactions: prev.recentTransactions.map((t) =>
          t.id === id ? { ...t, ...updates } as typeof t : t
        ),
      };
    });
  }

  /** Handles successful external login (Google/Auth0 SSO) by setting auth state directly. */
  function handleExternalLogin(response: ExternalAuthResponse) {
    accessTokenRef.current = response.accessToken;
    setAuth(response);
  }

  /** Reload summary when selected account changes */
  useEffect(() => {
    if (auth) {
      void loadSummary();
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selectedAccountId, auth]);

  // Keep a ref to the latest loadSummary so SignalR handler never has stale closure
  const loadSummaryRef = useRef(loadSummary);
  loadSummaryRef.current = loadSummary;

  // Handle real-time status updates from SignalR
  const handleStatusUpdate = useCallback((update: StatementStatusUpdate) => {
    setStatementStatus(update.status);
    setParsedTransactionCount(update.parsedTransactionCount ?? 0);
    setUpload((prev) => prev ? { ...prev, status: update.status, parsedTransactionCount: update.parsedTransactionCount ?? 0 } : prev);
    setPendingStatementId(null);

    setStatementRefreshKey((k) => k + 1);

    if (update.status === 'processed') {
      void loadSummaryRef.current();
      setTransactionRefreshKey((k) => k + 1);
    } else if (update.status === 'failed') {
      setAppMessage(update.errorMessage ?? 'Statement processing failed.');
    }
  }, []);

  // SignalR hub connection for real-time status updates
  useStatementHub(
    accessTokenRef.current,
    handleStatusUpdate,
    () => setSignalRReady(true),
    () => setSignalRReady(false),
  );

  // Auto-dismiss the upload success message after 5 seconds
  useEffect(() => {
    if (statementStatus === 'processed') {
      const timer = setTimeout(() => {
        setUpload(null);
        setStatementStatus(null);
      }, 5000);
      return () => clearTimeout(timer);
    }
  }, [statementStatus]);

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

        <AuthPanel
          authMode={authMode}
          setAuthMode={setAuthMode}
          displayName={displayName}
          setDisplayName={setDisplayName}
          email={email}
          setEmail={setEmail}
          password={password}
          setPassword={setPassword}
          authMessage={authMessage}
          isAuthLoading={isAuthLoading}
          handleAuthSubmit={handleAuthSubmit}
          authView={authView}
          setAuthView={setAuthView}
          onForgotPassword={handleForgotPassword}
          onVerifyEmail={handleVerifyEmail}
          onResetPassword={handleResetPassword}
          onExternalLogin={handleExternalLogin}
        />
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

      <AccountToolbar
        accounts={accounts}
        selectedAccountId={selectedAccountId}
        setSelectedAccountId={setSelectedAccountId}
        editingAccountId={editingAccountId}
        editingAccountName={editingAccountName}
        setEditingAccountName={setEditingAccountName}
        editInputRef={editInputRef}
        handleStartRename={handleStartRename}
        handleSaveRename={handleSaveRename}
        cancelRename={cancelRename}
        handleDeleteAccount={handleDeleteAccount}
        handleAddAccount={handleAddAccount}
        isAccountsLoading={isAccountsLoading}
        accountsMessage={accountsMessage}
      />

      <section className="dashboard-grid" style={{ marginBottom: 24 }}>
        <UploadPanel
          selectedAccountId={selectedAccountId}
          selectedFile={selectedFile}
          setSelectedFile={setSelectedFile}
          isUploadLoading={isUploadLoading}
          handleUpload={handleUpload}
          upload={upload}
          statementStatus={statementStatus}
          parsedTransactionCount={parsedTransactionCount}
          appMessage={appMessage}
          selectedAccountName={selectedAccount?.accountName ?? null}
        />

        <MetricStrip summary={summary} currency={currency} />
      </section>

      <section className="content-grid" style={{ marginBottom: 24 }}>
        <SpendingBreakdown
          summary={summary}
          loadSummary={loadSummary}
          isSummaryLoading={isSummaryLoading}
          currency={currency}
          budgetProgress={budgetProgress}
        />

        <RecentActivity
          summary={summary}
          currency={currency}
          categories={categories}
          authedFetch={authedFetch}
          onTransactionUpdated={handleTransactionUpdated}
          selectedAccountId={selectedAccountId}
          refreshKey={transactionRefreshKey}
          headerActions={
            <button className="secondary-button" type="button" onClick={() => void handleDownloadCsv()} title="Download as CSV">
              Download CSV
            </button>
          }
        />
      </section>

      <section style={{ marginBottom: 24 }}>
        <StatementManager authedFetch={authedFetch} refreshKey={statementRefreshKey} onDelete={() => { void loadSummary(); setTransactionRefreshKey((k) => k + 1); }} />
      </section>

      <BasiqPanel authedFetch={authedFetch} />

      <section style={{ marginBottom: 24 }}>
        <BudgetManager
          categories={categories}
          authedFetch={authedFetch}
          onBudgetsChanged={() => void loadBudgetProgress()}
          onMonthChange={(year, month) => void loadBudgetProgress(year, month)}
        />
      </section>
    </main>
  );
}

export default App;
