namespace CardGame.Core;

/// <summary>
/// A tiny deterministic generator. Keeping its state in one integer makes seeded
/// matches reproducible without depending on the implementation details of System.Random.
/// </summary>
internal sealed class DeterministicRandom
{
    private uint _state;

    public DeterministicRandom(int seed)
    {
        _state = unchecked((uint)seed);
        if (_state == 0)
        {
            _state = 0x9E3779B9u;
        }
    }

    public uint State => _state;

    public int Next(int exclusiveMax)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(exclusiveMax);
        return (int)(NextUInt() % (uint)exclusiveMax);
    }

    public double NextDouble() => NextUInt() / ((double)uint.MaxValue + 1d);

    public void Shuffle<T>(IList<T> values)
    {
        for (var i = values.Count - 1; i > 0; i--)
        {
            var j = Next(i + 1);
            (values[i], values[j]) = (values[j], values[i]);
        }
    }

    private uint NextUInt()
    {
        var x = _state;
        x ^= x << 13;
        x ^= x >> 17;
        x ^= x << 5;
        _state = x;
        return x;
    }
}
