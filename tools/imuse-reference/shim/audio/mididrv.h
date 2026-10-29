// Stand-in for ScummVM's audio/mididrv.h: MidiDriver_BASE, MidiDriver and MidiChannel as declared
// there (the parts iMuse uses), and the device flags. The tables are mididrv.cpp's (mididrv_tables.cpp).
#pragma once
#include "common/scummsys.h"
#include "common/util.h"
#include "common/textconsole.h"
#include "common/str.h"
#include "common/timer.h"
#include "common/array.h"

enum MusicType { MT_INVALID = -1, MT_AUTO = 0, MT_NULL, MT_PCSPK, MT_PCJR, MT_CMS, MT_ADLIB, MT_C64, MT_AMIGA, MT_APPLEIIGS, MT_TOWNS, MT_PC98, MT_SEGACD, MT_MACINTOSH, MT_GM, MT_MT32, MT_GS, MT_MT540, MT_CT460 };

enum MidiDriverFlags {
	MDT_NONE = 0, MDT_PCSPK = 1 << 0, MDT_CMS = 1 << 1, MDT_PCJR = 1 << 2, MDT_ADLIB = 1 << 3, MDT_C64 = 1 << 4, MDT_AMIGA = 1 << 5,
	MDT_APPLEIIGS = 1 << 6, MDT_TOWNS = 1 << 7, MDT_PC98 = 1 << 8, MDT_SEGACD = 1 << 9, MDT_MIDI = 1 << 10, MDT_PREFER_MT32 = 1 << 11,
	MDT_PREFER_GM = 1 << 12, MDT_PREFER_FLUID = 1 << 13, MDT_MACINTOSH = 1 << 14
};

class MidiChannel;

class MidiDriver_BASE {
public:
	static const uint8 MIDI_CHANNEL_COUNT = 16;
	static const uint8 MIDI_RHYTHM_CHANNEL = 9;
	static const byte MIDI_COMMAND_CONTROL_CHANGE = 0xB0;
	static const byte MIDI_CONTROLLER_MODULATION = 0x01;
	static const byte MIDI_CONTROLLER_DATA_ENTRY_MSB = 0x06;
	static const byte MIDI_CONTROLLER_VOLUME = 0x07;
	static const byte MIDI_CONTROLLER_PANNING = 0x0A;
	static const byte MIDI_CONTROLLER_DATA_ENTRY_LSB = 0x26;
	static const byte MIDI_CONTROLLER_SUSTAIN = 0x40;
	static const byte MIDI_CONTROLLER_REVERB = 0x5B;
	static const byte MIDI_CONTROLLER_CHORUS = 0x5D;
	static const byte MIDI_CONTROLLER_RPN_LSB = 0x64;
	static const byte MIDI_CONTROLLER_RPN_MSB = 0x65;
	static const byte MIDI_CONTROLLER_ALL_NOTES_OFF = 0x7B;
	static const uint16 MIDI_RPN_PITCH_BEND_SENSITIVITY = 0x0000;
	static const uint16 MIDI_RPN_NULL = 0x7F7F;

	MidiDriver_BASE() {}
	virtual ~MidiDriver_BASE() {}
	virtual void send(uint32 b) = 0;
	virtual void send(int8 source, uint32 b) { send(b); }
	void send(byte status, byte firstOp, byte secondOp) { send(status | ((uint32)firstOp << 8) | ((uint32)secondOp << 16)); }
	void send(int8 source, byte status, byte firstOp, byte secondOp) { send(source, status | ((uint32)firstOp << 8) | ((uint32)secondOp << 16)); }
	virtual void sysEx(const byte *msg, uint16 length) { }
	virtual uint16 sysExNoDelay(const byte *msg, uint16 length) { sysEx(msg, length); return 0; }
	virtual void metaEvent(byte type, const byte *data, uint16 length) { }
	virtual void metaEvent(int8 source, byte type, const byte *data, uint16 length) { metaEvent(type, data, length); }
	// mididrv.cpp's MidiDriver_BASE::stopAllNotes
	virtual void stopAllNotes(bool stopSustainedNotes = false) {
		for (int i = 0; i < 16; ++i) {
			send(0xB0 | i, MIDI_CONTROLLER_ALL_NOTES_OFF, 0);
			if (stopSustainedNotes)
				send(0xB0 | i, MIDI_CONTROLLER_SUSTAIN, 0);
		}
	}
	virtual bool isReady(int8 source = -1) { return true; }
};

class MidiDriver : public MidiDriver_BASE {
public:
	typedef uint32 DeviceHandle;
	static MidiDriver *createMidi(DeviceHandle handle);
	static const char *getErrorName(int error_code) { return "error"; }
	void midiDriverCommonSend(uint32 b) {}
	void midiDriverCommonSysEx(const byte *msg, uint16 length) {}
	virtual ~MidiDriver() { }
	static const byte _mt32ToGm[128];
	static const byte _gmToMt32[128];
	enum { MERR_CANNOT_CONNECT = 1, MERR_DEVICE_NOT_AVAILABLE = 3, MERR_ALREADY_OPEN = 4 };
	enum { PROP_OLD_ADLIB = 2, PROP_CHANNEL_MASK = 3, PROP_SCUMM_OPL3 = 4 };
	virtual int open() = 0;
	virtual bool isOpen() const = 0;
	virtual void close() = 0;
	virtual uint32 property(int prop, uint32 param) { return 0; }
	virtual void setPitchBendRange(byte channel, uint range) {}
	virtual void setTimerCallback(void *timer_param, Common::TimerManager::TimerProc timer_proc) = 0;
	virtual uint32 getBaseTempo() = 0;
	virtual MidiChannel *allocateChannel() = 0;
	virtual MidiChannel *getPercussionChannel() = 0;
};

class MidiChannel {
public:
	virtual ~MidiChannel() {}
	virtual MidiDriver *device() = 0;
	virtual byte getNumber() = 0;
	virtual void release() = 0;
	virtual void send(uint32 b) = 0;
	virtual void noteOff(byte note) = 0;
	virtual void noteOn(byte note, byte velocity) = 0;
	virtual void programChange(byte program) = 0;
	virtual void pitchBend(int16 bend) = 0;
	virtual void controlChange(byte control, byte value) = 0;
	virtual void modulationWheel(byte value) { controlChange(MidiDriver::MIDI_CONTROLLER_MODULATION, value); }
	virtual void volume(byte value) { controlChange(MidiDriver::MIDI_CONTROLLER_VOLUME, value); }
	virtual void panPosition(byte value) { controlChange(MidiDriver::MIDI_CONTROLLER_PANNING, value); }
	virtual void pitchBendFactor(byte value) = 0;
	virtual void transpose(int8 value) {}
	virtual void detune(int16 value) { controlChange(17, value & 0xff); }
	virtual void priority(byte value) { }
	virtual void sustain(bool value) { controlChange(MidiDriver::MIDI_CONTROLLER_SUSTAIN, value ? 1 : 0); }
	virtual void effectLevel(byte value) { controlChange(MidiDriver::MIDI_CONTROLLER_REVERB, value); }
	virtual void chorusLevel(byte value) { controlChange(MidiDriver::MIDI_CONTROLLER_CHORUS, value); }
	virtual void bankSelect(byte bank) {}
	virtual void allNotesOff() { controlChange(MidiDriver::MIDI_CONTROLLER_ALL_NOTES_OFF, 0); }
	virtual void sysEx_customInstrument(uint32 type, const byte *instr, uint32 dataSize) = 0;
};
