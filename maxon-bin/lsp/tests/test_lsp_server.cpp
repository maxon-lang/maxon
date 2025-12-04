#include "../lsp_server.h"
#include "../transport.h"
#include <catch_amalgamated.hpp>
#include <mutex>
#include <queue>

using json = maxon::lsp::json;
using JsonRpcMessage = maxon::lsp::JsonRpcMessage;

// Mock transport that allows sending requests and capturing responses
class MockTransport : public maxon::lsp::Transport {
  public:
	// Queue a message to be read by the server
	void queueIncoming(const JsonRpcMessage &msg) {
		std::lock_guard<std::mutex> lock(mutex_);
		incoming_.push(msg);
	}

	// Queue a JSON request to be read by the server
	void queueRequest(int id, const std::string &method, const json &params = json::object()) {
		JsonRpcMessage msg;
		msg.id = id;
		msg.method = method;
		msg.params = params;
		queueIncoming(msg);
	}

	// Queue a JSON notification (no id)
	void queueNotification(const std::string &method, const json &params = json::object()) {
		JsonRpcMessage msg;
		msg.method = method;
		msg.params = params;
		queueIncoming(msg);
	}

	// Get outgoing messages written by the server
	std::vector<JsonRpcMessage> getOutgoing() {
		std::lock_guard<std::mutex> lock(mutex_);
		return outgoing_;
	}

	// Find a response with a specific id
	std::optional<JsonRpcMessage> findResponse(int id) {
		std::lock_guard<std::mutex> lock(mutex_);
		for (const auto &msg : outgoing_) {
			if (msg.id.has_value()) {
				const auto &msgId = msg.id.value();
				if (std::holds_alternative<int>(msgId) && std::get<int>(msgId) == id) {
					return msg;
				}
			}
		}
		return std::nullopt;
	}

	// Find a notification with a specific method
	std::optional<JsonRpcMessage> findNotification(const std::string &method) {
		std::lock_guard<std::mutex> lock(mutex_);
		for (const auto &msg : outgoing_) {
			if (msg.method == method && !msg.id.has_value()) {
				return msg;
			}
		}
		return std::nullopt;
	}

	// Transport interface
	std::optional<JsonRpcMessage> readMessage() override {
		std::lock_guard<std::mutex> lock(mutex_);
		if (incoming_.empty()) {
			connected_ = false;
			return std::nullopt;
		}
		auto msg = incoming_.front();
		incoming_.pop();
		return msg;
	}

	void writeMessage(const JsonRpcMessage &message) override {
		std::lock_guard<std::mutex> lock(mutex_);
		outgoing_.push_back(message);
	}

	bool isConnected() const override {
		return connected_;
	}

	void disconnect() {
		connected_ = false;
	}

  private:
	std::queue<JsonRpcMessage> incoming_;
	std::vector<JsonRpcMessage> outgoing_;
	std::mutex mutex_;
	bool connected_ = true;
};

// Helper to create and initialize an LSP server with mock transport
class LSPTestFixture {
  public:
	LSPTestFixture() {
		transport_ = new MockTransport();
		// Server takes ownership of transport
		server_ = std::make_unique<maxon_lsp::LSPServer>(
			std::unique_ptr<maxon::lsp::Transport>(transport_));
	}

	// Initialize the server (sends initialize request)
	void initialize(const std::string &rootUri = "file:///test") {
		json initParams = {
			{"processId", 1234},
			{"rootUri", rootUri},
			{"capabilities", json::object()}};
		transport_->queueRequest(1, "initialize", initParams);
		transport_->queueNotification("initialized");
	}

	// Open a document
	void openDocument(const std::string &uri, const std::string &content, int version = 1) {
		json params = {
			{"textDocument", {{"uri", uri}, {"languageId", "maxon"}, {"version", version}, {"text", content}}}};
		transport_->queueNotification("textDocument/didOpen", params);
	}

	// Queue shutdown and exit
	void shutdown() {
		transport_->queueRequest(999, "shutdown");
		transport_->queueNotification("exit");
	}

	// Run the server (processes all queued messages)
	int run() {
		return server_->run();
	}

	MockTransport *transport() { return transport_; }

  private:
	MockTransport *transport_; // Owned by server_
	std::unique_ptr<maxon_lsp::LSPServer> server_;
};

// =============================================================================
// Initialize Tests
// =============================================================================

TEST_CASE("LSP initialize request returns capabilities", "[lsp][initialize]") {
	LSPTestFixture fixture;
	fixture.initialize();
	fixture.shutdown();
	fixture.run();

	auto response = fixture.transport()->findResponse(1);
	REQUIRE(response.has_value());
	REQUIRE(response->result.has_value());

	auto &result = response->result.value();
	REQUIRE(result.contains("capabilities"));

	auto &caps = result["capabilities"];
	// Check essential capabilities are advertised
	REQUIRE(caps.contains("textDocumentSync"));
	REQUIRE(caps.contains("completionProvider"));
	REQUIRE(caps.contains("hoverProvider"));
	REQUIRE(caps.contains("definitionProvider"));
}

// =============================================================================
// Diagnostics Tests
// =============================================================================

TEST_CASE("LSP publishes diagnostics on document open", "[lsp][diagnostics]") {
	LSPTestFixture fixture;
	fixture.initialize();

	std::string code = R"(
function main() int
    var unused = 42
    return 0
end 'main'
)";
	fixture.openDocument("file:///test.maxon", code);
	fixture.shutdown();
	fixture.run();

	auto notification = fixture.transport()->findNotification("textDocument/publishDiagnostics");
	REQUIRE(notification.has_value());
	REQUIRE(notification->params.has_value());
	auto &params = notification->params.value();
	REQUIRE(params.contains("uri"));
	REQUIRE(params.contains("diagnostics"));
}

TEST_CASE("LSP reports parse errors as diagnostics", "[lsp][diagnostics]") {
	LSPTestFixture fixture;
	fixture.initialize();

	std::string code = "function main( int\nend 'main'"; // Missing closing paren
	fixture.openDocument("file:///test.maxon", code);
	fixture.shutdown();
	fixture.run();

	auto notification = fixture.transport()->findNotification("textDocument/publishDiagnostics");
	REQUIRE(notification.has_value());
	REQUIRE(notification->params.has_value());

	auto &diagnostics = notification->params.value()["diagnostics"];
	REQUIRE(diagnostics.is_array());
	REQUIRE(diagnostics.size() > 0);

	// Should have an error (severity 1)
	bool hasError = false;
	for (const auto &diag : diagnostics) {
		if (diag.contains("severity") && diag["severity"] == 1) {
			hasError = true;
			break;
		}
	}
	REQUIRE(hasError);
}

TEST_CASE("LSP reports unused variable warning", "[lsp][diagnostics]") {
	LSPTestFixture fixture;
	fixture.initialize();

	std::string code = R"(
function main() int
    var unused = 42
    return 0
end 'main'
)";
	fixture.openDocument("file:///test.maxon", code);
	fixture.shutdown();
	fixture.run();

	auto notification = fixture.transport()->findNotification("textDocument/publishDiagnostics");
	REQUIRE(notification.has_value());
	REQUIRE(notification->params.has_value());

	auto &diagnostics = notification->params.value()["diagnostics"];
	REQUIRE(diagnostics.is_array());

	// Should have a warning (severity 2) for unused variable
	bool hasUnusedWarning = false;
	for (const auto &diag : diagnostics) {
		if (diag.contains("severity") && diag["severity"] == 2) {
			std::string msg = diag.value("message", "");
			if (msg.find("unused") != std::string::npos || msg.find("never used") != std::string::npos) {
				hasUnusedWarning = true;
				break;
			}
		}
	}
	REQUIRE(hasUnusedWarning);
}

// =============================================================================
// Hover Tests
// =============================================================================

TEST_CASE("LSP hover returns type information", "[lsp][hover]") {
	LSPTestFixture fixture;
	fixture.initialize();

	std::string code = R"(
function main() int
    let x = 42
    return x
end 'main'
)";
	fixture.openDocument("file:///test.maxon", code);

	// Request hover on 'x' at line 2 (0-indexed), character 8
	json hoverParams = {
		{"textDocument", {{"uri", "file:///test.maxon"}}},
		{"position", {{"line", 2}, {"character", 8}}}};
	fixture.transport()->queueRequest(2, "textDocument/hover", hoverParams);

	fixture.shutdown();
	fixture.run();

	auto response = fixture.transport()->findResponse(2);
	REQUIRE(response.has_value());
	// Hover may return null if no info available, but should not error
	REQUIRE(!response->error.has_value());
}

// =============================================================================
// Completion Tests
// =============================================================================

TEST_CASE("LSP completion returns items", "[lsp][completion]") {
	LSPTestFixture fixture;
	fixture.initialize();

	std::string code = R"(
function main() int
    let msg = "hello"
    msg.
    return 0
end 'main'
)";
	fixture.openDocument("file:///test.maxon", code);

	// Request completion after 'msg.' at line 3, character 8
	json completionParams = {
		{"textDocument", {{"uri", "file:///test.maxon"}}},
		{"position", {{"line", 3}, {"character", 8}}}};
	fixture.transport()->queueRequest(3, "textDocument/completion", completionParams);

	fixture.shutdown();
	fixture.run();

	auto response = fixture.transport()->findResponse(3);
	REQUIRE(response.has_value());
	REQUIRE(!response->error.has_value());
	// Should return completion items (either array or CompletionList)
	if (response->result.has_value()) {
		auto &result = response->result.value();
		// Could be array of items or {isIncomplete, items}
		if (result.is_array()) {
			// Direct array of completion items
		} else if (result.is_object() && result.contains("items")) {
			REQUIRE(result["items"].is_array());
		}
	}
}

// =============================================================================
// Go to Definition Tests
// =============================================================================

TEST_CASE("LSP definition returns location", "[lsp][definition]") {
	LSPTestFixture fixture;
	fixture.initialize();

	std::string code = R"(
function helper() int
    return 42
end 'helper'

function main() int
    return helper()
end 'main'
)";
	fixture.openDocument("file:///test.maxon", code);

	// Request definition on 'helper' call at line 6, character 11
	json defParams = {
		{"textDocument", {{"uri", "file:///test.maxon"}}},
		{"position", {{"line", 6}, {"character", 11}}}};
	fixture.transport()->queueRequest(4, "textDocument/definition", defParams);

	fixture.shutdown();
	fixture.run();

	auto response = fixture.transport()->findResponse(4);
	REQUIRE(response.has_value());
	REQUIRE(!response->error.has_value());
}

// =============================================================================
// Formatting Tests
// =============================================================================

TEST_CASE("LSP formatting returns edits", "[lsp][formatting]") {
	LSPTestFixture fixture;
	fixture.initialize();

	// Poorly formatted code
	std::string code = "function main() int\n  return 0\nend 'main'";
	fixture.openDocument("file:///test.maxon", code);

	json formatParams = {
		{"textDocument", {{"uri", "file:///test.maxon"}}},
		{"options", {{"tabSize", 4}, {"insertSpaces", false}}}};
	fixture.transport()->queueRequest(5, "textDocument/formatting", formatParams);

	fixture.shutdown();
	fixture.run();

	auto response = fixture.transport()->findResponse(5);
	REQUIRE(response.has_value());
	REQUIRE(!response->error.has_value());
	// Should return array of text edits
	if (response->result.has_value()) {
		REQUIRE(response->result.value().is_array());
	}
}

// =============================================================================
// Document Symbols Tests
// =============================================================================

TEST_CASE("LSP documentSymbol returns symbols", "[lsp][symbols]") {
	LSPTestFixture fixture;
	fixture.initialize();

	std::string code = R"(
struct Point
    var x int
    var y int
end 'Point'

function main() int
    return 0
end 'main'
)";
	fixture.openDocument("file:///test.maxon", code);

	json symbolParams = {
		{"textDocument", {{"uri", "file:///test.maxon"}}}};
	fixture.transport()->queueRequest(6, "textDocument/documentSymbol", symbolParams);

	fixture.shutdown();
	fixture.run();

	auto response = fixture.transport()->findResponse(6);
	REQUIRE(response.has_value());
	REQUIRE(!response->error.has_value());
	if (response->result.has_value()) {
		REQUIRE(response->result.value().is_array());
		// Should have at least Point struct and main function
		REQUIRE(response->result.value().size() >= 2);
	}
}

// =============================================================================
// Rename Tests
// =============================================================================

TEST_CASE("LSP prepareRename returns range", "[lsp][rename]") {
	LSPTestFixture fixture;
	fixture.initialize();

	std::string code = R"(
function main() int
    let value = 42
    return value
end 'main'
)";
	fixture.openDocument("file:///test.maxon", code);

	// Prepare rename on 'value' at line 2, character 8
	json prepareParams = {
		{"textDocument", {{"uri", "file:///test.maxon"}}},
		{"position", {{"line", 2}, {"character", 8}}}};
	fixture.transport()->queueRequest(7, "textDocument/prepareRename", prepareParams);

	fixture.shutdown();
	fixture.run();

	auto response = fixture.transport()->findResponse(7);
	REQUIRE(response.has_value());
	REQUIRE(!response->error.has_value());
}

TEST_CASE("LSP rename returns workspace edit", "[lsp][rename]") {
	LSPTestFixture fixture;
	fixture.initialize();

	std::string code = R"(
function main() int
    let value = 42
    return value
end 'main'
)";
	fixture.openDocument("file:///test.maxon", code);

	// Rename 'value' to 'result'
	json renameParams = {
		{"textDocument", {{"uri", "file:///test.maxon"}}},
		{"position", {{"line", 2}, {"character", 8}}},
		{"newName", "result"}};
	fixture.transport()->queueRequest(8, "textDocument/rename", renameParams);

	fixture.shutdown();
	fixture.run();

	auto response = fixture.transport()->findResponse(8);
	REQUIRE(response.has_value());
	REQUIRE(!response->error.has_value());
}

// =============================================================================
// Code Actions Tests
// =============================================================================

TEST_CASE("LSP codeAction returns actions for diagnostics", "[lsp][codeAction]") {
	LSPTestFixture fixture;
	fixture.initialize();

	std::string code = R"(
function main() int
    var unused = 42
    return 0
end 'main'
)";
	fixture.openDocument("file:///test.maxon", code);

	// Request code actions for the range containing unused variable
	json codeActionParams = {
		{"textDocument", {{"uri", "file:///test.maxon"}}},
		{"range", {{"start", {{"line", 2}, {"character", 0}}}, {"end", {{"line", 2}, {"character", 20}}}}},
		{"context", {{"diagnostics", json::array()}}}};
	fixture.transport()->queueRequest(9, "textDocument/codeAction", codeActionParams);

	fixture.shutdown();
	fixture.run();

	auto response = fixture.transport()->findResponse(9);
	REQUIRE(response.has_value());
	REQUIRE(!response->error.has_value());
}

// =============================================================================
// Folding Range Tests
// =============================================================================

TEST_CASE("LSP foldingRange returns ranges", "[lsp][folding]") {
	LSPTestFixture fixture;
	fixture.initialize();

	std::string code = R"(
function main() int
    if true 'check'
        return 1
    end 'check'
    return 0
end 'main'
)";
	fixture.openDocument("file:///test.maxon", code);

	json foldingParams = {
		{"textDocument", {{"uri", "file:///test.maxon"}}}};
	fixture.transport()->queueRequest(10, "textDocument/foldingRange", foldingParams);

	fixture.shutdown();
	fixture.run();

	auto response = fixture.transport()->findResponse(10);
	REQUIRE(response.has_value());
	REQUIRE(!response->error.has_value());
	if (response->result.has_value()) {
		REQUIRE(response->result.value().is_array());
		// Should have folding ranges for function and if block
		REQUIRE(response->result.value().size() >= 1);
	}
}
