import { DatePipe } from '@angular/common';
import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { catchError, of, Subject, switchMap } from 'rxjs';
import { AnalyticsApi } from '../../core/api/api';
import { Interval, ReportFilter, SessionReport, Traffic } from '../../core/models/models';
import { AnalyticsChart } from '../../shared/components/chart';
import { DataTable } from '../../shared/components/data-table';
import { number, periodLabel, videoTime } from '../../shared/utilities/format';

@Component({
  selector: 'ca-session-report',
  imports: [RouterLink, DatePipe, ReactiveFormsModule, AnalyticsChart, DataTable],
  templateUrl: './session-report.html',
})
export class SessionReportPage {
  readonly id = input.required<string>();
  private readonly api = inject(AnalyticsApi);
  private readonly requests = new Subject<{ id: string; filter: ReportFilter }>();
  readonly data = signal<SessionReport | null>(null);
  readonly loading = signal(true);
  readonly error = signal('');
  readonly inputError = signal('');
  readonly metric = signal<keyof Traffic>('arrivals');
  readonly number = number;
  readonly videoTime = videoTime;
  readonly form = new FormGroup({
    videoLoop: new FormControl('', { nonNullable: true }),
    start: new FormControl<number | null>(null, { validators: Validators.min(0) }),
    end: new FormControl<number | null>(null, { validators: Validators.min(0) }),
    interval: new FormControl<Interval>('minute', { nonNullable: true }),
  });
  readonly labels = computed(() => this.data()?.analytics.zones[0]?.series.map(p => periodLabel(p.period)) ?? []);
  readonly occupancy = computed(() => this.data()?.analytics.zones.map(z =>
    ({ name: z.zoneName, values: z.series.map(p => p.averageOccupancy) })).filter(x => x.values.some(p => p !== null)) ?? []);
  readonly traffic = computed(() => this.data()?.analytics.zones.map(z => ({ name: z.zoneName,
    values: z.series.map(p => p.dataQuality.kind === 'InsufficientData' ? null : p.traffic[this.metric()]) }))
    .filter(x => x.values.some(p => p !== null)) ?? []);

  constructor() {
    this.requests.pipe(switchMap(({ id, filter }) => this.api.sessionReport(id, filter).pipe(
      catchError(error => { this.error.set(error.message); return of(null); }),
    )), takeUntilDestroyed()).subscribe(result => { if (result) this.data.set(result); this.loading.set(false); });
    effect(() => {
      const id = this.id();
      this.data.set(null);
      this.form.reset({ start: null, end: null, interval: 'hour' });
      this.load(id, {});
    });
  }

  apply(): void {
    const video = this.data()?.analytics.period.timeBasis !== 'eventTime';
    const { videoLoop, start, end, interval } = this.form.getRawValue();
    if (this.form.invalid || (start != null && !Number.isSafeInteger(start)) || (end != null && !Number.isSafeInteger(end))
      || (start != null && end != null && start >= end)) {
      this.inputError.set('Use whole millisecond positions and an end position after the start.');
      return;
    }
    this.inputError.set('');
    this.load(this.id(), { interval, videoLoop: video ? videoLoop || undefined : undefined,
      fromSourceTimeMs: video ? start ?? undefined : undefined,
      toSourceTimeMs: video ? end ?? undefined : undefined });
  }

  private load(id: string, filter: ReportFilter): void {
    this.loading.set(true);
    this.error.set('');
    this.requests.next({ id, filter });
  }
}
