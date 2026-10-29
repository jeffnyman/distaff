#pragma once
#include "common/scummsys.h"
inline uint16 READ_LE_UINT16(const void *p) { const uint8 *b = (const uint8 *)p; return (uint16)(b[0] | (b[1] << 8)); }
inline uint32 READ_LE_UINT32(const void *p) { const uint8 *b = (const uint8 *)p; return b[0] | (b[1] << 8) | (b[2] << 16) | ((uint32)b[3] << 24); }
inline uint16 READ_BE_UINT16(const void *p) { const uint8 *b = (const uint8 *)p; return (uint16)((b[0] << 8) | b[1]); }
inline uint32 READ_BE_UINT32(const void *p) { const uint8 *b = (const uint8 *)p; return ((uint32)b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3]; }
