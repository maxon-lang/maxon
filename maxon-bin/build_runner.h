#pragma once

#include "compiler.h"

#include <optional>
#include <string>
#include <vector>

struct BuildConfig {
	std::string name;
	std::string output;
	std::vector<std::string> sources;
	bool optimize = false;
	bool debugInfo = false;
};

// Check if build.maxon exists in the given directory
bool hasBuildMaxon(const std::string &directory);

// Execute build.maxon and capture its JSON output
std::optional<std::string> executeBuildMaxon(const std::string &buildFile);

// Parse JSON output from build.maxon into BuildConfig
std::optional<BuildConfig> parseBuildConfig(const std::string &json);

// Convert BuildConfig to CompilationOptions
CompilationOptions buildConfigToOptions(const BuildConfig &config, const std::string &projectDir);
