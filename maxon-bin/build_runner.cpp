#include "build_runner.h"
#include "logger.h"

#include <array>
#include <cstdio>
#include <ctime>
#include <filesystem>
#include <fstream>
#include <memory>
#include <sstream>

bool hasBuildMaxon(const std::string &directory) {
	std::filesystem::path buildPath = std::filesystem::path(directory) / "build.maxon";
	return std::filesystem::exists(buildPath);
}

// Parse build.maxon to extract the project name from build("name") call
static std::optional<std::string> extractProjectName(const std::string &buildFile) {
	std::ifstream file(buildFile);
	if (!file.is_open()) {
		return std::nullopt;
	}

	std::string line;
	while (std::getline(file, line)) {
		// Look for build("name") pattern
		size_t buildPos = line.find("build(\"");
		if (buildPos != std::string::npos) {
			size_t nameStart = buildPos + 7; // length of build("
			size_t nameEnd = line.find("\")", nameStart);
			if (nameEnd != std::string::npos) {
				return line.substr(nameStart, nameEnd - nameStart);
			}
		}
	}
	return std::nullopt;
}

// Generate default JSON config for a project name
static std::string generateDefaultConfig(const std::string &projectName) {
	std::ostringstream json;
	json << "{\n";
	json << "  \"name\": \"" << projectName << "\",\n";
	json << "  \"output\": \"bin/" << projectName << ".exe\",\n";
	json << "  \"sources\": [],\n";
	json << "  \"optimize\": false,\n";
	json << "  \"debug_info\": false\n";
	json << "}\n";
	return json.str();
}

std::optional<std::string> executeBuildMaxon(const std::string &buildFile) {
	// Temporarily parse build.maxon directly instead of compiling it
	auto projectName = extractProjectName(buildFile);
	if (!projectName) {
		Logger &logger = GlobalLogger::instance();
		logger.error(LogPhase::Build, "Could not extract project name from build.maxon");
		logger.error(LogPhase::Build, "Expected: build(\"project-name\") call");
		return std::nullopt;
	}

	return generateDefaultConfig(*projectName);
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
		GlobalLogger::instance().error(LogPhase::Build, "build.maxon output missing 'name' field");
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
