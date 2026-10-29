// The SMF and XMIDI parsers iMuse's player can create for other games' sounds. Loom's Roland
// Upgrade plays RO resources only, so these stop the reference if they are ever reached.
#include "audio/midiparser.h"
#include "common/textconsole.h"

MidiParser *MidiParser::createParser_SMF(int8) { error("SMF parser not in the reference"); return nullptr; }
MidiParser *MidiParser::createParser_XMIDI(XMidiCallbackProc, void *, int) { error("XMIDI parser not in the reference"); return nullptr; }
void MidiParser::defaultXMidiCallback(byte, void *) {}
