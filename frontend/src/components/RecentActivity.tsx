import { useCallback, useRef, useState } from 'react';
import type { PaginatedTransactionsResponse, SpendingSummary } from '../types';
import { apiBaseUrl, TOTAL_ID } from '../types';
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
  selectedAccountId?: string;
}

export function RecentActivity({
  summary, currency, categories, authedFetch, onTransactionUpdated, headerActions, selectedAccountId,
}: RecentActivityProps) {
  const [showAll, setShowAll] = useState(false);
  const [pageData, setPageData] = useState<PaginatedTransactionsResponse | null>(null);
  const [loadingPage, setLoadingPage] = useState(false);
  const [pageSize, setPageSize] = useState(50);
  const [goPage, setGoPage] = useState('');
  const goInputRef = useRef<HTMLInputElement>(null);

  const loadPage = useCallback(async (page: number, size?: number) => {
    setLoadingPage(true);
    try {
      const params = new URLSearchParams();
      params.set('page', String(page));
      params.set('pageSize', String(size ?? pageSize));
      if (selectedAccountId && selectedAccountId !== TOTAL_ID) {
        params.set('bankAccountId', selectedAccountId);
      }
      const response = await authedFetch(`${apiBaseUrl}/api/v1/analysis/transactions?${params}`);
      if (response.ok) {
        const data = await response.json() as PaginatedTransactionsResponse;
        setPageData(data);
        setGoPage('');
        goInputRef.current?.focus();
      }
    } catch {
      // Silently fail
    } finally {
      setLoadingPage(false);
    }
  }, [authedFetch, selectedAccountId, pageSize]);

  async function handleChangePageSize(newSize: number) {
    setPageSize(newSize);
    await loadPage(1, newSize);
  }

  async function handleGoPage() {
    const p = parseInt(goPage, 10);
    if (!isNaN(p) && p >= 1 && pageData && p <= pageData.totalPages) {
      await loadPage(p);
    }
  }

  async function handleToggleAll() {
    if (showAll) {
      setShowAll(false);
      return;
    }
    setShowAll(true);
    await loadPage(1);
  }

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
        {(summary?.recentTransactions.length ?? 0) === 0 && !showAll && (
          <p className="empty-state">Parsed transactions will appear here.</p>
        )}
        {!showAll && summary?.recentTransactions.map((transaction) => (
          <TransactionRow
            key={transaction.id}
            transaction={transaction}
            categories={categories}
            currency={currency}
            authedFetch={authedFetch}
            onUpdated={(id, updates) => onTransactionUpdated(id, updates)}
          />
        ))}
        {showAll && pageData?.items.map((transaction) => (
          <TransactionRow
            key={transaction.id}
            transaction={transaction}
            categories={categories}
            currency={currency}
            authedFetch={authedFetch}
            onUpdated={(id, updates) => {
              onTransactionUpdated(id, updates);
              // Refresh the current page to reflect category changes
              void loadPage(pageData.page);
            }}
          />
        ))}
      </div>

      {/* Pagination controls */}
      {showAll && pageData && (
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginTop: 16, gap: 12, flexWrap: 'wrap' }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
            <span style={{ fontSize: 13, color: '#68758a' }}>Show</span>
            <select
              value={pageSize}
              onChange={(e) => void handleChangePageSize(Number(e.target.value))}
              style={{ minHeight: 32, padding: '0 8px', borderRadius: 6, border: '1px solid #c8d2df', fontSize: 13 }}
            >
              <option value={20}>20</option>
              <option value={50}>50</option>
              <option value={100}>100</option>
            </select>
            <span style={{ fontSize: 13, color: '#68758a' }}>per page</span>
          </div>

          <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
            <button
              className="secondary-button"
              type="button"
              disabled={pageData.page <= 1 || loadingPage}
              onClick={() => void loadPage(pageData.page - 1)}
              style={{ fontSize: 13, padding: '4px 10px', minHeight: 32 }}
            >
              ← Prev
            </button>

            <span style={{ fontSize: 13, color: '#68758a', whiteSpace: 'nowrap' }}>
              Page
            </span>
            <input
              ref={goInputRef}
              type="text"
              inputMode="numeric"
              value={goPage}
              onChange={(e) => setGoPage(e.target.value)}
              onKeyDown={(e) => { if (e.key === 'Enter') void handleGoPage(); }}
              placeholder={String(pageData.page)}
              style={{ width: 48, minHeight: 32, padding: '0 6px', borderRadius: 6, border: '1px solid #c8d2df', fontSize: 13, textAlign: 'center' }}
            />
            <span style={{ fontSize: 13, color: '#68758a', whiteSpace: 'nowrap' }}>
              of {pageData.totalPages} ({pageData.totalCount})
            </span>

            <button
              className="secondary-button"
              type="button"
              disabled={pageData.page >= pageData.totalPages || loadingPage}
              onClick={() => void loadPage(pageData.page + 1)}
              style={{ fontSize: 13, padding: '4px 10px', minHeight: 32 }}
            >
              Next →
            </button>
          </div>
        </div>
      )}

      {!showAll && (summary?.recentTransactions.length ?? 0) > 0 && (
        <button className="link-button" type="button" onClick={() => void handleToggleAll()} style={{ marginTop: 12 }}>
          View all transactions →
        </button>
      )}

      {showAll && (
        <button className="link-button" type="button" onClick={() => setShowAll(false)} style={{ marginTop: 12 }}>
          ← Show recent only
        </button>
      )}
    </section>
  );
}
