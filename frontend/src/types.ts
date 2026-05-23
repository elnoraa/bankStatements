/** Authentication mode — either login or registration. */
export type AuthMode = 'login' | 'register';

/** Authenticated user profile returned from the API. */
export type AuthUser = {
  id: string;
  email: string;
  displayName: string;
  emailVerified: boolean;
};

/** Auth response containing access token, expiry, and user info. */
export type AuthResponse = {
  accessToken: string;
  accessTokenExpiresAt: string;
  user: AuthUser;
};

/** A bank account belonging to the user. */
export type BankAccount = {
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
export type StatementUploadResponse = {
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
export type CategorySpending = {
  category: string;
  totalDebit: number;
  transactionCount: number;
};

/** A single recent transaction. */
export type RecentTransaction = {
  id: string;
  transactionDate: string;
  description: string;
  amount: number;
  transactionType: 'credit' | 'debit';
  category?: string | null;
  categoryId?: string | null;
};

/** Spending analysis summary for a period. */
export type SpendingSummary = {
  periodStart?: string | null;
  periodEnd?: string | null;
  totalCredit: number;
  totalDebit: number;
  netCashflow: number;
  isCashflowPositive: boolean;
  spendingByCategory: CategorySpending[];
  recentTransactions: RecentTransaction[];
};

export type PaginatedTransactionsResponse = {
  items: RecentTransaction[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
};

export const TOTAL_ID = '__total__';
export const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5213';

/** View states for the auth flow beyond login/register. */
export type AuthView = 'login' | 'register' | 'forgot-password' | 'verify-email' | 'email-sent';

/** A Basiq open banking connection. */
export type BasiqConnection = {
  id: string;
  userId: string;
  bankAccountId: string | null;
  institutionName: string;
  status: string; // 'pending' | 'active' | 'failed' | 'expired'
  syncEnabled: boolean;
  syncFrequencyMinutes: number;
  connectedAt: string | null;
  lastSyncAt: string | null;
  errorMessage?: string | null;
};

/** Response when initiating a new Basiq connection. */
export type InitiateBasiqConnectionResponse = {
  connectionId: string;
  consentUrl: string;
  institutionName: string;
  status: string;
};

/** Request body for updating Basiq sync config. */
export type UpdateBasiqSyncConfig = {
  syncEnabled?: boolean;
  syncFrequencyMinutes?: number;
};

/** Request body for completing a connection after consent. */
export type CompleteBasiqConnectionRequest = {
  jobId: string;
  connectionId: string;
};

/** A sync log entry for a Basiq connection. */
export type BasiqSyncLogEntry = {
  id: string;
  status: string;
  transactionsFetched: number;
  transactionsInserted: number;
  errorMessage: string | null;
  syncedAt: string;
};
