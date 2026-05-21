import { useState, useEffect, useCallback } from 'react';
import { apiBaseUrl } from '../types';
import '../StatementManager.css';

interface StatementListItem {
  id: string;
  originalFileName: string;
  status: string;
  uploadedAt: string;
  processedAt: string | null;
  failedAt: string | null;
  parsedTransactionCount: number;
  errorMessage: string | null;
}

interface StatementManagerProps {
  authedFetch: (url: string, options?: RequestInit) => Promise<Response>;
}

export function StatementManager({ authedFetch }: StatementManagerProps) {
  const [statements, setStatements] = useState<StatementListItem[]>([]);
  const [isLoading, setIsLoading] = useState(false);
  const [message, setMessage] = useState('');

  const loadStatements = useCallback(async () => {
    setIsLoading(true);
    try {
      const response = await authedFetch(`${apiBaseUrl}/api/v1/statements?page=1&pageSize=50`);
      if (!response.ok) throw new Error('Failed to load');
      setStatements(await response.json());
    } catch {
      // Silently ignore load failures — empty state shown below
    } finally {
      setIsLoading(false);
    }
  }, [authedFetch]);

  useEffect(() => {
    void loadStatements();
  }, [loadStatements]);

  async function handleRetry(statementId: string) {
    setMessage('');
    try {
      const response = await authedFetch(`${apiBaseUrl}/api/v1/statements/${statementId}/retry`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
      });
      if (!response.ok) throw new Error(await response.text());
      setStatements((prev) =>
        prev.map((s) => s.id === statementId ? { ...s, status: 'uploaded', errorMessage: null, failedAt: null } : s)
      );
    } catch (error) {
      setMessage(error instanceof Error ? error.message : 'Retry failed.');
    }
  }

  function statusBadgeClass(status: string): string {
    switch (status) {
      case 'processed': return 'status-badge status-processed';
      case 'failed': return 'status-badge status-failed';
      case 'processing': return 'status-badge status-processing';
      case 'uploaded': return 'status-badge status-uploaded';
      default: return 'status-badge';
    }
  }

  return (
    <section className="panel statement-manager">
      <div className="section-heading">
        <div>
          <p className="panel-label">History</p>
          <h2>Statements</h2>
        </div>
        <button className="secondary-button" type="button" onClick={() => void loadStatements()} disabled={isLoading}>
          {isLoading ? 'Loading...' : 'Refresh'}
        </button>
      </div>

      {statements.length === 0 && !isLoading && (
        <p className="empty-state">No statements uploaded yet.</p>
      )}

      <div className="statement-table">
        {statements.map((s) => (
          <div className="statement-row" key={s.id}>
            <div className="statement-info">
              <strong>{s.originalFileName}</strong>
              <span className="statement-meta">
                {new Date(s.uploadedAt).toLocaleDateString()} | {s.parsedTransactionCount} transactions
              </span>
            </div>
            <div className="statement-actions">
              <span className={statusBadgeClass(s.status)}>{s.status}</span>
              {s.status === 'failed' && (
                <button
                  className="retry-btn"
                  type="button"
                  title="Retry processing"
                  onClick={() => void handleRetry(s.id)}
                >
                  Retry
                </button>
              )}
            </div>
          </div>
        ))}
      </div>

      {message && <p className="error-text">{message}</p>}
    </section>
  );
}
