using Distaff.Scumm.Hosting;
using Distaff.Scumm.Scripts;

namespace Distaff.Scumm.Game;

/// <summary>An actor as it is at a moment of the game.</summary>
public sealed record ActorInfo
{
    /// <summary>The actor's number, 1 to 12.</summary>
    public int Number { get; init; }

    /// <summary>The name a script gave it, empty when none has.</summary>
    public string Name { get; init; } = "";

    /// <summary>The room it is in, 0 for none.</summary>
    public int Room { get; init; }

    /// <summary>Where it stands in its room, in pixels.</summary>
    public int X { get; init; }

    /// <summary>Where it stands in its room, in pixels.</summary>
    public int Y { get; init; }

    /// <summary>How far above where it stands it is drawn.</summary>
    public int Elevation { get; init; }

    /// <summary>The way it faces, in degrees: 0 up, 90 right, 180 down, 270 left.</summary>
    public int Facing { get; init; }

    /// <summary>Its costume, 0 for none.</summary>
    public int Costume { get; init; }

    /// <summary>The frame of its costume it shows.</summary>
    public int Frame { get; init; }

    /// <summary>The walk box it stands in.</summary>
    public int WalkBox { get; init; }

    /// <summary>Whether, and how, it is moving: 0 standing, otherwise ScummVM's <c>MF_</c> flags.</summary>
    public int Moving { get; init; }

    /// <summary>Whether it is drawn.</summary>
    public bool Visible { get; init; }
}

/// <summary>A script running at a moment of the game.</summary>
/// <param name="Number">The script's number; for a room's code, the room's object or 0.</param>
/// <param name="Kind">What kind of script it is.</param>
/// <param name="Waiting">Whether it waits: for a delay, or for something it asked of the game.</param>
/// <param name="Delay">The ticks it waits before running again, when it waits for a delay.</param>
/// <param name="Frozen">Whether it is frozen, and does not run.</param>
public readonly record struct RunningScript(int Number, ScriptKind Kind, bool Waiting, int Delay, bool Frozen);

/// <summary>A verb shown on the screen at a moment of the game: one of the distaff's notes, or a button of the interface.</summary>
/// <param name="Number">The verb's number.</param>
/// <param name="Left">Where it is on the screen, in pixels.</param>
/// <param name="Top">Where it is on the screen, in pixels.</param>
/// <param name="Right">Where it ends on the screen, in pixels, exclusive.</param>
/// <param name="Bottom">Where it ends on the screen, in pixels, exclusive.</param>
public readonly record struct VerbInfo(int Number, int Left, int Top, int Right, int Bottom)
{
    /// <summary>The verb's middle, where a click on it lands.</summary>
    public (int X, int Y) Middle => ((Left + Right) / 2, (Top + Bottom) / 2);
}

public sealed partial class GameWorld
{
    /// <summary>The sound the game plays, as stereo samples at its rate: what a host's audio device reads.</summary>
    public IAudioSource Audio => Sound.Output;

    /// <summary>The music's volume, 0 to 256 (<see cref="AudioVolume"/>).</summary>
    public int MusicVolume
    {
        get => Sound.MusicVolume;
        set => Sound.MusicVolume = value;
    }

    /// <summary>The sound effects' and voices' volume, 0 to 256 (<see cref="AudioVolume"/>).</summary>
    public int SfxVolume
    {
        get => Sound.SfxVolume;
        set => Sound.SfxVolume = value;
    }

    /// <summary>Where the camera's middle is in the room, in pixels.</summary>
    public int CameraX => Camera.X;

    /// <summary>The scripts' variables, as they are now: the engine's own below 50 (100 on the VGA CD), then the game's.</summary>
    public IReadOnlyList<int> Variables => Engine.Variables;

    /// <summary>An actor as it is now.</summary>
    /// <param name="number">The actor's number, 1 to 12.</param>
    public ActorInfo GetActor(int number)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(number, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(number, Actors.Length);
        var a = Actors[number];
        return new ActorInfo
        {
            Number = a.Number,
            Name = a.Name is { } name ? Plain(name) : "",
            Room = a.Room,
            X = a.X,
            Y = a.Y,
            Elevation = a.Elevation,
            Facing = a.Facing,
            Costume = a.Costume,
            Frame = a.Frame,
            WalkBox = a.WalkBox,
            Moving = a.Moving,
            Visible = a.Visible,
        };
    }

    /// <summary>The actors in the room on the screen, as they are now.</summary>
    public IReadOnlyList<ActorInfo> ActorsHere() => [.. Actors.Skip(1).Where(a => a.IsInCurrentRoom).Select(a => GetActor(a.Number))];

    /// <summary>The scripts running now, in their slots' order.</summary>
    public IReadOnlyList<RunningScript> RunningScripts()
        => [.. Engine.Slots.Where(s => !s.IsDead).Select(s => new RunningScript(s.Number, s.Kind, (s.Status & ~SlotStatus.Frozen) == SlotStatus.Paused, s.Delay, (s.Status & SlotStatus.Frozen) != 0))];

    /// <summary>The verbs shown on the screen now: the distaff's notes, or the interface's buttons.</summary>
    public IReadOnlyList<VerbInfo> ShownVerbs()
        => [.. Verbs.Where(v => v.VerbId != 0 && v.Mode == 1 && v.SaveId == 0).Select(v => new VerbInfo(v.VerbId, v.CurRect.Left, v.CurRect.Top, v.CurRect.Right, v.CurRect.Bottom))];
}
