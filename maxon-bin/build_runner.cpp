#include "build_runner.h"

#include <array>
#include <cstdio>
#include <ctime>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <memory>
#include <sstream>

bool hasBuildMaxon(const std::string &directory) {
	std::filesystem::path buildPath = std::filesystem::path(directory) / "build.maxon";
	return std::filesystem::exists(buildPath);
}

std::optional<std::string> executeBuildMaxon(const std::string &buildFile) {
	try {
		std::filesystem::path tempDir = "temp";
		std::filesystem::create_directories(tempDir);
		std::string tempExe =
			(tempDir / ("build_maxon_" + std::to_string(std::time(nullptr)) + ".exe")).string();

		CompilationOptions options;
		options.inputFiles = {buildFile};
		options.outputFile = tempExe;
		options.optimize = false;
		options.debugInfo = false;
		options.verboseLevel = 0;
		options.emitIR = false;
		options.compileOnly = false;
		options.silent = true; // Suppress output when building build.maxon

		std::string executablePath = compileProgram(options);

		// Run the executable and capture stdout
		std::string output;
#ifdef _WIN32
		std::string cmd = "\"" + executablePath + "\" 2>nul";
		std::unique_ptr<FILE, decltype(&_pclose)> pipe(_popen(cmd.c_str(), "r"), _pclose);
#else
		std::string cmd = "\"" + executablePath + "\" 2>/dev/null";
		std::unique_ptr<FILE, decltype(&pclose)> pipe(popen(cmd.c_str(), "r"), pclose);
#endif
		if (!pipe) {
			std::cerr << "Error: Failed to run build.maxon executable\n";
			std::filesystem::remove(executablePath);
			return std::nullopt;
		}

		std::array<char, 4096> buffer;
		while (fgets(buffer.data(), buffer.size(), pipe.get()) != nullptr) {
			output += buffer.data();
		}

		std::filesystem::remove(executablePath);
		return output;

	} catch (const std::exception &e) {
		std::cerr << "Error compiling build.maxon: " << e.what() << "\n";
		return std::nullopt;
	}
}

// Simple JSON string value extractor
static std::string extractJsonString(const std::string &json, const std::string &key) {
	std::string searchKey = "\"" + key + "\":";
	size_t keyPos = json.find(searchKey);
	if (keyPos == std::string::npos) {
		return "";
	}

	size_t valueStart = json.find('"', keyPos + searchKey.length());
	if (valueStart == std::string::npos) {
		return "";
	}
	valueStart++;

	std::string result;
	for (size_t i = valueStart; i < json.length(); i++) {
		if (json[i] == '\\' && i + 1 < json.length()) {
			// Handle escape sequences
			i++;
			if (json[i] == '"')
				result += '"';
			else if (json[i] == '\\')
				result += '\\';
			else if (json[i] == 'n')
				result += '\n';
			else if (json[i] == 't')
				result += '\t';
			else
				result += json[i];
		} else if (json[i] == '"') {
			break;
		} else {
			result += json[i];
		}
	}
	return result;
}

// Simple JSON bool value extractor
static bool extractJsonBool(const std::string &json, const std::string &key) {
	std::string searchKey = "\"" + key + "\":";
	size_t keyPos = json.find(searchKey);
	if (keyPos == std::string::npos) {
		return false;
	}

	size_t valueStart = keyPos + searchKey.length();
	// Skip whitespace
	while (valueStart < json.length() && std::isspace(json[valueStart])) {
		valueStart++;
	}

	return json.substr(valueStart, 4) == "true";
}

// Simple JSON string array extractor
static std::vector<std::string> extractJsonStringArray(const std::string &json,
													   const std::string &key) {
	std::vector<std::string> result;
	std::string searchKey = "\"" + key + "\":";
	size_t keyPos = json.find(searchKey);
	if (keyPos == std::string::npos) {
		return result;
	}

	size_t arrayStart = json.find('[', keyPos);
	if (arrayStart == std::string::npos) {
		return result;
	}

	size_t arrayEnd = json.find(']', arrayStart);
	if (arrayEnd == std::string::npos) {
		return result;
	}

	std::string arrayContent = json.substr(arrayStart + 1, arrayEnd - arrayStart - 1);

	// Extract each string from the array
	size_t pos = 0;
	while (pos < arrayContent.length()) {
		size_t stringStart = arrayContent.find('"', pos);
		if (stringStart == std::string::npos) {
			break;
		}
		stringStart++;

		std::string value;
		for (size_t i = stringStart; i < arrayContent.length(); i++) {
			if (arrayContent[i] == '\\' && i + 1 < arrayContent.length()) {
				i++;
				if (arrayContent[i] == '"')
					value += '"';
				else if (arrayContent[i] == '\\')
					value += '\\';
				else
					value += arrayContent[i];
			} else if (arrayContent[i] == '"') {
				pos = i + 1;
				break;
			} else {
				value += arrayContent[i];
			}
		}

		if (!value.empty()) {
			result.push_back(value);
		}
	}

	return result;
}

std::optional<BuildConfig> parseBuildConfig(const std::string &json) {
	BuildConfig config;

	config.name = extractJsonString(json, "name");
	if (config.name.empty()) {
		std::cerr << "Error: build.maxon output missing 'name' field\n";
		return std::nullopt;
	}

	config.output = extractJsonString(json, "output");
	if (config.output.empty()) {
		// Default to bin/<name>.exe
		config.output = "bin/" + config.name + ".exe";
	}

	config.sources = extractJsonStringArray(json, "sources");
	config.optimize = extractJsonBool(json, "optimize");
	config.debugInfo = extractJsonBool(json, "debug_info");

	return config;
}

CompilationOptions buildConfigToOptions(const BuildConfig &config, const std::string &projectDir) {
	CompilationOptions options;

	// If sources is empty, find all .maxon files in project directory (excluding build.maxon)
	if (config.sources.empty()) {
		for (const auto &entry : std::filesystem::recursive_directory_iterator(projectDir)) {
			if (entry.is_regular_file() && entry.path().extension() == ".maxon") {
				std::string filename = entry.path().filename().string();
				if (filename != "build.maxon") {
					options.inputFiles.push_back(entry.path().string());
				}
			}
		}
	} else {
		// Use specified sources, resolve relative to project directory
		for (const auto &source : config.sources) {
			std::filesystem::path sourcePath = std::filesystem::path(projectDir) / source;
			options.inputFiles.push_back(sourcePath.string());
		}
	}

	// Set output path relative to project directory
	std::filesystem::path outputPath = std::filesystem::path(projectDir) / config.output;
	options.outputFile = outputPath.string();

	options.optimize = config.optimize;
	options.debugInfo = config.debugInfo;
	options.verboseLevel = 0;
	options.emitIR = false;
	options.compileOnly = false;

	return options;
}
