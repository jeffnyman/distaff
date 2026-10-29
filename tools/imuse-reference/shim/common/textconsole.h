#pragma once
#include "common/scummsys.h"
inline void warning(const char *s, ...) { va_list va; va_start(va, s); fprintf(stderr, "warning: "); vfprintf(stderr, s, va); fprintf(stderr, "\n"); va_end(va); }
inline void error(const char *s, ...) { va_list va; va_start(va, s); fprintf(stderr, "error: "); vfprintf(stderr, s, va); fprintf(stderr, "\n"); va_end(va); exit(1); }
