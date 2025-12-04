#ifndef MAXON_LSP_TRANSPORT_H
#define MAXON_LSP_TRANSPORT_H

#include "json_rpc.h"
#include <memory>
#include <atomic>

namespace maxon::lsp {

// Abstract transport interface for JSON-RPC communication
class Transport {
public:
    virtual ~Transport() = default;

    // Read the next message from the transport
    // Returns empty optional on EOF or connection closed
    virtual std::optional<JsonRpcMessage> readMessage() = 0;

    // Write a message to the transport
    virtual void writeMessage(const JsonRpcMessage& message) = 0;

    // Check if the transport is still connected/usable
    virtual bool isConnected() const = 0;
};

// Standard I/O transport for LSP communication via stdin/stdout
class StdioTransport : public Transport {
public:
    StdioTransport();
    ~StdioTransport() override;

    std::optional<JsonRpcMessage> readMessage() override;
    void writeMessage(const JsonRpcMessage& message) override;
    bool isConnected() const override;

    // Close the transport (will cause isConnected to return false)
    void close();

private:
    std::atomic<bool> connected_;

    // Platform-specific initialization for binary mode
    void initBinaryMode();
};

} // namespace maxon::lsp

#endif // MAXON_LSP_TRANSPORT_H
