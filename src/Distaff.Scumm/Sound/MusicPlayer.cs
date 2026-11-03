
namespace Distaff.Scumm.Sound;

/// <summary>
/// A version's music and sound player, as the sound system drives it: sounds start, stop and are
/// asked after, and the scripts may read a music timer. Mirrors ScummVM's <c>MusicEngine</c>.
/// </summary>
internal abstract class MusicPlayer(int sampleRate) : MonoAudioSource(sampleRate)
{
    /// <summary>The music timer the scripts read. Mirrors <c>getMusicTimer</c>.</summary>
    public abstract int MusicTimer { get; }

    /// <summary>Plays a sound resource, from its size word, or queues it, as its priority allows. Mirrors <c>startSound</c>.</summary>
    public abstract void StartSound(int number, byte[] data);

    /// <summary>Mirrors <c>stopSound</c>.</summary>
    public abstract void StopSound(int number);

    /// <summary>Mirrors <c>stopAllSounds</c>.</summary>
    public abstract void StopAllSounds();

    /// <summary>Whether a sound plays or waits to. Mirrors <c>getSoundStatus</c>.</summary>
    public abstract bool IsPlaying(int number);

    /// <summary>The music and effects volumes, 0 to 256, as the settings give them. Mirrors <c>setMusicVolume</c> and <c>setSfxVolume</c>.</summary>
    public abstract void SetVolumes(int music, int sfx);
}
