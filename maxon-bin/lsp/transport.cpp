#include "transport.h"
#include <iostream>

#ifdef _WIN32
#include <io.h>
#include <fcntl.h>
#endif

namespace maxon::lsp {

StdioTransport::StdioTransport() : connected_(true) {
    initBinaryMode();
}

StdioTransport::~StdioTransport() {
    close();
}

void StdioTransport::initBinaryMode() {
    // On Windows, we need to set stdin/stdout to binary mode to prevent
    // automatic CR/LF translation which corrupts the Content-Length framing
#ifdef _WIN32
    _setmode(_fileno(stdin), _O_BINARY);
    _setmode(_fileno(stdout), _O_BINARY);
#endif
}

std::optional<JsonRpcMessage> StdioTransport::readMessage() {
    if (!connected_) {
        return std::nullopt;
    }

    // Read the raw message content using the json_rpc helper
    auto content = maxon::lsp::readMessage(std::cin);
    if (!content.has_value()) {
        // EOF or read error
        connected_ = false;
        return std::nullopt;
    }

    // Parse the JSON-RPC message
    auto parseResult = parseMessage(content.value());
    if (parseResult.hasError()) {
        // Return an error response as a message so the caller can handle it
        // For parse errors, we create a message with the error set
        JsonRpcMessage errorMsg;
        errorMsg.jsonrpc = "2.0";
        errorMsg.error = parseResult.error;
        // ID is null for parse errors since we couldn't parse the request
        errorMsg.id = std::monostate{};
        return errorMsg;
    }

    return parseResult.message;
}

void StdioTransport::writeMessage(const JsonRpcMessage& message) {
    if (!connected_) {
        return;
    }

    maxon::lsp::writeMessage(std::cout, message);
}

bool StdioTransport::isConnected() const {
    return connected_ && !std::cin.eof();
}

void StdioTransport::close() {
    connected_ = false;
}

} // namespace maxon::lsp
