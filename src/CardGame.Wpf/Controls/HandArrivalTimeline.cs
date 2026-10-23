namespace CardGame.Wpf.Controls;

/// <summary>In-flight presentation times only; no card identity or gameplay state.</summary>
public sealed class HandArrivalTimeline
{
    private readonly List<(long Revision, HandArrivalClock Clock)> _incoming = [];
    private readonly List<WeakReference<HandArrivalBatch>> _batches = [];

    internal HandArrivalBatch Begin(long previousRevision, long revision, TimeProvider clock, double scale)
    {
        Prune();
        var batch = new HandArrivalBatch(previousRevision, revision, clock, scale);
        foreach (var incoming in _incoming) batch.Observe(incoming.Revision, incoming.Clock);
        _batches.Add(new(batch));
        return batch;
    }

    internal void Register(long revision, TimeProvider clock, long start, double delay, double scale)
    {
        Prune();
        var incoming = new HandArrivalClock(clock, start, delay + CardLiftSurface.ArrivalStartTime, scale);
        _incoming.Add((revision, incoming));
        foreach (var reference in _batches)
            if (reference.TryGetTarget(out var batch)) batch.Observe(revision, incoming);
    }

    internal void SetTimeScale(double scale)
    {
        foreach (var incoming in _incoming) incoming.Clock.SetTimeScale(scale);
        foreach (var reference in _batches)
            if (reference.TryGetTarget(out var batch)) batch.SetTimeScale(scale);
        Prune();
    }

    internal void Prune()
    {
        _incoming.RemoveAll(item => item.Clock.ElapsedAfterArrival >= CardLiftSurface.ArrivalDuration);
        _batches.RemoveAll(reference => !reference.TryGetTarget(out var batch) || batch.HasSurface && batch.IsComplete);
    }

    internal void Clear()
    {
        foreach (var reference in _batches)
            if (reference.TryGetTarget(out var batch)) batch.Finish();
        _batches.Clear();
        _incoming.Clear();
    }
}

internal sealed class HandArrivalBatch(long previousRevision, long revision, TimeProvider clock, double scale)
{
    private readonly HandArrivalClock _fallback = new(clock, clock.GetTimestamp(), CardLiftSurface.ArrivalStartTime, scale);
    private HandArrivalClock? _incoming;
    private bool _finished;
    internal bool HasSurface { get; private set; }
    internal double ElapsedAfterArrival => _finished ? CardLiftSurface.ArrivalDuration :
        (_incoming ?? _fallback).ElapsedAfterArrival;
    internal bool IsComplete => ElapsedAfterArrival >= CardLiftSurface.ArrivalDuration;

    internal void Observe(long incomingRevision, HandArrivalClock incoming)
    {
        if (_finished || incomingRevision <= previousRevision || incomingRevision > revision) return;
        // A coalesced local snapshot can contain several incoming results. Its
        // new faces wait for the last one, without linking hidden card IDs to cues.
        if (_incoming is null || incoming.ElapsedAfterArrival < _incoming.ElapsedAfterArrival)
            _incoming = incoming;
    }

    internal void SetTimeScale(double scale)
    {
        _fallback.SetTimeScale(scale);
        _incoming?.SetTimeScale(scale);
    }
    internal void SetFallbackClock(TimeProvider clock) => _fallback.SetClock(clock);
    internal void Activate() => HasSurface = true;
    internal void Finish() => _finished = true;
}

internal sealed class HandArrivalClock(TimeProvider clock, long start, double arrival, double scale)
{
    private TimeProvider _clock = clock;
    private long _start = start;
    private double _elapsed;
    private double _scale = scale;
    internal double ElapsedAfterArrival => _elapsed + _clock.GetElapsedTime(_start).TotalSeconds / _scale - arrival;

    internal void SetTimeScale(double value)
    {
        if (_scale == value) return;
        _elapsed += _clock.GetElapsedTime(_start).TotalSeconds / _scale;
        _start = _clock.GetTimestamp();
        _scale = value;
    }

    internal void SetClock(TimeProvider value)
    {
        if (ReferenceEquals(_clock, value)) return;
        _elapsed += _clock.GetElapsedTime(_start).TotalSeconds / _scale;
        _clock = value;
        _start = value.GetTimestamp();
    }
}
