namespace Distaff.Scumm.Sound.Opl;

/// <summary>
/// An emulated OPL2 (YM3812) FM chip, as the AdLib music player drives it: register writes and
/// mono 16-bit samples at the rate the emulator was created with. Mirrors the parts of ScummVM's
/// <c>OPL::OPL</c> and <c>Audio::EmulatedChip</c> (audio/fmopl.h, audio/chip.h) the player uses.
/// </summary>
public interface IOpl
{
    /// <summary>Resets the chip, as <c>OPL::reset</c> does: every register is written with zero.</summary>
    void Reset();

    /// <summary>Writes a value to a chip register, as <c>OPL::writeReg</c> does.</summary>
    /// <param name="register">The register number (0x00 to 0xFF).</param>
    /// <param name="value">The value (0x00 to 0xFF).</param>
    void WriteRegister(int register, int value);

    /// <summary>Fills the span with the chip's next samples, as <c>EmulatedChip::generateSamples</c> does.</summary>
    /// <param name="mono">The samples to fill: mono, signed 16-bit, at the emulator's sample rate.</param>
    void Generate(Span<short> mono);
}
