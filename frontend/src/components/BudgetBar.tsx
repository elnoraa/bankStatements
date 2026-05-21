interface BudgetBarProps {
  categoryName: string;
  spent: number;
  budget: number;
  currency: Intl.NumberFormat;
}

export function BudgetBar({ categoryName, spent, budget, currency }: BudgetBarProps) {
  const percentage = Math.min((spent / budget) * 100, 100);
  const remaining = budget - spent;
  const isOver = remaining < 0;

  return (
    <div className="budget-bar">
      <div className="budget-bar-track">
        <div
          className={`budget-bar-fill ${isOver ? 'over' : percentage > 80 ? 'warning' : 'ok'}`}
          style={{ width: `${percentage}%` }}
        />
      </div>
      <span className={isOver ? 'budget-over' : 'budget-remaining'}>
        {isOver
          ? `${currency.format(Math.abs(remaining))} over`
          : `${currency.format(remaining)} left`}
      </span>
    </div>
  );
}
