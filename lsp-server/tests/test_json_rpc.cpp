#include "../include/json_rpc.h"
#include "catch_amalgamated.hpp"
#include <iostream>
#include <sstream>

#define CATCH_CONFIG_MAIN

// Test helper to capture output
class TestJsonRpcHandler : public JsonRpcHandler {
public:
    std::vector<std::string> sentMessages;

    void testWriteMessage(const json& message) {
        std::string content = message.dump();
        sentMessages.push_back(content);
    }
};

TEST_CASE("request_handler_registration", "[json_rpc]") {
    JsonRpcHandler handler;
    bool handlerCalled = false;

    handler.registerRequestHandler("test/method", [&handlerCalled](const json& params) -> json {
        handlerCalled = true;
        return json::object();
    });

    // Simulate processing a request
    std::string request = R"({"jsonrpc":"2.0","id":1,"method":"test/method","params":{}})";
    handler.processMessage(request);

    REQUIRE(handlerCalled);
}

TEST_CASE("notification_handler_registration", "[json_rpc]") {
    JsonRpcHandler handler;
    bool handlerCalled = false;

    handler.registerNotificationHandler("test/notification", [&handlerCalled](const json& params) {
        handlerCalled = true;
    });

    // Simulate processing a notification (no id)
    std::string notification = R"({"jsonrpc":"2.0","method":"test/notification","params":{}})";
    handler.processMessage(notification);

    REQUIRE(handlerCalled);
}

TEST_CASE("request_with_params", "[json_rpc]") {
    JsonRpcHandler handler;
    json receivedParams;

    handler.registerRequestHandler("test/echo", [&receivedParams](const json& params) -> json {
        receivedParams = params;
        return params;
    });

    std::string request = R"({"jsonrpc":"2.0","id":1,"method":"test/echo","params":{"message":"hello"}})";
    handler.processMessage(request);

    REQUIRE(receivedParams.contains("message"));
    REQUIRE(receivedParams["message"] == "hello");
}
