using JasperFx.Events;

namespace LupiraCalApi.UnitTests;

/// <summary>Wraps event payloads as <see cref="IEvent{T}"/> the way Marten hydrates them on replay: a server
/// timestamp and a monotonically increasing global sequence, so SectionLww's unstamped fallback preserves
/// append order exactly as it would against a live store.</summary>
internal static class TestEvents
{
    private static long _sequence;
    public static readonly DateTimeOffset T0 = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    public static IEvent<T> Ev<T>(T data, DateTimeOffset? at = null) where T : class
    {
        var seq = Interlocked.Increment(ref _sequence);
        var e = Event.For(data);
        e.Sequence = seq;
        e.Timestamp = at ?? T0.AddSeconds(seq);
        return e;
    }
}
