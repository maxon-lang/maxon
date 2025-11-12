#include "lsp_server.h"
#include <iostream>

#ifdef _WIN32
#include <io.h>
#include <fcntl.h>
#endif

int main(int argc, char** argv) {
#ifdef _WIN32
    // CRITICAL: On Windows, stdin/stdout must be in binary mode for LSP
    // Without this, Windows performs CRLF translation that corrupts the protocol
    _setmode(_fileno(stdin), _O_BINARY);
    _setmode(_fileno(stdout), _O_BINARY);
#endif

    // Log to stderr since stdout is used for LSP communication
    std::cerr << "Maxon LSP Server starting..." << std::endl;
    
    try {
        LspServer server;
        server.run();
    } catch (const std::exception& e) {
        std::cerr << "Fatal error: " << e.what() << std::endl;
        return 1;
    }
    
    return 0;
}
