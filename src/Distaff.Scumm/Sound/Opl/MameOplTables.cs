namespace Distaff.Scumm.Sound.Opl;

/// <summary>
/// The tables <see cref="MameOpl"/> shares between chips, built as ScummVM's <c>OPLBuildTables</c>
/// and <c>OPLOpenTable</c> build them (mame.cpp) at the high quality ScummVM uses on desktops
/// (<c>FMOPL_ENV_BITS_HQ</c>, <c>FMOPL_EG_ENT_HQ</c>).
/// </summary>
/// <remarks>
/// The C++ keeps pointers into <c>TL_TABLE</c> in <c>SIN_TABLE</c>; here <see cref="Sin"/> holds
/// indices into <see cref="Tl"/>.
/// </remarks>
internal static class MameOplTables
{
    // ENV_BITS, EG_ENT and the envelope counter points derived from them
    public const int EnvBits = 16;
    public const int EgEnt = 4096;
    public const int EgOff = (2 * EgEnt) << EnvBits;
    public const int EgDed = EgOff;
    public const int EgDst = EgEnt << EnvBits;
    public const int EgAed = EgDst;
    public const int EgAst = 0;
    public const double EgStep = 96.0 / EgEnt;

    // sine entries (SIN_ENT_SHIFT, SIN_ENT) and the total level table's halves (TL_MAX)
    public const int SinEntShift = 11;
    public const int SinEnt = 1 << SinEntShift;
    public const int TlMax = EgEnt * 2;

    // FREQ_BITS, FREQ_RATE, TL_BITS and the output shift and limits (OPL_OUTSB, OPL_MAXOUT, OPL_MINOUT)
    public const int FreqBits = 24;
    public const int FreqRate = 1 << (FreqBits - 20);
    public const int TlBits = FreqBits + 2;
    public const int OutShift = TlBits + 3 - 16;
    public const int MaxOut = 0x7fff << OutShift;
    public const int MinOut = -(0x8000 << OutShift);

    // LFO tables (VIB_ENT, VIB_SHIFT, AMS_ENT, AMS_SHIFT, VIB_RATE_SHIFT, VIB_RATE)
    public const int VibEnt = 512;
    public const int VibShift = 32 - 9;
    public const int AmsEnt = 512;
    public const int AmsShift = 32 - 9;
    public const int VibRateShift = 8;
    public const int VibRate = 1 << VibRateShift;

    /// <summary>KSL_TABLE: the key scale level per block and top four bits of the F-number.</summary>
    public static readonly uint[] Ksl = new uint[8 * 16];

    /// <summary>SL_TABLE: the sustain level as an envelope counter, 3 dB per step.</summary>
    public static readonly int[] Sl = new int[16];

    /// <summary>TL_TABLE: attenuation to amplitude, the positive half then the negative half.</summary>
    public static readonly int[] Tl = new int[TlMax * 2];

    /// <summary>SIN_TABLE: the four waveforms, each <see cref="SinEnt"/> indices into <see cref="Tl"/>.</summary>
    public static readonly int[] Sin = new int[SinEnt * 4];

    /// <summary>ENV_CURVE: envelope counter to attenuation (attack, then decay and release, then off).</summary>
    public static readonly int[] EnvCurve = new int[(2 * EgEnt) + 1];

    /// <summary>AMS_TABLE: tremolo, 1 dB deep then 4.8 dB deep.</summary>
    public static readonly int[] Ams = new int[AmsEnt * 2];

    /// <summary>VIB_TABLE: vibrato, 7 cent deep then 14 cent deep.</summary>
    public static readonly int[] Vib = new int[VibEnt * 2];

    /// <summary>MUL_TABLE: the frequency multiple, doubled.</summary>
    public static readonly uint[] Mul = [1, 2, 4, 6, 8, 10, 12, 14, 16, 18, 20, 20, 24, 24, 30, 30];

    /// <summary>slot_array: an operator register's low five bits to the slot (channel * 2 + operator).</summary>
    public static readonly int[] SlotArray =
    [
        0, 2, 4, 1, 3, 5, -1, -1,
        6, 8, 10, 7, 9, 11, -1, -1,
        12, 14, 16, 13, 15, 17, -1, -1,
        -1, -1, -1, -1, -1, -1, -1, -1,
    ];

    private static readonly double[] KslSeed =
    [
        // OCT 0
        0.000, 0.000, 0.000, 0.000, 0.000, 0.000, 0.000, 0.000,
        0.000, 0.000, 0.000, 0.000, 0.000, 0.000, 0.000, 0.000,

        // OCT 1
        0.000, 0.000, 0.000, 0.000, 0.000, 0.000, 0.000, 0.000,
        0.000, 0.750, 1.125, 1.500, 1.875, 2.250, 2.625, 3.000,

        // OCT 2
        0.000, 0.000, 0.000, 0.000, 0.000, 1.125, 1.875, 2.625,
        3.000, 3.750, 4.125, 4.500, 4.875, 5.250, 5.625, 6.000,

        // OCT 3
        0.000, 0.000, 0.000, 1.875, 3.000, 4.125, 4.875, 5.625,
        6.000, 6.750, 7.125, 7.500, 7.875, 8.250, 8.625, 9.000,

        // OCT 4
        0.000, 0.000, 3.000, 4.875, 6.000, 7.125, 7.875, 8.625,
        9.000, 9.750, 10.125, 10.500, 10.875, 11.250, 11.625, 12.000,

        // OCT 5
        0.000, 3.000, 6.000, 7.875, 9.000, 10.125, 10.875, 11.625,
        12.000, 12.750, 13.125, 13.500, 13.875, 14.250, 14.625, 15.000,

        // OCT 6
        0.000, 6.000, 9.000, 10.875, 12.000, 13.125, 13.875, 14.625,
        15.000, 15.750, 16.125, 16.500, 16.875, 17.250, 17.625, 18.000,

        // OCT 7
        0.000, 9.000, 12.000, 13.875, 15.000, 16.125, 16.875, 17.625,
        18.000, 18.750, 19.125, 19.500, 19.875, 20.250, 20.625, 21.000,
    ];

    private static readonly uint[] SlSeed = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 31];

    static MameOplTables()
    {
        // OPLBuildTables: SC_KSL and SC_SL
        for (int i = 0; i < KslSeed.Length; i++)
        {
            Ksl[i] = (uint)(KslSeed[i] / (EgStep / 2));
        }

        for (int i = 0; i < SlSeed.Length; i++)
        {
            Sl[i] = (int)(SlSeed[i] * ((3 / EgStep) * (1 << EnvBits))) + EgDst;
        }

        // OPLOpenTable: the total level table
        for (int t = 0; t < EgEnt - 1; t++)
        {
            double rate = ((1 << TlBits) - 1) / Math.Pow(10.0, EgStep * t / 20);
            Tl[t] = (int)rate;
            Tl[TlMax + t] = -Tl[t];
        }

        for (int t = EgEnt - 1; t < TlMax; t++)
        {
            Tl[t] = Tl[TlMax + t] = 0;
        }

        // the sine wave as total level offsets; degree 0 and degree 180 are off
        Sin[0] = Sin[SinEnt / 2] = EgEnt - 1;
        for (int s = 1; s <= SinEnt / 4; s++)
        {
            double pom = Math.Sin(2 * Math.PI * s / SinEnt);
            pom = 20 * Math.Log10(1 / pom);
            int j = (int)(pom / EgStep);
            Sin[s] = Sin[(SinEnt / 2) - s] = j;
            Sin[(SinEnt / 2) + s] = Sin[SinEnt - s] = TlMax + j;
        }

        // waveforms 1 (half sine), 2 (absolute sine) and 3 (pulse sine)
        for (int s = 0; s < SinEnt; s++)
        {
            Sin[SinEnt + s] = s < (SinEnt / 2) ? Sin[s] : EgEnt;
            Sin[(SinEnt * 2) + s] = Sin[s % (SinEnt / 2)];
            Sin[(SinEnt * 3) + s] = ((s / (SinEnt / 4)) & 1) != 0 ? EgEnt : Sin[(SinEnt * 2) + s];
        }

        // envelope counter to envelope output
        for (int i = 0; i < EgEnt; i++)
        {
            double pom = Math.Pow((double)(EgEnt - 1 - i) / EgEnt, 8) * EgEnt;
            EnvCurve[i] = (int)pom;
            EnvCurve[(EgDst >> EnvBits) + i] = i;
        }

        EnvCurve[EgOff >> EnvBits] = EgEnt - 1;

        for (int i = 0; i < AmsEnt; i++)
        {
            double pom = (1.0 + Math.Sin(2 * Math.PI * i / AmsEnt)) / 2;
            Ams[i] = (int)((1.0 / EgStep) * pom);
            Ams[AmsEnt + i] = (int)((4.8 / EgStep) * pom);
        }

        for (int i = 0; i < VibEnt; i++)
        {
            double pom = (double)VibRate * 0.06 * Math.Sin(2 * Math.PI * i / VibEnt);
            Vib[i] = (int)(VibRate + (pom * 0.07));
            Vib[VibEnt + i] = (int)(VibRate + (pom * 0.14));
        }
    }
}
