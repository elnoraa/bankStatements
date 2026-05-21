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

export const TOTAL_ID = '__total__';
export const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5213';
