#include "json_rpc.h"
#include <sstream>
#include <cstring>
#include <fstream>

JsonRpcHandler::JsonRpcHandler() {}

void JsonRpcHandler::registerRequestHandler(const std::string& method, RequestHandler handler) {
    requestHandlers[method] = handler;
}

void JsonRpcHandler::registerNotificationHandler(const std::string& method, NotificationHandler handler) {
    notificationHandlers[method] = handler;
}

void JsonRpcHandler::processMessage(const std::string& message) {
    try {
        json msg = json::parse(message);
        
        std::string method = msg["method"].get<std::string>();
        json params = msg.contains("params") ? msg["params"] : json::object();
        
        if (msg.contains("id")) {
            // Request
            json id = msg["id"];
            auto it = requestHandlers.find(method);
            if (it != requestHandlers.end()) {
                try {
                    json result = it->second(params);
                    sendResponse(id, result);
                } catch (const std::exception& e) {
                    sendError(id, -32603, std::string("Internal error: ") + e.what());
                }
            } else {
                sendError(id, -32601, "Method not found: " + method);
            }
        } else {
            // Notification
            auto it = notificationHandlers.find(method);
            if (it != notificationHandlers.end()) {
                it->second(params);
            }
        }
    } catch (const std::exception& e) {
        std::cerr << "Error processing message: " << e.what() << std::endl;
    }
}

void JsonRpcHandler::sendResponse(const json& id, const json& result) {
    json response = {
        {"jsonrpc", "2.0"},
        {"id", id},
        {"result", result}
    };
    writeMessage(response);
}

void JsonRpcHandler::sendError(const json& id, int code, const std::string& message) {
    json response = {
        {"jsonrpc", "2.0"},
        {"id", id},
        {"error", {
            {"code", code},
            {"message", message}
        }}
    };
    writeMessage(response);
}

void JsonRpcHandler::sendNotification(const std::string& method, const json& params) {
    json notification = {
        {"jsonrpc", "2.0"},
        {"method", method},
        {"params", params}
    };
    writeMessage(notification);
}

int JsonRpcHandler::sendRequest(const std::string& method, const json& params) {
    int id = nextRequestId++;
    json request = {
        {"jsonrpc", "2.0"},
        {"id", id},
        {"method", method},
        {"params", params}
    };
    writeMessage(request);
    return id;
}

void JsonRpcHandler::writeMessage(const json& message) {
    std::string content = message.dump();
    std::string header = "Content-Length: " + std::to_string(content.length()) + "\r\n\r\n";
    
    // Send to stdout
    std::cout << header << content;
    std::cout.flush();
}

std::string JsonRpcHandler::readMessage() {
    std::string line;
    int contentLength = 0;
    
    // Read headers
    while (std::getline(std::cin, line)) {
        if (line == "\r" || line.empty()) {
            break;
        }
        
        if (line.find("Content-Length:") == 0) {
            contentLength = std::stoi(line.substr(16));
        }
    }
    
    // Read content
    if (contentLength > 0) {
        std::string content(contentLength, '\0');
        std::cin.read(&content[0], contentLength);
        return content;
    }
    
    return "";
}
