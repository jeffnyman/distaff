// engines/scumm/music.h, without saving.
#pragma once
#include "../shim.h"

namespace Scumm {

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

} // namespace Scumm
