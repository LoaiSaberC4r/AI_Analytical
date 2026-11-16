import { Freshness, PeriodPoint } from '../../core/models/models';

const values = new Intl.NumberFormat(undefined, { maximumFractionDigits: 1 });
export function number(value: number | null | undefined, unit = ''): string {
  return value == null ? '—' : values.format(value) + unit;
}
export function videoTime(ms: number | null | undefined, precise = false): string {
  if (ms == null) return '—';
  const s = Math.floor(ms / 1000);
  const pieces = [Math.floor(s / 3600), Math.floor(s / 60) % 60, s % 60].map(x => String(x).padStart(2, '0'));
  return pieces.join(':') + (precise ? '.' + String(ms % 1000).padStart(3, '0') : '');
}
export function periodLabel(point: PeriodPoint | null | undefined): string {
  if (!point) return '—';
  if (point.startSourceTimeMs != null) return videoTime(point.startSourceTimeMs);
  return point.localStart ? point.localStart.slice(0, 16).replace('T', ' ') + ' ' + point.localStart.slice(-6) : '—';
}
export function freshnessLabel(value: Freshness): string {
  return ({ Fresh: 'Fresh', Stale: 'Stale', NoRecentData: 'No recent data', NeverReceived: 'Awaiting data' })[value];
}
export function dateInput(time: Date): string {
  return new Date(time.getTime() - time.getTimezoneOffset() * 60_000).toISOString().slice(0, 16);
}
