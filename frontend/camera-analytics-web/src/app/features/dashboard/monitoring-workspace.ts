import { DatePipe } from '@angular/common';
import { Component, computed, DestroyRef, effect, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { catchError, EMPTY, forkJoin, Observable, of, timer } from 'rxjs';
import { AnalyticsApi } from '../../core/api/api';
import { CONFIGURATION } from '../../core/configuration/configuration';
import { CameraEvent, LiveCamera, LiveSite, MonitoringStatus, OccupancyZone, Overview, Site, StatusEvent, ZoneEvent } from '../../core/models/models';
import { LiveConnection, LiveScope } from '../../core/signalr/live-connection';
import { cameraEvent, mergeSnapshot, zoneEvent } from '../../core/signalr/live-state';
import { AnalyticsChart } from '../../shared/components/chart';
import { DataTable } from '../../shared/components/data-table';
import { freshnessLabel, number, periodLabel } from '../../shared/utilities/format';

@Component({
  selector: 'ca-monitoring',
  templateUrl: './monitoring-workspace.html',
  imports: [RouterLink, DatePipe, AnalyticsChart, DataTable],
  providers: [LiveConnection],
})
export class MonitoringWorkspace {
  readonly mode = input.required<'overview' | 'live'>();
  readonly cameraId = input<string>();
  readonly live = inject(LiveConnection);
  private readonly api = inject(AnalyticsApi);
  private readonly destroy = inject(DestroyRef);
  private readonly refreshMs = inject(CONFIGURATION).refreshIntervalSeconds * 1000;
  private scope?: LiveScope;
  private epoch = 0;
  private loadingScope = false;
  private pendingRefresh = false;
  private lastHistoryLoad = 0;
  private lastStatusLoad = 0;
  readonly sites = signal<Site[]>([]);
  readonly siteId = signal('');
  readonly cameras = signal<LiveCamera[]>([]);
  readonly selectedCamera = signal('');
  readonly initial = signal(true);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly historyError = signal('');
  readonly siteName = signal('');
  readonly status = signal<MonitoringStatus | null>(null);
  readonly history = signal<Overview | null>(null);
  readonly occupancy = signal<OccupancyZone[]>([]);
  readonly visibleCameras = computed(() => this.selectedCamera()
    ? this.cameras().filter(camera => camera.cameraId === this.selectedCamera()) : this.cameras());
  readonly freshCameras = computed(() => this.cameras().filter(x => x.isActive && x.dataFreshness === 'Fresh'));
  readonly currentZones = computed(() => this.freshCameras().flatMap(x => x.zones)
    .filter(z => z.isActive && !z.previousSession && z.dataFreshness === 'Fresh' && z.occupancy !== null));
  readonly currentOccupancy = computed(() => this.currentZones().length
    ? this.currentZones().reduce((sum, z) => sum + (z.occupancy ?? 0), 0) : null);
  readonly chartLabels = computed(() => this.occupancy()[0]?.series.map(x => periodLabel(x.period)) ?? []);
  readonly chartLines = computed(() => this.occupancy().filter(z => z.series.some(x => x.averageOccupancy !== null))
    .map(z => ({ name: z.zoneName, values: z.series.map(x => x.averageOccupancy) })));
  readonly number = number;
  readonly freshnessLabel = freshnessLabel;

  constructor() {
    this.api.all(page => this.api.sites({ page, pageSize: 100 })).pipe(takeUntilDestroyed()).subscribe({
      next: sites => { this.sites.set(sites); if (!this.siteId()) this.siteId.set(sites[0]?.id ?? ''); this.initial.set(false); },
      error: error => { this.error.set(error.message); this.initial.set(false); },
    });
    effect(() => {
      const camera = this.cameraId();
      const site = this.siteId();
      this.setScope(camera ? { kind: 'Camera', id: camera } : site ? { kind: 'Site', id: site } : undefined);
    });
    timer(0, this.refreshMs).pipe(takeUntilDestroyed()).subscribe(() => {
      this.refreshLive();
      this.refreshStatus();
      if (this.mode() === 'overview') this.refreshHistory();
    });
  }

  selectSite(id: string): void { this.siteId.set(id); }

  private setScope(scope: LiveScope | undefined): void {
    if (scope?.kind === this.scope?.kind && scope?.id === this.scope?.id) return;
    this.live.stop();
    this.scope = scope;
    this.epoch++;
    this.loadingScope = false;
    this.pendingRefresh = false;
    this.selectedCamera.set('');
    this.siteName.set('');
    this.cameras.set([]);
    this.history.set(null);
    this.occupancy.set([]);
    this.historyError.set('');
    this.lastHistoryLoad = 0;
    if (!scope) return;
    this.busy.set(true);
    this.refreshLive();
    if (scope.kind === 'Site' && this.mode() === 'overview') this.refreshHistory();
    this.live.watch(scope, {
      camera: event => this.applyCamera(event),
      status: event => { this.applyCamera(event); this.refreshStatus(); },
      zone: event => this.applyZone(event),
      summary: () => { if (this.mode() === 'overview') this.refreshHistory(); },
      synchronized: () => {
        this.refreshLive(true);
        this.refreshStatus();
        if (this.mode() === 'overview') this.refreshHistory(true);
      },
    });
  }

  private applyCamera(event: CameraEvent | StatusEvent): void {
    if (!this.cameras().some(x => x.cameraId === event.cameraId)) {
      this.refreshLive();
      return;
    }
    this.cameras.update(rows => rows.map(x => cameraEvent(x, event)));
  }

  private applyZone(event: ZoneEvent): void {
    if (!this.cameras().some(x => x.cameraId === event.cameraId && x.zones.some(z => z.zoneId === event.zoneId))) {
      this.refreshLive();
      return;
    }
    this.cameras.update(rows => rows.map(x => zoneEvent(x, event)));
  }

  private refreshLive(resynchronize = false): void {
    const scope = this.scope;
    if (!scope) return;
    if (this.loadingScope) {
      this.pendingRefresh ||= resynchronize;
      return;
    }
    this.loadingScope = true;
    const epoch = this.epoch;
    const request: Observable<LiveCamera | LiveSite> = scope.kind === 'Camera' ? this.api.cameraLive(scope.id) : this.api.siteLive(scope.id);
    request.pipe(takeUntilDestroyed(this.destroy)).subscribe({
      next: result => {
        if (epoch !== this.epoch) return;
        const incoming = 'cameras' in result ? result.cameras : [result];
        this.siteName.set('cameras' in result ? result.siteName : result.cameraName);
        this.cameras.update(current => incoming.map(camera => mergeSnapshot(camera, current.find(x => x.cameraId === camera.cameraId))));
        this.error.set('');
        this.busy.set(false);
        this.finishRefresh();
      },
      error: error => {
        if (epoch !== this.epoch) return;
        this.error.set(error.message);
        this.busy.set(false);
        this.finishRefresh();
      },
    });
  }

  private finishRefresh(): void {
    this.loadingScope = false;
    if (this.pendingRefresh) {
      this.pendingRefresh = false;
      this.refreshLive();
    }
  }

  private refreshStatus(): void {
    if (Date.now() - this.lastStatusLoad < this.refreshMs) return;
    this.lastStatusLoad = Date.now();
    this.api.monitoring().pipe(takeUntilDestroyed(this.destroy), catchError(() => EMPTY)).subscribe(result => this.status.set(result));
  }

  private refreshHistory(force = false): void {
    const scope = this.scope;
    if (scope?.kind !== 'Site' || (!force && Date.now() - this.lastHistoryLoad < this.refreshMs * 2)) return;
    this.lastHistoryLoad = Date.now();
    const epoch = this.epoch;
    const to = new Date();
    const from = new Date(to.getTime() - 12 * 60 * 60 * 1000);
    const filter = { siteId: scope.id, from: from.toISOString(), to: to.toISOString(), interval: 'hour' as const };
    forkJoin({ summary: this.api.overview(filter), occupancy: this.api.occupancy(filter) })
      .pipe(takeUntilDestroyed(this.destroy), catchError(error => {
        if (epoch === this.epoch) this.historyError.set(error.message);
        return of(null);
      })).subscribe(result => {
        if (!result || epoch !== this.epoch) return;
        this.historyError.set('');
        this.history.set(result.summary);
        this.occupancy.set(result.occupancy.zones);
      });
  }
}
