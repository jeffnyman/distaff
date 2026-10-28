// Minimal stand-ins for the ScummVM headers that the FM Towns audio emulation
// (audio/softsynth/fmtowns_pc98: towns_audio.cpp, towns_pc98_fmsynth.cpp, pcm_common.cpp,
// towns_euphony.cpp) and engines/scumm/players/player_towns.cpp include, so they can be
// compiled on their own as the reference for Distaff's port.
#pragma once

#define _USE_MATH_DEFINES
#include <math.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdarg.h>
#include <assert.h>
#include <new>
#include <vector>

typedef uint8_t uint8;
typedef int8_t int8;
typedef uint16_t uint16;
typedef int16_t int16;
typedef uint32_t uint32;
typedef int32_t int32;
typedef unsigned int uint;
typedef uint8_t byte;

#define ARRAYSIZE(x) ((int)(sizeof(x) / sizeof(x[0])))

template<typename T> inline T CLIP(T v, T amin, T amax) {
	return v < amin ? amin : (v > amax ? amax : v);
}
template<typename T> inline T MIN(T a, T b) { return a < b ? a : b; }
template<typename T> inline T MAX(T a, T b) { return a > b ? a : b; }
template<typename T> inline T ABS(T x) { return x >= 0 ? x : -x; }

inline void error(const char *s, ...) {
	va_list va;
	va_start(va, s);
	fprintf(stderr, "error: ");
	vfprintf(stderr, s, va);
	fprintf(stderr, "\n");
	va_end(va);
	exit(1);
}

inline void warning(const char *s, ...) {
	va_list va;
	va_start(va, s);
	fprintf(stderr, "warning: ");
	vfprintf(stderr, s, va);
	fprintf(stderr, "\n");
	va_end(va);
}

inline void debugN(int, const char *, ...) {}

inline uint16 READ_LE_UINT16(const void *p) { const uint8 *b = (const uint8 *)p; return (uint16)(b[0] | (b[1] << 8)); }
inline uint32 READ_LE_UINT32(const void *p) { const uint8 *b = (const uint8 *)p; return (uint32)b[0] | ((uint32)b[1] << 8) | ((uint32)b[2] << 16) | ((uint32)b[3] << 24); }
inline uint16 READ_BE_UINT16(const void *p) { const uint8 *b = (const uint8 *)p; return (uint16)((b[0] << 8) | b[1]); }
inline uint32 READ_BE_UINT32(const void *p) { const uint8 *b = (const uint8 *)p; return ((uint32)b[0] << 24) | ((uint32)b[1] << 16) | ((uint32)b[2] << 8) | (uint32)b[3]; }
inline void WRITE_LE_UINT32(void *p, uint32 v) { uint8 *b = (uint8 *)p; b[0] = v & 0xff; b[1] = (v >> 8) & 0xff; b[2] = (v >> 16) & 0xff; b[3] = v >> 24; }
inline void WRITE_BE_UINT32(void *p, uint32 v) { uint8 *b = (uint8 *)p; b[0] = v >> 24; b[1] = (v >> 16) & 0xff; b[2] = (v >> 8) & 0xff; b[3] = v & 0xff; }
#define MKTAG(a0, a1, a2, a3) ((uint32)((a3) | ((a2) << 8) | ((a1) << 16) | ((a0) << 24)))

namespace DisposeAfterUse {
enum Flag { NO, YES };
}

namespace Common {

// The reference runs on one thread: the mixer's mutex guards nothing.
class Mutex {
public:
	void lock() {}
	void unlock() {}
};

class StackLock {
public:
	StackLock(Mutex &) {}
};

// common/array.h, with the bounds check of its operator[] made loud.
template<class T> class Array {
	std::vector<T> _v;
public:
	typedef T *iterator;
	typedef const T *const_iterator;
	void push_back(const T &x) { _v.push_back(x); }
	void clear() { _v.clear(); }
	uint size() const { return (uint)_v.size(); }
	bool empty() const { return _v.empty(); }
	T &operator[](uint i) {
		if (i >= _v.size())
			error("Common::Array: index %u out of range %u", i, (uint)_v.size());
		return _v[i];
	}
	const T &operator[](uint i) const {
		if (i >= _v.size())
			error("Common::Array: index %u out of range %u", i, (uint)_v.size());
		return _v[i];
	}
	iterator begin() { return _v.data(); }
	iterator end() { return _v.data() + _v.size(); }
	const_iterator begin() const { return _v.data(); }
	const_iterator end() const { return _v.data() + _v.size(); }
};

template<class In, class T> In find(In first, In last, const T &v) {
	for (; first != last; ++first) {
		if (*first == v)
			break;
	}
	return first;
}

template<class Res, class T> class Functor0Mem {
	typedef Res (T::*FuncType)();
	T *_t;
	FuncType _func;
public:
	Functor0Mem(T *t, FuncType func) : _t(t), _func(func) {}
	bool isValid() const { return _func != nullptr && _t != nullptr; }
	Res operator()() const { return (_t->*_func)(); }
};

// common/memorypool.h: chunks come from the heap.
class MemoryPool {
public:
	void *allocChunk(size_t n) { return malloc(n); }
	void freeChunk(void *p) { free(p); }
};

template<class T> class ObjectPool : public MemoryPool {
public:
	void deleteChunk(T *ptr) {
		if (ptr) {
			ptr->~T();
			freeChunk(ptr);
		}
	}
};

class Serializer {
public:
	template<typename T> void syncAsByte(T &, int = 0) {}
	template<typename T> void syncAsSint16LE(T &, int = 0) {}
	template<typename T> void syncAsUint32LE(T &, int = 0) {}
	template<typename T, typename F> void syncArray(T *, int, F) {}
	int getVersion() const { return 0; }
};

class Serializable {
public:
	virtual ~Serializable() {}
	virtual void saveLoadWithSerializer(Serializer &s) = 0;
};

} // namespace Common

inline void *operator new(size_t nbytes, Common::MemoryPool &pool) { return pool.allocChunk(nbytes); }
inline void operator delete(void *p, Common::MemoryPool &pool) { pool.freeChunk(p); }

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

// The mixer: the one stream the Towns' audio plays on is read by the harness itself, at the
// stream's full volume (the channel's 255 at the plain sound type's 256 leaves every sample as it is).
class Mixer {
public:
	enum SoundType { kPlainSoundType = 0, kMusicSoundType, kSFXSoundType, kSpeechSoundType };
	enum { kMaxChannelVolume = 255, kMaxMixerVolume = 256 };

	Mixer(int rate) : _rate(rate), _stream(nullptr) {}
	Common::Mutex &mutex() { return _mutex; }
	int getOutputRate() const { return _rate; }
	void playStream(SoundType, SoundHandle *, AudioStream *stream, int = -1, byte = kMaxChannelVolume, int8 = 0,
	                DisposeAfterUse::Flag = DisposeAfterUse::YES, bool = false, bool = false) {
		_stream = stream;
	}
	void stopHandle(SoundHandle) { _stream = nullptr; }
	AudioStream *stream() { return _stream; }

private:
	Common::Mutex _mutex;
	int _rate;
	AudioStream *_stream;
};

} // namespace Audio

// The CD's volume and balance as the Towns' audio sets them (backends/audiocd/audiocd.h).
class AudioCDManager {
public:
	AudioCDManager() : volume(255), balance(0) {}
	void setVolume(byte v) { volume = v; }
	void setBalance(int8 b) { balance = b; }
	int volume, balance;
};

struct ShimSystem {
	AudioCDManager _cd;
	AudioCDManager *getAudioCDManager() { return &_cd; }
};
extern ShimSystem *g_system;
