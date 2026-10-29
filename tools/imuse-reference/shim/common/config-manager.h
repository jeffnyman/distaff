// Stand-in for the configuration manager: the rate converter asks it the active game's name for a debug line only.
#pragma once
#include "common/str.h"
namespace Common { class ConfigManager { public: String getActiveDomainName() const { return String(); } }; }
static Common::ConfigManager ConfMan;
