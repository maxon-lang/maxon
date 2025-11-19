#ifndef TEST_HELPERS_H
#define TEST_HELPERS_H

#include <stdexcept>
#include <string>

// Helper to check conditions and throw on failure instead of using assert
// This prevents abort() dialogs on Windows
#define CHECK(condition, message) \
    if (!(condition)) { \
        throw std::runtime_error(std::string("Check failed: ") + message); \
    }

#endif // TEST_HELPERS_H
