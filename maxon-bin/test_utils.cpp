#include "test_utils.h"

#include <cstdio>

std::string normalizeIR(const std::string& ir) {
    std::string normalized = ir;

    size_t pos = 0;
    while ((pos = normalized.find("source_filename = \"", pos)) != std::string::npos) {
        size_t start = pos + 19;
        size_t end = normalized.find("\"", start);
        if (end != std::string::npos) {
            normalized.replace(start, end - start, "test.maxon");
            pos = end + 1;
        } else {
            break;
        }
    }

    pos = 0;
    while ((pos = normalized.find("ModuleID = '", pos)) != std::string::npos) {
        size_t start = pos + 12;
        size_t end = normalized.find("'", start);
        if (end != std::string::npos) {
            normalized.replace(start, end - start, "test.maxon");
            pos = end + 1;
        } else {
            break;
        }
    }

    pos = 0;
    while ((pos = normalized.find("DIFile(filename: \"", pos)) != std::string::npos) {
        size_t start = pos + 18;
        size_t end = normalized.find("\"", start);
        if (end != std::string::npos) {
            normalized.replace(start, end - start, "test.maxon");
            pos = end + 1;
        } else {
            break;
        }
    }

    return normalized;
}

std::string showWithEscapes(const std::string& s, size_t maxLen) {
    std::string result;
    for (size_t i = 0; i < s.length() && result.length() < maxLen; ++i) {
        unsigned char c = s[i];
        if (c == '\n') result += "\\n";
        else if (c == '\r') result += "\\r";
        else if (c == '\t') result += "\\t";
        else if (c == '\\') result += "\\\\";
        else if (c >= 32 && c < 127) result += c;
        else {
            char buf[5];
            sprintf(buf, "\\x%02x", c);
            result += buf;
        }
    }
    if (s.length() > maxLen) {
        result += "...";
    }
    return result;
}
