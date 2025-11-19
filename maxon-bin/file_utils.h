#pragma once

#include <map>
#include <set>
#include <string>
#include <vector>

std::string getExecutableDirectory();
std::string findStdlibDirectory();
std::string deriveNamespace(const std::string& filePath);
std::vector<std::string> findMaxonFiles(const std::string& directory);
std::vector<std::string> extractFunctionNames(const std::string& filePath);
const std::map<std::string, std::string>& getStdlibIndex();
std::vector<std::string> findStdlibFilesDefining(const std::set<std::string>& functionNames);
std::string readFile(const std::string& filename);
