import { useState, useRef, useEffect } from 'react';
import type { RecentTransaction } from '../types';
import { apiBaseUrl } from '../types';
import '../TransactionRow.css';

interface CategoryOption {
  id: string;
  name: string;
  transactionType: string;
}

interface TransactionRowProps {
  transaction: RecentTransaction;
  categories: CategoryOption[];
  currency: Intl.NumberFormat;
  authedFetch: (url: string, options?: RequestInit) => Promise<Response>;
  onUpdated: (id: string, updates: Partial<RecentTransaction>) => void;
}

export function TransactionRow({
  transaction, categories, currency, authedFetch, onUpdated,
}: TransactionRowProps) {
  const [isEditing, setIsEditing] = useState(false);
  const [editDescription, setEditDescription] = useState(transaction.description);
  const [editCategoryId, setEditCategoryId] = useState(transaction.categoryId ?? '');
  const [applyToAll, setApplyToAll] = useState(false);
  const [isSaving, setIsSaving] = useState(false);
  const inputRef = useRef<HTMLInputElement | null>(null);

  useEffect(() => {
    if (isEditing && inputRef.current) {
      inputRef.current.focus();
    }
  }, [isEditing]);

  function handleStartEdit() {
    setEditDescription(transaction.description);
    setEditCategoryId(transaction.categoryId ?? '');
    setApplyToAll(false);
    setIsEditing(true);
  }

  function handleCancel() {
    setIsEditing(false);
  }

  async function handleSave() {
    setIsSaving(true);
    try {
      const body: Record<string, unknown> = {};
      if (editDescription !== transaction.description) {
        body.description = editDescription;
      }
      const newCategoryId = editCategoryId === '' ? null : editCategoryId;
      if (newCategoryId !== transaction.categoryId) {
        body.categoryId = newCategoryId;
      }

      if (Object.keys(body).length === 0) {
        setIsEditing(false);
        return;
      }

      if (applyToAll && body.categoryId !== undefined) {
        body.applyToAll = true;
      }

      const response = await authedFetch(`${apiBaseUrl}/api/v1/transactions/${transaction.id}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
      });

      if (!response.ok) throw new Error(await response.text());

      const selectedCat = categories.find((c) => c.id === editCategoryId);
      onUpdated(transaction.id, {
        description: editDescription,
        category: selectedCat?.name ?? null,
        categoryId: editCategoryId === '' ? null : editCategoryId,
      });
      setIsEditing(false);
    } catch {
      // Silently fail — user can retry
    } finally {
      setIsSaving(false);
    }
  }

  if (isEditing) {
    return (
      <div className="transaction-row editing">
        <div className="transaction-edit-fields">
          <input
            ref={inputRef}
            className="transaction-edit-input"
            value={editDescription}
            onChange={(e) => setEditDescription(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter') void handleSave();
              if (e.key === 'Escape') handleCancel();
            }}
            maxLength={255}
          />
          <select
            className="transaction-edit-select"
            value={editCategoryId}
            onChange={(e) => setEditCategoryId(e.target.value)}
          >
            <option value="">Uncategorised</option>
            {categories.map((cat) => (
              <option key={cat.id} value={cat.id}>{cat.name}</option>
            ))}
          </select>
        </div>
        <label className="transaction-apply-all" style={{ fontSize: 12, display: 'flex', alignItems: 'center', gap: 4, cursor: 'pointer' }}>
          <input type="checkbox" checked={applyToAll} onChange={(e) => setApplyToAll(e.target.checked)} />
          Apply to all transactions with this description
        </label>
        <div className="transaction-edit-actions">
          <button className="transaction-save-btn" type="button" onClick={() => void handleSave()} disabled={isSaving}>
            Save
          </button>
          <button className="transaction-cancel-btn" type="button" onClick={handleCancel}>
            Cancel
          </button>
        </div>
      </div>
    );
  }

  return (
    <div className="transaction-row" onDoubleClick={handleStartEdit} title="Double-click to edit">
      <div>
        <strong>{transaction.description}</strong>
        <span>{transaction.transactionDate} | {transaction.category ?? 'Uncategorised'}</span>
      </div>
      <b className={transaction.transactionType}>
        {transaction.transactionType === 'credit' ? '+' : '-'}
        {currency.format(transaction.amount)}
      </b>
    </div>
  );
}
