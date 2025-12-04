#ifndef JSON_RPC_H
#define JSON_RPC_H

#include "json.hpp"
#include <string>
#include <functional>
#include <map>
#include <iostream>
#include <atomic>

using json = nlohmann::json;

class JsonRpcHandler {
public:
    using RequestHandler = std::function<json(const json&)>;
    using NotificationHandler = std::function<void(const json&)>;

    JsonRpcHandler();

    // Register handlers
    void registerRequestHandler(const std::string& method, RequestHandler handler);
    void registerNotificationHandler(const std::string& method, NotificationHandler handler);

    // Process incoming message
    void processMessage(const std::string& message);

    // Send response
    void sendResponse(const json& id, const json& result);
    void sendError(const json& id, int code, const std::string& message);
    void sendNotification(const std::string& method, const json& params);

    // Send a request to the client (returns the request ID)
    int sendRequest(const std::string& method, const json& params);

private:
    std::map<std::string, RequestHandler> requestHandlers;
    std::map<std::string, NotificationHandler> notificationHandlers;
    std::atomic<int> nextRequestId{1};

    void writeMessage(const json& message);
    std::string readMessage();
};

#endif // JSON_RPC_H
