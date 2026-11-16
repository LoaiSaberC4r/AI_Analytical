import { ChangeDetectionStrategy, Component, computed, DestroyRef, effect, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Observable, Subject, switchMap, catchError, of } from 'rxjs';
import { AnalyticsApi } from '../../core/api/api';
import { Camera, Page, Site, Zone } from '../../core/models/models';
import { DataTable } from '../../shared/components/data-table';
import { catalogForm, CatalogKind } from './catalog-form';

type Row = Site | Camera | Zone;

@Component({
  selector: 'ca-catalog',
  imports: [ReactiveFormsModule, RouterLink, DataTable],
  templateUrl: './catalog.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Catalog {
  readonly kind = input.required<CatalogKind>();
  readonly cameraId = input<string>();
  private readonly api = inject(AnalyticsApi);
  private readonly destroy = inject(DestroyRef);
  private readonly requests = new Subject<{ kind: CatalogKind; cameraId?: string; page: number; search: string; siteId: string }>();
  readonly form = computed(() => catalogForm(this.kind()));
  readonly siteOptions = signal<Site[]>([]);
  readonly linkedCamera = signal<Camera | null>(null);
  readonly page = signal(1);
  readonly result = signal<Page<Row> | null>(null);
  readonly loading = signal(true);
  readonly error = signal('');
  readonly saveError = signal('');
  readonly saving = signal(false);
  readonly editor = signal<'closed' | 'create' | 'edit'>('closed');
  readonly editing = signal<Row | null>(null);
  readonly search = signal('');
  readonly siteFilter = signal('');
  readonly noun = computed(() => ({ sites: 'site', cameras: 'camera', zones: 'zone' })[this.kind()]);
  readonly headers = computed(() => ({
    sites: ['Site', 'Code', 'Time zone', 'Status', 'Actions'],
    cameras: ['Camera', 'Site', 'MQTT topic', 'Zones', 'Status', 'Actions'],
    zones: ['Zone', 'External ID', 'Capacity', 'Warning / critical', 'Status', 'Actions'],
  })[this.kind()]);

  constructor() {
    this.requests.pipe(switchMap(query => this.read(query).pipe(catchError((error: Error) => {
      this.error.set(error.message);
      return of(null);
    }))), takeUntilDestroyed()).subscribe(result => { this.result.set(result); this.loading.set(false); });
    effect(() => {
      const kind = this.kind();
      const cameraId = this.cameraId();
      this.page.set(1);
      this.editor.set('closed');
      this.requests.next({ kind, cameraId, page: 1, search: '', siteId: '' });
      if (kind === 'cameras') this.api.all(p => this.api.sites({ page: p, pageSize: 100 }))
        .pipe(takeUntilDestroyed(this.destroy)).subscribe({ next: rows => this.siteOptions.set(rows), error: e => this.error.set(e.message) });
      if (kind === 'zones' && cameraId) this.api.camera(cameraId)
        .pipe(takeUntilDestroyed(this.destroy)).subscribe({ next: row => this.linkedCamera.set(row), error: e => this.error.set(e.message) });
    });
  }

  reload(page = 1): void {
    this.page.set(page);
    this.loading.set(true);
    this.error.set('');
    this.requests.next({ kind: this.kind(), cameraId: this.cameraId(), page, search: this.search(), siteId: this.siteFilter() });
  }

  private read(query: { kind: CatalogKind; cameraId?: string; page: number; search: string; siteId: string }): Observable<Page<Row>> {
    const base = { page: query.page, pageSize: 20, search: query.search };
    switch (query.kind) {
      case 'sites': return this.api.sites(base);
      case 'cameras': return this.api.cameras({ ...base, siteId: query.siteId });
      case 'zones': return this.api.zones(query.cameraId!, base);
    }
  }

  open(row?: Row): void {
    this.editing.set(row ?? null);
    this.saveError.set('');
    this.form().reset({
      name: row?.name ?? '', description: row?.description ?? '',
      code: row && 'code' in row ? row.code : '', timeZoneId: row && 'timeZoneId' in row ? row.timeZoneId : '',
      siteId: row && 'siteId' in row ? row.siteId : this.siteFilter(),
      externalId: row && 'externalCameraId' in row ? row.externalCameraId : row && 'externalZoneId' in row ? row.externalZoneId : '',
      capacity: row && 'capacity' in row ? row.capacity : null,
      warningThresholdPercent: row && 'capacity' in row ? row.warningThresholdPercent : null,
      criticalThresholdPercent: row && 'capacity' in row ? row.criticalThresholdPercent : null,
    });
    this.editor.set(row ? 'edit' : 'create');
  }

  save(): void {
    const form = this.form();
    form.markAllAsTouched();
    if (form.invalid || this.saving()) return;
    const value = form.getRawValue();
    const row = this.editing();
    const base = { name: value.name.trim(), description: value.description.trim(), rowVersion: row?.rowVersion };
    let request: Observable<Row>;
    switch (this.kind()) {
      case 'sites': request = this.api.saveSite({ ...base, code: value.code.trim(), timeZoneId: value.timeZoneId.trim() }, row?.id); break;
      case 'cameras': request = this.api.saveCamera({ ...base, siteId: value.siteId, externalCameraId: value.externalId }, row?.id); break;
      case 'zones': request = this.api.saveZone(this.cameraId()!, {
        ...base, externalZoneId: value.externalId, capacity: value.capacity,
        warningThresholdPercent: value.warningThresholdPercent, criticalThresholdPercent: value.criticalThresholdPercent,
      }, row?.id); break;
    }
    this.saving.set(true);
    this.saveError.set('');
    request.pipe(takeUntilDestroyed(this.destroy)).subscribe({
      next: () => { this.saving.set(false); this.editor.set('closed'); this.reload(row ? this.page() : 1); },
      error: (error: Error) => { this.saveError.set(error.message); this.saving.set(false); },
    });
  }

  toggle(row: Row): void {
    this.saving.set(true);
    this.api.status(this.kind(), row.id, !row.isActive, row.rowVersion).pipe(takeUntilDestroyed(this.destroy)).subscribe({
      next: () => { this.saving.set(false); this.reload(this.page()); },
      error: (error: Error) => { this.saving.set(false); this.error.set(error.message); },
    });
  }
}
