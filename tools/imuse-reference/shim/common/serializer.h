// Stand-in for ScummVM's Common::Serializer: the reference does not save, so every sync does nothing.
#pragma once
#include "common/scummsys.h"
namespace Common {
class Serializer {
public:
	enum { Uint16LE, Uint32LE, Byte };
	bool isSaving() const { return true; }
	bool isLoading() const { return false; }
	uint32 getVersion() const { return 200; }
	template<class T> void syncAsByte(T &, uint32 = 0, uint32 = 0xFFFFFFFF) {}
	template<class T> void syncAsSByte(T &, uint32 = 0, uint32 = 0xFFFFFFFF) {}
	template<class T> void syncAsUint16LE(T &, uint32 = 0, uint32 = 0xFFFFFFFF) {}
	template<class T> void syncAsSint16LE(T &, uint32 = 0, uint32 = 0xFFFFFFFF) {}
	template<class T> void syncAsUint32LE(T &, uint32 = 0, uint32 = 0xFFFFFFFF) {}
	template<class T> void syncAsSint32BE(T &, uint32 = 0, uint32 = 0xFFFFFFFF) {}
	template<class T> void syncArray(T *, size_t, void (*)(Serializer &, T &), uint32 = 0, uint32 = 0xFFFFFFFF) {}
	template<class T> void syncArray(T *, size_t, int, uint32 = 0, uint32 = 0xFFFFFFFF) {}
	void syncBytes(byte *, uint32, uint32 = 0, uint32 = 0xFFFFFFFF) {}
	void skip(uint32, uint32 = 0, uint32 = 0xFFFFFFFF) {}
};
class Serializable { public: virtual ~Serializable() {} virtual void saveLoadWithSerializer(Serializer &s) = 0; };
}
