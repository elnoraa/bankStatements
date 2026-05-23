import { useCallback, useEffect, useState } from 'react';
import type { BasiqConnection, InitiateBasiqConnectionResponse, BasiqSyncLogEntry } from '../types';
import { apiBaseUrl } from '../types';
import { ConfirmDialog } from './ConfirmDialog';

interface BasiqPanelProps {
  authedFetch: (url: string, options?: RequestInit) => Promise<Response>;
}

const FREQUENCY_OPTIONS = [
  { label: 'Every hour', value: 60 },
  { label: 'Every 6 hours', value: 360 },
  { label: 'Daily', value: 1440 },
  { label: 'Weekly', value: 10080 },
];

export function BasiqPanel({ authedFetch }: BasiqPanelProps) {
  const [connections, setConnections] = useState<BasiqConnection[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [message, setMessage] = useState('');

  // Add connection form
  const [showForm, setShowForm] = useState(false);
  const [institutionName, setInstitutionName] = useState('');
  const [isInitiating, setIsInitiating] = useState(false);

  // Sync log visibility per connection
  const [expandedLogs, setExpandedLogs] = useState<Set<string>>(new Set());
  const [syncLogs, setSyncLogs] = useState<Record<string, BasiqSyncLogEntry[]>>({});
  const [loadingLogs, setLoadingLogs] = useState<Set<string>>(new Set());

  // Confirm delete state
  const [confirmDeleteId, setConfirmDeleteId] = useState<string | null>(null);

  const loadConnections = useCallback(async () => {
    try {
      const response = await authedFetch(`${apiBaseUrl}/api/v1/basiq/connections`);
      if (!response.ok) return;
      const data = await response.json();
      setConnections(data.connections ?? []);
    } catch {
      // Silently fail
    } finally {
      setIsLoading(false);
    }
  }, [authedFetch]);

  useEffect(() => {
    void loadConnections();
  }, [loadConnections]);

  async function handleInitiate(event: React.FormEvent) {
    event.preventDefault();
    if (!institutionName.trim()) return;

    setIsInitiating(true);
    setMessage('');

    try {
      const response = await authedFetch(`${apiBaseUrl}/api/v1/basiq/connections`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ institutionName: institutionName.trim() }),
      });

      if (!response.ok) {
        throw new Error(await response.text());
      }

      const result: InitiateBasiqConnectionResponse = await response.json();

      // Open consent UI in new tab
      window.open(result.consentUrl, '_blank');

      setMessage('Connection initiated. Complete the authentication in the new tab, then refresh this page.');
      setShowForm(false);
      setInstitutionName('');

      // Poll for connection status change
      await pollConnectionStatus(result.connectionId);
    } catch (error) {
      setMessage(error instanceof Error ? error.message : 'Failed to initiate connection.');
    } finally {
      setIsInitiating(false);
    }
  }

  async function pollConnectionStatus(connectionId: string) {
    for (let i = 0; i < 60; i++) {
      await new Promise((r) => setTimeout(r, 5000));

      try {
        const response = await authedFetch(`${apiBaseUrl}/api/v1/basiq/connections/${connectionId}`);
        if (!response.ok) continue;

        const conn: BasiqConnection = await response.json();
        setConnections((prev) =>
          prev.map((c) => (c.id === connectionId ? conn : c))
        );

        if (conn.status !== 'pending') {
          setMessage(
            conn.status === 'active'
              ? 'Connection established successfully!'
              : `Connection status: ${conn.status}`
          );
          return;
        }
      } catch {
        // Continue polling
      }
    }
    setMessage('Connection is taking longer than expected. Check back later.');
  }

  async function handleToggleSync(connection: BasiqConnection) {
    try {
      const response = await authedFetch(
        `${apiBaseUrl}/api/v1/basiq/connections/${connection.id}/sync`,
        {
          method: 'PUT',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ syncEnabled: !connection.syncEnabled }),
        }
      );

      if (!response.ok) {
        throw new Error(await response.text());
      }

      const updated: BasiqConnection = await response.json();
      setConnections((prev) =>
        prev.map((c) => (c.id === updated.id ? updated : c))
      );
    } catch (error) {
      setMessage(error instanceof Error ? error.message : 'Failed to update sync.');
    }
  }

  async function handleFrequencyChange(connection: BasiqConnection, frequency: number) {
    try {
      const response = await authedFetch(
        `${apiBaseUrl}/api/v1/basiq/connections/${connection.id}/sync`,
        {
          method: 'PUT',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ syncFrequencyMinutes: frequency }),
        }
      );

      if (!response.ok) {
        throw new Error(await response.text());
      }

      const updated: BasiqConnection = await response.json();
      setConnections((prev) =>
        prev.map((c) => (c.id === updated.id ? updated : c))
      );
    } catch (error) {
      setMessage(error instanceof Error ? error.message : 'Failed to update frequency.');
    }
  }

  async function handleRefresh(connection: BasiqConnection) {
    try {
      const response = await authedFetch(
        `${apiBaseUrl}/api/v1/basiq/connections/${connection.id}/refresh`,
        { method: 'POST' }
      );

      if (!response.ok) {
        throw new Error(await response.text());
      }

      const updated: BasiqConnection = await response.json();
      setConnections((prev) =>
        prev.map((c) => (c.id === updated.id ? updated : c))
      );
      setMessage('Sync triggered successfully.');
    } catch (error) {
      setMessage(error instanceof Error ? error.message : 'Failed to refresh.');
    }
  }

  async function handleDelete(connectionId: string) {
    try {
      const response = await authedFetch(
        `${apiBaseUrl}/api/v1/basiq/connections/${connectionId}`,
        { method: 'DELETE' }
      );

      if (!response.ok) {
        throw new Error(await response.text());
      }

      setConnections((prev) => prev.filter((c) => c.id !== connectionId));
      setConfirmDeleteId(null);
    } catch (error) {
      setConfirmDeleteId(null);
      setMessage(error instanceof Error ? error.message : 'Failed to delete connection.');
    }
  }

  async function handleToggleLogs(connectionId: string) {
    const newExpanded = new Set(expandedLogs);
    if (newExpanded.has(connectionId)) {
      newExpanded.delete(connectionId);
      setExpandedLogs(newExpanded);
      return;
    }
    newExpanded.add(connectionId);
    setExpandedLogs(newExpanded);

    if (!syncLogs[connectionId]) {
      setLoadingLogs((prev) => new Set(prev).add(connectionId));
      try {
        const response = await authedFetch(
          `${apiBaseUrl}/api/v1/basiq/connections/${connectionId}/sync-log?limit=10`
        );
        if (response.ok) {
          const data = await response.json();
          setSyncLogs((prev) => ({ ...prev, [connectionId]: data }));
        }
      } catch {
        // Silently fail
      } finally {
        setLoadingLogs((prev) => {
          const next = new Set(prev);
          next.delete(connectionId);
          return next;
        });
      }
    }
  }

  function statusBadgeClass(status: string): string {
    switch (status) {
      case 'active': return 'badge badge--success';
      case 'pending': return 'badge badge--warning';
      case 'failed':
      case 'expired':
      case 'disabled': return 'badge badge--error';
      default: return 'badge';
    }
  }

  function formatDate(dateStr: string | null): string {
    if (!dateStr) return 'Never';
    return new Date(dateStr).toLocaleString('en-AU');
  }

  if (isLoading) {
    return <div className="section-card"><p>Loading connections...</p></div>;
  }

  return (
    <div className="section-card">
      <div className="section-card__header">
        <h2>Open Banking Connections</h2>
        <button
          className="primary-button"
          type="button"
          onClick={() => setShowForm(true)}
          disabled={showForm}
        >
          + Add Connection
        </button>
      </div>

      {message && (
        <p className="message" onClick={() => setMessage('')}>
          {message}
        </p>
      )}

      {showForm && (
        <form className="inline-form" onSubmit={(e) => void handleInitiate(e)}>
          <input
            type="text"
            placeholder="Institution name (e.g. ANZ, CommBank)"
            value={institutionName}
            onChange={(e) => setInstitutionName(e.target.value)}
            disabled={isInitiating}
            required
          />
          <button className="primary-button" type="submit" disabled={isInitiating}>
            {isInitiating ? 'Connecting...' : 'Connect'}
          </button>
          <button
            className="secondary-button"
            type="button"
            onClick={() => setShowForm(false)}
            disabled={isInitiating}
          >
            Cancel
          </button>
        </form>
      )}

      {connections.length === 0 && !isLoading && (
        <p className="empty-state">
          No bank connections yet. Add a connection to auto-import transactions.
        </p>
      )}

      <div className="connection-list">
        {connections.map((conn) => (
          <div key={conn.id} className="connection-card">
            <div className="connection-card__header">
              <div>
                <strong>{conn.institutionName || 'Unknown Institution'}</strong>
                <span className={statusBadgeClass(conn.status)}>
                  {conn.status}
                </span>
              </div>
              <div className="connection-card__actions">
                <button
                  className="secondary-button"
                  type="button"
                  onClick={() => void handleRefresh(conn)}
                  title="Manual sync"
                >
                  Sync Now
                </button>
                <button
                  className="danger-button"
                  type="button"
                  onClick={() => setConfirmDeleteId(conn.id)}
                  title="Remove connection"
                >
                  Remove
                </button>
              </div>
            </div>

            <div className="connection-card__details">
              <div className="detail-row">
                <span className="detail-label">Auto-sync</span>
                <label className="toggle">
                  <input
                    type="checkbox"
                    checked={conn.syncEnabled}
                    onChange={() => void handleToggleSync(conn)}
                  />
                  <span className="toggle__slider" />
                </label>
              </div>

              {conn.syncEnabled && (
                <div className="detail-row">
                  <span className="detail-label">Frequency</span>
                  <select
                    value={conn.syncFrequencyMinutes}
                    onChange={(e) => void handleFrequencyChange(conn, Number(e.target.value))}
                  >
                    {FREQUENCY_OPTIONS.map((opt) => (
                      <option key={opt.value} value={opt.value}>
                        {opt.label}
                      </option>
                    ))}
                  </select>
                </div>
              )}

              <div className="detail-row">
                <span className="detail-label">Connected</span>
                <span>{formatDate(conn.connectedAt)}</span>
              </div>

              <div className="detail-row">
                <span className="detail-label">Last sync</span>
                <span>{formatDate(conn.lastSyncAt)}</span>
              </div>
            </div>

            <button
              className="link-button"
              type="button"
              onClick={() => void handleToggleLogs(conn.id)}
            >
              {expandedLogs.has(conn.id) ? 'Hide' : 'Show'} sync history
            </button>

            {expandedLogs.has(conn.id) && (
              <div className="sync-log">
                {loadingLogs.has(conn.id) ? (
                  <p>Loading...</p>
                ) : (syncLogs[conn.id]?.length ?? 0) === 0 ? (
                  <p className="empty-state">No sync history yet.</p>
                ) : (
                  <table className="sync-log-table">
                    <thead>
                      <tr>
                        <th>Time</th>
                        <th>Fetched</th>
                        <th>New</th>
                        <th>Status</th>
                      </tr>
                    </thead>
                    <tbody>
                      {syncLogs[conn.id]?.map((log) => (
                        <tr key={log.id}>
                          <td>{new Date(log.syncedAt).toLocaleString('en-AU')}</td>
                          <td>{log.transactionsFetched}</td>
                          <td>{log.transactionsInserted}</td>
                          <td>
                            <span className={statusBadgeClass(log.status)}>
                              {log.status}
                            </span>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                )}
              </div>
            )}
          </div>
        ))}
      </div>

      <ConfirmDialog
        open={confirmDeleteId !== null}
        title="Remove connection"
        message="Remove this Basiq connection? Imported transactions will be preserved."
        confirmLabel="Remove"
        onConfirm={async () => {
          if (confirmDeleteId) await handleDelete(confirmDeleteId);
        }}
        onCancel={() => setConfirmDeleteId(null)}
        destructive={true}
      />
    </div>
  );
}
