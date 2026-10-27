// Plays a register script through ScummVM's MAME OPL2 emulator (audio/softsynth/opl/mame.cpp)
// and writes the 16-bit mono samples (little-endian), at 44100 Hz unless a rate is given.
// Usage: opl-reference <script> <output.raw> [random seed, hex] [sample rate]
// Script lines: "w <reg hex> <val hex>" writes a register, "g <count>" generates samples
// in one call, "r" resets the chip, "#" starts a comment.
#include <stdio.h>
#include <string.h>
#include <vector>
#include "shim.h"
#include "mame.h"

uint32 g_shimRandomSeed = 1;
int g_shimOutputRate = 44100;
static ShimSystem s_system;
ShimSystem *g_system = &s_system;

class ReferenceOpl : public OPL::MAME::OPL {
public:
	void generate(int16 *buffer, int length) { generateSamples(buffer, length); }
};

int main(int argc, char **argv) {
	if (argc < 3) {
		fprintf(stderr, "usage: opl-reference <script> <output.raw> [seed hex]\n");
		return 2;
	}
	if (argc > 3)
		g_shimRandomSeed = (uint32)strtoul(argv[3], nullptr, 16);
	if (argc > 4)
		g_shimOutputRate = atoi(argv[4]);

	FILE *in = fopen(argv[1], "r");
	FILE *out = fopen(argv[2], "wb");
	if (!in || !out) {
		fprintf(stderr, "cannot open files\n");
		return 1;
	}

	ReferenceOpl opl;
	if (!opl.init())
		return 1;

	char line[256];
	long total = 0;
	while (fgets(line, sizeof(line), in)) {
		unsigned r, v;
		int n;
		char c;
		if (sscanf(line, " w %x %x", &r, &v) == 2) {
			opl.writeReg((int)r, (int)v);
		} else if (sscanf(line, " g %d", &n) == 1) {
			std::vector<int16> buf(n);
			opl.generate(buf.data(), n);
			fwrite(buf.data(), sizeof(int16), n, out);
			total += n;
		} else if (sscanf(line, " %c", &c) == 1 && c == 'r') {
			opl.reset();
		}
	}
	fclose(in);
	fclose(out);
	printf("%ld samples\n", total);
	return 0;
}
