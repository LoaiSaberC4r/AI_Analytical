import { CameraEvent, LiveCamera, LiveZone, StatusEvent, ZoneEvent } from '../models/models';

const version = (value: number | null) => value ?? -1;

export function cameraEvent(camera: LiveCamera, event: StatusEvent | CameraEvent): LiveCamera {
  if (event.cameraId !== camera.cameraId || event.stateVersion < version(camera.stateVersion)) return camera;
  const previousSession = camera.sessionId !== event.sessionId;
  return {
    ...camera, sessionId: event.sessionId, stateVersion: event.stateVersion, analyticsStatus: event.analyticsStatus,
    dataFreshness: event.dataFreshness, lastReceivedAtUtc: event.receivedAtUtc,
    ...('sequence' in event ? { lastSequence: event.sequence, sourceType: event.sourceType,
      staffCount: event.staffCount, processingFps: event.processingFps } : {}),
    zones: camera.zones.map(zone => ({
      ...zone,
      previousSession: previousSession ? zone.sessionId !== null : zone.previousSession,
      dataFreshness: event.dataFreshness === 'NoRecentData' && zone.dataFreshness !== 'NeverReceived'
        ? 'NoRecentData' : event.dataFreshness === 'Stale' && zone.dataFreshness === 'Fresh' ? 'Stale' : zone.dataFreshness,
    })),
  };
}

export function zoneEvent(camera: LiveCamera, event: ZoneEvent): LiveCamera {
  if (event.cameraId !== camera.cameraId || event.sessionId !== camera.sessionId) return camera;
  const apply = (zone: LiveZone): LiveZone => {
    if (zone.zoneId !== event.zoneId || event.stateVersion <= version(zone.stateVersion)) return zone;
    const utilization = zone.capacity === null ? null : event.occupancy / zone.capacity * 100;
    const capacityStatus = utilization === null ? null : utilization >= zone.criticalThresholdPercent ? 'Critical'
      : utilization >= zone.warningThresholdPercent ? 'Warning' : 'WithinCapacity';
    return {
      ...zone, sessionId: event.sessionId, sequence: event.sequence, stateVersion: event.stateVersion,
      previousSession: false, dataFreshness: 'Fresh', occupancy: event.occupancy, activeVisitorsCount: event.activeVisitorsCount,
      zoneArrivals: event.arrivals, zoneDepartures: event.departures, entry: event.entry, exit: event.exit,
      wrongWayIn: event.wrongWayIn, wrongWayOut: event.wrongWayOut,
      reportedAverageDwellSeconds: event.reportedAverageDwellSeconds, longestActiveDwellSeconds: event.longestActiveDwellSeconds,
      utilizationPercent: utilization, capacityStatus,
      lastUpdatedAtUtc: event.receivedAtUtc,
    };
  };
  return { ...camera, zones: camera.zones.map(apply) };
}

export function mergeSnapshot(incoming: LiveCamera, current: LiveCamera | undefined): LiveCamera {
  if (!current) return incoming;
  const zones = new Map(current.zones.map(zone => [zone.zoneId, zone]));
  const latest = version(current.stateVersion) > version(incoming.stateVersion) ? current : incoming;
  return {
    ...latest, zones: incoming.zones.map(zone => {
      const local = zones.get(zone.zoneId);
      const selected = local && version(local.stateVersion) > version(zone.stateVersion) ? local : zone;
      return { ...selected, previousSession: selected.sessionId !== null && selected.sessionId !== latest.sessionId };
    }),
  };
}
