// Stand-in for ScummVM's common/scummsys.h: the standard headers and the basic types.
#pragma once
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdarg.h>
#include <assert.h>
#include <new>
typedef uint8_t uint8;
typedef int8_t int8;
typedef uint16_t uint16;
typedef int16_t int16;
typedef uint32_t uint32;
typedef int32_t int32;
typedef int64_t int64;
typedef uint64_t uint64;
typedef unsigned int uint;
typedef uint8_t byte;
#define ARRAYSIZE(x) ((int)(sizeof(x) / sizeof(x[0])))
#define MKTAG(a0, a1, a2, a3) ((uint32)((a3) | ((a2) << 8) | ((a1) << 16) | ((a0) << 24)))
#define PACKED_STRUCT
#define FORCEINLINE inline
