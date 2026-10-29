// Renders a scripted MIDI sequence through Munt (ScummVM's copy) for comparison with Distaff's port (by way of Daventry).
//   harness <control.rom> <pcm.rom> <script.txt> <out.raw>
// Script lines: "<frame> M <msg hex>" (short message, packed), "<frame> S <sysex hex bytes>",
// "<frame> W <device> <address and data hex>" (a Roland data set written to memory), "<frame> E" (end);
// others, as imuse-reference's "T" lines, are passed over.
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <vector>
#include <string>
#include "Synth.h"
#include "ROMInfo.h"
#include "FileStream.h"

extern "C" int mt32_test_rand(void) { return 0; }

using namespace MT32Emu;

int main(int argc, char **argv) {
	if (argc < 5) { fprintf(stderr, "usage\n"); return 2; }
	unsigned int chunk = argc > 5 ? (unsigned int)atoi(argv[5]) : 0;
	FileStream c, p;
	if (!c.open(argv[1]) || !p.open(argv[2])) { fprintf(stderr, "rom open failed\n"); return 1; }
	const ROMImage *ci = ROMImage::makeROMImage(&c);
	const ROMImage *pi = ROMImage::makeROMImage(&p);
	Synth synth;
	if (!synth.open(*ci, *pi, DEFAULT_MAX_PARTIALS, AnalogOutputMode_COARSE)) { fprintf(stderr, "open failed\n"); return 1; }
	synth.setOutputGain(1.0f);
	synth.setReverbOutputGain(1.0f);
	synth.setMIDIDelayMode(MIDIDelayMode_IMMEDIATE);

	FILE *script = fopen(argv[3], "r");
	FILE *out = fopen(argv[4], "wb");
	std::vector<Bit16s> buf;
	unsigned int pos = 0;
	char line[4096];
	while (fgets(line, sizeof line, script)) {
		unsigned int frame; char kind;
		int consumed = 0;
		if (sscanf(line, "%u %c %n", &frame, &kind, &consumed) < 2) continue;
		// rendered up to the event, in pieces of at most `chunk` frames on multiples of it when given
		while (frame > pos) {
			unsigned int n = frame - pos;
			if (chunk && n > chunk - pos % chunk)
				n = chunk - pos % chunk;
			buf.resize(n * 2);
			synth.render(buf.data(), n);
			fwrite(buf.data(), 2, n * 2, out);
			pos += n;
		}
		if (kind == 'M') {
			unsigned int msg = (unsigned int)strtoul(line + consumed, NULL, 16);
			synth.playMsg(msg);
		} else if (kind == 'S') {
			std::vector<Bit8u> bytes;
			char *q = line + consumed;
			while (*q) {
				while (*q == ' ') q++;
				if (!q[0] || q[0] == '\n' || q[0] == '\r') break;
				bytes.push_back((Bit8u)strtoul(std::string(q, 2).c_str(), NULL, 16));
				q += 2;
			}
			synth.playSysex(bytes.data(), (Bit32u)bytes.size());
		} else if (kind == 'W') {
			// a Roland data set written straight to the synth's memory, as ScummVM's
			// MidiDriver_MT32::sysEx writes one: "<device> <address and data hex>"
			char *q = line + consumed;
			unsigned int device = (unsigned int)strtoul(q, &q, 16);
			std::vector<Bit8u> bytes;
			while (*q) {
				while (*q == ' ') q++;
				if (!q[0] || q[0] == '\n' || q[0] == '\r') break;
				bytes.push_back((Bit8u)strtoul(std::string(q, 2).c_str(), NULL, 16));
				q += 2;
			}
			synth.writeSysex((Bit8u)device, bytes.data(), (Bit32u)bytes.size());
		} else if (kind == 'E') {
			break;
		}
	}
	fclose(out);
	fclose(script);
	synth.close();
	return 0;
}
