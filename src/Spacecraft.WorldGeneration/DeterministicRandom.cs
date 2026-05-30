namespace Spacecraft.WorldGeneration;

/// <summary>
/// Tiny deterministic xorshift PRNG. Seeded from <see cref="Noise.Hash"/>, it gives
/// reproducible sequences so the same seed always yields the same universe — without
/// relying on the platform's <c>System.Random</c>.
/// </summary>
public sealed class DeterministicRandom
{
    private ulong _state;

    public DeterministicRandom(long seed)
    {
        _state = (ulong)seed;
        if (_state == 0)
        {
            _state = 0x9E3779B97F4A7C15UL;
        }
    }

    public ulong NextUInt64()
    {
        unchecked
        {
            _state ^= _state << 13;
            _state ^= _state >> 7;
            _state ^= _state << 17;
            return _state;
        }
    }

    /// <summary>Double in [0, 1).</summary>
    public double NextDouble() => (NextUInt64() >> 11) * (1.0 / 9007199254740992.0);

    public float NextFloat() => (float)NextDouble();

    /// <summary>Integer in [minInclusive, maxInclusive].</summary>
    public int Range(int minInclusive, int maxInclusive)
    {
        if (maxInclusive <= minInclusive)
        {
            return minInclusive;
        }

        return minInclusive + (int)(NextDouble() * (maxInclusive - minInclusive + 1));
    }
}
