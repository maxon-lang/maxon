#include "file_utils.h"

#include "lexer.h"

#include <algorithm>
#include <filesystem>
#include <fstream>
#include <sstream>
#include <stdexcept>

#ifdef _WIN32
#include <windows.h>
#else
#include <linux/limits.h>
#include <unistd.h>
#endif

std::string getExecutableDirectory() {
#ifdef _WIN32
	char buffer[MAX_PATH];
	GetModuleFileNameA(NULL, buffer, MAX_PATH);
	std::string execPath(buffer);
	size_t pos = execPath.find_last_of("\\/");
	return (pos != std::string::npos) ? execPath.substr(0, pos) : ".";
#else
	char buffer[PATH_MAX];
	ssize_t len = readlink("/proc/self/exe", buffer, sizeof(buffer) - 1);
	if (len != -1) {
		buffer[len] = '\0';
		std::string execPath(buffer);
		size_t pos = execPath.find_last_of('/');
		return (pos != std::string::npos) ? execPath.substr(0, pos) : ".";
	}
	return ".";
#endif
}

std::string findStdlibDirectory() {
	if (std::filesystem::exists("stdlib")) {
		return "stdlib";
	}

	std::string execDir = getExecutableDirectory();
	std::filesystem::path stdlibPath = std::filesystem::path(execDir) / ".." / "stdlib";
	if (std::filesystem::exists(stdlibPath)) {
		return stdlibPath.string();
	}

	stdlibPath = std::filesystem::path(execDir) / ".." / ".." / "stdlib";
	if (std::filesystem::exists(stdlibPath)) {
		return stdlibPath.string();
	}

	return "stdlib";
}

std::string deriveNamespace(const std::string &filePath) {
	std::filesystem::path p(filePath);
	std::string pathStr = p.string();

	// Skip "temp" directory (used for temporary files)
	size_t tempPos = pathStr.find("temp");
	if (tempPos != std::string::npos) {
		// Check if "temp" is preceded by a separator (to avoid matching "temporary" etc)
		bool isTemp = false;
		if (tempPos == 0 || pathStr[tempPos - 1] == '/' || pathStr[tempPos - 1] == '\\') {
			// Check if "temp" is followed by a separator or end of string
			size_t tempEnd = tempPos + 4; // "temp" is 4 chars
			if (tempEnd >= pathStr.size() || pathStr[tempEnd] == '/' || pathStr[tempEnd] == '\\') {
				isTemp = true;
			}
		}
		if (isTemp) {
			// Skip to after the "temp" directory
			size_t startPos = tempPos + 4; // "temp" is 4 chars
			if (startPos < pathStr.size() && (pathStr[startPos] == '/' || pathStr[startPos] == '\\')) {
				startPos++; // skip the separator
			}
			std::string tempRelative = pathStr.substr(startPos);
			p = std::filesystem::path(tempRelative);
			// Return empty namespace for temp files
			if (tempRelative.empty() || tempRelative == "." || p.parent_path().string().empty()) {
				return "";
			}
		}
	}

	size_t stdlibPos = pathStr.find("stdlib");
	if (stdlibPos != std::string::npos) {
		// Skip "stdlib" and the following separator to get the namespace within stdlib
		size_t startPos = stdlibPos + 7; // "stdlib" is 6 chars + 1 for separator
		if (startPos < pathStr.size() && (pathStr[startPos] == '/' || pathStr[startPos] == '\\')) {
			startPos++; // skip the separator
		}
		std::string stdlibRelative = pathStr.substr(startPos);
		p = std::filesystem::path(stdlibRelative);
	}

	std::filesystem::path dir = p.parent_path();
	std::string ns = dir.string();

	// Check for empty or root directory before any processing
	if (ns.empty() || ns == "." || ns == "/" || ns == "\\") {
		return "";
	}

	std::replace(ns.begin(), ns.end(), '/', '.');
	std::replace(ns.begin(), ns.end(), '\\', '.');

	std::string result;
	for (size_t i = 0; i < ns.size(); ++i) {
		if (ns[i] == '.') {
			// Skip consecutive dots
			if (result.empty() || result.back() != '.') {
				result += '.';
			}
			while (i + 1 < ns.size() && (ns[i + 1] == '.' || ns[i + 1] == '/' || ns[i + 1] == '\\')) {
				++i;
			}
		} else {
			result += ns[i];
		}
	}

	return result;
}

std::vector<std::string> findMaxonFiles(const std::string &directory) {
	std::vector<std::string> files;
	if (!std::filesystem::exists(directory)) {
		return files;
	}

	for (const auto &entry : std::filesystem::recursive_directory_iterator(directory)) {
		if (entry.is_regular_file() && entry.path().extension() == ".maxon") {
			files.push_back(entry.path().string());
		}
	}

	return files;
}

std::vector<std::string> extractFunctionNames(const std::string &filePath) {
	std::vector<std::string> functionNames;
	try {
		std::string source = readFile(filePath);
		std::vector<Token> tokens = tokenize(source);

		for (size_t i = 0; i < tokens.size(); ++i) {
			if (tokens[i].type == TokenType::KEYWORD && tokens[i].value == "function" && i + 1 < tokens.size()) {
				if (tokens[i + 1].type == TokenType::IDENTIFIER) {
					std::string funcName = tokens[i + 1].value;

					// Check for method syntax: function Type.method(...)
					// If next tokens are DOT and IDENTIFIER, this is a method
					if (i + 3 < tokens.size() &&
						tokens[i + 2].type == TokenType::DOT &&
						tokens[i + 3].type == TokenType::IDENTIFIER) {
						// Register as "Type.method" for method-style lookup
						functionNames.push_back(funcName + "." + tokens[i + 3].value);
						// Also register just the method name for compatibility
						functionNames.push_back(tokens[i + 3].value);
					} else {
						// Regular function
						functionNames.push_back(funcName);
					}
				}
			}
		}
	} catch (...) {
		// Ignore parsing issues while building index
	}

	return functionNames;
}

std::vector<std::string> extractStructNames(const std::string &filePath) {
	std::vector<std::string> structNames;
	try {
		std::string source = readFile(filePath);
		std::vector<Token> tokens = tokenize(source);

		for (size_t i = 0; i < tokens.size(); ++i) {
			if (tokens[i].type == TokenType::KEYWORD && tokens[i].value == "struct" && i + 1 < tokens.size()) {
				// Accept both IDENTIFIER and KEYWORD as struct names (e.g., "array" is a keyword but valid struct name)
				if (tokens[i + 1].type == TokenType::IDENTIFIER || tokens[i + 1].type == TokenType::KEYWORD) {
					structNames.push_back(tokens[i + 1].value);
				}
			}
		}
	} catch (...) {
		// Ignore parsing issues while building index
	}

	return structNames;
}

const std::map<std::string, std::string> &getStdlibIndex() {
	static std::map<std::string, std::string> index;
	static bool initialized = false;

	if (!initialized) {
		std::string stdlibDir = findStdlibDirectory();
		std::vector<std::string> stdlibFiles = findMaxonFiles(stdlibDir);

		for (const auto &file : stdlibFiles) {
			std::vector<std::string> definedFunctions = extractFunctionNames(file);
			for (const auto &func : definedFunctions) {
				index[func] = file;
			}
		}

		initialized = true;
	}

	return index;
}

std::vector<std::string> findStdlibFilesDefining(const std::set<std::string> &functionNames) {
	std::vector<std::string> resultFiles;
	std::set<std::string> uniqueFiles;
	const auto &index = getStdlibIndex();

	for (const auto &funcName : functionNames) {
		std::string unqualifiedName = funcName;
		size_t lastSep = funcName.rfind(".");
		if (lastSep != std::string::npos) {
			unqualifiedName = funcName.substr(lastSep + 1);
		}

		auto it = index.find(unqualifiedName);
		if (it != index.end()) {
			if (uniqueFiles.insert(it->second).second) {
				resultFiles.push_back(it->second);
			}
		}
	}

	return resultFiles;
}

const std::map<std::string, std::string> &getStdlibStructIndex() {
	static std::map<std::string, std::string> index;
	static bool initialized = false;

	if (!initialized) {
		std::string stdlibDir = findStdlibDirectory();
		std::vector<std::string> stdlibFiles = findMaxonFiles(stdlibDir);

		for (const auto &file : stdlibFiles) {
			std::vector<std::string> definedStructs = extractStructNames(file);
			for (const auto &structName : definedStructs) {
				index[structName] = file;
			}
		}

		initialized = true;
	}

	return index;
}

std::vector<std::string> findStdlibFilesDefiningStructs(const std::set<std::string> &structNames) {
	std::vector<std::string> resultFiles;
	std::set<std::string> uniqueFiles;
	const auto &index = getStdlibStructIndex();

	for (const auto &structName : structNames) {
		auto it = index.find(structName);
		if (it != index.end()) {
			if (uniqueFiles.insert(it->second).second) {
				resultFiles.push_back(it->second);
			}
		}
	}

	return resultFiles;
}

std::vector<std::string> extractInterfaceNames(const std::string &filePath) {
	std::vector<std::string> interfaceNames;
	try {
		std::string source = readFile(filePath);
		std::vector<Token> tokens = tokenize(source);

		for (size_t i = 0; i < tokens.size(); ++i) {
			if (tokens[i].type == TokenType::KEYWORD && tokens[i].value == "interface" && i + 1 < tokens.size()) {
				if (tokens[i + 1].type == TokenType::IDENTIFIER) {
					interfaceNames.push_back(tokens[i + 1].value);
				}
			}
		}
	} catch (...) {
		// Ignore parsing issues while building index
	}

	return interfaceNames;
}

const std::map<std::string, std::string> &getStdlibInterfaceIndex() {
	static std::map<std::string, std::string> index;
	static bool initialized = false;

	if (!initialized) {
		std::string stdlibDir = findStdlibDirectory();
		std::vector<std::string> stdlibFiles = findMaxonFiles(stdlibDir);

		for (const auto &file : stdlibFiles) {
			std::vector<std::string> definedInterfaces = extractInterfaceNames(file);
			for (const auto &proto : definedInterfaces) {
				index[proto] = file;
			}
		}

		initialized = true;
	}

	return index;
}

std::vector<std::string> findStdlibFilesDefiningInterfaces(const std::set<std::string> &interfaceNames) {
	std::vector<std::string> resultFiles;
	std::set<std::string> uniqueFiles;
	const auto &index = getStdlibInterfaceIndex();

	for (const auto &protoName : interfaceNames) {
		auto it = index.find(protoName);
		if (it != index.end()) {
			if (uniqueFiles.insert(it->second).second) {
				resultFiles.push_back(it->second);
			}
		}
	}

	return resultFiles;
}

// Reverse index: file -> set of functions defined in that file
static std::map<std::string, std::set<std::string>> &getStdlibFileFunctionsIndex() {
	static std::map<std::string, std::set<std::string>> index;
	static bool initialized = false;

	if (!initialized) {
		std::string stdlibDir = findStdlibDirectory();
		std::vector<std::string> stdlibFiles = findMaxonFiles(stdlibDir);

		for (const auto &file : stdlibFiles) {
			std::vector<std::string> definedFunctions = extractFunctionNames(file);
			for (const auto &func : definedFunctions) {
				index[file].insert(func);
			}
		}

		initialized = true;
	}

	return index;
}

const std::set<std::string> &getStdlibFunctionsInFile(const std::string &filePath) {
	static const std::set<std::string> emptySet;
	const auto &index = getStdlibFileFunctionsIndex();
	auto it = index.find(filePath);
	if (it != index.end()) {
		return it->second;
	}
	return emptySet;
}

std::vector<std::string> findStdlibFilesForFunctions(const std::set<std::string> &neededFunctions,
                                                      const std::set<std::string> &alreadyDefinedFunctions) {
	std::vector<std::string> resultFiles;
	std::set<std::string> uniqueFiles;
	const auto &index = getStdlibIndex();

	for (const auto &funcName : neededFunctions) {
		// Skip if already defined
		if (alreadyDefinedFunctions.count(funcName) > 0) {
			continue;
		}

		// Get unqualified name for lookup
		std::string unqualifiedName = funcName;
		size_t lastSep = funcName.rfind(".");
		if (lastSep != std::string::npos) {
			unqualifiedName = funcName.substr(lastSep + 1);
		}

		auto it = index.find(unqualifiedName);
		if (it != index.end()) {
			if (uniqueFiles.insert(it->second).second) {
				resultFiles.push_back(it->second);
			}
		}
	}

	return resultFiles;
}

std::string readFile(const std::string &filename) {
	std::ifstream file(filename);
	if (!file) {
		throw std::runtime_error("Could not open file: " + normalizePathForDisplay(filename));
	}

	std::stringstream buffer;
	std::string line;

	while (std::getline(file, line)) {
		if (line == "---") {
			break;
		}
		buffer << line << '\n';
	}

	return buffer.str();
}

std::string normalizePathForDisplay(const std::string &path) {
	std::string result = path;
	std::replace(result.begin(), result.end(), '\\', '/');
	return result;
}
