namespace Distaff.Scumm.Sound;

/// <summary>
/// A device that takes MIDI messages and renders what it plays: what a MIDI driver sends to, as
/// the MT-32 emulator is for the EGA's Roland Upgrade. Channel messages are packed like ScummVM's
/// <c>MidiDriver::send</c> (status | data1 &lt;&lt; 8 | data2 &lt;&lt; 16); system exclusive
/// messages come without their F0 and F7. Ported by way of Daventry.
/// </summary>
public interface IMidiDevice
{
    void Send(uint message);

    void SysEx(ReadOnlySpan<byte> message);

    /// <summary>Renders interleaved stereo.</summary>
    void Render(Span<short> interleavedStereo);
}
