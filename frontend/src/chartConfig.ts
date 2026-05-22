import { Chart, ArcElement, Tooltip, Legend, CategoryScale, LinearScale, BarElement, PointElement, LineElement } from 'chart.js';

Chart.register(ArcElement, Tooltip, Legend, CategoryScale, LinearScale, BarElement, PointElement, LineElement);

export const CHART_COLORS = [
  '#245f73', '#386a5f', '#4a8c7a', '#6aab96',
  '#3671c6', '#5a8edb', '#8fb5e8',
  '#b42318', '#d95c4a', '#e89282',
  '#e6a817', '#f0c75e',
  '#8b5cf6', '#a78bfa',
];

export function currencyTooltip(value: number): string {
  return new Intl.NumberFormat('en-AU', { style: 'currency', currency: 'AUD' }).format(value);
}
