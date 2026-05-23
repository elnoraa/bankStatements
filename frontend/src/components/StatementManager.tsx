import { useState, useEffect, useCallback } from 'react';
import { apiBaseUrl } from '../types';
import { ConfirmDialog } from './ConfirmDialog';
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
  refreshKey?: number;
  onDelete?: () => void;
}

export function StatementManager({ authedFetch, refreshKey, onDelete }: StatementManagerProps) {
  const [statements, setStatements] = useState<StatementListItem[]>([]);
  const [isLoading, setIsLoading] = useState(false);
  const [message, setMessage] = useState('');
  const [confirmDeleteId, setConfirmDeleteId] = useState<string | null>(null);

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
  }, [loadStatements, refreshKey]);

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

  async function handleDelete(statementId: string) {
    try {
      const response = await authedFetch(`${apiBaseUrl}/api/v1/statements/${statementId}`, {
        method: 'DELETE',
      });
      if (!response.ok) throw new Error(await response.text());
      setStatements((prev) => prev.filter((s) => s.id !== statementId));
      setConfirmDeleteId(null);
      onDelete?.();
    } catch (error) {
      setConfirmDeleteId(null);
      setMessage(error instanceof Error ? error.message : 'Failed to delete statement.');
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
              {s.status !== 'processing' && (
                <button
                  className="delete-btn"
                  type="button"
                  title="Delete statement"
                  onClick={() => setConfirmDeleteId(s.id)}
                >
                  ×
                </button>
              )}
            </div>
          </div>
        ))}
      </div>

      <ConfirmDialog
        open={confirmDeleteId !== null}
        title="Delete statement"
        message="Delete this statement and all its transactions? This cannot be undone."
        confirmLabel="Delete"
        onConfirm={async () => {
          if (confirmDeleteId) await handleDelete(confirmDeleteId);
        }}
        onCancel={() => setConfirmDeleteId(null)}
        destructive={true}
      />

      {message && <p className="error-text">{message}</p>}
    </section>
  );
}
