using System.Collections;

namespace CardGame.Core;

/// <summary>The one frame stack. Runtime owns transitions; callers can only inspect it.</summary>
internal sealed class FrameStore : IReadOnlyList<ResolutionFrame>
{
    private readonly List<ResolutionFrame> _frames = [];
    public int Count => _frames.Count;
    public ResolutionFrame this[int index] => _frames[index];
    public ResolutionFrame? LastOrDefault() => _frames.Count == 0 ? null : _frames[^1];

    public void Push(ResolutionFrame frame)
    {
        if (frame.Id <= 0 || _frames.Any(current => current.Id == frame.Id))
            throw new InvalidOperationException("A runtime frame requires a unique positive identity.");
        _frames.Add(frame);
    }

    public void Replace(ResolutionFrame next)
    {
        var index = _frames.FindLastIndex(frame => frame.Id == next.Id);
        if (index < 0 || _frames[index].Kind != next.Kind || _frames[index].GetType() != next.GetType())
            throw new InvalidOperationException("A runtime write must preserve the existing frame identity and type.");
        _frames[index] = next;
    }

    public ResolutionFrame CompleteTop(long frameId, ResolutionFrameKind expectedKind)
    {
        if (_frames.Count == 0 || _frames[^1].Id != frameId || _frames[^1].Kind != expectedKind)
            throw new InvalidOperationException($"Resolution frame {frameId} is not the top {expectedKind} frame.");
        var completed = _frames[^1];
        _frames.RemoveAt(_frames.Count - 1);
        return completed;
    }

    public int FindIndex(Predicate<ResolutionFrame> match) => _frames.FindIndex(match);
    public int FindLastIndex(Predicate<ResolutionFrame> match) => _frames.FindLastIndex(match);
    public int IndexOf(ResolutionFrame frame) => _frames.IndexOf(frame);
    public IReadOnlyList<ResolutionFrame> AsReadOnly() => _frames.AsReadOnly();
    public IEnumerator<ResolutionFrame> GetEnumerator() => _frames.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
