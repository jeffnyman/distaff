// Stand-in for OSystem: the MT-32 driver's delays while it sets the module up take no time here.
#pragma once
#include "common/scummsys.h"
class OSystem { public: void delayMillis(uint) {} };
extern OSystem *g_system;
