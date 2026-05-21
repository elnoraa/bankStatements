import type { SpendingSummary } from '../types';
import { TransactionRow } from './TransactionRow';

interface CategoryOption {
  id: string;
  name: string;
  transactionType: string;
}

interface RecentActivityProps {
  summary: SpendingSummary | null;
  currency: Intl.NumberFormat;
  categories: CategoryOption[];
  authedFetch: (url: string, options?: RequestInit) => Promise<Response>;
  onTransactionUpdated: (id: string, updates: Record<string, unknown>) => void;
  headerActions?: React.ReactNode;
}

export function RecentActivity({
  summary, currency, categories, authedFetch, onTransactionUpdated, headerActions,
}: RecentActivityProps) {
  return (
    <section className="panel">
      <div className="section-heading">
        <div>
          <p className="panel-label">Transactions</p>
          <h2>Recent activity</h2>
        </div>
        <div className="section-heading-actions">
          <span className="date-range">
            {summary?.periodStart && summary?.periodEnd
              ? `${summary.periodStart} to ${summary.periodEnd}`
              : 'No period yet'}
          </span>
          {headerActions}
        </div>
      </div>
      <div className="transaction-list">
        {(summary?.recentTransactions.length ?? 0) === 0 && (
          <p className="empty-state">Parsed transactions will appear here.</p>
        )}
        {summary?.recentTransactions.map((transaction) => (
          <TransactionRow
            key={transaction.id}
            transaction={transaction}
            categories={categories}
            currency={currency}
            authedFetch={authedFetch}
            onUpdated={(id, updates) => onTransactionUpdated(id, updates)}
          />
        ))}
      </div>
    </section>
  );
}
