import { Component, effect, ElementRef, input, viewChild } from '@angular/core';
import { CategoryScale, Chart, Legend, LinearScale, LineController, LineElement, PointElement, Tooltip, Filler, Decimation } from 'chart.js';

Chart.register(CategoryScale, LinearScale, LineController, LineElement, PointElement, Tooltip, Legend, Filler, Decimation);

export interface ChartLine { name: string; values: (number | null)[]; }
const colors = ['#4165e4', '#0b9d80', '#a855c0', '#e27a32', '#1486bd', '#db537c', '#6c47a9'];

@Component({
  selector: 'ca-chart',
  template: `<div class="chart"><canvas #plot role="img" [attr.aria-label]="label()"></canvas></div>`,
})
export class AnalyticsChart {
  readonly label = input.required<string>();
  readonly labels = input.required<string[]>();
  readonly lines = input.required<ChartLine[]>();
  private readonly canvas = viewChild<ElementRef<HTMLCanvasElement>>('plot');

  constructor() {
    effect(cleanup => {
      const canvas = this.canvas()?.nativeElement;
      const labels = this.labels();
      const lines = this.lines();
      if (!canvas) return;
      const multiDay = new Set(labels.map(label => label.slice(0, 10))).size > 1;
      const chart = new Chart(canvas, {
        type: 'line',
        data: {
          labels,
          datasets: lines.map((line, i) => ({
            label: line.name, data: line.values, borderColor: colors[i % colors.length], backgroundColor: colors[i % colors.length] + '15',
            borderWidth: 2, fill: lines.length === 1, tension: 0.17, pointRadius: labels.length < 20 ? 2 : 0, spanGaps: false,
          })),
        },
        options: {
          maintainAspectRatio: false, responsive: true, animation: false, color: '#53637b',
          interaction: { mode: 'index', intersect: false },
          plugins: { legend: { display: true, labels: { usePointStyle: true, boxWidth: 8 } } },
          scales: {
            x: { grid: { display: false }, ticks: { maxTicksLimit: 8, maxRotation: 0, callback: (value: string | number) => {
              const label = labels[Number(value)] ?? '';
              return /^\d{4}-/.test(label) ? label.slice(multiDay ? 5 : 11, 16) : label;
            } } },
            y: { beginAtZero: true, grid: { color: '#e8edf4' } },
          },
        },
      });
      cleanup(() => chart.destroy());
    });
  }
}
