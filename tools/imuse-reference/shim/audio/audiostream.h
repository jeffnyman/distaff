#pragma once
#include "common/scummsys.h"
namespace Audio {
class AudioStream {
public:
	virtual ~AudioStream() {}
	virtual int readBuffer(int16 *buffer, const int numSamples) = 0;
	virtual bool isStereo() const = 0;
	virtual bool endOfData() const = 0;
	virtual int getRate() const = 0;
};
}
