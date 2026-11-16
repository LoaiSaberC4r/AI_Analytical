import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { EMPTY, expand, map, Observable, reduce } from 'rxjs';
import { CONFIGURATION } from '../configuration/configuration';
import {
  Camera, CameraWrite, DwellZone, LiveCamera, LiveSite, LiveZone, MonitoringStatus, OccupancyZone, Overview,
  Page, PeakHour, Ranking, ReportFilter, Session, SessionReport, Site, SiteWrite, StaffReport,
  Traffic, TrafficZone, Zone, ZoneReport, ZoneWrite,
} from '../models/models';

type Query = Record<string, string | number | boolean | undefined | null>;

@Injectable({ providedIn: 'root' })
export class AnalyticsApi {
  private readonly http = inject(HttpClient);
  private readonly root = inject(CONFIGURATION).apiBaseUrl + '/api/v1';

  private get<T>(path: string, query: Query = {}): Observable<T> {
    let params = new HttpParams();
    for (const [key, value] of Object.entries(query)) {
      if (value !== undefined && value !== null && value !== '') params = params.set(key, value);
    }
    return this.http.get<T>(this.root + path, { params });
  }
  private save<T>(path: string, body: object, id?: string): Observable<T> {
    return id ? this.http.put<T>(this.root + path + '/' + id, body) : this.http.post<T>(this.root + path, body);
  }
  sites(query?: Query) { return this.get<Page<Site>>('/sites', query); }
  cameras(query?: Query) { return this.get<Page<Camera>>('/cameras', query); }
  zones(cameraId: string, query?: Query) { return this.get<Page<Zone>>(`/cameras/${cameraId}/zones`, query); }
  site(id: string) { return this.get<Site>('/sites/' + id); }
  camera(id: string) { return this.get<Camera>('/cameras/' + id); }
  saveSite(input: SiteWrite, id?: string) { return this.save<Site>('/sites', input, id); }
  saveCamera(input: CameraWrite, id?: string) { return this.save<Camera>('/cameras', input, id); }
  saveZone(cameraId: string, input: ZoneWrite, id?: string) {
    return this.save<Zone>(id ? '/zones' : `/cameras/${cameraId}/zones`, input, id);
  }
  status(kind: 'sites' | 'cameras' | 'zones', id: string, isActive: boolean, rowVersion: string) {
    return this.http.patch<void>(`${this.root}/${kind}/${id}/status`, { isActive, rowVersion });
  }
  cameraLive(id: string) { return this.get<LiveCamera>(`/cameras/${id}/live`); }
  siteLive(id: string) { return this.get<LiveSite>(`/sites/${id}/live`); }
  zoneLive(id: string) { return this.get<LiveZone>(`/zones/${id}/live`); }
  monitoring() { return this.get<MonitoringStatus>('/monitoring/status'); }
  overview(filter: ReportFilter) { return this.get<Overview>('/analytics/overview', { ...filter }); }
  rankings(filter: ReportFilter, metric: string) {
    return this.get<Ranking>('/analytics/zones/rankings', { ...filter, metric });
  }
  occupancy(filter: ReportFilter) { return this.get<ZoneReport<OccupancyZone>>('/analytics/occupancy', { ...filter }); }
  peaks(filter: ReportFilter) { return this.get<ZoneReport<PeakHour>>('/analytics/peak-hours', { ...filter }); }
  traffic(filter: ReportFilter) {
    return this.get<ZoneReport<TrafficZone> & { totals: Traffic }>('/analytics/traffic/trends', { ...filter });
  }
  dwell(filter: ReportFilter) { return this.get<ZoneReport<DwellZone>>('/analytics/dwell', { ...filter }); }
  wrongWay(filter: ReportFilter) {
    return this.get<ZoneReport<TrafficZone> & { wrongWayIn: number; wrongWayOut: number; wrongWayEvents: number }>(
      '/analytics/wrong-way', { ...filter });
  }
  staff(filter: ReportFilter) { return this.get<StaffReport>('/analytics/staff', { ...filter }); }
  sessions(query?: Query) { return this.get<Page<Session>>('/analytics/sessions', query); }
  session(id: string) { return this.get<Session>('/analytics/sessions/' + id); }
  sessionReport(id: string, filter: ReportFilter = {}) {
    return this.get<SessionReport>(`/analytics/sessions/${id}/report`, { ...filter });
  }

  all<T>(read: (page: number) => Observable<Page<T>>): Observable<T[]> {
    return read(1).pipe(
      expand(page => page.page < page.totalPages ? read(page.page + 1) : EMPTY),
      map(page => page.items),
      reduce((all, items) => all.concat(items), [] as T[]),
    );
  }
}
