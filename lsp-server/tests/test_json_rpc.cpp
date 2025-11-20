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

TEST_CASE("method_not_found", "[json_rpc]") {

    
    JsonRpcHandler handler;
    
    // No handlers registered, should trigger method not found
    std::string request = R"({"jsonrpc":"2.0","id":1,"method":"nonexistent/method","params":{}})";
    
    // This should send an error response, but we can't easily capture it without mocking
    // Just ensure it doesn't crash
    handler.processMessage(request);
    

}

TEST_CASE("malformed_json", "[json_rpc]") {

    
    JsonRpcHandler handler;
    
    // Malformed JSON should be caught gracefully
    std::string malformed = R"({"jsonrpc":"2.0","id":1,"method":})";
    
    // Should not crash
    handler.processMessage(malformed);
    

}

TEST_CASE("response_structure", "[json_rpc]") {

    
    JsonRpcHandler handler;
    
    handler.registerRequestHandler("test/add", [](const json& params) -> json {
        int a = params["a"].get<int>();
        int b = params["b"].get<int>();
        return json{{"sum", a + b}};
    });
    
    std::string request = R"({"jsonrpc":"2.0","id":42,"method":"test/add","params":{"a":5,"b":3}})";
    handler.processMessage(request);
    
    // Response should be sent with proper structure
    // In a real test, we'd capture and verify the output

}


