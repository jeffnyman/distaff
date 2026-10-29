// common/ptr.h: the scoped pointer audio/chip.h keeps its callback in.
#pragma once
#include "../shim.h"

namespace Common {

template<class T>
class ScopedPtr {
public:
	ScopedPtr() : _p(nullptr) {}
	~ScopedPtr() { delete _p; }
	void reset(T *p = nullptr) { delete _p; _p = p; }
	operator bool() const { return _p != nullptr; }
	T *operator->() const { return _p; }
	T &operator*() const { return *_p; }

private:
	T *_p;
};

} // namespace Common
