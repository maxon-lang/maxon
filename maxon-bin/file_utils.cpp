#include "file_utils.h"

#include "lexer.h"

#include <algorithm>
#include <fstream>
#include <filesystem>
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

std::string deriveNamespace(const std::string& filePath) {
    std::filesystem::path p(filePath);
    std::string pathStr = p.string();
    size_t stdlibPos = pathStr.find("stdlib");
    if (stdlibPos != std::string::npos) {
        std::string stdlibRelative = pathStr.substr(stdlibPos);
        p = std::filesystem::path(stdlibRelative);
    }

    std::filesystem::path dir = p.parent_path();
    std::string ns = dir.string();

    if (ns.empty() || ns == ".") {
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

std::vector<std::string> findMaxonFiles(const std::string& directory) {
    std::vector<std::string> files;
    if (!std::filesystem::exists(directory)) {
        return files;
    }

    for (const auto& entry : std::filesystem::recursive_directory_iterator(directory)) {
        if (entry.is_regular_file() && entry.path().extension() == ".maxon") {
            files.push_back(entry.path().string());
        }
    }

    return files;
}

std::vector<std::string> extractFunctionNames(const std::string& filePath) {
    std::vector<std::string> functionNames;
    try {
        std::string source = readFile(filePath);
        Lexer lexer(source);
        std::vector<Token> tokens = lexer.tokenize();

        for (size_t i = 0; i < tokens.size(); ++i) {
            if (tokens[i].type == TokenType::FUNCTION && i + 1 < tokens.size()) {
                if (tokens[i + 1].type == TokenType::IDENTIFIER) {
                    functionNames.push_back(tokens[i + 1].value);
                }
            }
        }
    } catch (...) {
        // Ignore parsing issues while building index
    }

    return functionNames;
}

const std::map<std::string, std::string>& getStdlibIndex() {
    static std::map<std::string, std::string> index;
    static bool initialized = false;

    if (!initialized) {
        std::string stdlibDir = findStdlibDirectory();
        std::vector<std::string> stdlibFiles = findMaxonFiles(stdlibDir);

        for (const auto& file : stdlibFiles) {
            std::vector<std::string> definedFunctions = extractFunctionNames(file);
            for (const auto& func : definedFunctions) {
                index[func] = file;
            }
        }

        initialized = true;
    }

    return index;
}

std::vector<std::string> findStdlibFilesDefining(const std::set<std::string>& functionNames) {
    std::vector<std::string> resultFiles;
    std::set<std::string> uniqueFiles;
    const auto& index = getStdlibIndex();

    for (const auto& funcName : functionNames) {
        std::string unqualifiedName = funcName;
        size_t lastSep = funcName.rfind("::");
        if (lastSep != std::string::npos) {
            unqualifiedName = funcName.substr(lastSep + 2);
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

std::string readFile(const std::string& filename) {
    std::ifstream file(filename);
    if (!file) {
        throw std::runtime_error("Could not open file: " + filename);
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
