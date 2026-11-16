import { DatePipe } from '@angular/common';
import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { catchError, of, Subject, switchMap } from 'rxjs';
import { AnalyticsApi } from '../../core/api/api';
import { Camera, Page, Session } from '../../core/models/models';
import { DataTable } from '../../shared/components/data-table';
import { videoTime } from '../../shared/utilities/format';

@Component({
  selector: 'ca-sessions',
  imports: [RouterLink, DatePipe, DataTable],
  template: `
    <header class="page-heading"><div><h1>Sessions</h1><p>Browse camera processing sessions and their associated observations.</p></div></header>
    <section class="panel">
      <form class="toolbar" (submit)="$event.preventDefault(); load()">
        <label>Search<input maxlength="128" placeholder="Source name or session ID" [value]="search()" (input)="search.set($any($event.target).value)"></label>
        <label>Source<select (change)="source.set($any($event.target).value); load()" [value]="source()">
          <option value="video_file">Recorded video</option><option value="live">Live</option><option value="">All sources</option>
        </select></label>
        <label>Camera<select (change)="camera.set($any($event.target).value); load()" [value]="camera()"><option value="">All cameras</option>
          @for (row of cameras(); track row.id) { <option [value]="row.id">{{ row.name }}</option> }
        </select></label>
        <button type="submit" class="secondary">Search</button>
      </form>
      @if (error()) { <p class="notice error" role="alert">{{ error() }}</p> }
      @else if (loading()) { <p class="notice" role="status">Loading sessions…</p> }
      @else if (!data()?.items?.length) {
        <div class="empty"><h2>No sessions match</h2><p>Sessions appear after a registered camera publishes analytics.</p></div>
      } @else if (data(); as page) {
        <ca-table [headers]="['Source','Camera','Status','Processed position','First received','']">
          @for (row of page.items; track row.id) {
            <tr><td><strong>{{ row.sourceName }}</strong><small>{{ row.externalSessionId }}</small></td><td>{{ row.cameraName }}</td>
              <td><span class="pill" [class.good]="row.status === 'running'">{{ row.status }}</span></td>
              <td>{{ row.sourceType === 'video_file' ? videoTime(row.lastSourceTimeMs, true) : '—' }}</td>
              <td>{{ row.startedAtUtc | date:'medium' }}</td>
              <td><a [routerLink]="['/sessions',row.id]">View report</a></td></tr>
          }
        </ca-table>
        @if (page.totalPages > 1) { <div class="pager">
          <button type="button" class="secondary" [disabled]="page.page <= 1" (click)="load(page.page-1)">Previous</button>
          <span>Page {{ page.page }} of {{ page.totalPages }}</span>
          <button type="button" class="secondary" [disabled]="page.page >= page.totalPages" (click)="load(page.page+1)">Next</button>
        </div> }
      }
    </section>
  `,
})
export class Sessions {
  private readonly api = inject(AnalyticsApi);
  private readonly destroy = inject(DestroyRef);
  private readonly requests = new Subject<number>();
  readonly videoTime = videoTime;
  readonly cameras = signal<Camera[]>([]);
  readonly search = signal('');
  readonly source = signal('video_file');
  readonly camera = signal('');
  readonly error = signal('');
  readonly loading = signal(true);
  readonly data = signal<Page<Session> | null>(null);

  constructor() {
    this.requests.pipe(switchMap(page => this.api.sessions({ page, pageSize: 20, sourceType: this.source(),
      search: this.search(), cameraId: this.camera() }).pipe(catchError(error => {
      this.error.set(error.message);
      return of(null);
    }))), takeUntilDestroyed()).subscribe(result => { this.data.set(result); this.loading.set(false); });
    this.api.all(page => this.api.cameras({ page, pageSize: 100 })).pipe(takeUntilDestroyed(this.destroy))
      .subscribe({ next: rows => this.cameras.set(rows), error: e => this.error.set(e.message) });
    this.load();
  }

  load(page = 1): void {
    this.loading.set(true);
    this.error.set('');
    this.requests.next(page);
  }
}
