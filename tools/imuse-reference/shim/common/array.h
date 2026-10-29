#pragma once
#include "common/scummsys.h"
#include <vector>
namespace Common {
template<class T> class Array {
	std::vector<T> _v;
public:
	typedef typename std::vector<T>::iterator iterator;
	bool empty() const { return _v.empty(); }
	uint size() const { return (uint)_v.size(); }
	void push_back(const T &x) { _v.push_back(x); }
	T remove_at(uint i) { T x = _v[i]; _v.erase(_v.begin() + i); return x; }
	iterator begin() { return _v.begin(); }
	iterator end() { return _v.end(); }
	void insert(iterator it, const T &x) { _v.insert(it, x); }
	void erase(iterator it) { _v.erase(it); }
	T &operator[](uint i) { return _v[i]; }
};
}
