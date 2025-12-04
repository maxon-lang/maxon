#include "json_rpc.h"
#include <sstream>
#include <cctype>

namespace maxon::lsp {

// Helper to convert JsonRpcId to json
static json idToJson(const JsonRpcId& id) {
    if (std::holds_alternative<std::monostate>(id)) {
        return nullptr;
    } else if (std::holds_alternative<int>(id)) {
        return std::get<int>(id);
    } else {
        return std::get<std::string>(id);
    }
}

// Helper to convert json to JsonRpcId
static JsonRpcId jsonToId(const json& j) {
    if (j.is_null()) {
        return std::monostate{};
    } else if (j.is_number_integer()) {
        return j.get<int>();
    } else if (j.is_string()) {
        return j.get<std::string>();
    }
    return std::monostate{};
}

JsonRpcMessage JsonRpcMessage::createRequest(const JsonRpcId& id, const std::string& method,
                                             const json& params) {
    JsonRpcMessage msg;
    msg.jsonrpc = "2.0";
    msg.id = id;
    msg.method = method;
    if (!params.is_null()) {
        msg.params = params;
    }
    return msg;
}

JsonRpcMessage JsonRpcMessage::createNotification(const std::string& method, const json& params) {
    JsonRpcMessage msg;
    msg.jsonrpc = "2.0";
    msg.method = method;
    if (!params.is_null()) {
        msg.params = params;
    }
    return msg;
}

JsonRpcMessage JsonRpcMessage::createResponse(const JsonRpcId& id, const json& result) {
    JsonRpcMessage msg;
    msg.jsonrpc = "2.0";
    msg.id = id;
    msg.result = result;
    return msg;
}

JsonRpcMessage JsonRpcMessage::createErrorResponse(const JsonRpcId& id, const JsonRpcError& error) {
    JsonRpcMessage msg;
    msg.jsonrpc = "2.0";
    msg.id = id;
    msg.error = error;
    return msg;
}

JsonRpcMessage JsonRpcMessage::createErrorResponse(const JsonRpcId& id, int code,
                                                   const std::string& message) {
    return createErrorResponse(id, JsonRpcError(code, message));
}

ParseResult parseMessage(const std::string& jsonStr) {
    ParseResult result;

    try {
        json j = json::parse(jsonStr);

        // Validate JSON-RPC version
        if (!j.contains("jsonrpc") || j["jsonrpc"] != "2.0") {
            result.error = JsonRpcError(ErrorCode::InvalidRequest,
                                        "Invalid JSON-RPC version or missing jsonrpc field");
            return result;
        }

        JsonRpcMessage msg;
        msg.jsonrpc = "2.0";

        // Parse id if present
        if (j.contains("id")) {
            msg.id = jsonToId(j["id"]);
        }

        // Parse method if present
        if (j.contains("method")) {
            if (!j["method"].is_string()) {
                result.error = JsonRpcError(ErrorCode::InvalidRequest,
                                            "Method must be a string");
                return result;
            }
            msg.method = j["method"].get<std::string>();
        }

        // Parse params if present
        if (j.contains("params")) {
            // Params must be object or array
            if (!j["params"].is_object() && !j["params"].is_array()) {
                result.error = JsonRpcError(ErrorCode::InvalidParams,
                                            "Params must be object or array");
                return result;
            }
            msg.params = j["params"];
        }

        // Parse result if present
        if (j.contains("result")) {
            msg.result = j["result"];
        }

        // Parse error if present
        if (j.contains("error")) {
            const auto& errJson = j["error"];
            if (!errJson.is_object()) {
                result.error = JsonRpcError(ErrorCode::InvalidRequest,
                                            "Error must be an object");
                return result;
            }

            JsonRpcError err;
            if (errJson.contains("code") && errJson["code"].is_number_integer()) {
                err.code = errJson["code"].get<int>();
            }
            if (errJson.contains("message") && errJson["message"].is_string()) {
                err.message = errJson["message"].get<std::string>();
            }
            if (errJson.contains("data")) {
                err.data = errJson["data"];
            }
            msg.error = err;
        }

        // Validate message structure
        bool hasMethod = msg.method.has_value();
        bool hasResult = msg.result.has_value();
        bool hasError = msg.error.has_value();

        // Request/notification must have method
        // Response must have either result or error (not both)
        if (!hasMethod && !hasResult && !hasError) {
            result.error = JsonRpcError(ErrorCode::InvalidRequest,
                                        "Message must have method, result, or error");
            return result;
        }

        if (hasResult && hasError) {
            result.error = JsonRpcError(ErrorCode::InvalidRequest,
                                        "Response cannot have both result and error");
            return result;
        }

        result.message = msg;
    } catch (const json::parse_error& e) {
        result.error = JsonRpcError(ErrorCode::ParseError,
                                    std::string("JSON parse error: ") + e.what());
    } catch (const std::exception& e) {
        result.error = JsonRpcError(ErrorCode::InternalError,
                                    std::string("Internal error: ") + e.what());
    }

    return result;
}

std::string serializeMessage(const JsonRpcMessage& message) {
    json j;
    j["jsonrpc"] = "2.0";

    // Add id if present
    if (message.id.has_value()) {
        j["id"] = idToJson(message.id.value());
    }

    // Add method if present
    if (message.method.has_value()) {
        j["method"] = message.method.value();
    }

    // Add params if present
    if (message.params.has_value()) {
        j["params"] = message.params.value();
    }

    // Add result if present
    if (message.result.has_value()) {
        j["result"] = message.result.value();
    }

    // Add error if present
    if (message.error.has_value()) {
        json errJson;
        errJson["code"] = message.error->code;
        errJson["message"] = message.error->message;
        if (message.error->data.has_value()) {
            errJson["data"] = message.error->data.value();
        }
        j["error"] = errJson;
    }

    return j.dump();
}

std::optional<std::string> readMessage(std::istream& in) {
    std::string line;
    int contentLength = -1;

    // Read headers until we hit an empty line
    while (std::getline(in, line)) {
        // Remove trailing \r if present (for \r\n line endings)
        if (!line.empty() && line.back() == '\r') {
            line.pop_back();
        }

        // Empty line signals end of headers
        if (line.empty()) {
            break;
        }

        // Parse Content-Length header (case-insensitive)
        const std::string prefix = "Content-Length:";
        if (line.length() >= prefix.length()) {
            bool matches = true;
            for (size_t i = 0; i < prefix.length(); ++i) {
                if (std::tolower(static_cast<unsigned char>(line[i])) !=
                    std::tolower(static_cast<unsigned char>(prefix[i]))) {
                    matches = false;
                    break;
                }
            }
            if (matches) {
                // Skip whitespace after colon
                size_t pos = prefix.length();
                while (pos < line.length() && std::isspace(static_cast<unsigned char>(line[pos]))) {
                    ++pos;
                }
                // Parse the number
                try {
                    contentLength = std::stoi(line.substr(pos));
                } catch (...) {
                    // Invalid content length, continue reading
                }
            }
        }
    }

    // Check for EOF or missing Content-Length
    if (in.eof() || contentLength < 0) {
        return std::nullopt;
    }

    // Read the content body
    std::string content(contentLength, '\0');
    if (!in.read(&content[0], contentLength)) {
        return std::nullopt;
    }

    return content;
}

void writeMessage(std::ostream& out, const JsonRpcMessage& message) {
    writeRawMessage(out, serializeMessage(message));
}

void writeRawMessage(std::ostream& out, const std::string& jsonStr) {
    out << "Content-Length: " << jsonStr.length() << "\r\n";
    out << "\r\n";
    out << jsonStr;
    out.flush();
}

} // namespace maxon::lsp
