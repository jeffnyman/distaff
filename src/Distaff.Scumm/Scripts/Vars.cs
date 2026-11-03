namespace Distaff.Scumm.Scripts;

/// <summary>
/// The variables the engine itself reads and writes, by the numbers version 3 gives them. Mirrors
/// ScummVM's <c>ScummEngine::setupScummVars</c> and <c>ScummEngine_v5::setupScummVars</c> for a
/// version 3 game. The other numbers are the scripts' own.
/// </summary>
internal static class Vars
{
    public const int KeyPress = 0, Ego = 1, CameraPosX = 2, HaveMessage = 3, Room = 4, Override = 5,
        MachineSpeed = 6, Me = 7, NumActor = 8, CurrentLights = 9, CurrentDrive = 10, Timer1 = 11, Timer2 = 12,
        Timer3 = 13, MusicTimer = 14, ActorRangeMin = 15, ActorRangeMax = 16, CameraMinX = 17, CameraMaxX = 18,
        TimerNext = 19, VirtualMouseX = 20, VirtualMouseY = 21, RoomResource = 22, LastSound = 23,
        CutsceneExitKey = 24, TalkActor = 25, CameraFastX = 26, EntryScript = 28, EntryScript2 = 29,
        ExitScript = 30, ExitScript2 = 31, VerbScript = 32, SentenceScript = 33, InventoryScript = 34,
        CutsceneStartScript = 35, CutsceneEndScript = 36, CharInc = 37, WalkToObject = 38, HeapSpace = 40,
        RestartKey = 42, PauseKey = 43, MouseX = 44, MouseY = 45, Timer = 46, TimerTotal = 47, SoundCard = 48,
        VideoMode = 49;

    /// <summary>The number of variables (<c>_numVariables</c> of <c>ScummEngine_v4::readMAXS</c>).</summary>
    public const int Count = 800;

    /// <summary>
    /// What Loom DOS keeps in variable 39: the number of sound resources (<c>resetScummVars</c>).
    /// </summary>
    public const int SoundResourceCount = 39;
}
