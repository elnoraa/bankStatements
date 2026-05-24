import { useCallback, useEffect, useRef, useState } from 'react';
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
  refreshKey?: number;
}

export function RecentActivity({
  summary, currency, categories, authedFetch, onTransactionUpdated, headerActions, selectedAccountId,
  refreshKey,
}: RecentActivityProps) {
  const [pageData, setPageData] = useState<PaginatedTransactionsResponse | null>(null);
  const [loadingPage, setLoadingPage] = useState(false);
  const [pageSize, setPageSize] = useState(20);
  const [goPage, setGoPage] = useState('');
  const goInputRef = useRef<HTMLInputElement>(null);

  // Filter state
  const [searchInput, setSearchInput] = useState('');
  const [debouncedSearch, setDebouncedSearch] = useState('');
  const [filterCategoryId, setFilterCategoryId] = useState('');
  const [minAmountStr, setMinAmountStr] = useState('');
  const [maxAmountStr, setMaxAmountStr] = useState('');
  const [filterType, setFilterType] = useState<'all' | 'credit' | 'debit'>('all');

  // Debounce search input
  useEffect(() => {
    const timer = setTimeout(() => setDebouncedSearch(searchInput), 350);
    return () => clearTimeout(timer);
  }, [searchInput]);

  const loadPage = useCallback(async (page: number, size?: number) => {
    setLoadingPage(true);
    try {
      const params = new URLSearchParams();
      params.set('page', String(page));
      params.set('pageSize', String(size ?? pageSize));
      if (selectedAccountId && selectedAccountId !== TOTAL_ID) {
        params.set('bankAccountId', selectedAccountId);
      }
      if (debouncedSearch) params.set('search', debouncedSearch);
      if (filterCategoryId) params.set('categoryId', filterCategoryId);
      if (minAmountStr) params.set('minAmount', minAmountStr);
      if (maxAmountStr) params.set('maxAmount', maxAmountStr);
      if (filterType !== 'all') params.set('transactionType', filterType);

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
  }, [authedFetch, selectedAccountId, pageSize, debouncedSearch, filterCategoryId, minAmountStr, maxAmountStr, filterType]);

  // Reload page 1 when filters change or refreshKey increments
  useEffect(() => {
    void loadPage(1);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [debouncedSearch, filterCategoryId, minAmountStr, maxAmountStr, filterType, selectedAccountId, refreshKey]);

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

  return (
    <section className="panel transactions-panel">
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

      {/* Filter bar */}
      <div style={{ display: 'flex', gap: 8, alignItems: 'center', marginBottom: 12, flexWrap: 'wrap' }}>
        <input
          type="text"
          placeholder="Search descriptions..."
          value={searchInput}
          onChange={(e) => setSearchInput(e.target.value)}
          style={{ flex: '1 1 180px', minHeight: 32, padding: '0 8px', borderRadius: 6, border: '1px solid #c8d2df', fontSize: 13 }}
        />
        <select
          value={filterCategoryId}
          onChange={(e) => setFilterCategoryId(e.target.value)}
          style={{ minHeight: 32, padding: '0 8px', borderRadius: 6, border: '1px solid #c8d2df', fontSize: 13 }}
        >
          <option value="">All categories</option>
          {categories.map((cat) => (
            <option key={cat.id} value={cat.id}>{cat.name}</option>
          ))}
        </select>
        <input
          type="number"
          placeholder="Min $"
          value={minAmountStr}
          onChange={(e) => setMinAmountStr(e.target.value)}
          min="0"
          step="0.01"
          style={{ width: 80, minHeight: 32, padding: '0 8px', borderRadius: 6, border: '1px solid #c8d2df', fontSize: 13 }}
        />
        <input
          type="number"
          placeholder="Max $"
          value={maxAmountStr}
          onChange={(e) => setMaxAmountStr(e.target.value)}
          min="0"
          step="0.01"
          style={{ width: 80, minHeight: 32, padding: '0 8px', borderRadius: 6, border: '1px solid #c8d2df', fontSize: 13 }}
        />
        <div style={{ display: 'inline-flex', borderRadius: 6, border: '1px solid #c8d2df', overflow: 'hidden' }}>
          {(['all', 'credit', 'debit'] as const).map((type) => (
            <button
              key={type}
              type="button"
              onClick={() => setFilterType(type)}
              style={{
                padding: '4px 12px', minHeight: 32, fontSize: 13,
                background: filterType === type ? '#e6f0ff' : 'transparent',
                border: 'none', cursor: 'pointer',
                fontWeight: filterType === type ? 600 : 400,
                color: type === 'credit' ? '#16a34a' : type === 'debit' ? '#dc2626' : 'inherit',
              }}
            >
              {type === 'all' ? 'All' : type === 'credit' ? 'Credits' : 'Debits'}
            </button>
          ))}
        </div>
      </div>

      {/* Transaction list */}
      <div className="transaction-list">
        {loadingPage && pageData === null && (
          <p className="empty-state">Loading transactions...</p>
        )}
        {!loadingPage && (pageData?.items.length ?? 0) === 0 && (
          <p className="empty-state">No transactions found for the current filters.</p>
        )}
        {pageData?.items.map((transaction) => (
          <TransactionRow
            key={transaction.id}
            transaction={transaction}
            categories={categories}
            currency={currency}
            authedFetch={authedFetch}
            onUpdated={(id, updates) => {
              onTransactionUpdated(id, updates);
              void loadPage(pageData.page);
            }}
          />
        ))}
      </div>

      {/* Pagination controls — always shown when data is loaded */}
      {pageData && (
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
    </section>
  );
}
