// Converts a 32 kHz stereo stream (an MT-32 render) to another rate through ScummVM's own mixer
// rate converter (audio/rate.cpp, compiled in place), as the mixer plays MidiDriver_MT32's stream
// at full volume, as the reference for Distaff's Mt32Emulator.
//
// rate-reference <input.raw> <output.raw> <output rate>
#include "common/scummsys.h"
#include "audio/audiostream.h"
#include "audio/rate.h"
#include "audio/mixer.h"
#include <vector>

class RawStream : public Audio::AudioStream {
public:
	std::vector<int16> data;
	size_t pos = 0;
	int readBuffer(int16 *buffer, const int numSamples) override {
		int n = 0;
		for (; n < numSamples && pos < data.size(); ++n)
			buffer[n] = data[pos++];
		return n;
	}
	bool isStereo() const override { return true; }
	bool endOfData() const override { return pos >= data.size(); }
	int getRate() const override { return 32000; }
};

int main(int argc, char **argv) {
	if (argc < 4) {
		fprintf(stderr, "usage: rate-reference <input.raw> <output.raw> <output rate>\n");
		return 2;
	}
	RawStream stream;
	FILE *in = fopen(argv[1], "rb");
	int16 s;
	while (fread(&s, 2, 1, in) == 1)
		stream.data.push_back(s);
	fclose(in);
	int rate = atoi(argv[3]);
	Audio::RateConverter *conv = Audio::makeRateConverter(32000, rate, true, true, false);
	FILE *out = fopen(argv[2], "wb");
	std::vector<int16> buf(2 * 1024);
	while (true) {
		memset(buf.data(), 0, buf.size() * 2);
		int got = conv->convert(stream, (byte *)buf.data(), sizeof(int16), 1024, Audio::Mixer::kMaxMixerVolume, Audio::Mixer::kMaxMixerVolume, Audio::MIX_CLAMPED_ADD);
		fwrite(buf.data(), 2, 2 * got, out);
		if (got < 1024)
			break;
	}
	fclose(out);
	return 0;
}
