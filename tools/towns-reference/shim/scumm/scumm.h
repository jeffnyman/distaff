// Stand-ins for the parts of the SCUMM engine that engines/scumm/players/player_towns.cpp
// reaches: the game's version and id, its sound resources, and Sound's CD and stopSound.
#pragma once
#include "../shim.h"

#define VER(x) (x)

namespace Scumm {

enum { GID_INDY3 = 1, GID_LOOM = 2 };
enum ResType { rtSound = 4 };
enum { kEnhAudioChanges = 1 };

// engines/scumm/music.h
class MusicEngine : public Common::Serializable {
public:
	~MusicEngine() override {}
	virtual void setMusicVolume(int vol) = 0;
	virtual void setSfxVolume(int vol) {}
	virtual void startSound(int sound) = 0;
	virtual void startSoundWithTrackID(int sound, int track) { startSound(sound); }
	virtual void stopSound(int sound) = 0;
	virtual void stopAllSounds() = 0;
	virtual int getSoundStatus(int sound) const = 0;
	virtual int getMusicTimer() { return 0; }
	virtual void setQuality(int qual) {}
	virtual void toggleMusic(bool enable) {}
	virtual void toggleSoundEffects(bool enable) {}
	void saveLoadWithSerializer(Common::Serializer &ser) override {}
	virtual void restoreAfterLoad() {}
};

// Sound: stopSound reaches the player (Sound::stopSound, without its queue); the CD is not played.
class Sound {
public:
	Sound() : _engine(nullptr) {}
	void stopSound(int sound) {
		if (_engine)
			_engine->stopSound(sound);
	}
	void stopCD() {}
	int pollCD() const { return 0; }
	void playCDTrack(int track, int numLoops, int startFrame, int duration) {}
	MusicEngine *_engine;
};

struct GameSettings {
	int version;
	int id;
};

class ScummEngine {
public:
	ScummEngine() : _sound(&_soundObj) {
		_game.version = 3;
		_game.id = GID_LOOM;
		memset(_resources, 0, sizeof(_resources));
	}
	uint8 *getResourceAddress(int type, int idx) { return (idx >= 0 && idx < 256) ? _resources[idx] : nullptr; }
	bool enhancementEnabled(int) const { return false; }

	GameSettings _game;
	Sound *_sound;
	Sound _soundObj;
	uint8 *_resources[256];
};

} // namespace Scumm
