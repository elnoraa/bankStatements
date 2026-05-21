import type { SpendingSummary } from '../types';

interface MetricStripProps {
  summary: SpendingSummary | null;
  currency: Intl.NumberFormat;
}

export function MetricStrip({ summary, currency }: MetricStripProps) {
  return (
    <section className="metric-strip">
      <article className="metric">
        <span>Total credit</span>
        <strong>{currency.format(summary?.totalCredit ?? 0)}</strong>
      </article>
      <article className="metric">
        <span>Total debit</span>
        <strong>{currency.format(summary?.totalDebit ?? 0)}</strong>
      </article>
      <article className={summary?.isCashflowPositive ? 'metric positive' : 'metric negative'}>
        <span>Net cashflow</span>
        <strong>{currency.format(summary?.netCashflow ?? 0)}</strong>
      </article>
    </section>
  );
}
