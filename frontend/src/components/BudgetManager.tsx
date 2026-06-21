import { useState, useEffect, useCallback } from 'react';
import { apiBaseUrl } from '../types';
import '../Budget.css';

interface BudgetItem {
  id: string;
  categoryId: string;
  categoryName: string;
  amount: number;
}

interface BudgetManagerProps {
  categories: { id: string; name: string }[];
  authedFetch: (url: string, options?: RequestInit) => Promise<Response>;
  onBudgetsChanged?: () => void;
  onMonthChange?: (year: number, month: number) => void;
}

export function BudgetManager({ categories, authedFetch, onBudgetsChanged, onMonthChange }: BudgetManagerProps) {
  const now = new Date();
  const [year, setYear] = useState(now.getFullYear());
  const [month, setMonth] = useState(now.getMonth() + 1);
  const [budgets, setBudgets] = useState<BudgetItem[]>([]);
  const [isLoading, setIsLoading] = useState(false);
  const [editAmounts, setEditAmounts] = useState<Record<string, string>>({});
  const [message, setMessage] = useState('');


  const loadBudgets = useCallback(async () => {
    setIsLoading(true);
    try {
      const response = await authedFetch(`${apiBaseUrl}/api/v1/budgets?year=${year}&month=${month}`);
      if (!response.ok) throw new Error('Failed to load');
      const data: BudgetItem[] = await response.json();
      setBudgets(data);
      const amounts: Record<string, string> = {};
      for (const b of data) {
        amounts[b.categoryId] = b.amount.toString();
      }
      setEditAmounts(amounts);
    } catch {
      // Silently ignore load failures — empty state shown below
    } finally {
      setIsLoading(false);
    }
  }, [authedFetch, year, month]);

  useEffect(() => {
    void loadBudgets();
  }, [loadBudgets]);

  // Notify parent when selected month changes, so budget progress chart updates
  useEffect(() => {
    onMonthChange?.(year, month);
  }, [year, month, onMonthChange]);

  async function handleSave(categoryId: string) {
    const amount = parseFloat(editAmounts[categoryId]);
    if (isNaN(amount) || amount <= 0) return;

    try {
      const response = await authedFetch(`${apiBaseUrl}/api/v1/budgets`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          categoryId,
          monthYear: `${year}-${String(month).padStart(2, '0')}-01`,
          amount,
        }),
      });
      if (!response.ok) throw new Error(await response.text());
      await loadBudgets();
      onBudgetsChanged?.();
      setMessage('');
    } catch (error) {
      setMessage(error instanceof Error ? error.message : 'Failed to save budget.');
    }
  }

  async function handleDelete(budgetId: string) {
    try {
      const response = await authedFetch(`${apiBaseUrl}/api/v1/budgets/${budgetId}`, {
        method: 'DELETE',
      });
      if (!response.ok) throw new Error('Failed to delete');
      await loadBudgets();
      onBudgetsChanged?.();
    } catch {
      setMessage('Failed to delete budget.');
    }
  }

  const budgetedCategoryIds = new Set(budgets.map((b) => b.categoryId));

  return (
    <section className="panel budget-manager">
      <div className="section-heading">
        <div>
          <p className="panel-label">Planning</p>
          <h2>Monthly budgets</h2>
        </div>
        <div className="budget-month-picker">
          <select value={month} onChange={(e) => setMonth(parseInt(e.target.value))}>
            {Array.from({ length: 12 }, (_, i) => (
              <option key={i + 1} value={i + 1}>
                {new Date(2000, i).toLocaleString('default', { month: 'long' })}
              </option>
            ))}
          </select>
          <input
            type="number"
            value={year}
            onChange={(e) => setYear(parseInt(e.target.value))}
            min={2020}
            max={2030}
            className="budget-year-input"
          />
        </div>
      </div>

      <div className="budget-list">
        {budgets.map((b) => (
          <div className="budget-row" key={b.id}>
            <strong>{b.categoryName}</strong>
            <div className="budget-row-controls">
              <input
                type="number"
                min="0.01"
                step="0.01"
                value={editAmounts[b.categoryId] ?? ''}
                onChange={(e) => setEditAmounts((prev) => ({ ...prev, [b.categoryId]: e.target.value }))}
                className="budget-amount-input"
              />
              <button className="budget-save-btn" type="button" onClick={() => void handleSave(b.categoryId)}>
                Save
              </button>
              <button className="budget-delete-btn" type="button" onClick={() => void handleDelete(b.id)}>
                ×
              </button>
            </div>
          </div>
        ))}

        {!isLoading && categories
          .filter((c) => !budgetedCategoryIds.has(c.id))
          .slice(0, 5)
          .map((cat) => (
            <div className="budget-row budget-row-new" key={cat.id}>
              <strong>{cat.name}</strong>
              <div className="budget-row-controls">
                <input
                  type="number"
                  min="0.01"
                  step="0.01"
                  placeholder="Amount"
                  value={editAmounts[cat.id] ?? ''}
                  onChange={(e) => setEditAmounts((prev) => ({ ...prev, [cat.id]: e.target.value }))}
                  className="budget-amount-input"
                />
                <button className="budget-save-btn" type="button" onClick={() => void handleSave(cat.id)}>
                  Set
                </button>
              </div>
            </div>
          ))}
      </div>

      {message && <p className="error-text">{message}</p>}
    </section>
  );
}
