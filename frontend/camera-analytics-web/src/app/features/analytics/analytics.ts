import { Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { catchError, forkJoin, map, of, Subject, switchMap } from 'rxjs';
import { AnalyticsApi } from '../../core/api/api';
import {
  Camera, DwellZone, Interval, OccupancyZone, PeakHour, Ranking, ReportFilter, Site, StaffReport,
  Traffic, TrafficZone, Zone, ZoneReport,
} from '../../core/models/models';
import { AnalyticsChart, ChartLine } from '../../shared/components/chart';
import { DataTable } from '../../shared/components/data-table';
import { dateInput, number, periodLabel } from '../../shared/utilities/format';

type Panel = 'rankings' | 'occupancy' | 'traffic' | 'dwell' | 'wrongWay' | 'staff';
type Result =
  | { kind: 'rankings'; data: Ranking }
  | { kind: 'occupancy'; data: ZoneReport<OccupancyZone>; peaks: ZoneReport<PeakHour> }
  | { kind: 'traffic'; data: ZoneReport<TrafficZone> & { totals: Traffic } }
  | { kind: 'wrongWay'; data: ZoneReport<TrafficZone> & { wrongWayIn: number; wrongWayOut: number; wrongWayEvents: number } }
  | { kind: 'dwell'; data: ZoneReport<DwellZone> }
  | { kind: 'staff'; data: StaffReport };

@Component({
  selector: 'ca-analytics',
  imports: [ReactiveFormsModule, RouterLink, DataTable, AnalyticsChart],
  templateUrl: './analytics.html',
})
export class Analytics {
  private readonly api = inject(AnalyticsApi);
  private readonly destroy = inject(DestroyRef);
  readonly panels: { key: Panel; label: string }[] = [
    { key: 'rankings', label: 'Zone rankings' }, { key: 'occupancy', label: 'Occupancy & peak hours' },
    { key: 'traffic', label: 'Traffic' }, { key: 'dwell', label: 'Dwell' }, { key: 'wrongWay', label: 'Wrong-way' }, { key: 'staff', label: 'Staff' },
  ];
  readonly metrics = [
    ['arrivals', 'Most visited'], ['entries', 'Top entry'], ['departures', 'Most departed'], ['exits', 'Top exit'],
    ['averageOccupancy', 'Most crowded'], ['peakOccupancy', 'Peak occupancy'], ['utilization', 'Average utilization'],
    ['reportedAverageDwell', 'Latest reported average dwell'], ['estimatedAverageDwell', 'Estimated average dwell'],
    ['longestActiveDwell', 'Longest observed active stay'], ['wrongWayEvents', 'Wrong-way events'],
  ];
  readonly trafficMetrics = [
    ['arrivals', 'Arrivals'], ['departures', 'Departures'], ['entries', 'Entries'], ['exits', 'Exits'],
    ['wrongWayIn', 'Wrong-way in'], ['wrongWayOut', 'Wrong-way out'],
  ] as const;
  readonly tab = signal<Panel>('rankings');
  readonly trafficMetric = signal<keyof Traffic>('arrivals');
  readonly sites = signal<Site[]>([]);
  readonly cameras = signal<Camera[]>([]);
  readonly zones = signal<Zone[]>([]);
  readonly loading = signal(true);
  readonly error = signal('');
  readonly catalogError = signal('');
  readonly result = signal<Result | null>(null);
  readonly number = number;
  readonly periodLabel = periodLabel;
  private readonly requests = new Subject<{ panel: Panel; filter: ReportFilter; metric: string }>();
  readonly form = new FormGroup({
    siteId: new FormControl('', { nonNullable: true }),
    cameraId: new FormControl('', { nonNullable: true }),
    zoneId: new FormControl('', { nonNullable: true }),
    from: new FormControl(dateInput(new Date(Date.now() - 24 * 60 * 60 * 1000)), { nonNullable: true, validators: Validators.required }),
    to: new FormControl(dateInput(new Date(Math.ceil(Date.now() / 60_000) * 60_000)), { nonNullable: true, validators: Validators.required }),
    interval: new FormControl<Interval>('hour', { nonNullable: true }),
    metric: new FormControl('arrivals', { nonNullable: true }),
  });

  readonly plot = computed<{ labels: string[]; lines: ChartLine[] }>(() => {
    const report = this.result();
    if (!report) return { labels: [], lines: [] };
    if (report.kind === 'staff') return {
      labels: report.data.cameras[0]?.series.map(x => periodLabel(x.period)) ?? [],
      lines: report.data.cameras.filter(x => x.series.some(p => p.averageStaffCount !== null))
        .map(x => ({ name: x.cameraName, values: x.series.map(p => p.averageStaffCount) })),
    };
    if (report.kind === 'occupancy' || report.kind === 'traffic' || report.kind === 'wrongWay') {
      const metric = report.kind === 'wrongWay' ? 'wrongWayEvents' : this.trafficMetric();
      return {
        labels: report.data.zones[0]?.series.map(x => periodLabel(x.period)) ?? [],
        lines: report.data.zones.map(z => ({ name: z.zoneName, values: z.series.map(p => report.kind === 'occupancy'
          ? p.averageOccupancy : p.dataQuality.kind === 'InsufficientData' ? null : p.traffic[metric]) }))
          .filter(line => line.values.some(value => value !== null)),
      };
    }
    return { labels: [], lines: [] };
  });

  constructor() {
    this.requests.pipe(switchMap(({ panel, filter, metric }) => this.read(panel, filter, metric).pipe(
      catchError((error: Error) => { this.error.set(error.message); return of(null); }),
    )), takeUntilDestroyed()).subscribe(data => { this.result.set(data); this.loading.set(false); });
    this.api.all(page => this.api.sites({ page, pageSize: 100 })).pipe(takeUntilDestroyed())
      .subscribe({ next: sites => this.sites.set(sites), error: e => this.catalogError.set(e.message) });
    this.form.controls.siteId.valueChanges.pipe(switchMap(siteId => {
      this.form.controls.cameraId.setValue('');
      this.cameras.set([]);
      this.catalogError.set('');
      return this.api.all(page => this.api.cameras({ siteId, page, pageSize: 100 })).pipe(
        catchError(e => { this.catalogError.set(e.message); return of([]); }),
      );
    }), takeUntilDestroyed()).subscribe(rows => this.cameras.set(rows));
    this.form.controls.cameraId.valueChanges.pipe(switchMap(cameraId => {
      this.form.controls.zoneId.setValue('');
      this.zones.set([]);
      return cameraId ? this.api.all(page => this.api.zones(cameraId, { page, pageSize: 100 })).pipe(
        catchError(e => { this.catalogError.set(e.message); return of([]); }),
      ) : of([]);
    }), takeUntilDestroyed()).subscribe(rows => this.zones.set(rows));
    this.form.controls.siteId.setValue('');
    this.run();
  }

  select(panel: Panel): void { this.tab.set(panel); this.run(); }

  run(): void {
    const { siteId, cameraId, zoneId, from, to, interval, metric } = this.form.getRawValue();
    const start = new Date(from), end = new Date(to);
    this.error.set('');
    if (this.form.invalid || !Number.isFinite(start.getTime()) || !Number.isFinite(end.getTime()) || start >= end) {
      this.loading.set(false);
      this.error.set('Select a start time before the end time.');
      return;
    }
    this.loading.set(true);
    this.result.set(null);
    this.requests.next({ panel: this.tab(), metric, filter: { siteId, cameraId, zoneId,
      from: start.toISOString(), to: end.toISOString(), interval, sourceType: 'live' } });
  }

  private read(panel: Panel, filter: ReportFilter, metric: string) {
    switch (panel) {
      case 'rankings': return this.api.rankings(filter, metric).pipe(map((data): Result => ({ kind: panel, data })));
      case 'occupancy': return forkJoin({ data: this.api.occupancy(filter), peaks: this.api.peaks(filter) })
        .pipe(map((value): Result => ({ kind: panel, ...value })));
      case 'traffic': return this.api.traffic(filter).pipe(map((data): Result => ({ kind: panel, data })));
      case 'wrongWay': return this.api.wrongWay(filter).pipe(map((data): Result => ({ kind: panel, data })));
      case 'dwell': return this.api.dwell(filter).pipe(map((data): Result => ({ kind: panel, data })));
      case 'staff': return this.api.staff(filter).pipe(map((data): Result => ({ kind: panel, data })));
    }
  }
}
