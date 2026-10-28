// Plays a script through ScummVM's Player_Towns_v1 (engines/scumm/players/player_towns.cpp) on
// its FM Towns audio emulation (audio/softsynth/fmtowns_pc98) and writes the 16-bit stereo
// samples (little-endian, left then right) as the mixer would take them from the Towns' stream.
// Usage: towns-reference <script> <output.raw> [sample rate]
// Script lines:
//   load <sound> <file>    the sound's resource, its 6-byte header included, from a file
//   start <sound>          startSound
//   stop <sound>           stopSound (Sound::stopSound)
//   stopall                stopAllSounds
//   vol <sound> <l> <r>    setSoundVolume
//   note <sound> <note>    setSoundNote
//   cdvol <l> <r>          setVolumeCD
//   g <frames> [chunk]     reads that many stereo frames, in reads of [chunk] frames (all at once by default)
//   status <sound>         prints getSoundStatus
//   # ...                  a comment
// Or: towns-reference --tables <output.bin> writes the synth's tables (TownsPC98_FmSynth::generateTables
// for the Towns): rates, rate shifts and attack/decay steps (bytes), then frequencies and sines
// (uint32), output levels and detunes (int32), little-endian.
#include <stdio.h>
#include <string.h>
#include <vector>
#include "shim.h"

// the tables are the synth's own; opened up for the --tables dump
#define private public
#define protected public
#include "audio/softsynth/fmtowns_pc98/towns_pc98_fmsynth.h"
#undef private
#undef protected

#include "scumm/scumm.h"
#include "scumm/players/player_towns.h"

static ShimSystem s_system;
ShimSystem *g_system = &s_system;

class TableSynth : public TownsPC98_FmSynth {
public:
	TableSynth(Audio::Mixer *mixer) : TownsPC98_FmSynth(mixer, kTypeTowns) {}
	void timerCallbackA() override {}
	void timerCallbackB() override {}
};

static int dumpTables(const char *path) {
	Audio::Mixer mixer(44100);
	TableSynth synth(&mixer);
	synth.init();
	FILE *out = fopen(path, "wb");
	if (!out)
		return 1;
	fwrite(synth._oprRates, 1, 130, out);
	fwrite(synth._oprRateshift, 1, 130, out);
	fwrite(synth._oprAttackDecay, 1, 152, out);
	fwrite(synth._oprFrq, 4, 0x1000, out);
	fwrite(synth._oprSinTbl, 4, 1024, out);
	fwrite(synth._oprLevelOut, 4, 0x1a00, out);
	fwrite(synth._oprDetune, 4, 256, out);
	fclose(out);
	return 0;
}

int main(int argc, char **argv) {
	if (argc == 3 && strcmp(argv[1], "--tables") == 0)
		return dumpTables(argv[2]);

	if (argc < 3) {
		fprintf(stderr, "usage: towns-reference <script> <output.raw> [sample rate]\n");
		return 2;
	}

	int rate = argc > 3 ? atoi(argv[3]) : 44100;
	FILE *in = fopen(argv[1], "r");
	FILE *out = fopen(argv[2], "wb");
	if (!in || !out) {
		fprintf(stderr, "cannot open files\n");
		return 1;
	}

	Audio::Mixer mixer(rate);
	Scumm::ScummEngine vm;
	Scumm::Player_Towns_v1 *player = new Scumm::Player_Towns_v1(&vm, &mixer);
	vm._sound->_engine = player;
	if (!player->init()) {
		fprintf(stderr, "init failed\n");
		return 1;
	}

	// ScummEngine::syncSoundSettings at the full music and effects volumes
	player->setMusicVolume(Audio::Mixer::kMaxMixerVolume);
	player->setSfxVolume(Audio::Mixer::kMaxMixerVolume);

	char line[1024];
	long total = 0;
	while (fgets(line, sizeof(line), in)) {
		int a, b, c;
		char path[900];
		if (sscanf(line, " load %d %899s", &a, path) == 2) {
			FILE *f = fopen(path, "rb");
			if (!f || a < 0 || a > 255) {
				fprintf(stderr, "cannot load %s\n", path);
				return 1;
			}
			fseek(f, 0, SEEK_END);
			long size = ftell(f);
			fseek(f, 0, SEEK_SET);
			delete[] vm._resources[a];
			vm._resources[a] = new uint8[size];
			fread(vm._resources[a], 1, size, f);
			fclose(f);
		} else if (sscanf(line, " start %d", &a) == 1) {
			player->startSound(a);
		} else if (sscanf(line, " stop %d", &a) == 1) {
			vm._sound->stopSound(a);
		} else if (strncmp(line, "stopall", 7) == 0) {
			player->stopAllSounds();
		} else if (sscanf(line, " vol %d %d %d", &a, &b, &c) == 3) {
			player->setSoundVolume(a, b, c);
		} else if (sscanf(line, " note %d %d", &a, &b) == 2) {
			player->setSoundNote(a, b);
		} else if (sscanf(line, " cdvol %d %d", &a, &b) == 2) {
			player->setVolumeCD(a, b);
		} else if (sscanf(line, " status %d", &a) == 1) {
			printf("status %d %d at %ld\n", a, player->getSoundStatus(a), total);
		} else if (line[0] == 'g' && sscanf(line + 1, " %d", &a) == 1) {
			int chunk = a;
			if (sscanf(line + 1, " %d %d", &a, &b) == 2 && b > 0)
				chunk = b;
			std::vector<int16> buf;
			for (int left = a; left > 0;) {
				int n = left < chunk ? left : chunk;
				buf.assign(n * 2, 0);
				if (mixer.stream())
					mixer.stream()->readBuffer(buf.data(), n * 2);
				fwrite(buf.data(), sizeof(int16), n * 2, out);
				left -= n;
				total += n;
			}
		}
	}

	fclose(in);
	fclose(out);
	printf("%ld frames\n", total);
	delete player;
	return 0;
}
