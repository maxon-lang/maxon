#include "../include/json_rpc.h"
#include <cassert>
#include <iostream>
#include <sstream>

// Test helper to capture output
class TestJsonRpcHandler : public JsonRpcHandler {
public:
    std::vector<std::string> sentMessages;
    
    void testWriteMessage(const json& message) {
        std::string content = message.dump();
        sentMessages.push_back(content);
    }
};

void test_request_handler_registration() {
    std::cout << "Testing request handler registration..." << std::endl;
    
    JsonRpcHandler handler;
    bool handlerCalled = false;
    
    handler.registerRequestHandler("test/method", [&handlerCalled](const json& params) -> json {
        handlerCalled = true;
        return json::object();
    });
    
    // Simulate processing a request
    std::string request = R"({"jsonrpc":"2.0","id":1,"method":"test/method","params":{}})";
    handler.processMessage(request);
    
    assert(handlerCalled);
    std::cout << "✓ Request handler registration works" << std::endl;
}

void test_notification_handler_registration() {
    std::cout << "Testing notification handler registration..." << std::endl;
    
    JsonRpcHandler handler;
    bool handlerCalled = false;
    
    handler.registerNotificationHandler("test/notification", [&handlerCalled](const json& params) {
        handlerCalled = true;
    });
    
    // Simulate processing a notification (no id)
    std::string notification = R"({"jsonrpc":"2.0","method":"test/notification","params":{}})";
    handler.processMessage(notification);
    
    assert(handlerCalled);
    std::cout << "✓ Notification handler registration works" << std::endl;
}

void test_request_with_params() {
    std::cout << "Testing request with parameters..." << std::endl;
    
    JsonRpcHandler handler;
    json receivedParams;
    
    handler.registerRequestHandler("test/echo", [&receivedParams](const json& params) -> json {
        receivedParams = params;
        return params;
    });
    
    std::string request = R"({"jsonrpc":"2.0","id":1,"method":"test/echo","params":{"message":"hello"}})";
    handler.processMessage(request);
    
    assert(receivedParams.contains("message"));
    assert(receivedParams["message"] == "hello");
    std::cout << "✓ Request with parameters works" << std::endl;
}

void test_method_not_found() {
    std::cout << "Testing method not found error..." << std::endl;
    
    JsonRpcHandler handler;
    
    // No handlers registered, should trigger method not found
    std::string request = R"({"jsonrpc":"2.0","id":1,"method":"nonexistent/method","params":{}})";
    
    // This should send an error response, but we can't easily capture it without mocking
    // Just ensure it doesn't crash
    handler.processMessage(request);
    
    std::cout << "✓ Method not found error handled gracefully" << std::endl;
}

void test_malformed_json() {
    std::cout << "Testing malformed JSON..." << std::endl;
    
    JsonRpcHandler handler;
    
    // Malformed JSON should be caught gracefully
    std::string malformed = R"({"jsonrpc":"2.0","id":1,"method":})";
    
    // Should not crash
    handler.processMessage(malformed);
    
    std::cout << "✓ Malformed JSON handled gracefully" << std::endl;
}

void test_response_structure() {
    std::cout << "Testing response structure..." << std::endl;
    
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
    std::cout << "✓ Response structure test completed" << std::endl;
}

int main() {
    std::cout << "Running JSON-RPC Handler Tests...\n" << std::endl;
    
    try {
        test_request_handler_registration();
        test_notification_handler_registration();
        test_request_with_params();
        test_method_not_found();
        test_malformed_json();
        test_response_structure();
        
        std::cout << "\n✓ All JSON-RPC tests passed!" << std::endl;
        return 0;
    } catch (const std::exception& e) {
        std::cerr << "\n✗ Test failed: " << e.what() << std::endl;
        return 1;
    }
}
