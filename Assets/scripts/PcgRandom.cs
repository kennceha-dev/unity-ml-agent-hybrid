using System;

/// <summary>
/// PCG32 random number generator (XSH-RR variant).
/// Deterministic and fast, suitable for procedural generation.
/// </summary>
[Serializable]
public struct PcgRandom
{
    private ulong state;
    private ulong inc;

    // Construct with a seed and an optional stream/sequence ID
    public PcgRandom(ulong seed, ulong sequence = 54u)
    {
        state = 0UL;
        inc = (sequence << 1) | 1UL; // must be odd
        NextUInt();                  // move from initial 0 state
        state += seed;
        NextUInt();
    }

    public PcgRandom(uint seed) : this(seed, 54u) { }

    /// <summary>
    /// Core PCG step. Returns 32 random bits.
    /// </summary>
    public uint NextUInt()
    {
        ulong oldstate = state;
        // PCG32 multiplier + increment
        state = oldstate * 6364136223846793005UL + inc;
        uint xorshifted = (uint)(((oldstate >> 18) ^ oldstate) >> 27);
        int rot = (int)(oldstate >> 59);
        return (xorshifted >> rot) | (xorshifted << ((-rot) & 31));
    }

    /// <summary>
    /// Returns a float in [0,1).
    /// </summary>
    public float NextFloat()
    {
        // Take 24 high bits and scale to [0,1)
        return (NextUInt() >> 8) * (1.0f / 16777216.0f); // 2^24
    }

    /// <summary>
    /// Integer range [minInclusive, maxExclusive).
    /// </summary>
    public int Range(int minInclusive, int maxExclusive)
    {
        if (maxExclusive <= minInclusive)
            throw new ArgumentException("maxExclusive must be > minInclusive");

        uint span = (uint)(maxExclusive - minInclusive);

        // Slight bias is usually fine for PCG; for most game purposes this is acceptable.
        uint value = NextUInt() % span;
        return (int)value + minInclusive;
    }

    /// <summary>
    /// Float range [minInclusive, maxInclusive).
    /// </summary>
    public float Range(float minInclusive, float maxInclusive)
    {
        if (maxInclusive < minInclusive)
            throw new ArgumentException("maxInclusive must be >= minInclusive");

        float t = NextFloat();
        return minInclusive + t * (maxInclusive - minInclusive);
    }

    /// <summary>
    /// Bernoulli trial: returns true with probability p.
    /// </summary>
    public bool Chance(float p)
    {
        if (p <= 0f) return false;
        if (p >= 1f) return true;
        return NextFloat() < p;
    }
}
