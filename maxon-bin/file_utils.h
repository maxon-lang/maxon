#pragma once

#include <map>
#include <set>
#include <string>
#include <vector>

std::string getExecutableDirectory();
std::string findStdlibDirectory();
std::string deriveNamespace(const std::string &filePath);
std::vector<std::string> findMaxonFiles(const std::string &directory);
std::vector<std::string> extractFunctionNames(const std::string &filePath);
std::vector<std::string> extractStructNames(const std::string &filePath);
std::vector<std::string> extractInterfaceNames(const std::string &filePath);
const std::map<std::string, std::string> &getStdlibIndex();
const std::map<std::string, std::string> &getStdlibStructIndex();
const std::map<std::string, std::string> &getStdlibInterfaceIndex();
std::vector<std::string> findStdlibFilesDefining(const std::set<std::string> &functionNames);
std::vector<std::string> findStdlibFilesDefiningStructs(const std::set<std::string> &structNames);
std::vector<std::string> findStdlibFilesDefiningInterfaces(const std::set<std::string> &interfaceNames);
std::string readFile(const std::string &filename);
std::string normalizePathForDisplay(const std::string &path);

// Get all functions defined in a specific stdlib file
const std::set<std::string> &getStdlibFunctionsInFile(const std::string &filePath);

// Find stdlib files that define any of the given functions
// Returns only files containing at least one needed function
std::vector<std::string> findStdlibFilesForFunctions(const std::set<std::string> &neededFunctions,
                                                      const std::set<std::string> &alreadyDefinedFunctions);
