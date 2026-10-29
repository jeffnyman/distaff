namespace Distaff.Scumm.Scripts;

/// <summary>
/// The game's random numbers, as ScummVM makes them (<c>Common::RandomSource</c>): a 32-bit
/// xorshift (13, 21, 11) scrambled before each number, the number the seed times 0xDEADBF03 taken
/// modulo the range. ScummVM keeps one for the engine (<c>_rnd</c>), which the scripts
/// (<c>o5_getRandomNr</c>) and the dissolve's order (<c>dissolveEffect</c>) both draw from; with
/// the same seed (ScummVM's <c>--random-seed</c>), the two give the same numbers in the same order.
/// The state is not saved, as ScummVM's is not.
/// </summary>
public sealed class ScummRandom
{
    private uint _seed;

    public ScummRandom(uint seed) => _seed = seed == 0 ? 1 : seed;

    /// <summary>A number from 0 to <paramref name="max"/>, both included (<c>getRandomNumber</c>).</summary>
    public int Next(int max)
    {
        _seed ^= _seed >> 13;
        _seed ^= _seed << 21;
        _seed ^= _seed >> 11;
        return (int)((_seed * 0xDEADBF03u) % ((uint)max + 1));
    }
}
