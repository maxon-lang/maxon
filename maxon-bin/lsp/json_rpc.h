#ifndef MAXON_LSP_JSON_RPC_H
#define MAXON_LSP_JSON_RPC_H

#include <iostream>
#include <nlohmann/json.hpp>
#include <optional>
#include <string>
#include <variant>

namespace maxon::lsp {

using json = nlohmann::json;

// JSON-RPC 2.0 standard error codes
namespace ErrorCode {
constexpr int ParseError = -32700;
constexpr int InvalidRequest = -32600;
constexpr int MethodNotFound = -32601;
constexpr int InvalidParams = -32602;
constexpr int InternalError = -32603;

// Server error range: -32099 to -32000
constexpr int ServerErrorStart = -32099;
constexpr int ServerErrorEnd = -32000;
} // namespace ErrorCode

// JSON-RPC error object
struct JsonRpcError {
	int code;
	std::string message;
	std::optional<json> data;

	JsonRpcError() : code(0) {}
	JsonRpcError(int c, const std::string &m) : code(c), message(m) {}
	JsonRpcError(int c, const std::string &m, const json &d) : code(c), message(m), data(d) {}
};

// ID can be integer, string, or null (for notifications)
using JsonRpcId = std::variant<std::monostate, int, std::string>;

// JSON-RPC 2.0 message (unified request/response/notification)
struct JsonRpcMessage {
	std::string jsonrpc; // Should always be "2.0"
	std::optional<JsonRpcId> id;
	std::optional<std::string> method;
	std::optional<json> params;
	std::optional<json> result;
	std::optional<JsonRpcError> error;

	// Helper methods to determine message type
	bool isRequest() const {
		return method.has_value() && id.has_value() &&
			   !std::holds_alternative<std::monostate>(id.value());
	}

	bool isNotification() const {
		return method.has_value() && (!id.has_value() ||
									  std::holds_alternative<std::monostate>(id.value()));
	}

	bool isResponse() const {
		return !method.has_value() && id.has_value() &&
			   (result.has_value() || error.has_value());
	}

	bool isErrorResponse() const {
		return isResponse() && error.has_value();
	}

	// Factory methods for creating messages
	static JsonRpcMessage createRequest(const JsonRpcId &id, const std::string &method,
										const json &params = json::object());
	static JsonRpcMessage createNotification(const std::string &method,
											 const json &params = json::object());
	static JsonRpcMessage createResponse(const JsonRpcId &id, const json &result);
	static JsonRpcMessage createErrorResponse(const JsonRpcId &id, const JsonRpcError &error);
	static JsonRpcMessage createErrorResponse(const JsonRpcId &id, int code,
											  const std::string &message);
};

// Parse error result
struct ParseResult {
	std::optional<JsonRpcMessage> message;
	std::optional<JsonRpcError> error;

	bool hasError() const { return error.has_value(); }
	bool hasMessage() const { return message.has_value(); }
};

// Parse a JSON string into a JsonRpcMessage
ParseResult parseMessage(const std::string &jsonStr);

// Serialize a JsonRpcMessage to a JSON string
std::string serializeMessage(const JsonRpcMessage &message);

// Read a message from a stream (handles Content-Length header)
// Returns empty optional on EOF or error
std::optional<std::string> readMessage(std::istream &in);

// Write a message to a stream (adds Content-Length header)
void writeMessage(std::ostream &out, const JsonRpcMessage &message);

// Write raw JSON string with Content-Length header
void writeRawMessage(std::ostream &out, const std::string &jsonStr);

} // namespace maxon::lsp

#endif // MAXON_LSP_JSON_RPC_H
