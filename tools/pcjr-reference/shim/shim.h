// Minimal stand-ins for the ScummVM headers that engines/scumm/players/player_v2base.cpp and
// player_v2.cpp include, so they can be compiled on their own as the reference for Distaff's
// PlayerV2.
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

inline void debug(int, const char *, ...) {}
inline void warning(const char *s, ...) {
	va_list va;
	va_start(va, s);
	fprintf(stderr, "warning: ");
	vfprintf(stderr, s, va);
	fprintf(stderr, "\n");
	va_end(va);
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

// The mixer: the player's one stream is read by the harness itself, at full volume.
class Mixer {
public:
	enum SoundType { kPlainSoundType = 0 };
	enum { kMaxChannelVolume = 255 };

	Mixer(int rate) : _rate(rate) {}
	int getOutputRate() const { return _rate; }
	void playStream(SoundType, SoundHandle *, AudioStream *, int = -1, byte = kMaxChannelVolume, int8 = 0,
	                DisposeAfterUse::Flag = DisposeAfterUse::YES, bool = false, bool = false) {}
	void stopHandle(SoundHandle) {}

private:
	int _rate;
};

} // namespace Audio
