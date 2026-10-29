// Minimal stand-ins for the ScummVM headers that engines/scumm/players/player_v2base.cpp and
// player_v2cms.cpp, audio/chip.cpp, audio/cms.cpp and audio/softsynth/cms.cpp include, so they can
// be compiled on their own as the reference for Distaff's PlayerV2Cms.
#pragma once

#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdarg.h>
#include <assert.h>

typedef uint8_t uint8;
typedef int8_t int8;
typedef uint16_t uint16;
typedef int16_t int16;
typedef uint32_t uint32;
typedef int32_t int32;
typedef unsigned int uint;
typedef uint8_t byte;

#define ARRAYSIZE(x) ((int)(sizeof(x) / sizeof(x[0])))
#define PACKED_STRUCT

template<typename T> inline T MIN(T a, T b) { return a < b ? a : b; }
template<typename T> inline T MAX(T a, T b) { return a > b ? a : b; }
template<typename T> inline T CLIP(T v, T a, T b) { return v < a ? a : v > b ? b : v; }

inline void debug(int, const char *, ...) {}
inline void warning(const char *s, ...) {
	va_list va;
	va_start(va, s);
	fprintf(stderr, "warning: ");
	vfprintf(stderr, s, va);
	fprintf(stderr, "\n");
	va_end(va);
}

inline void error(const char *s, ...) {
	va_list va;
	va_start(va, s);
	fprintf(stderr, "error: ");
	vfprintf(stderr, s, va);
	fprintf(stderr, "\n");
	va_end(va);
	exit(1);
}

inline uint16 READ_LE_UINT16(const void *p) { const uint8 *b = (const uint8 *)p; return (uint16)(b[0] | (b[1] << 8)); }

namespace DisposeAfterUse {
enum Flag { NO, YES };
}

namespace Common {

// The reference runs on one thread: the mutex guards nothing.
class Mutex {
public:
	void lock() {}
	void unlock() {}
};

class StackLock {
public:
	StackLock(Mutex &) {}
};

class Serializer {};

class Serializable {
public:
	virtual ~Serializable() {}
	virtual void saveLoadWithSerializer(Serializer &s) = 0;
};

} // namespace Common

namespace Audio {

class AudioStream {
public:
	virtual ~AudioStream() {}
	virtual int readBuffer(int16 *buffer, const int numSamples) = 0;
	virtual bool isStereo() const = 0;
	virtual bool endOfData() const = 0;
	virtual int getRate() const = 0;
};

class SoundHandle {};

// The mixer: it keeps the one stream it is given, the chip's, which the harness reads itself,
// at full volume.
class Mixer {
public:
	enum SoundType { kPlainSoundType = 0 };
	enum { kMaxChannelVolume = 255 };

	Mixer(int rate) : _rate(rate), stream(nullptr) {}
	int getOutputRate() const { return _rate; }
	void playStream(SoundType, SoundHandle *, AudioStream *s, int = -1, byte = kMaxChannelVolume, int8 = 0,
	                DisposeAfterUse::Flag = DisposeAfterUse::YES, bool = false, bool = false) { stream = s; }
	void stopHandle(SoundHandle) { stream = nullptr; }

	AudioStream *stream;

private:
	int _rate;
};

} // namespace Audio

namespace Common {

class TimerManager {
public:
	typedef void (*TimerProc)(void *refCon);
	bool installTimerProc(TimerProc, int32, void *, const char *) { return true; }
	void removeTimerProc(TimerProc) {}
};

} // namespace Common

// The system: its mixer, at 44100 Hz unless the harness says otherwise; no real timers.
class OSystem {
public:
	OSystem() : _mixer(44100) {}
	Audio::Mixer *getMixer() { return &_mixer; }
	Common::TimerManager *getTimerManager() { return &_timers; }

private:
	Audio::Mixer _mixer;
	Common::TimerManager _timers;
};

extern OSystem *g_system;
