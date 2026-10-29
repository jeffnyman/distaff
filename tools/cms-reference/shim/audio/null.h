// audio/null.h: enough of the music plugins for audio/softsynth/cms.cpp to register its own, which
// the reference never uses.
#pragma once
#include "../shim.h"
#include <vector>

enum MusicType { MT_CMS = 1 };

class MusicPluginObject;

struct MusicDevice {
	MusicDevice(const MusicPluginObject *, const char *, MusicType) {}
};

typedef std::vector<MusicDevice> MusicDevices;

class MusicPluginObject {
public:
	virtual ~MusicPluginObject() {}
	virtual const char *getName() const = 0;
	virtual const char *getId() const = 0;
	virtual MusicDevices getDevices() const = 0;
};

class NullMusicPlugin : public MusicPluginObject {
};

#define REGISTER_PLUGIN_STATIC(ID, TYPE, PLUGINCLASS)
