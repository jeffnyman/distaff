#pragma once
#include "common/mutex.h"
namespace Audio { class Mixer { Common::Mutex _m; public: enum { kMaxChannelVolume = 255, kMaxMixerVolume = 256 }; Common::Mutex &mutex() { return _m; } }; }
