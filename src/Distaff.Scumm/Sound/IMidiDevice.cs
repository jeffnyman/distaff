namespace Distaff.Scumm.Sound;

/// <summary>
/// A device that takes MIDI messages and renders what it plays: what a MIDI driver sends to, as
/// the MT-32 emulator is for the EGA's Roland Upgrade. Channel messages are packed like ScummVM's
/// <c>MidiDriver::send</c> (status | data1 &lt;&lt; 8 | data2 &lt;&lt; 16); system exclusive
/// messages come without their F0 and F7. Ported by way of Daventry.
/// </summary>
public interface IMidiDevice
{
    /// <summary>Takes a channel message, packed as ScummVM packs it: status | data1 &lt;&lt; 8 | data2 &lt;&lt; 16.</summary>
    void Send(uint message);

    /// <summary>Takes a system exclusive message, without its F0 and F7.</summary>
    void SysEx(ReadOnlySpan<byte> message);

    /// <summary>Renders interleaved stereo.</summary>
    void Render(Span<short> interleavedStereo);

    /// <summary>
    /// Calls <paramref name="tick"/> <paramref name="ticksPerSecond"/> times a second of the
    /// device's own output, before the frame it falls on, as ScummVM's emulated MIDI drivers
    /// call their timer (<c>MidiDriver_Emulated::readBuffer</c>); null stops it.
    /// </summary>
    void SetTimer(int ticksPerSecond, Action? tick);
}
