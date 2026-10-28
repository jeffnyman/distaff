// Stand-ins for the parts of the SCUMM engine that player_v2base.cpp and player_v2.cpp reach:
// the game's version and features, and its sound resources.
#pragma once
#include "../shim.h"
#include "music.h"

namespace Scumm {

enum ResType { rtSound = 4 };
enum { GF_OLD_BUNDLE = 1 << 0 };

struct GameSettings {
	int version;
	uint32 features;
};

class ScummEngine {
public:
	GameSettings _game;
	byte *_sounds[256];

	ScummEngine() : _game() { memset(_sounds, 0, sizeof(_sounds)); }
	byte *getResourceAddress(ResType, int nr) { return _sounds[nr & 0xff]; }
};

} // namespace Scumm
