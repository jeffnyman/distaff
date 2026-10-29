#pragma once
#include "common/scummsys.h"
template<typename T> inline T MIN(T a, T b) { return a < b ? a : b; }
template<typename T> inline T MAX(T a, T b) { return a > b ? a : b; }
template<typename T> inline T ABS(T x) { return x >= 0 ? x : -x; }
template<typename T> inline T CLIP(T v, T a, T b) { return v < a ? a : v > b ? b : v; }
template<typename T> inline void SWAP(T &a, T &b) { T t = a; a = b; b = t; }
namespace Common {
inline size_t strlcpy(char *dst, const char *src, size_t size) { size_t n = strlen(src); if (size) { size_t c = n < size - 1 ? n : size - 1; memcpy(dst, src, c); dst[c] = 0; } return n; }
template<class In, class T> In find(In first, In last, const T &v) { for (; first != last; ++first) if (*first == v) break; return first; }
}
inline const char *tag2str(uint32 tag) {
	static char s[5];
	s[0] = (char)(tag >> 24); s[1] = (char)(tag >> 16); s[2] = (char)(tag >> 8); s[3] = (char)tag; s[4] = 0;
	return s;
}
