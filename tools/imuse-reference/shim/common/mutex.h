#pragma once
#include "common/scummsys.h"
// The reference runs on one thread: the mutex guards nothing.
namespace Common {
class Mutex { public: void lock() {} void unlock() {} };
class StackLock { public: StackLock(Mutex &) {} };
}
