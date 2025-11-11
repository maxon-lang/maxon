#ifndef ERROR_FORMATTER_H
#define ERROR_FORMATTER_H

#include <string>
#include <vector>

// Utility class for formatting compiler errors with source context
class ErrorFormatter {
public:
    // Format an error message with source code context
    // Shows the relevant line with a caret (^) pointing to the error location
    static std::string formatError(
        const std::string& errorMessage,
        const std::string& sourceCode,
        int line,
        int column,
        const std::string& errorType = "Error"
    );
    
    // Get a specific line from source code (1-indexed)
    static std::string getSourceLine(const std::string& sourceCode, int lineNumber);
    
    // Split source code into lines
    static std::vector<std::string> splitLines(const std::string& sourceCode);
    
    // Create a caret line pointing to the error column
    static std::string createCaretLine(int column, int length = 1);
};

#endif // ERROR_FORMATTER_H
