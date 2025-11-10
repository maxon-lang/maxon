#include "lsp_server.h"
#include <iostream>

int main(int argc, char** argv) {
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
