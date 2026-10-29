// Plays a Roland sound of Loom's EGA release through ScummVM's own iMuse (engines/scumm/imuse:
// imuse.cpp, imuse_player.cpp, imuse_part.cpp, instrument.cpp, drivers/midi.cpp), its RO parser
// (engines/scumm/midiparser_ro.cpp) and its MIDI parser (audio/midiparser.cpp), all compiled in
// place, as the reference for Distaff's iMuse. In place of the MT-32 emulator, a recording device
// writes what reaches it, timed as ScummVM's MidiDriver_MT32 times it (MidiDriver_Emulated at
// 32000 Hz, a timer tick every 128 frames, 250 a second).
//
// imuse-reference <sound.bin> <number> <output.txt> <frames>
//
// The sound is the resource as ScummVM loads it for the Roland Upgrade: "RO", then the block
// after its 4-byte header (readSoundResourceSmallHeader for isRolandLoom). iMuse is set up as
// ScummVM's setupMusic sets it up for an MT-32 (IMuseDriver_MT32, the old system, native MT-32,
// tempo 100%) with ScummVM's default music volume, 192. The output has a line for each message:
// "<frame> M <packed message hex>", "<frame> S <sysex hex, F0 to F7>", or for a Roland data set
// "<frame> W <device> <address and data hex>" (MidiDriver_MT32::sysEx writes these straight to
// the synth's memory); the music timer as it changes, "<frame> T <value>"; and "<frame> E" at
// the end. It is mt32-reference's script format, so that Munt can render it.
#include "common/scummsys.h"
#include "common/system.h"
#include "audio/mididrv.h"
#include "scumm/scumm.h"
#include "scumm/imuse/imuse.h"
#include "scumm/imuse/drivers/midi.h"

#include <vector>

const char *gScummVMVersion = "2026.4.0";
static OSystem systemInstance;
OSystem *g_system = &systemInstance;
namespace Scumm { ScummEngine *g_scumm; }

static FILE *out;
static uint32 frame;

// What MidiDriver_MT32 is to iMuse: MidiDriver_Emulated's timer, and messages played at once.
class RecordingDriver : public MidiDriver {
public:
	Common::TimerManager::TimerProc timerProc = nullptr;
	void *timerParam = nullptr;
	bool opened = false;

	int open() override { opened = true; return 0; }
	bool isOpen() const override { return opened; }
	void close() override { opened = false; }
	void send(uint32 b) override { fprintf(out, "%u M %x\n", frame, b); }
	// MidiDriver_MT32::sysEx: a message with its F0 is played as it is; a Roland data set
	// (DT1 or DAT) without it is written straight to the synth's memory, checksum left out
	void sysEx(const byte *msg, uint16 length) override {
		if (msg[0] == 0xF0) {
			fprintf(out, "%u S ", frame);
			for (uint16 i = 0; i < length; ++i)
				fprintf(out, "%02x", msg[i]);
			fprintf(out, "\n");
		} else if (msg[3] == 0x12 || msg[3] == 0x42) {
			fprintf(out, "%u W %02x ", frame, msg[1]);
			for (uint16 i = 4; i + 1 < length; ++i)
				fprintf(out, "%02x", msg[i]);
			fprintf(out, "\n");
		}
	}
	void setTimerCallback(void *param, Common::TimerManager::TimerProc proc) override { timerParam = param; timerProc = proc; }
	uint32 getBaseTempo() override { return 1000000 / 250; }
	MidiChannel *allocateChannel() override { return nullptr; }
	MidiChannel *getPercussionChannel() override { return nullptr; }
};

static RecordingDriver recorder;
MidiDriver *MidiDriver::createMidi(DeviceHandle) { return &recorder; }

int main(int argc, char **argv) {
	if (argc < 5) {
		fprintf(stderr, "usage: imuse-reference <sound.bin> <number> <output.txt> <frames>\n");
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
	uint32 frames = (uint32)atol(argv[4]);
	out = fopen(argv[3], "w");

	Scumm::ScummEngine vm;
	Scumm::g_scumm = &vm;
	vm._res->_types[Scumm::rtSound][number]._address = data.data();

	Scumm::IMuse *imuse = Scumm::IMuse::create(&vm, new Scumm::IMuseDriver_MT32(0, false), nullptr, MDT_MIDI, true);
	imuse->setMusicVolume(192);
	imuse->startSound(number);

	// MidiDriver_Emulated::readBuffer: the timer at frame 0 and then every 128 frames
	const int samplesPerTick = (32000 / 250) << 16;
	int nextTick = 0;
	int timer = -1;
	while (frame < frames) {
		uint32 step = MIN<uint32>(frames - frame, (uint32)(nextTick >> 16));
		frame += step;
		nextTick -= step << 16;
		if (!(nextTick >> 16)) {
			if (recorder.timerProc)
				recorder.timerProc(recorder.timerParam);
			nextTick += samplesPerTick;
			int now = imuse->getMusicTimer();
			if (now != timer) {
				fprintf(out, "%u T %d\n", frame, now);
				timer = now;
			}
		}
	}

	fprintf(out, "%u E\n", frame);
	fclose(out);
	return 0;
}
