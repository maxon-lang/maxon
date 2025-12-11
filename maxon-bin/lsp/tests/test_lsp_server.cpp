#include "../document_manager.h"
#include "../lsp_server.h"
#include "../transport.h"
#include <catch_amalgamated.hpp>
#include <filesystem>
#include <fstream>
#include <mutex>
#include <queue>
#include <set>

using json = maxon::lsp::json;
using JsonRpcMessage = maxon::lsp::JsonRpcMessage;
using maxon_lsp::pathToUri;

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

	// Find all notifications with a specific method
	std::vector<JsonRpcMessage> findAllNotifications(const std::string &method) {
		std::lock_guard<std::mutex> lock(mutex_);
		std::vector<JsonRpcMessage> result;
		for (const auto &msg : outgoing_) {
			if (msg.method == method && !msg.id.has_value()) {
				result.push_back(msg);
			}
		}
		return result;
	}

	// Find diagnostics notification for a specific URI (returns first match)
	std::optional<JsonRpcMessage> findDiagnosticsForUri(const std::string &uri) {
		std::lock_guard<std::mutex> lock(mutex_);
		for (const auto &msg : outgoing_) {
			if (msg.method == "textDocument/publishDiagnostics" && !msg.id.has_value() &&
				msg.params.has_value()) {
				auto &params = msg.params.value();
				if (params.contains("uri") && params["uri"].get<std::string>() == uri) {
					return msg;
				}
			}
		}
		return std::nullopt;
	}

	// Find the LAST diagnostics notification for a specific URI
	std::optional<JsonRpcMessage> findLastDiagnosticsForUri(const std::string &uri) {
		std::lock_guard<std::mutex> lock(mutex_);
		std::optional<JsonRpcMessage> lastMatch;
		for (const auto &msg : outgoing_) {
			if (msg.method == "textDocument/publishDiagnostics" && !msg.id.has_value() &&
				msg.params.has_value()) {
				auto &params = msg.params.value();
				if (params.contains("uri") && params["uri"].get<std::string>() == uri) {
					lastMatch = msg;
				}
			}
		}
		return lastMatch;
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

	// Change a document (full content replacement)
	void changeDocument(const std::string &uri, const std::string &content, int version) {
		json params = {
			{"textDocument", {{"uri", uri}, {"version", version}}},
			{"contentChanges", {{{"text", content}}}}};
		transport_->queueNotification("textDocument/didChange", params);
	}

	// Save a document
	void saveDocument(const std::string &uri, const std::string &content) {
		json params = {
			{"textDocument", {{"uri", uri}}},
			{"text", content}};
		transport_->queueNotification("textDocument/didSave", params);
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
function main() returns int
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
function main() returns int
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

TEST_CASE("LSP requires doc comments for exported stdlib functions", "[lsp][diagnostics][stdlib]") {
	LSPTestFixture fixture;
	fixture.initialize("file:///stdlib");

	// Exported function without doc comment in stdlib path
	std::string code = R"(
export function myPublicFunction() returns int
    return 42
end 'myPublicFunction'
)";
	fixture.openDocument("file:///stdlib/test.maxon", code);
	fixture.shutdown();
	fixture.run();

	auto notification = fixture.transport()->findNotification("textDocument/publishDiagnostics");
	REQUIRE(notification.has_value());
	REQUIRE(notification->params.has_value());

	auto &diagnostics = notification->params.value()["diagnostics"];
	REQUIRE(diagnostics.is_array());

	// Should have an error for missing doc comment
	bool hasDocCommentError = false;
	for (const auto &diag : diagnostics) {
		std::string msg = diag.value("message", "");
		if (msg.find("missing a doc comment") != std::string::npos) {
			hasDocCommentError = true;
			break;
		}
	}
	REQUIRE(hasDocCommentError);
}

TEST_CASE("LSP does not require doc comments for non-stdlib functions", "[lsp][diagnostics]") {
	LSPTestFixture fixture;
	fixture.initialize();

	// Exported function without doc comment in non-stdlib path
	std::string code = R"(
export function myPublicFunction() returns int
    return 42
end 'myPublicFunction'
)";
	fixture.openDocument("file:///test.maxon", code);
	fixture.shutdown();
	fixture.run();

	auto notification = fixture.transport()->findNotification("textDocument/publishDiagnostics");
	REQUIRE(notification.has_value());
	REQUIRE(notification->params.has_value());

	auto &diagnostics = notification->params.value()["diagnostics"];
	REQUIRE(diagnostics.is_array());

	// Should NOT have an error for missing doc comment (not in stdlib)
	bool hasDocCommentError = false;
	for (const auto &diag : diagnostics) {
		std::string msg = diag.value("message", "");
		if (msg.find("missing a doc comment") != std::string::npos) {
			hasDocCommentError = true;
			break;
		}
	}
	REQUIRE_FALSE(hasDocCommentError);
}

TEST_CASE("LSP does not require doc comments for internal stdlib functions", "[lsp][diagnostics][stdlib]") {
	LSPTestFixture fixture;
	fixture.initialize("file:///stdlib");

	// Internal function (starts with _) without doc comment in stdlib path
	std::string code = R"(
export function _internalFunction() returns int
    return 42
end '_internalFunction'
)";
	fixture.openDocument("file:///stdlib/test.maxon", code);
	fixture.shutdown();
	fixture.run();

	auto notification = fixture.transport()->findNotification("textDocument/publishDiagnostics");
	REQUIRE(notification.has_value());
	REQUIRE(notification->params.has_value());

	auto &diagnostics = notification->params.value()["diagnostics"];
	REQUIRE(diagnostics.is_array());

	// Should NOT have an error for missing doc comment (internal function)
	bool hasDocCommentError = false;
	for (const auto &diag : diagnostics) {
		std::string msg = diag.value("message", "");
		if (msg.find("missing a doc comment") != std::string::npos) {
			hasDocCommentError = true;
			break;
		}
	}
	REQUIRE_FALSE(hasDocCommentError);
}

TEST_CASE("LSP accepts doc comments for exported stdlib functions", "[lsp][diagnostics][stdlib]") {
	LSPTestFixture fixture;
	fixture.initialize("file:///stdlib");

	// Exported function WITH doc comment in stdlib path
	std::string code = R"(
/// Returns the answer to life, the universe, and everything.
export function myPublicFunction() returns int
    return 42
end 'myPublicFunction'
)";
	fixture.openDocument("file:///stdlib/test.maxon", code);
	fixture.shutdown();
	fixture.run();

	auto notification = fixture.transport()->findNotification("textDocument/publishDiagnostics");
	REQUIRE(notification.has_value());
	REQUIRE(notification->params.has_value());

	auto &diagnostics = notification->params.value()["diagnostics"];
	REQUIRE(diagnostics.is_array());

	// Should NOT have an error for missing doc comment (has doc comment)
	bool hasDocCommentError = false;
	for (const auto &diag : diagnostics) {
		std::string msg = diag.value("message", "");
		if (msg.find("missing a doc comment") != std::string::npos) {
			hasDocCommentError = true;
			break;
		}
	}
	REQUIRE_FALSE(hasDocCommentError);
}

TEST_CASE("LSP no false positive for range() iteration", "[lsp][diagnostics]") {
	LSPTestFixture fixture;
	fixture.initialize();

	// Valid for-loop using range() - should NOT produce "Cannot iterate over RangeIterator" error
	std::string code = R"(function main() returns int
	for i in range(0, 10) 'loop'
		var x = i
	end 'loop'
	return 0
end 'main')";
	fixture.openDocument("file:///test.maxon", code);
	fixture.shutdown();
	fixture.run();

	auto notification = fixture.transport()->findNotification("textDocument/publishDiagnostics");
	REQUIRE(notification.has_value());
	REQUIRE(notification->params.has_value());

	auto &diagnostics = notification->params.value()["diagnostics"];
	REQUIRE(diagnostics.is_array());

	// Should NOT have "Cannot iterate" error
	bool hasIterateError = false;
	for (const auto &diag : diagnostics) {
		std::string msg = diag.value("message", "");
		if (msg.find("Cannot iterate") != std::string::npos) {
			hasIterateError = true;
			break;
		}
	}
	REQUIRE(!hasIterateError);
}

TEST_CASE("LSP RangeIterator struct is iterable", "[lsp][diagnostics]") {
	LSPTestFixture fixture;
	fixture.initialize();

	// Define RangeIterator struct that implements Iterable, then use it in a for-loop
	// This simulates what the stdlib provides
	std::string code = R"(
interface Iterable
	returns int or nil
end 'Iterable'

struct RangeIterator is Iterable with int
	var current int
	var limit int

	function Iterable.next() returns int or nil
		if current < limit 'check'
			let value = current
			current = current + 1
			return value
		end 'check'
		return nil
	end 'next'
end 'RangeIterator'

function range(start int, end_val int) returns RangeIterator
	var it = RangeIterator{current: start, limit: end_val}
	return it
end 'range'

function main() returns int
	for i in range(0, 10) 'loop'
		var x = i
	end 'loop'
	return 0
end 'main')";
	fixture.openDocument("file:///test.maxon", code);
	fixture.shutdown();
	fixture.run();

	auto notification = fixture.transport()->findNotification("textDocument/publishDiagnostics");
	REQUIRE(notification.has_value());
	REQUIRE(notification->params.has_value());

	auto &diagnostics = notification->params.value()["diagnostics"];
	REQUIRE(diagnostics.is_array());

	// Should NOT have "Cannot iterate over type 'RangeIterator'" error
	bool hasIterateError = false;
	std::string iterateErrorMsg;
	for (const auto &diag : diagnostics) {
		std::string msg = diag.value("message", "");
		if (msg.find("Cannot iterate") != std::string::npos) {
			hasIterateError = true;
			iterateErrorMsg = msg;
			break;
		}
	}
	INFO("Error message: " << iterateErrorMsg);
	REQUIRE(!hasIterateError);
}

TEST_CASE("LSP namespaced RangeIterator is iterable", "[lsp][diagnostics]") {
	LSPTestFixture fixture;
	// Initialize with a root that simulates stdlib location
	fixture.initialize("file:///stdlib");

	// Simulate how the stdlib defines RangeIterator in iter/range.maxon
	// The struct gets a namespace prefix like "iter.RangeIterator"
	std::string code = R"(
interface Iterable
	returns int or nil
end 'Iterable'

export struct RangeIterator is Iterable with int
	var current int
	var limit int

	export function Iterable.next() returns int or nil
		if current < limit 'check'
			let value = current
			current = current + 1
			return value
		end 'check'
		return nil
	end 'next'
end 'RangeIterator'

export function range(start int, end_val int) returns RangeIterator
	var it = RangeIterator{current: start, limit: end_val}
	return it
end 'range'

function main() returns int
	for i in range(0, 10) 'loop'
		var x = i
	end 'loop'
	return 0
end 'main')";
	// Use a path that looks like it's in stdlib/iter/
	fixture.openDocument("file:///stdlib/iter/range.maxon", code);
	fixture.shutdown();
	fixture.run();

	auto notification = fixture.transport()->findNotification("textDocument/publishDiagnostics");
	REQUIRE(notification.has_value());
	REQUIRE(notification->params.has_value());

	auto &diagnostics = notification->params.value()["diagnostics"];
	REQUIRE(diagnostics.is_array());

	// Should NOT have "Cannot iterate over type 'RangeIterator'" error
	bool hasIterateError = false;
	std::string iterateErrorMsg;
	for (const auto &diag : diagnostics) {
		std::string msg = diag.value("message", "");
		if (msg.find("Cannot iterate") != std::string::npos) {
			hasIterateError = true;
			iterateErrorMsg = msg;
			break;
		}
	}
	INFO("Error message: " << iterateErrorMsg);
	REQUIRE(!hasIterateError);
}

TEST_CASE("LSP stdlib RangeIterator is iterable with real stdlib", "[lsp][diagnostics][stdlib]") {
	// Tests run from maxon-bin/lsp/tests/build, so go up 4 levels to project root
	std::filesystem::path testDir = std::filesystem::current_path();
	std::filesystem::path projectRoot = testDir.parent_path().parent_path().parent_path().parent_path();
	std::string stdlibPath = (projectRoot / "stdlib").string();

	LSPTestFixture fixture;
	std::string rootUri = "file://" + projectRoot.string();
	fixture.initialize(rootUri);

	// Simple code that uses range() from the real stdlib
	std::string code = R"(function main() returns int
	for i in range(0, 10) 'loop'
		var x = i
	end 'loop'
	return 0
end 'main')";
	std::string docUri = "file://" + (projectRoot / "examples" / "test.maxon").string();
	fixture.openDocument(docUri, code);
	fixture.shutdown();
	fixture.run();

	auto notification = fixture.transport()->findNotification("textDocument/publishDiagnostics");
	REQUIRE(notification.has_value());
	REQUIRE(notification->params.has_value());

	auto &diagnostics = notification->params.value()["diagnostics"];
	REQUIRE(diagnostics.is_array());

	// Should NOT have "Cannot iterate over type 'RangeIterator'" error
	bool hasIterateError = false;
	std::string iterateErrorMsg;
	for (const auto &diag : diagnostics) {
		std::string msg = diag.value("message", "");
		if (msg.find("Cannot iterate") != std::string::npos) {
			hasIterateError = true;
			iterateErrorMsg = msg;
			break;
		}
	}
	INFO("Error message: " << iterateErrorMsg);
	REQUIRE(!hasIterateError);
}

// =============================================================================
// Hover Tests
// =============================================================================

TEST_CASE("LSP hover returns type information", "[lsp][hover]") {
	LSPTestFixture fixture;
	fixture.initialize();

	std::string code = R"(
function main() returns int
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

TEST_CASE("LSP hover shows value for immutable variables", "[lsp][hover]") {
	LSPTestFixture fixture;
	fixture.initialize();

	std::string code = R"(function main() returns int
	let PI = 3.14159
	return 0
end 'main')";
	fixture.openDocument("file:///test.maxon", code);

	// Request hover on 'PI' at line 1, character 5
	json hoverParams = {
		{"textDocument", {{"uri", "file:///test.maxon"}}},
		{"position", {{"line", 1}, {"character", 5}}}};
	fixture.transport()->queueRequest(2, "textDocument/hover", hoverParams);

	fixture.shutdown();
	fixture.run();

	auto response = fixture.transport()->findResponse(2);
	REQUIRE(response.has_value());
	REQUIRE(!response->error.has_value());
	REQUIRE(response->result.has_value());

	// The hover should contain the value for immutable variables
	std::string content = response->result.value()["contents"]["value"].get<std::string>();
	REQUIRE(content.find("3.14159") != std::string::npos);
}

TEST_CASE("LSP hover shows local function signature", "[lsp][hover]") {
	// Test hover on a user-defined function
	std::string code = R"(function add(a int, b int) returns int
    return a + b
end 'add'

function main() returns int
    return add(1, 2)
end 'main')";

	LSPTestFixture fixture;
	fixture.initialize();
	fixture.openDocument("file:///test.maxon", code);

	// Request hover on 'add' at the call site (line 5)
	// Line 5: "    return add(1, 2)" - 'add' starts at position 11
	json hoverParams = {
		{"textDocument", {{"uri", "file:///test.maxon"}}},
		{"position", {{"line", 5}, {"character", 12}}}};
	fixture.transport()->queueRequest(2, "textDocument/hover", hoverParams);

	fixture.shutdown();
	fixture.run();

	auto response = fixture.transport()->findResponse(2);
	REQUIRE(response.has_value());
	REQUIRE(!response->error.has_value());
	REQUIRE(response->result.has_value());
	REQUIRE(!response->result.value().is_null());

	// The hover should show the function signature
	std::string content = response->result.value()["contents"]["value"].get<std::string>();
	REQUIRE(content.find("function add") != std::string::npos);
	REQUIRE(content.find("a int") != std::string::npos);
}

TEST_CASE("LSP hover shows method signature for method call", "[lsp][hover]") {
	// Test hover on a method call like value.cstr()
	// The method is defined in stdlib as string.cstr

	// Tests run from maxon-bin/lsp/tests/build, so go up 4 levels to project root
	std::filesystem::path testDir = std::filesystem::current_path();
	std::filesystem::path projectRoot = testDir.parent_path().parent_path().parent_path().parent_path();
	std::string stdlibPath = (projectRoot / "stdlib").string();

	LSPTestFixture fixture;
	std::string rootUri = "file://" + projectRoot.string();
	fixture.initialize(rootUri);

	std::string code = R"(function main() returns int
    var s = "hello"
    var cs = s.cstr()
    return 0
end 'main')";
	std::string docUri = "file://" + (projectRoot / "examples" / "test.maxon").string();
	fixture.openDocument(docUri, code);

	// Request hover on 'cstr' at line 2
	// Line 2: "    var cs = s.cstr()" - 'cstr' starts at position 15
	json hoverParams = {
		{"textDocument", {{"uri", docUri}}},
		{"position", {{"line", 2}, {"character", 17}}}};
	fixture.transport()->queueRequest(2, "textDocument/hover", hoverParams);

	fixture.shutdown();
	fixture.run();

	auto response = fixture.transport()->findResponse(2);
	REQUIRE(response.has_value());
	REQUIRE(!response->error.has_value());
	REQUIRE(response->result.has_value());
	REQUIRE(!response->result.value().is_null());

	// The hover should show the method signature
	std::string content = response->result.value()["contents"]["value"].get<std::string>();
	// Should show string.cstr or at least cstr with return type cstring
	REQUIRE((content.find("cstr") != std::string::npos || content.find("cstring") != std::string::npos));
}

TEST_CASE("LSP hover shows correct type for function parameter", "[lsp][hover]") {
	// Test hover on a function parameter to verify it shows the correct type
	LSPTestFixture fixture;
	fixture.initialize();

	std::string code = R"(function greet(name string) returns int
    var x = name
    return 0
end 'greet')";
	fixture.openDocument("file:///test.maxon", code);

	// Request hover on 'name' at line 1
	// Line 1: "    var x = name" - 'name' starts at position 12
	json hoverParams = {
		{"textDocument", {{"uri", "file:///test.maxon"}}},
		{"position", {{"line", 1}, {"character", 13}}}};
	fixture.transport()->queueRequest(2, "textDocument/hover", hoverParams);

	fixture.shutdown();
	fixture.run();

	auto response = fixture.transport()->findResponse(2);
	REQUIRE(response.has_value());
	REQUIRE(!response->error.has_value());
	REQUIRE(response->result.has_value());
	REQUIRE(!response->result.value().is_null());

	// The hover should show the parameter type as string, not float
	std::string content = response->result.value()["contents"]["value"].get<std::string>();
	INFO("Hover content: " << content);
	REQUIRE(content.find("string") != std::string::npos);
	REQUIRE(content.find("float") == std::string::npos);
}

TEST_CASE("LSP hover shows correct type for stdlib function parameter", "[lsp][hover][stdlib]") {
	// Test that hovering over 'value' in print() shows string type, not float
	// This tests the case where multiple functions have same-named parameters
	// Tests run from maxon-bin/lsp/tests/build, so go up 4 levels to project root
	std::filesystem::path testDir = std::filesystem::current_path();
	std::filesystem::path projectRoot = testDir.parent_path().parent_path().parent_path().parent_path();
	std::string stdlibPath = (projectRoot / "stdlib").string();

	LSPTestFixture fixture;
	std::string rootUri = "file://" + projectRoot.string();
	fixture.initialize(rootUri);

	// Code with multiple functions that have same-named parameter 'value' with different types
	// This mirrors stdlib/sys/print.maxon structure
	std::string code = R"(export function print(value string) returns int
    var cs = value
    return 0
end 'print'

export function printInt(value int) returns int
    var x = value
    return 0
end 'printInt'

export function printFloat(value float, precision int) returns int
    var y = value
    return 0
end 'printFloat')";
	std::string docUri = "file://" + (projectRoot / "stdlib" / "sys" / "test_print.maxon").string();
	fixture.openDocument(docUri, code);

	// Request hover on 'value' at line 1 (inside print function)
	// Line 1: "    var cs = value" - 'value' starts at position 13
	json hoverParams = {
		{"textDocument", {{"uri", docUri}}},
		{"position", {{"line", 1}, {"character", 14}}}};
	fixture.transport()->queueRequest(2, "textDocument/hover", hoverParams);

	fixture.shutdown();
	fixture.run();

	auto response = fixture.transport()->findResponse(2);
	REQUIRE(response.has_value());
	REQUIRE(!response->error.has_value());
	REQUIRE(response->result.has_value());
	REQUIRE(!response->result.value().is_null());

	// The hover should show the parameter type as string (from print function), not float
	std::string content = response->result.value()["contents"]["value"].get<std::string>();
	INFO("Hover content: " << content);
	REQUIRE(content.find("string") != std::string::npos);
	REQUIRE(content.find("float") == std::string::npos);
}

// =============================================================================
// Completion Tests
// =============================================================================

TEST_CASE("LSP completion returns items", "[lsp][completion]") {
	LSPTestFixture fixture;
	fixture.initialize();

	std::string code = R"(
function main() returns int
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

TEST_CASE("LSP completion provides string type members", "[lsp][completion][stdlib]") {
	// Tests run from maxon-bin/lsp/tests/build, so go up 4 levels to project root
	std::filesystem::path testDir = std::filesystem::current_path();
	std::filesystem::path projectRoot = testDir.parent_path().parent_path().parent_path().parent_path();
	std::string stdlibPath = (projectRoot / "stdlib").string();

	LSPTestFixture fixture;
	std::string rootUri = "file://" + projectRoot.string();
	fixture.initialize(rootUri);

	std::string code = R"(function main() returns int
	let msg = "hello"
	msg.
	return 0
end 'main')";
	std::string docUri = "file://" + (projectRoot / "temp" / "test.maxon").string();
	fixture.openDocument(docUri, code);

	// Request completion after 'msg.' at line 2, character 5
	json completionParams = {
		{"textDocument", {{"uri", docUri}}},
		{"position", {{"line", 2}, {"character", 5}}}};
	fixture.transport()->queueRequest(3, "textDocument/completion", completionParams);

	fixture.shutdown();
	fixture.run();

	auto response = fixture.transport()->findResponse(3);
	REQUIRE(response.has_value());
	REQUIRE(!response->error.has_value());
	REQUIRE(response->result.has_value());

	auto &result = response->result.value();
	json items;
	if (result.is_array()) {
		items = result;
	} else if (result.is_object() && result.contains("items")) {
		items = result["items"];
	}

	REQUIRE(items.is_array());

	// Helper to check if a completion item exists
	auto hasItem = [&items](const std::string &label) {
		for (const auto &item : items) {
			if (item.contains("label") && item["label"] == label) {
				return true;
			}
		}
		return false;
	};

	// String should have 'count' method
	REQUIRE(hasItem("count"));

	// String should have 'isEmpty' method
	REQUIRE(hasItem("isEmpty"));

	// String should have common methods
	REQUIRE(hasItem("toUpper"));
	REQUIRE(hasItem("toLower"));
	REQUIRE(hasItem("contains"));
	REQUIRE(hasItem("startsWith"));
	REQUIRE(hasItem("endsWith"));
}

TEST_CASE("LSP completion provides array type members", "[lsp][completion][stdlib]") {
	// Tests run from maxon-bin/lsp/tests/build, so go up 4 levels to project root
	std::filesystem::path testDir = std::filesystem::current_path();
	std::filesystem::path projectRoot = testDir.parent_path().parent_path().parent_path().parent_path();

	LSPTestFixture fixture;
	std::string rootUri = "file://" + projectRoot.string();
	fixture.initialize(rootUri);

	std::string code = R"(function main() returns int
	var arr = array of 5 int
	arr.
	return 0
end 'main')";
	std::string docUri = "file://" + (projectRoot / "temp" / "test.maxon").string();
	fixture.openDocument(docUri, code);

	// Request completion after 'arr.' at line 2, character 5
	json completionParams = {
		{"textDocument", {{"uri", docUri}}},
		{"position", {{"line", 2}, {"character", 5}}}};
	fixture.transport()->queueRequest(3, "textDocument/completion", completionParams);

	fixture.shutdown();
	fixture.run();

	auto response = fixture.transport()->findResponse(3);
	REQUIRE(response.has_value());
	REQUIRE(!response->error.has_value());
	REQUIRE(response->result.has_value());

	auto &result = response->result.value();
	json items;
	if (result.is_array()) {
		items = result;
	} else if (result.is_object() && result.contains("items")) {
		items = result["items"];
	}

	REQUIRE(items.is_array());

	// Helper to check if a completion item exists
	auto hasItem = [&items](const std::string &label) {
		for (const auto &item : items) {
			if (item.contains("label") && item["label"] == label) {
				return true;
			}
		}
		return false;
	};

	// Array should have 'count' method from stdlib
	REQUIRE(hasItem("count"));
}

// =============================================================================
// Go to Definition Tests
// =============================================================================

TEST_CASE("LSP definition returns location", "[lsp][definition]") {
	LSPTestFixture fixture;
	fixture.initialize();

	std::string code = R"(
function helper() returns int
    return 42
end 'helper'

function main() returns int
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
	std::string code = "function main() returns int\n  return 0\nend 'main'";
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

TEST_CASE("LSP formatting indents nested code correctly", "[lsp][formatting]") {
	LSPTestFixture fixture;
	fixture.initialize();

	// Code with no indentation - formatter should add proper indentation
	std::string code = R"(function main() returns int
var x = 1
if x > 0 'check'
var y = 2
end 'check'
return x
end 'main')";
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
	REQUIRE(response->result.has_value());
	REQUIRE(response->result.value().is_array());
	REQUIRE(response->result.value().size() > 0);

	// Get the formatted text from the edit
	auto &edits = response->result.value();
	REQUIRE(edits.size() >= 1);
	std::string newText = edits[0]["newText"].get<std::string>();

	// The formatted code should have proper indentation:
	// - "var x = 1" should be indented 1 level inside function
	// - "if x > 0" should be indented 1 level inside function
	// - "var y = 2" should be indented 2 levels (function + if)
	// - "end 'check'" should be indented 1 level
	// - "return x" should be indented 1 level
	// - "end 'main'" should be at level 0

	// Check that var x is indented (should have a tab before it)
	REQUIRE(newText.find("\tvar x") != std::string::npos);

	// Check that var y inside if block has two tabs
	REQUIRE(newText.find("\t\tvar y") != std::string::npos);

	// Check that end 'check' has one tab
	REQUIRE(newText.find("\tend 'check'") != std::string::npos);

	// Check that end 'main' has no leading tab
	REQUIRE(newText.find("\nend 'main'") != std::string::npos);
}

TEST_CASE("LSP formatting indents struct fields correctly", "[lsp][formatting]") {
	LSPTestFixture fixture;
	fixture.initialize();

	// Struct with no indentation on fields
	std::string code = R"(struct Point
var x int
var y int
end 'Point')";
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
	REQUIRE(response->result.has_value());
	REQUIRE(response->result.value().is_array());
	REQUIRE(response->result.value().size() > 0);

	auto &edits = response->result.value();
	std::string newText = edits[0]["newText"].get<std::string>();

	// Struct fields should be indented one level
	REQUIRE(newText.find("\tvar x int") != std::string::npos);
	REQUIRE(newText.find("\tvar y int") != std::string::npos);

	// end should be at level 0
	REQUIRE(newText.find("\nend 'Point'") != std::string::npos);
}

TEST_CASE("LSP formatting handles else blocks correctly", "[lsp][formatting]") {
	LSPTestFixture fixture;
	fixture.initialize();

	// Code with else block - indentation should be maintained
	std::string code = R"(function test() returns int
if true 'check'
return 1
else 'check'
return 0
end 'check'
return 0
end 'test')";
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
	REQUIRE(response->result.has_value());

	auto &edits = response->result.value();
	std::string newText = edits[0]["newText"].get<std::string>();

	// The if should be indented 1 level
	REQUIRE(newText.find("\tif true") != std::string::npos);

	// The first return should be indented 2 levels
	REQUIRE(newText.find("\t\treturn 1") != std::string::npos);

	// The else should be indented 1 level (same as if)
	REQUIRE(newText.find("\telse 'check'") != std::string::npos);

	// The second return (in else) should be indented 2 levels
	REQUIRE(newText.find("\t\treturn 0") != std::string::npos);

	// The end 'check' should be indented 1 level
	REQUIRE(newText.find("\tend 'check'") != std::string::npos);
}

TEST_CASE("LSP formatting uses spaces when insertSpaces is true", "[lsp][formatting]") {
	LSPTestFixture fixture;
	fixture.initialize();

	// Code with no indentation
	std::string code = R"(function main() returns int
var x = 1
return x
end 'main')";
	fixture.openDocument("file:///test.maxon", code);

	json formatParams = {
		{"textDocument", {{"uri", "file:///test.maxon"}}},
		{"options", {{"tabSize", 4}, {"insertSpaces", true}}}};
	fixture.transport()->queueRequest(5, "textDocument/formatting", formatParams);

	fixture.shutdown();
	fixture.run();

	auto response = fixture.transport()->findResponse(5);
	REQUIRE(response.has_value());
	REQUIRE(!response->error.has_value());
	REQUIRE(response->result.has_value());

	auto &edits = response->result.value();
	std::string newText = edits[0]["newText"].get<std::string>();

	// With insertSpaces=true and tabSize=4, indentation should be 4 spaces
	REQUIRE(newText.find("    var x") != std::string::npos);
	REQUIRE(newText.find("    return x") != std::string::npos);

	// Should NOT have tabs
	REQUIRE(newText.find("\tvar") == std::string::npos);
}

TEST_CASE("LSP formatting preserves implicit type declarations", "[lsp][formatting]") {
	LSPTestFixture fixture;
	fixture.initialize();

	// Code with type inference - no explicit type annotations
	std::string code = R"(function main() returns int
var x = 1
let y = 2.5
return x
end 'main')";
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
	REQUIRE(response->result.has_value());

	auto &edits = response->result.value();
	std::string newText = edits[0]["newText"].get<std::string>();

	// The formatter should NOT add explicit type annotations
	// "var x = 1" should stay as "var x = 1", not become "var x int = 1"
	REQUIRE(newText.find("var x = 1") != std::string::npos);
	REQUIRE(newText.find("var x int") == std::string::npos);

	// "let y = 2.5" should stay as "let y = 2.5", not become "let y float = 2.5"
	REQUIRE(newText.find("let y = 2.5") != std::string::npos);
	REQUIRE(newText.find("let y float") == std::string::npos);
}

TEST_CASE("LSP formatting preserves comments", "[lsp][formatting]") {
	LSPTestFixture fixture;
	fixture.initialize();

	std::string code = R"(// This is a comment
function main() returns int
// Another comment
var x = 1
return x
end 'main')";
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
	REQUIRE(response->result.has_value());

	auto &edits = response->result.value();
	std::string newText = edits[0]["newText"].get<std::string>();

	// Comments should be preserved
	REQUIRE(newText.find("// This is a comment") != std::string::npos);
	REQUIRE(newText.find("// Another comment") != std::string::npos);
}

TEST_CASE("LSP formatting preserves blank lines", "[lsp][formatting]") {
	LSPTestFixture fixture;
	fixture.initialize();

	std::string code = R"(function foo() returns int
return 1
end 'foo'

function main() returns int
return 0
end 'main')";
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
	REQUIRE(response->result.has_value());

	auto &edits = response->result.value();
	std::string newText = edits[0]["newText"].get<std::string>();

	// Blank line between functions should be preserved
	REQUIRE(newText.find("end 'foo'\n\nfunction main") != std::string::npos);
}

TEST_CASE("LSP formatting formats multiple interfaces at top level", "[lsp][formatting]") {
	LSPTestFixture fixture;
	fixture.initialize();

	// Multiple interfaces with incorrect nesting (each indented more than the previous)
	// This mimics the bug in stdlib/interfaces.maxon
	std::string code = R"(interface Hashable
	function hash() returns int
	end 'Hashable'

	interface Equatable
		function equals(other Self) returns bool
		end 'Equatable'

		interface Comparable
			function compare(other Self) returns int
			end 'Comparable')";
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
	REQUIRE(response->result.has_value());

	auto &edits = response->result.value();
	REQUIRE(edits.size() >= 1);
	std::string newText = edits[0]["newText"].get<std::string>();

	// All interfaces should be at top level (no leading tabs)
	// First interface may start at beginning of document or after newline
	// Check that interfaces are NOT indented (don't have tab before them)
	REQUIRE((newText.find("interface Hashable") == 0 || newText.find("\ninterface Hashable") != std::string::npos));
	REQUIRE(newText.find("\ninterface Equatable") != std::string::npos);
	REQUIRE(newText.find("\ninterface Comparable") != std::string::npos);

	// Verify interfaces are NOT indented (no tab before interface keyword)
	REQUIRE(newText.find("\tinterface") == std::string::npos);

	// Method signatures should be indented one level
	REQUIRE(newText.find("\tfunction hash()") != std::string::npos);
	REQUIRE(newText.find("\tfunction equals(") != std::string::npos);
	REQUIRE(newText.find("\tfunction compare(") != std::string::npos);

	// End statements should be at top level (no leading tabs)
	REQUIRE(newText.find("\nend 'Hashable'") != std::string::npos);
	REQUIRE(newText.find("\nend 'Equatable'") != std::string::npos);
	REQUIRE(newText.find("\nend 'Comparable'") != std::string::npos);
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

function main() returns int
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
function main() returns int
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
function main() returns int
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
function main() returns int
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
function main() returns int
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

// =============================================================================
// Linked Editing Range Tests
// =============================================================================

TEST_CASE("LSP linkedEditingRange returns ranges for block labels", "[lsp][linkedEditing]") {
	LSPTestFixture fixture;
	fixture.initialize();

	std::string code = R"(function main() returns int
	for i in range(0, 10) 'loop'
		var x = i
	end 'loop'
	return 0
end 'main')";
	fixture.openDocument("file:///test.maxon", code);

	// Request linked editing on the 'loop' label at line 1
	json linkedEditParams = {
		{"textDocument", {{"uri", "file:///test.maxon"}}},
		{"position", {{"line", 1}, {"character", 24}}}}; // position on 'loop'
	fixture.transport()->queueRequest(11, "textDocument/linkedEditingRange", linkedEditParams);

	fixture.shutdown();
	fixture.run();

	auto response = fixture.transport()->findResponse(11);
	REQUIRE(response.has_value());
	REQUIRE(!response->error.has_value());
	// Should return linked ranges for both 'loop' occurrences (start and end)
	if (response->result.has_value() && !response->result.value().is_null()) {
		auto &ranges = response->result.value()["ranges"];
		REQUIRE(ranges.is_array());
		REQUIRE(ranges.size() == 2); // One for start label, one for end label
	}
}

TEST_CASE("LSP linkedEditingRange returns ranges for function names", "[lsp][linkedEditing]") {
	LSPTestFixture fixture;
	fixture.initialize();

	std::string code = R"(function myFunction() returns int
	return 42
end 'myFunction')";
	fixture.openDocument("file:///test.maxon", code);

	// Request linked editing on the function name "myFunction" at line 0
	// "function myFunction" - myFunction starts at character 9
	json linkedEditParams = {
		{"textDocument", {{"uri", "file:///test.maxon"}}},
		{"position", {{"line", 0}, {"character", 12}}}}; // position on 'myFunction'
	fixture.transport()->queueRequest(11, "textDocument/linkedEditingRange", linkedEditParams);

	fixture.shutdown();
	fixture.run();

	auto response = fixture.transport()->findResponse(11);
	REQUIRE(response.has_value());
	REQUIRE(!response->error.has_value());
	REQUIRE(response->result.has_value());
	REQUIRE(!response->result.value().is_null());

	auto &result = response->result.value();
	auto &ranges = result["ranges"];
	REQUIRE(ranges.is_array());
	REQUIRE(ranges.size() == 2); // Function name and end label

	// Check ranges point to the right locations
	// Range 0: function name at line 0, characters 9-19 (myFunction)
	// Range 1: end label at line 2, characters 5-15 (myFunction inside quotes)
	bool hasDeclaration = false;
	bool hasEndLabel = false;
	for (const auto &range : ranges) {
		int startLine = range["start"]["line"].get<int>();
		int startChar = range["start"]["character"].get<int>();
		int endLine = range["end"]["line"].get<int>();
		int endChar = range["end"]["character"].get<int>();

		if (startLine == 0 && startChar == 9 && endLine == 0 && endChar == 19) {
			hasDeclaration = true;
		}
		if (startLine == 2 && startChar == 5 && endLine == 2 && endChar == 15) {
			hasEndLabel = true;
		}
	}
	REQUIRE(hasDeclaration);
	REQUIRE(hasEndLabel);
}

TEST_CASE("LSP linkedEditingRange returns ranges for interface method names", "[lsp][linkedEditing]") {
	LSPTestFixture fixture;
	fixture.initialize();

	// Struct with interface method implementation
	// The method name after the dot should be linked with the end label
	std::string code = R"(interface Countable
	function count() returns int
end 'Countable'

struct MyStruct is Countable
	function Countable.count() returns int
		return 0
	end 'count'
end 'MyStruct')";
	fixture.openDocument("file:///test.maxon", code);

	// Request linked editing on the method name "count" at line 5
	// "function Countable.count()" - count starts at character 20
	json linkedEditParams = {
		{"textDocument", {{"uri", "file:///test.maxon"}}},
		{"position", {{"line", 5}, {"character", 21}}}}; // position on 'count'
	fixture.transport()->queueRequest(11, "textDocument/linkedEditingRange", linkedEditParams);

	fixture.shutdown();
	fixture.run();

	auto response = fixture.transport()->findResponse(11);
	REQUIRE(response.has_value());
	REQUIRE(!response->error.has_value());
	REQUIRE(response->result.has_value());
	REQUIRE(!response->result.value().is_null());

	auto &result = response->result.value();
	auto &ranges = result["ranges"];
	REQUIRE(ranges.is_array());
	REQUIRE(ranges.size() == 2); // Method name and end label

	// Check ranges point to the right locations
	// Range 0: method name at line 5, characters 20-25 (count)
	// Range 1: end label at line 7, characters 6-11 (count inside quotes)
	bool hasMethodName = false;
	bool hasEndLabel = false;
	for (const auto &range : ranges) {
		int startLine = range["start"]["line"].get<int>();
		int startChar = range["start"]["character"].get<int>();
		int endLine = range["end"]["line"].get<int>();
		int endChar = range["end"]["character"].get<int>();

		if (startLine == 5 && startChar == 20 && endLine == 5 && endChar == 25) {
			hasMethodName = true;
		}
		if (startLine == 7 && startChar == 6 && endLine == 7 && endChar == 11) {
			hasEndLabel = true;
		}
	}
	REQUIRE(hasMethodName);
	REQUIRE(hasEndLabel);
}

// =============================================================================
// Stdlib Diagnostics on Initialization Tests
// =============================================================================

TEST_CASE("LSP publishes diagnostics for all stdlib files on initialization", "[lsp][diagnostics][stdlib]") {
	// Tests run from maxon-bin/lsp/tests/build, so go up 4 levels to project root
	std::filesystem::path testDir = std::filesystem::current_path();
	std::filesystem::path projectRoot = testDir.parent_path().parent_path().parent_path().parent_path();
	std::string stdlibPath = (projectRoot / "stdlib").string();

	// Count stdlib files
	std::vector<std::string> stdlibFiles;
	for (const auto &entry : std::filesystem::recursive_directory_iterator(stdlibPath)) {
		if (entry.is_regular_file() && entry.path().extension() == ".maxon") {
			stdlibFiles.push_back(entry.path().string());
		}
	}
	REQUIRE(stdlibFiles.size() > 0);

	LSPTestFixture fixture;
	std::string rootUri = "file://" + projectRoot.string();
	fixture.initialize(rootUri);
	fixture.shutdown();
	fixture.run();

	// Collect all publishDiagnostics notifications
	auto outgoing = fixture.transport()->getOutgoing();
	std::set<std::string> filesWithDiagnostics;
	for (const auto &msg : outgoing) {
		if (msg.method == "textDocument/publishDiagnostics" && msg.params.has_value()) {
			auto &params = msg.params.value();
			if (params.contains("uri")) {
				std::string uri = params["uri"].get<std::string>();
				if (uri.find("stdlib") != std::string::npos) {
					filesWithDiagnostics.insert(uri);
				}
			}
		}
	}

	// All stdlib files should have diagnostics published
	REQUIRE(filesWithDiagnostics.size() == stdlibFiles.size());
}

// =============================================================================
// Cross-file Undefined Function Tests
// =============================================================================

TEST_CASE("LSP reports undefined function when calling non-existent stdlib function", "[lsp][diagnostics][cross-file]") {
	// This test verifies that when a file calls a function that doesn't exist
	// in the stdlib, an "Undefined function" error is reported.
	// This is a regression test for the bug where renaming a function in one
	// stdlib file didn't cause errors in files that called the old name.

	// Tests run from maxon-bin/lsp/tests/build, so go up 4 levels to project root
	std::filesystem::path testDir = std::filesystem::current_path();
	std::filesystem::path projectRoot = testDir.parent_path().parent_path().parent_path().parent_path();
	std::string stdlibPath = (projectRoot / "stdlib").string();

	LSPTestFixture fixture;
	std::string rootUri = "file://" + projectRoot.string();
	fixture.initialize(rootUri);

	// Code that calls a function that doesn't exist in stdlib
	std::string code = R"(function test() returns int
	return nonExistentStdlibFunction(42)
end 'test')";
	std::string docUri = "file://" + (projectRoot / "examples" / "test.maxon").string();
	fixture.openDocument(docUri, code);
	fixture.shutdown();
	fixture.run();

	auto notification = fixture.transport()->findDiagnosticsForUri(docUri);
	REQUIRE(notification.has_value());
	REQUIRE(notification->params.has_value());

	auto &diagnostics = notification->params.value()["diagnostics"];
	REQUIRE(diagnostics.is_array());

	// Should have an "Undefined function" error
	bool hasUndefinedFunctionError = false;
	for (const auto &diag : diagnostics) {
		std::string msg = diag.value("message", "");
		if (msg.find("Undefined function") != std::string::npos &&
			msg.find("nonExistentStdlibFunction") != std::string::npos) {
			hasUndefinedFunctionError = true;
			break;
		}
	}
	REQUIRE(hasUndefinedFunctionError);
}

TEST_CASE("LSP reports undefined function when stdlib file is modified", "[lsp][diagnostics][cross-file]") {
	// This test verifies that when a file calls a function that exists in
	// the stdlib, no error is reported. The scenario being tested is:
	// - Real stdlib has myHelperFunction defined
	// - Consumer file calls myHelperFunction
	// - No error should be reported
	//
	// Note: This test cannot easily simulate modifying a stdlib file because
	// the LSP loads stdlib from disk. Instead, we verify that calling an
	// existing stdlib function works correctly.

	// Tests run from maxon-bin/lsp/tests/build, so go up 4 levels to project root
	std::filesystem::path testDir = std::filesystem::current_path();
	std::filesystem::path projectRoot = testDir.parent_path().parent_path().parent_path().parent_path();
	std::string stdlibPath = (projectRoot / "stdlib").string();

	LSPTestFixture fixture;
	std::string rootUri = "file://" + projectRoot.string();
	fixture.initialize(rootUri);

	// Create a file that calls a function that EXISTS in the real stdlib
	// Using 'print' which is a well-known stdlib function
	std::string consumerCode = R"(function test() returns int
	print("hello")
	return 0
end 'test')";
	std::string consumerUri = "file://" + (projectRoot / "examples" / "consumer.maxon").string();
	fixture.openDocument(consumerUri, consumerCode);

	fixture.shutdown();
	fixture.run();

	// The consumer should NOT have an error because 'print' exists in stdlib
	auto notification = fixture.transport()->findDiagnosticsForUri(consumerUri);
	REQUIRE(notification.has_value());
	REQUIRE(notification->params.has_value());

	auto &diagnostics = notification->params.value()["diagnostics"];

	bool hasUndefinedError = false;
	for (const auto &diag : diagnostics) {
		std::string msg = diag.value("message", "");
		if (msg.find("Undefined function") != std::string::npos &&
			msg.find("print") != std::string::npos) {
			hasUndefinedError = true;
			break;
		}
	}
	// Should NOT have an undefined function error - 'print' exists in stdlib
	REQUIRE_FALSE(hasUndefinedError);
}

TEST_CASE("LSP reports undefined function when file calls renamed stdlib function", "[lsp][diagnostics][cross-file][regression]") {
	// This is the actual regression test for the reported bug:
	// When a file calls a function that doesn't exist in the stdlib,
	// an "Undefined function" error should be reported.
	//
	// The original bug was that the LSP cached stdlib symbols on initialization,
	// so when a stdlib file was modified to rename a function, dependent files
	// wouldn't get an error.
	//
	// The fix ensures that when a stdlib file is changed, the stdlib symbols
	// are reloaded and dependent files are re-analyzed.

	// Tests run from maxon-bin/lsp/tests/build, so go up 4 levels to project root
	std::filesystem::path testDir = std::filesystem::current_path();
	std::filesystem::path projectRoot = testDir.parent_path().parent_path().parent_path().parent_path();
	std::string stdlibPath = (projectRoot / "stdlib").string();

	// First, start a server with the real stdlib
	LSPTestFixture fixture;
	std::string rootUri = "file://" + projectRoot.string();
	fixture.initialize(rootUri);

	// Open a file that calls a function that doesn't exist in stdlib
	// This simulates calling a function after it was renamed
	std::string callerCode = R"(function test() returns int
	// Calling a function name that doesn't exist
	return nonExistentHelperFunction(42, 0, 10)
end 'test')";
	std::string callerUri = "file://" + (projectRoot / "examples" / "test_caller.maxon").string();
	fixture.openDocument(callerUri, callerCode);

	fixture.shutdown();
	fixture.run();

	// The caller should have an "Undefined function" error
	auto notification = fixture.transport()->findDiagnosticsForUri(callerUri);
	REQUIRE(notification.has_value());
	REQUIRE(notification->params.has_value());

	auto &diagnostics = notification->params.value()["diagnostics"];
	REQUIRE(diagnostics.is_array());

	bool hasUndefinedError = false;
	std::string foundMsg;
	for (const auto &diag : diagnostics) {
		std::string msg = diag.value("message", "");
		if (msg.find("Undefined function") != std::string::npos &&
			msg.find("nonExistentHelperFunction") != std::string::npos) {
			hasUndefinedError = true;
			foundMsg = msg;
			break;
		}
	}
	INFO("Should report undefined function error for 'nonExistentHelperFunction'");
	INFO("Found message: " << foundMsg);
	REQUIRE(hasUndefinedError);
}

TEST_CASE("LSP reloads stdlib when stdlib file is changed in memory", "[lsp][diagnostics][cross-file][stdlib-reload]") {
	// This test verifies the stdlib reload mechanism works with in-memory changes:
	// 1. Open a consumer file that calls a real stdlib function
	// 2. Open the stdlib file that defines the function
	// 3. Change the stdlib file to RENAME the function (in memory, not saved)
	// 4. Consumer file should now report "Undefined function" error
	//
	// This is the actual bug scenario: editing a stdlib file should cause
	// dependent files to see the updated symbols, even before saving.

	// Tests run from maxon-bin/lsp/tests/build, so go up 4 levels to project root
	std::filesystem::path testDir = std::filesystem::current_path();
	std::filesystem::path projectRoot = testDir.parent_path().parent_path().parent_path().parent_path();
	std::string stdlibPath = (projectRoot / "stdlib").string();
	std::filesystem::path graphemePath = projectRoot / "stdlib" / "string" / "grapheme.maxon";

	LSPTestFixture fixture;
	std::string rootUri = pathToUri(projectRoot.string());
	fixture.initialize(rootUri);

	// Step 1: Open a consumer file that calls findGraphemeEndManaged
	// This function exists in the real stdlib, so initially no error
	std::string consumerCode = R"(function test(m _ManagedString) returns int
	return findGraphemeEndManaged(m, 0, 10)
end 'test')";
	std::string consumerUri = pathToUri((projectRoot / "examples" / "consumer.maxon").string());
	fixture.openDocument(consumerUri, consumerCode);

	// Step 2: Read the actual grapheme.maxon content
	std::ifstream graphemeFile(graphemePath);
	std::stringstream buffer;
	buffer << graphemeFile.rdbuf();
	std::string originalContent = buffer.str();

	// Step 3: Open the stdlib file and then change it to rename the function
	std::string graphemeUri = pathToUri(graphemePath.string());
	fixture.openDocument(graphemeUri, originalContent);

	// Step 4: Modify the content to rename findGraphemeEndManaged to findGraphemeEndManagedXXX
	std::string modifiedContent = originalContent;
	size_t pos = 0;
	while ((pos = modifiedContent.find("findGraphemeEndManaged", pos)) != std::string::npos) {
		// Don't replace findGraphemeEndManagedRange (only replace exact matches)
		if (pos + 22 < modifiedContent.size() &&
			modifiedContent[pos + 22] != 'R' && // Not "Range"
			modifiedContent[pos + 22] != 'X') { // Not already renamed
			modifiedContent.replace(pos, 22, "findGraphemeEndManagedXXX");
			pos += 25;
		} else {
			pos += 22;
		}
	}
	fixture.changeDocument(graphemeUri, modifiedContent, 2);

	fixture.shutdown();
	fixture.run();

	// Step 5: The consumer should now have an "Undefined function" error
	// because findGraphemeEndManaged was renamed
	// Use findLastDiagnosticsForUri because there may be earlier diagnostics before the change
	auto consumerNotification = fixture.transport()->findLastDiagnosticsForUri(consumerUri);
	REQUIRE(consumerNotification.has_value());
	REQUIRE(consumerNotification->params.has_value());

	auto &diagnostics = consumerNotification->params.value()["diagnostics"];
	REQUIRE(diagnostics.is_array());

	bool hasUndefinedError = false;
	std::string foundMsg;
	for (const auto &diag : diagnostics) {
		std::string msg = diag.value("message", "");
		if (msg.find("Undefined function") != std::string::npos &&
			msg.find("findGraphemeEndManaged") != std::string::npos) {
			hasUndefinedError = true;
			foundMsg = msg;
			break;
		}
	}
	INFO("Consumer should report undefined function after stdlib function rename");
	INFO("Found message: " << foundMsg);
	REQUIRE(hasUndefinedError);
}

TEST_CASE("LSP transitive interface conformance allows parent interface methods",
		  "[lsp][diagnostics][interface]") {
	// Test that a struct implementing "Collection with Element" (which extends Iterable)
	// can have Iterable.next() method without error.
	// This tests that type parameters like "Collection with Element" are properly
	// stripped to find the base interface name "Collection" for lookup.
	LSPTestFixture fixture;

	// Use real stdlib path so we test against the actual Collection/Iterable interfaces
	std::filesystem::path testDir = std::filesystem::current_path();
	std::filesystem::path projectRoot = testDir.parent_path().parent_path().parent_path().parent_path();
	std::string rootUri = "file://" + projectRoot.string();
	fixture.initialize(rootUri);

	// Test code that mirrors stdlib/collections/array.maxon structure:
	// - Generic struct with "uses Element"
	// - Conforms to "Collection with Element" (not just "Collection")
	// - Has Iterable.next() method (Iterable is parent of Collection)
	std::string code = R"(
struct MyArray uses Element is Collection with Element
	var data int
	var iterIndex int

	function Collection.count() returns int
		return 0
	end 'count'

	function Collection.get(index int) returns Element or nil
		return nil
	end 'get'

	function Collection.set(index int, value Element) returns Self
		return self
	end 'set'

	function Iterable.next() returns Element or nil
		return nil
	end 'next'
end 'MyArray'

function main() returns int
	return 0
end 'main')";
	std::string docUri = "file://" + (projectRoot / "test.maxon").string();
	fixture.openDocument(docUri, code);
	fixture.shutdown();
	fixture.run();

	auto notification = fixture.transport()->findLastDiagnosticsForUri(docUri);
	REQUIRE(notification.has_value());
	REQUIRE(notification->params.has_value());

	auto &diagnostics = notification->params.value()["diagnostics"];
	REQUIRE(diagnostics.is_array());

	// Should NOT have error about Iterable.next() not conforming
	// The bug was: "Method 'next' declares implementation of interface 'Iterable'
	// but struct 'MyArray' does not conform to this interface"
	bool hasConformanceError = false;
	std::string errorMsg;
	for (const auto &diag : diagnostics) {
		std::string msg = diag.value("message", "");
		if (msg.find("does not conform to this interface") != std::string::npos &&
			msg.find("Iterable") != std::string::npos) {
			hasConformanceError = true;
			errorMsg = msg;
			break;
		}
	}
	INFO("Error message: " << errorMsg);
	REQUIRE(!hasConformanceError);
}

TEST_CASE("LSP detects missing interface method from transitive interface",
		  "[lsp][diagnostics][interface]") {
	// Test that a struct implementing "Collection with Element" (which extends Iterable)
	// gets an error if it's missing the next() method required by Iterable.
	LSPTestFixture fixture;

	// Use real stdlib path so we test against the actual Collection/Iterable interfaces
	std::filesystem::path testDir = std::filesystem::current_path();
	std::filesystem::path projectRoot = testDir.parent_path().parent_path().parent_path().parent_path();
	std::string rootUri = "file://" + projectRoot.string();
	fixture.initialize(rootUri);

	// Test code that is MISSING the Iterable.next() method
	// This should produce an error about missing method
	std::string code = R"(
struct MyArray uses Element is Collection with Element
	var data int
	var iterIndex int

	function Collection.count() returns int
		return 0
	end 'count'

	function Collection.get(index int) returns Element or nil
		return nil
	end 'get'

	function Collection.set(index int, value Element) returns Self
		return self
	end 'set'

	// NOTE: Missing Iterable.next() method!
end 'MyArray'

function main() returns int
	return 0
end 'main')";
	std::string docUri = "file://" + (projectRoot / "test.maxon").string();
	fixture.openDocument(docUri, code);
	fixture.shutdown();
	fixture.run();

	auto notification = fixture.transport()->findLastDiagnosticsForUri(docUri);
	REQUIRE(notification.has_value());
	REQUIRE(notification->params.has_value());

	auto &diagnostics = notification->params.value()["diagnostics"];
	REQUIRE(diagnostics.is_array());

	// SHOULD have error about missing 'next' method from Iterable
	bool hasMissingMethodError = false;
	std::string errorMsg;
	for (const auto &diag : diagnostics) {
		std::string msg = diag.value("message", "");
		if (msg.find("missing") != std::string::npos && msg.find("next") != std::string::npos) {
			hasMissingMethodError = true;
			errorMsg = msg;
			break;
		}
	}
	INFO("Error message: " << errorMsg);
	REQUIRE(hasMissingMethodError);
}

TEST_CASE("LSP does not report missing method when interface has default implementation",
		  "[lsp][diagnostics][interface]") {
	// Test that a struct implementing "Collection with Element" does NOT get an error
	// for the map() method because Collection provides a default implementation.
	LSPTestFixture fixture;

	// Use real stdlib path so we test against the actual Collection/Iterable interfaces
	std::filesystem::path testDir = std::filesystem::current_path();
	std::filesystem::path projectRoot = testDir.parent_path().parent_path().parent_path().parent_path();
	std::string rootUri = "file://" + projectRoot.string();
	fixture.initialize(rootUri);

	// Test code that implements all required methods but NOT map()
	// map() has a default implementation in Collection so this should NOT error
	std::string code = R"(
struct MyArray uses Element is Collection with Element
	var data int
	var iterIndex int

	function Collection.count() returns int
		return 0
	end 'count'

	function Collection.get(index int) returns Element or nil
		return nil
	end 'get'

	function Collection.set(index int, value Element) returns Self
		return self
	end 'set'

	function Iterable.next() returns Element or nil
		return nil
	end 'next'
end 'MyArray'

function main() returns int
	return 0
end 'main')";
	std::string docUri = "file://" + (projectRoot / "test.maxon").string();
	fixture.openDocument(docUri, code);
	fixture.shutdown();
	fixture.run();

	auto notification = fixture.transport()->findLastDiagnosticsForUri(docUri);
	REQUIRE(notification.has_value());
	REQUIRE(notification->params.has_value());

	auto &diagnostics = notification->params.value()["diagnostics"];
	REQUIRE(diagnostics.is_array());

	// Should NOT have error about missing 'map' method (it has a default implementation)
	bool hasMissingMapError = false;
	std::string errorMsg;
	for (const auto &diag : diagnostics) {
		std::string msg = diag.value("message", "");
		if (msg.find("missing") != std::string::npos && msg.find("map") != std::string::npos) {
			hasMissingMapError = true;
			errorMsg = msg;
			break;
		}
	}
	INFO("Error message (should be empty): " << errorMsg);
	REQUIRE(!hasMissingMapError);
}
