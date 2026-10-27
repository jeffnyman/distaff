// Minimal stand-ins for the ScummVM headers that audio/softsynth/opl/mame.cpp includes,
// so it can be compiled on its own as the reference for Distaff's MameOpl.
#pragma once

#define _USE_MATH_DEFINES
#include <math.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>

typedef uint8_t uint8;
typedef int16_t int16;
typedef uint32_t uint32;
typedef unsigned int uint;

#define ARRAYSIZE(x) ((int)(sizeof(x) / sizeof(x[0])))

template<typename T> inline T CLIP(T v, T amin, T amax) {
	return v < amin ? amin : (v > amax ? amax : v);
}

inline void error(const char *s, ...) { fprintf(stderr, "error: %s\n", s); exit(1); }
inline void warning(const char *s, ...) { fprintf(stderr, "warning: %s\n", s); }

// The seed every RandomSource starts from (ScummVM seeds from the clock; the reference fixes it).
extern uint32 g_shimRandomSeed;

namespace Common {
// common/random.cpp: setSeed (0 becomes 1), scrambleSeed and getRandomBit.
class RandomSource {
	uint32 _randSeed;
	void scrambleSeed() {
		_randSeed ^= _randSeed >> 13;
		_randSeed ^= _randSeed << 21;
		_randSeed ^= _randSeed >> 11;
	}
public:
	RandomSource(const char *) { _randSeed = g_shimRandomSeed ? g_shimRandomSeed : 1; }
	uint getRandomBit() { scrambleSeed(); return _randSeed & 1; }
};
} // namespace Common

namespace Audio {
class Chip {
public:
	virtual ~Chip() {}
	virtual bool isStereo() const = 0;
	void stop() {}
};

class EmulatedChip : virtual public Chip {
protected:
	void stopCallbacks() {}
	virtual void generateSamples(int16 *buffer, int numSamples) = 0;
};
} // namespace Audio

namespace OPL {
class OPL : virtual public Audio::Chip {
public:
	virtual bool init() = 0;
	virtual void reset() = 0;
	virtual void write(int a, int v) = 0;
	virtual void writeReg(int r, int v) = 0;
};
} // namespace OPL

extern int g_shimOutputRate;
struct ShimMixer { int getOutputRate() const { return g_shimOutputRate; } };
struct ShimSystem { ShimMixer _mixer; ShimMixer *getMixer() { return &_mixer; } };
extern ShimSystem *g_system;
