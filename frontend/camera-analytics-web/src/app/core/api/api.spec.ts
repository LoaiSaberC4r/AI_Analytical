import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { AnalyticsApi } from './api';
import { CONFIGURATION } from '../configuration/configuration';
import { problemDetailsInterceptor } from '../interceptors/problem-details';

describe('AnalyticsApi', () => {
  let http: HttpTestingController;
  let api: AnalyticsApi;
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [
      provideHttpClient(withInterceptors([problemDetailsInterceptor])), provideHttpClientTesting(),
      { provide: CONFIGURATION, useValue: { apiBaseUrl: 'http://localhost:5080' } },
    ] });
    api = TestBed.inject(AnalyticsApi);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  it('passes UTC bounds and filters to the versioned ranking route', () => {
    api.rankings({ zoneId: 'zone-id', from: '2026-11-16T13:17:54Z', interval: 'hour' }, 'entries').subscribe();
    const request = http.expectOne(x => x.url === 'http://localhost:5080/api/v1/analytics/zones/rankings');
    expect(request.request.params.get('from')).toBe('2026-11-16T13:17:54Z');
    expect(request.request.params.get('zoneId')).toBe('zone-id');
    expect(request.request.params.get('metric')).toBe('entries');
    request.flush({ items: [] });
  });

  it('includes the row version in a status patch and surfaces ProblemDetails', () => {
    let message = '';
    api.status('zones', 'zone-id', false, 'AAAAAAABAAA=').subscribe({ error: error => message = error.message });
    const request = http.expectOne('http://localhost:5080/api/v1/zones/zone-id/status');
    expect(request.request.method).toBe('PATCH');
    expect(request.request.body).toEqual({ isActive: false, rowVersion: 'AAAAAAABAAA=' });
    request.flush({ detail: 'Reload before saving.', traceId: 'trace-1' }, { status: 409, statusText: 'Conflict' });
    expect(message).toBe('Reload before saving. (trace trace-1)');
  });
});
