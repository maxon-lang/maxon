#pragma once

#include <string>

// Compile and run a Maxon source file in a temporary executable
int compileAndRunTemporary(const std::string& sourceFile);

// Compile and run Maxon source code from stdin in a temporary executable
int compileAndRunFromStdin();
