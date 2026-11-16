import { describe, expect, it } from 'vitest';
import { CameraEvent, LiveCamera, LiveZone, ZoneEvent } from '../models/models';
import { freshnessLabel } from '../../shared/utilities/format';
import { cameraEvent, mergeSnapshot, zoneEvent } from './live-state';

const zone: LiveZone = {
  zoneId: 'z', cameraId: 'c', zoneName: 'Entrance', externalZoneId: 'zone_01', isActive: true,
  sessionId: 's', sequence: 2, stateVersion: 2, dataFreshness: 'Fresh', previousSession: false,
  occupancy: 3, zoneArrivals: 4, zoneDepartures: 1, entry: 3, exit: 1, wrongWayIn: 0, wrongWayOut: 0,
  reportedAverageDwellSeconds: 0, activeVisitorsCount: 0, longestActiveDwellSeconds: null,
  capacity: 10, warningThresholdPercent: 80, criticalThresholdPercent: 100,
  utilizationPercent: 30, capacityStatus: 'WithinCapacity', lastUpdatedAtUtc: '2026-11-16T14:00:00Z',
};
const camera: LiveCamera = {
  cameraId: 'c', siteId: 'a', externalCameraId: '1', cameraName: 'Entrance', isActive: true,
  sessionId: 's', lastSequence: 2, sourceType: 'video_file', analyticsStatus: 'running', dataFreshness: 'Fresh',
  processingFps: 8, staffCount: 2, lastReceivedAtUtc: zone.lastUpdatedAtUtc, stateVersion: 2, zones: [zone],
};
const cameraUpdate: CameraEvent = { cameraId: 'c', siteId: 'a', sessionId: 's', sequence: 3, stateVersion: 3,
  sourceType: 'live', analyticsStatus: 'running', dataFreshness: 'Fresh', processingFps: 9, staffCount: 3, receivedAtUtc: '2026-11-16T14:00:01Z' };
const zoneUpdate: ZoneEvent = {
  cameraId: 'c', zoneId: 'z', sessionId: 's', sequence: 3, stateVersion: 3, occupancy: 11,
  arrivals: 10, departures: 2, entry: 4, exit: 1, wrongWayIn: 1, wrongWayOut: 0,
  reportedAverageDwellSeconds: 9, activeVisitorsCount: 1, longestActiveDwellSeconds: 10,
  receivedAtUtc: cameraUpdate.receivedAtUtc,
};

describe('SignalR state', () => {
  it('updates live camera and zone fields and ignores older REST snapshots', () => {
    const latest = zoneEvent(cameraEvent(camera, cameraUpdate), zoneUpdate);
    const reconnected = mergeSnapshot(camera, latest);
    expect(reconnected.staffCount).toBe(3);
    expect(reconnected.sourceType).toBe('live');
    expect(reconnected.zones[0].occupancy).toBe(11);
    expect(reconnected.zones[0].utilizationPercent).toBeCloseTo(110);
    expect(reconnected.zones[0].capacityStatus).toBe('Critical');
    expect(cameraEvent(latest, { ...cameraUpdate, staffCount: 99, stateVersion: 1 })).toBe(latest);
    expect(zoneEvent(latest, { ...zoneUpdate, occupancy: 99, stateVersion: 2 }).zones[0]).toBe(latest.zones[0]);
  });

  it('marks retained readings stale and prevents a previous session from overriding the latest data', () => {
    const stale = cameraEvent(camera, { ...cameraUpdate, dataFreshness: 'Stale' });
    expect(freshnessLabel(stale.dataFreshness)).toBe('Stale');
    expect(stale.zones[0].occupancy).toBe(3);
    const newSession = cameraEvent(stale, { ...cameraUpdate, sessionId: 'new', stateVersion: 4 });
    expect(newSession.zones[0].previousSession).toBe(true);
    expect(zoneEvent(newSession, zoneUpdate)).toBe(newSession);
  });
});
