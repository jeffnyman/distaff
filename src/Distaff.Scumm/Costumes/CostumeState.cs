namespace Distaff.Scumm.Costumes;

/// <summary>
/// Where each of an actor's 16 limbs is in its costume's commands. Mirrors ScummVM's
/// <c>CostumeData</c>, with <c>ClassicCostumeLoader::costumeDecodeData</c> (starting an animation)
/// and <c>increaseAnims</c> (one animation step). Ported by way of Maniacal.
/// </summary>
internal sealed class CostumeState
{
    /// <summary>Mirrors the 0xFFFF of <c>CostumeData::reset</c>: no command, or no animation.</summary>
    public const int None = 0xFFFF;

    /// <summary>The bit of a position that stops the run at its end instead of looping (<c>curpos |= 0x8000</c>).</summary>
    public const int NoLoop = 0x8000;

    public const int Limbs = 16;

    private readonly int[] _position = new int[Limbs];
    private readonly int[] _start = new int[Limbs];
    private readonly int[] _end = new int[Limbs];
    private readonly int[] _animation = new int[Limbs];

    public CostumeState() => Reset();

    /// <summary>The limbs an animation has stopped (bit per limb). Mirrors <c>stopped</c>.</summary>
    public int Stopped { get; private set; }

    /// <summary>The sound cues the commands have passed (<c>soundCounter</c>), for the actor's sound.</summary>
    public int SoundCounter { get; set; }

    /// <summary>The counter commands passed (<c>animCounter</c>).</summary>
    public int AnimCounter { get; set; }

    /// <summary>The command each limb is at, with <see cref="NoLoop"/>, or <see cref="None"/>. Mirrors <c>curpos</c>.</summary>
    public int Position(int limb) => _position[limb];

    /// <summary>The animation that last set each limb (<c>frame * 4 + direction</c>), or <see cref="None"/>. Mirrors <c>frame</c>.</summary>
    public int Animation(int limb) => _animation[limb];

    public bool IsStopped(int limb) => (Stopped & (1 << limb)) != 0;

    /// <summary>Mirrors <c>CostumeData::reset</c>.</summary>
    public void Reset()
    {
        Array.Fill(_position, None);
        Array.Fill(_start, None);
        Array.Fill(_end, None);
        Array.Fill(_animation, None);
        Stopped = 0;
        SoundCounter = 0;
        AnimCounter = 0;
    }

    /// <summary>The limbs as a save holds them (<c>_cost.stopped</c>, <c>curpos</c>, <c>start</c>, <c>end</c>, <c>frame</c>).</summary>
    public void SaveTo(Saves.ScummState.ActorState s)
    {
        s.Stopped = Stopped;
        _position.CopyTo(s.CurPos, 0);
        _start.CopyTo(s.Start, 0);
        _end.CopyTo(s.End, 0);
        _animation.CopyTo(s.CostumeFrame, 0);
    }

    /// <summary>The limbs from a save, with the actor's counters.</summary>
    public void LoadFrom(Saves.ScummState.ActorState s)
    {
        Stopped = s.Stopped;
        s.CurPos.CopyTo(_position, 0);
        s.Start.CopyTo(_start, 0);
        s.End.CopyTo(_end, 0);
        s.CostumeFrame.CopyTo(_animation, 0);
        SoundCounter = s.SoundCounter;
        AnimCounter = s.AnimCounter;
    }

    /// <summary>
    /// Starts frame <paramref name="frame"/> facing <paramref name="direction"/> (0 left, 1 right,
    /// 2 front, 3 back) on the limbs it lists that <paramref name="useMask"/> lets through, limb 0
    /// being its bit 15. A run whose first command is <see cref="Costume.Stop"/> or
    /// <see cref="Costume.Start"/> only stops or restarts the limb; a hidden limb has no command.
    /// Mirrors <c>costumeDecodeData</c>.
    /// </summary>
    public void Decode(Costume costume, int frame, int direction, int useMask = 0xFFFF)
    {
        int animation = direction + frame * 4;
        foreach (var limb in costume.Animation(animation))
        {
            int i = limb.Limb;
            if (((useMask << i) & 0x8000) == 0)
            {
                continue;
            }

            if (limb.Start is not { } start)
            {
                _position[i] = None;
                _start[i] = 0;
                _animation[i] = animation;
                continue;
            }

            byte command = costume.Command(start);
            if (command == Costume.Start)
            {
                Stopped &= ~(1 << i);
            }
            else if (command == Costume.Stop)
            {
                Stopped |= 1 << i;
            }
            else
            {
                _position[i] = _start[i] = start;
                _end[i] = start + limb.Length;
                if (!limb.Loop)
                {
                    _position[i] |= NoLoop;
                }

                _animation[i] = animation;
            }
        }
    }

    /// <summary>One animation step for every limb with a command; true when some limb's picture changed. Mirrors <c>increaseAnims</c>.</summary>
    public bool Increase(Costume costume)
    {
        bool changed = false;
        for (int limb = 0; limb < Limbs; limb++)
        {
            if (_position[limb] != None)
            {
                changed |= Increase(costume, limb);
            }
        }

        return changed;
    }

    /// <summary>
    /// Moves a limb to its next command: a looping run starts again after its end, a run that does
    /// not loop stays there; counter (<see cref="Costume.Counter"/>) and sound
    /// (<see cref="Costume.Sound"/>) commands are counted and passed over. A command with bit 7
    /// also counts a sound cue. Mirrors <c>ClassicCostumeLoader::increaseAnim</c> for version 3.
    /// </summary>
    private bool Increase(Costume costume, int limb)
    {
        int noLoop = _position[limb] & NoLoop;
        int i = _position[limb] & ~NoLoop;
        int end = _end[limb];
        int code = costume.Command(i) & 0x7F;
        if ((costume.Command(i) & 0x80) != 0)
        {
            SoundCounter++;
        }

        // bounded, in case a run holds nothing but sound and counter commands
        for (int guard = 0; guard <= costume.CommandCount + 1; guard++)
        {
            if (noLoop == 0)
            {
                if (i++ >= end)
                {
                    i = _start[limb];
                }
            }
            else if (i != end)
            {
                i++;
            }

            byte next = costume.Command(i);
            if (next == Costume.Counter)
            {
                AnimCounter++;
                if (_start[limb] != end)
                {
                    continue;
                }
            }
            else if (next == Costume.Sound)
            {
                SoundCounter++;
                if (_start[limb] != end)
                {
                    continue;
                }
            }

            _position[limb] = i | noLoop;
            return (costume.Command(i) & 0x7F) != code;
        }

        return false;
    }
}
