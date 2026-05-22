import { Doughnut } from 'react-chartjs-2';
import { CHART_COLORS, currencyTooltip } from '../chartConfig';

interface DonutChartProps {
  labels: string[];
  values: number[];
  title?: string;
}

export function DonutChart({ labels, values, title }: DonutChartProps) {
  const data = {
    labels,
    datasets: [{
      data: values,
      backgroundColor: CHART_COLORS.slice(0, labels.length),
      borderWidth: 2,
      borderColor: '#ffffff',
    }],
  };

  const options = {
    responsive: true,
    maintainAspectRatio: false,
    plugins: {
      legend: {
        display: true,
        position: 'bottom' as const,
        labels: { padding: 16, usePointStyle: true },
      },
      tooltip: {
        callbacks: {
          label: (context: { parsed: number; label: string }) => {
            const total = context.dataset.data.reduce((a: number, b: number) => a + b, 0);
            const pct = total > 0 ? ((context.parsed / total) * 100).toFixed(1) : '0';
            return ` ${context.label}: ${currencyTooltip(context.parsed)} (${pct}%)`;
          },
        },
      },
      title: title ? { display: true, text: title } : undefined,
    },
  };

  return (
    <div style={{ width: '100%', maxWidth: 400, margin: '0 auto', minHeight: 300 }}>
      <Doughnut data={data} options={options} />
    </div>
  );
}
