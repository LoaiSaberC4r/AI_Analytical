namespace CameraAnalytics.Domain.Enums;

public enum VisitStatus { Active, TemporarilyMissing, EstimatedCompleted, Uncertain }
public enum CounterQuality { Baseline, Valid, Reset, SequenceGap }
public enum MessageStatus { Accepted, HistoricalOnly, Rejected }
public enum DataFreshness { NeverReceived, Fresh, Stale, NoRecentData }
