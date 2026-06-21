import type { SpendingSummary } from '../types';
import { BudgetBar } from './BudgetBar';

interface BudgetProgress {
  categoryId: string;
  categoryName: string;
  budgetAmount: number;
  actualSpending: number;
  percentageUsed: number;
  remaining: number;
  isOverBudget: boolean;
}

interface SpendingBreakdownProps {
  summary: SpendingSummary | null;
  loadSummary: () => Promise<void>;
  isSummaryLoading: boolean;
  currency: Intl.NumberFormat;
  budgetProgress: BudgetProgress[];
}

export function SpendingBreakdown({
  summary, loadSummary, isSummaryLoading, currency, budgetProgress,
}: SpendingBreakdownProps) {
  const progressMap = new Map(budgetProgress.map((p) => [p.categoryName, p]));

  return (
    <section className="panel">
      <div className="section-heading">
        <div>
          <p className="panel-label">Categories</p>
          <h2>Spending breakdown</h2>
        </div>
        <button className="secondary-button" type="button" onClick={() => void loadSummary()} disabled={isSummaryLoading}>
          Refresh
        </button>
      </div>
      <div className="category-list">
        {(summary?.spendingByCategory.length ?? 0) === 0 && (
          <p className="empty-state">Upload a PDF statement to see category totals.</p>
        )}
        {summary?.spendingByCategory.map((category) => (
          <div className="category-row" key={category.category}>
            <div style={{ flex: 1 }}>
              <strong>{category.category}</strong>
              <span>{category.transactionCount} transactions</span>
              {progressMap.has(category.category) && (
                <BudgetBar
                  categoryName={category.category}
                  spent={progressMap.get(category.category)!.actualSpending}
                  budget={progressMap.get(category.category)!.budgetAmount}
                  currency={currency}
                />
              )}
            </div>
            <b>{currency.format(category.totalDebit)}</b>
          </div>
        ))}
      </div>
    </section>
  );
}
