#pragma once

#include <string>

// Generate TextMate grammar for Maxon language
// outputFile: path to write the .tmLanguage.json file
// Returns 0 on success, 1 on error
int generateGrammar(const std::string &outputFile);
