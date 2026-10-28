// Stand-in for the iMuse that Player_Towns_v2 (not used by Loom) drives.
#pragma once
#include "../../shim.h"

namespace Scumm {
class IMuse {
public:
	void setMusicVolume(int) {}
	int getSoundStatus(int) const { return 0; }
	void startSound(int) {}
	void stopSound(int) {}
	void stopAllSounds() {}
	int32 doCommand(int, int[]) { return 0; }
};
} // namespace Scumm
