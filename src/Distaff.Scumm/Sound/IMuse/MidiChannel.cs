namespace Distaff.Scumm.Sound.IMuse;

/// <summary>
/// A channel of a MIDI driver as iMuse's parts play on it. Mirrors ScummVM's <c>MidiChannel</c>,
/// with only what the parts call.
/// </summary>
internal abstract class MidiChannel
{
    public abstract byte Number { get; }

    public abstract void Release();

    public abstract void NoteOff(byte note);

    public abstract void NoteOn(byte note, byte velocity);

    public abstract void ProgramChange(byte program);

    /// <summary>-0x2000 to 0x1FFF.</summary>
    public abstract void PitchBend(short bend);

    public abstract void PitchBendFactor(byte value);

    public abstract void Transpose(sbyte value);

    public abstract void Detune(short value);

    public abstract void Priority(byte value);

    public abstract void Sustain(bool value);

    public abstract void AllNotesOff();

    public abstract void ModulationWheel(byte value);

    public abstract void Volume(byte value);

    public abstract void PanPosition(byte value);

    public abstract void EffectLevel(byte value);

    public abstract void ChorusLevel(byte value);
}
