// Stand-in for ScummVM's Common::Serializer (common/serializer.h): a field outside its version range
// is neither read nor written; otherwise it is read from or written to a buffer, little-endian
// unless named big-endian, as there.
#pragma once
#include "common/scummsys.h"
#include <vector>
namespace Common {
class Serializer {
public:
	enum { Uint16LE, Uint32LE, Byte };
	typedef uint32 Version;
	static const Version kLastVersion = 0xFFFFFFFF;

	std::vector<byte> *_buffer;
	size_t _pos = 0;
	bool _loading;
	Version _version;

	Serializer(std::vector<byte> *buffer, bool loading, Version version) : _buffer(buffer), _loading(loading), _version(version) {}

	bool isSaving() const { return !_loading; }
	bool isLoading() const { return _loading; }
	Version getVersion() const { return _version; }

	template<class T> void syncAsByte(T &v, Version mn = 0, Version mx = kLastVersion) { sync<byte, 1, false>(v, mn, mx); }
	template<class T> void syncAsSByte(T &v, Version mn = 0, Version mx = kLastVersion) { sync<int8, 1, false>(v, mn, mx); }
	template<class T> void syncAsUint16LE(T &v, Version mn = 0, Version mx = kLastVersion) { sync<uint16, 2, false>(v, mn, mx); }
	template<class T> void syncAsSint16LE(T &v, Version mn = 0, Version mx = kLastVersion) { sync<int16, 2, false>(v, mn, mx); }
	template<class T> void syncAsUint32LE(T &v, Version mn = 0, Version mx = kLastVersion) { sync<uint32, 4, false>(v, mn, mx); }
	template<class T> void syncAsSint32BE(T &v, Version mn = 0, Version mx = kLastVersion) { sync<int32, 4, true>(v, mn, mx); }

	template<class T> void syncArray(T *arr, size_t n, void (*fn)(Serializer &, T &), Version mn = 0, Version mx = kLastVersion) {
		if (!has(mn, mx))
			return;
		for (size_t i = 0; i < n; ++i)
			fn(*this, arr[i]);
	}
	template<class T> void syncArray(T *arr, size_t n, int type, Version mn = 0, Version mx = kLastVersion) {
		if (!has(mn, mx))
			return;
		for (size_t i = 0; i < n; ++i) {
			if (type == Uint16LE)
				syncAsUint16LE(arr[i]);
			else if (type == Uint32LE)
				syncAsUint32LE(arr[i]);
			else
				syncAsByte(arr[i]);
		}
	}

	void syncBytes(byte *buf, uint32 size, Version mn = 0, Version mx = kLastVersion) {
		if (!has(mn, mx))
			return;
		for (uint32 i = 0; i < size; ++i)
			syncAsByte(buf[i]);
	}

	void skip(uint32 size, Version mn = 0, Version mx = kLastVersion) {
		if (!has(mn, mx))
			return;
		if (_loading) {
			_pos += size;
		} else {
			for (uint32 i = 0; i < size; ++i)
				_buffer->push_back(0);
		}
	}

private:
	bool has(Version mn, Version mx) const { return _version >= mn && _version <= mx; }

	template<class U, int N, bool BE, class T> void sync(T &v, Version mn, Version mx) {
		if (!has(mn, mx))
			return;
		if (_loading) {
			uint32 raw = 0;
			for (int i = 0; i < N; ++i) {
				byte b = _pos < _buffer->size() ? (*_buffer)[_pos] : 0;
				++_pos;
				raw |= (uint32)b << (8 * (BE ? N - 1 - i : i));
			}
			v = (T)(U)raw;
		} else {
			uint32 raw = (uint32)(U)v;
			for (int i = 0; i < N; ++i)
				_buffer->push_back((byte)(raw >> (8 * (BE ? N - 1 - i : i))));
		}
	}
};
class Serializable { public: virtual ~Serializable() {} virtual void saveLoadWithSerializer(Serializer &s) = 0; };
}
