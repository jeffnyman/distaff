// common/func.h: the nullary functors audio/chip.h and player_v2cms.cpp use.
#pragma once
#include "../shim.h"

namespace Common {

template<class Res>
struct Functor0 {
	virtual ~Functor0() {}
	virtual bool isValid() const = 0;
	virtual Res operator()() const = 0;
};

template<class Res, class T>
class Functor0Mem : public Functor0<Res> {
public:
	typedef Res (T::*FuncType)();
	Functor0Mem(T *t, const FuncType &func) : _t(t), _func(func) {}
	bool isValid() const override { return _func != 0 && _t != 0; }
	Res operator()() const override { return (_t->*_func)(); }

private:
	mutable T *_t;
	const FuncType _func;
};

} // namespace Common
