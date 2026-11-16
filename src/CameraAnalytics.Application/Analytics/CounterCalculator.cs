using CameraAnalytics.Application.Abstractions;
using CameraAnalytics.Domain.Enums;

namespace CameraAnalytics.Application.Analytics;

public readonly record struct DeltaResult(Counters Counters, CounterQuality Quality, int ResetMask, bool HasGap);

public static class CounterCalculator
{
    public static DeltaResult Calculate(Counters? previous, Counters current, bool hasGap)
    {
        if (!previous.HasValue)
            return new(default, CounterQuality.Baseline, 0, false);

        var p = previous.Value;
        var reset = 0;
        var changes = new Counters(Delta(p.Arrivals, current.Arrivals, 1), Delta(p.Departures, current.Departures, 2),
            Delta(p.Entry, current.Entry, 4), Delta(p.Exit, current.Exit, 8),
            Delta(p.WrongWayIn, current.WrongWayIn, 16), Delta(p.WrongWayOut, current.WrongWayOut, 32));
        return new(changes, reset != 0 ? CounterQuality.Reset : hasGap ? CounterQuality.SequenceGap : CounterQuality.Valid,
            reset, hasGap);

        long Delta(long from, long to, int bit)
        {
            if (to >= from)
                return to - from;
            reset |= bit;
            return 0;
        }
    }
}
