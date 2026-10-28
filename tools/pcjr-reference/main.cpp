// Plays a sound resource through ScummVM's Player_V2 (engines/scumm/players/player_v2.cpp and
// player_v2base.cpp, compiled in place) and writes what it renders, as the reference for
// Distaff's PlayerV2.
//
// pcjr-reference <sound.bin> <number> <output.raw> <frames> [chunk] [speaker]
//
// The sound is the resource as the player takes it, its 4-byte old-bundle header first. The
// player is made for a version 3 game with old-bundle resources, as Loom's EGA release, at
// 44100 Hz; it renders the PCjr unless `speaker` is given. `frames` stereo frames are read in
// reads of `chunk` frames (1024 by default). The output is the 16-bit little-endian stereo samples.
#include "shim.h"
#include "scumm/scumm.h"
#include "scumm/players/player_v2.h"

#include <vector>

int main(int argc, char **argv) {
	if (argc < 5) {
		fprintf(stderr, "usage: pcjr-reference <sound.bin> <number> <output.raw> <frames> [chunk] [speaker]\n");
		return 2;
	}

	FILE *in = fopen(argv[1], "rb");
	if (!in) {
		fprintf(stderr, "cannot read %s\n", argv[1]);
		return 1;
	}
	std::vector<byte> data;
	for (int c; (c = fgetc(in)) != EOF;)
		data.push_back((byte)c);
	fclose(in);

	int number = atoi(argv[2]);
	long frames = atol(argv[4]);
	int chunk = argc > 5 ? atoi(argv[5]) : 1024;
	bool pcjr = !(argc > 6 && strcmp(argv[6], "speaker") == 0);

	Scumm::ScummEngine vm;
	vm._game.version = 3;
	vm._game.features = Scumm::GF_OLD_BUNDLE;
	vm._sounds[number & 0xff] = data.data();
	Audio::Mixer mixer(44100);
	Scumm::Player_V2 player(&vm, &mixer, pcjr);
	player.startSound(number);

	FILE *out = fopen(argv[3], "wb");
	std::vector<int16> buffer(2 * chunk);
	for (long done = 0; done < frames; done += chunk) {
		int n = (int)MIN<long>(chunk, frames - done);
		player.readBuffer(buffer.data(), 2 * n);
		fwrite(buffer.data(), sizeof(int16), 2 * n, out);
	}
	fclose(out);
	return 0;
}
