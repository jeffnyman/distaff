// Stand-in for Common::String: what the MT-32 driver's welcome message uses.
#pragma once
#include "common/scummsys.h"
#include <string>
namespace Common {
class String {
	std::string _s;
public:
	String() {}
	String(const char *s) : _s(s) {}
	String(const std::string &s) : _s(s) {}
	uint32 size() const { return (uint32)_s.size(); }
	bool empty() const { return _s.empty(); }
	const char *c_str() const { return _s.c_str(); }
	String substr(uint32 pos, uint32 len = (uint32)-1) const { return String(_s.substr(pos, len)); }
	uint32 findFirstNotOf(const char *chars) const { size_t p = _s.find_first_not_of(chars); return p == std::string::npos ? (uint32)-1 : (uint32)p; }
	static String format(const char *fmt, ...) { char buf[512]; va_list va; va_start(va, fmt); vsnprintf(buf, sizeof buf, fmt, va); va_end(va); return String(buf); }
	String &operator+=(const String &b) { _s += b._s; return *this; }
	friend String operator+(const String &a, const String &b) { return String(a._s + b._s); }
	friend String operator+(const char *a, const String &b) { return String(std::string(a) + b._s); }
	friend String operator+(char a, const String &b) { return String(std::string(1, a) + b._s); }
	friend String operator+(const String &a, char b) { return String(a._s + std::string(1, b)); }
};
}
