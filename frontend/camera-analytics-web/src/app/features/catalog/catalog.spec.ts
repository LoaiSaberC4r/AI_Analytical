import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { CONFIGURATION } from '../../core/configuration/configuration';
import { problemDetailsInterceptor } from '../../core/interceptors/problem-details';
import { Catalog } from './catalog';
import { catalogForm } from './catalog-form';

describe('Catalog', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [
    provideRouter([]), provideHttpClient(withInterceptors([problemDetailsInterceptor])), provideHttpClientTesting(),
    { provide: CONFIGURATION, useValue: { apiBaseUrl: '' } },
  ] }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('shows loading followed by an API error', async () => {
    const fixture = TestBed.createComponent(Catalog);
    fixture.componentRef.setInput('kind', 'sites');
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="status"]').textContent).toContain('Loading sites');
    TestBed.inject(HttpTestingController).expectOne('/api/v1/sites?page=1&pageSize=20').flush(
      { detail: 'Database unavailable' }, { status: 500, statusText: 'Internal Server Error' },
    );
    await fixture.whenStable();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain('Database unavailable');
  });

  it('validates required fields, MQTT identifiers, whole capacity and threshold ordering', () => {
    const form = catalogForm('zones');
    expect(form.invalid).toBe(true);
    form.patchValue({ name: 'Zone', externalId: 'zone/+1', capacity: 1.5, warningThresholdPercent: 90, criticalThresholdPercent: 80 });
    expect(form.hasError('thresholds')).toBe(true);
    expect(form.controls.capacity.invalid).toBe(true);
    expect(form.controls.externalId.invalid).toBe(true);
    form.patchValue({ externalId: 'zone_01', capacity: 20, warningThresholdPercent: 80, criticalThresholdPercent: 100 });
    expect(form.valid).toBe(true);
  });
});
