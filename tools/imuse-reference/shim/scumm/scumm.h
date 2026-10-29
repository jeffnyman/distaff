// Stand-in for the parts of the SCUMM engine iMuse reaches: the game's id, the sound resources
// by number, the mixer's mutex, and the speech check (Sam & Max's only).
#pragma once
#include "common/scummsys.h"
#include "common/util.h"
#include "common/endian.h"
#include "common/debug.h"
#include "common/textconsole.h"
#include "audio/mixer.h"
#include "common/system.h"

#define VER(x) x

namespace Scumm {

enum { GID_TENTACLE = 1, GID_MONKEY2, GID_INDY4, GID_MONKEY, GID_SAMNMAX, GID_LOOM };
enum ResType { rtSound = 4 };
enum { DEBUG_IMUSE = 1 };

struct ResourceEntry { byte *_address = nullptr; };
struct ResourceManager { ResourceEntry _types[8][256]; };

struct GameSettings { int id; const char *variant; int heversion; };

class Sound { public: bool speechIsPlaying() { return false; } };

class ScummEngine {
public:
	GameSettings _game = { GID_LOOM, "", 0 };
	ResourceManager *_res = new ResourceManager();
	Audio::Mixer *_mixer = new Audio::Mixer();
	Sound *_sound = new Sound();
	OSystem *_system = g_system;
	byte *getResourceAddress(ResType type, int idx) { return _res->_types[type][idx & 0xFF]._address; }
};

extern ScummEngine *g_scumm;

} // namespace Scumm
