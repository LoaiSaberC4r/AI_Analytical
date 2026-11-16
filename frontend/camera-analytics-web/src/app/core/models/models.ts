export interface Page<T> {
  items: T[]; page: number; pageSize: number; totalCount: number; totalPages: number;
}
export interface CatalogRecord {
  id: string; name: string; description: string | null; isActive: boolean; rowVersion: string;
  createdAtUtc: string; updatedAtUtc: string;
}
export interface Site extends CatalogRecord { code: string; timeZoneId: string; }
export interface Camera extends CatalogRecord {
  siteId: string; siteName: string; externalCameraId: string; zonesCount: number;
  mqttTopic: string; lastReceivedAtUtc: string | null;
}
export interface Zone extends CatalogRecord {
  cameraId: string; externalZoneId: string; capacity: number | null;
  warningThresholdPercent: number; criticalThresholdPercent: number;
}
export interface SiteWrite { name: string; code: string; description: string; timeZoneId: string; rowVersion?: string; }
export interface CameraWrite { name: string; siteId: string; externalCameraId: string; description: string; rowVersion?: string; }
export interface ZoneWrite {
  name: string; externalZoneId: string; description: string; capacity: number | null;
  warningThresholdPercent: number | null; criticalThresholdPercent: number | null; rowVersion?: string;
}
export type Freshness = 'NeverReceived' | 'Fresh' | 'Stale' | 'NoRecentData';
export interface LiveZone {
  zoneId: string; cameraId: string; externalZoneId: string; zoneName: string; isActive: boolean;
  sessionId: string | null; sequence: number | null; stateVersion: number | null;
  previousSession: boolean; dataFreshness: Freshness; occupancy: number | null;
  zoneArrivals: number | null; zoneDepartures: number | null; entry: number | null; exit: number | null;
  wrongWayIn: number | null; wrongWayOut: number | null; activeVisitorsCount: number | null;
  reportedAverageDwellSeconds: number | null; longestActiveDwellSeconds: number | null;
  capacity: number | null; warningThresholdPercent: number; criticalThresholdPercent: number;
  utilizationPercent: number | null; capacityStatus: string | null; lastUpdatedAtUtc: string | null;
}
export interface LiveCamera {
  cameraId: string; siteId: string; externalCameraId: string; cameraName: string; isActive: boolean;
  sessionId: string | null; lastSequence: number | null; sourceType: string | null; analyticsStatus: string | null;
  dataFreshness: Freshness; processingFps: number | null; staffCount: number | null; lastReceivedAtUtc: string | null;
  stateVersion: number | null; zones: LiveZone[];
}
export interface LiveSite { siteId: string; siteName: string; isActive: boolean; cameras: LiveCamera[]; }
export interface MonitoringStatus {
  registeredCameras: number; activeCameras: number; fresh: number; stale: number; noRecentData: number;
  neverReceived: number; ended: number; mqttEnabled: boolean; mqttSubscribed: boolean; checkedAtUtc: string;
}
export interface StatusEvent {
  cameraId: string; siteId: string; sessionId: string; stateVersion: number; analyticsStatus: string;
  dataFreshness: Freshness; receivedAtUtc: string;
}
export interface CameraEvent extends StatusEvent { sequence: number; sourceType: string; processingFps: number; staffCount: number; }
export interface ZoneEvent {
  cameraId: string; zoneId: string; sessionId: string; sequence: number; stateVersion: number; occupancy: number;
  arrivals: number; departures: number; entry: number; exit: number; wrongWayIn: number; wrongWayOut: number;
  reportedAverageDwellSeconds: number; activeVisitorsCount: number; longestActiveDwellSeconds: number | null; receivedAtUtc: string;
}
export interface SummaryEvent { cameraId: string; siteId: string; sessionId: string; stateVersion: number; receivedAtUtc: string; }

export type Interval = 'minute' | 'hour' | 'day';
export interface ReportFilter {
  siteId?: string; cameraId?: string; zoneId?: string; sessionId?: string; sourceType?: 'live' | 'video_file';
  from?: string; to?: string; interval?: Interval; fromSourceTimeMs?: number; toSourceTimeMs?: number;
  videoLoop?: string;
}
export interface Period {
  timeBasis: 'eventTime' | 'sourceTime'; sourceType: string; interval: Interval; timeZoneId: string;
  from: string | null; to: string | null; fromSourceTimeMs: number | null; toSourceTimeMs: number | null;
  videoLoop?: number;
}
export interface PeriodPoint {
  startUtc: string | null; endUtc: string | null; localStart: string | null;
  startSourceTimeMs: number | null; endSourceTimeMs: number | null;
}
export interface Quality {
  kind: 'Reported' | 'Derived' | 'Estimated' | 'InsufficientData'; sampleCount: number; coveragePercent: number;
  observationDurationSeconds: number; counterResetCount: number; baselineCount: number; gapCount: number;
}
export interface Traffic {
  arrivals: number; departures: number; entries: number; exits: number; wrongWayIn: number; wrongWayOut: number;
  wrongWayEvents: number;
}
export interface ZonePoint {
  period: PeriodPoint; averageOccupancy: number | null; peakOccupancy: number | null;
  peakAtUtc: string | null; peakAtSourceTimeMs: number | null; traffic: Traffic; dataQuality: Quality;
}
export interface OccupancyZone {
  zoneId: string; zoneName: string; averageOccupancy: number | null; peakOccupancy: number | null;
  peakAtUtc: string | null; peakAtSourceTimeMs: number | null; peakHour: ZonePoint | null; capacity: number | null;
  peakUtilizationPercent: number | null; dataQuality: Quality; series: ZonePoint[];
}
export interface DwellZone {
  zoneId: string; zoneName: string; reportedAverageDwellSeconds: number | null; longestActiveDwellSeconds: number | null;
  estimatedCompletedVisits: number; estimatedTotalDwellSeconds: number | null;
  estimatedHistoricalAverageDwellSeconds: number | null; uncertainVisits: number; dataQuality: Quality;
}
export interface TrafficZone { zoneId: string; zoneName: string; traffic: Traffic; dataQuality: Quality; series: ZonePoint[]; }
export interface ZoneResult extends DwellZone, TrafficZone {
  cameraId: string; capacity: number | null; averageOccupancy: number | null; peakOccupancy: number | null;
  peakAtUtc: string | null; peakAtSourceTimeMs: number | null; peakUtilizationPercent: number | null;
  averageUtilizationPercent: number | null;
}
export interface ZoneReport<T> { period: Period; zones: T[]; }
export interface RankingRow { rank: number; zoneId: string; zoneName: string; value: number; dataQuality: Quality; }
export interface Ranking { period: Period; metric: string; unit: string; items: RankingRow[]; }
export interface Overview {
  period: Period; mostVisitedZone: RankingRow | null; topEntryZone: RankingRow | null; mostCrowdedZone: RankingRow | null;
  mostDepartedZone: RankingRow | null; topExitZone: RankingRow | null; peakOccupancy: RankingRow | null;
  longestActiveStay: RankingRow | null; estimatedHistoricalDwell: RankingRow | null; highestWrongWayZone: RankingRow | null;
  observedZoneAverageOccupancy: number | null; traffic: Traffic; dataQuality: Quality;
}
export interface PeakHour {
  zoneId: string; zoneName: string; occupancy: ZonePoint | null; arrivals: ZonePoint | null;
  entries: ZonePoint | null; departures: ZonePoint | null; wrongWayEvents: ZonePoint | null;
}
export interface StaffPoint { period: PeriodPoint; averageStaffCount: number | null; peakStaffCount: number | null; dataQuality: Quality; }
export interface StaffCamera {
  cameraId: string; cameraName: string; latestStaffCount: number | null; lastReceivedAtUtc: string | null;
  averageStaffCount: number | null; peakStaffCount: number | null; dataQuality: Quality; series: StaffPoint[];
}
export interface StaffReport { period: Period; cameras: StaffCamera[]; }
export interface Session {
  id: string; cameraId: string; cameraName: string; externalSessionId: string; sourceType: string; sourceName: string; status: string;
  firstSequence: number; lastSequence: number; firstSourceTimeMs: number | null; lastSourceTimeMs: number | null;
  startedAtUtc: string; lastReceivedAtUtc: string; endedAtUtc: string | null;
}
export interface SessionReport {
  session: Session; analytics: ZoneReport<ZoneResult>; staff: StaffReport; untimedVideoObservations: number;
  availableVideoLoops?: (number | null)[];
}
