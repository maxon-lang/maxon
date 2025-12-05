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

TEST_CASE("LSP no false positive for range() iteration", "[lsp][diagnostics]") {
	LSPTestFixture fixture;
	fixture.initialize();

	// Valid for-loop using range() - should NOT produce "Cannot iterate over RangeIterator" error
	std::string code = R"(function main() int
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
	function next() int or nil
end 'Iterable'

struct RangeIterator is Iterable with int
	var current int
	var limit int

	function Iterable.next() int or nil
		if current < limit 'check'
			let value = current
			current = current + 1
			return value
		end 'check'
		return nil
	end 'next'
end 'RangeIterator'

function range(start int, end_val int) RangeIterator
	var it = RangeIterator{current: start, limit: end_val}
	return it
end 'range'

function main() int
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
	function next() int or nil
end 'Iterable'

export struct RangeIterator is Iterable with int
	var current int
	var limit int

	export function Iterable.next() int or nil
		if current < limit 'check'
			let value = current
			current = current + 1
			return value
		end 'check'
		return nil
	end 'next'
end 'RangeIterator'

export function range(start int, end_val int) RangeIterator
	var it = RangeIterator{current: start, limit: end_val}
	return it
end 'range'

function main() int
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
	LSPTestFixture fixture;
	// Initialize with the real project root so stdlib is loaded
	// The test runs from maxon-bin/lsp/tests/build, so project root is ../../../..
	fixture.initialize("file:///C:/Users/Eric/Dev/maxon");

	// Simple code that uses range() from the real stdlib
	std::string code = R"(function main() int
	for i in range(0, 10) 'loop'
		var x = i
	end 'loop'
	return 0
end 'main')";
	fixture.openDocument("file:///C:/Users/Eric/Dev/maxon/examples/test.maxon", code);
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

TEST_CASE("LSP hover shows value for immutable variables", "[lsp][hover]") {
	LSPTestFixture fixture;
	fixture.initialize();

	std::string code = R"(function main() int
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
	std::string code = R"(function add(a int, b int) int
    return a + b
end 'add'

function main() int
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

TEST_CASE("LSP completion provides string type members", "[lsp][completion][stdlib]") {
	LSPTestFixture fixture;
	// Initialize with real project root so stdlib is loaded
	fixture.initialize("file:///C:/Users/Eric/Dev/maxon");

	std::string code = R"(function main() int
	let msg = "hello"
	msg.
	return 0
end 'main')";
	fixture.openDocument("file:///C:/Users/Eric/Dev/maxon/temp/test.maxon", code);

	// Request completion after 'msg.' at line 2, character 5
	json completionParams = {
		{"textDocument", {{"uri", "file:///C:/Users/Eric/Dev/maxon/temp/test.maxon"}}},
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

TEST_CASE("LSP completion provides array type members", "[lsp][completion]") {
	LSPTestFixture fixture;
	fixture.initialize();

	std::string code = R"(function main() int
	var arr = [5]int
	arr.
	return 0
end 'main')";
	fixture.openDocument("file:///test.maxon", code);

	// Request completion after 'arr.' at line 2, character 5
	json completionParams = {
		{"textDocument", {{"uri", "file:///test.maxon"}}},
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

	// Array should have 'length' property
	REQUIRE(hasItem("length"));
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

TEST_CASE("LSP formatting indents nested code correctly", "[lsp][formatting]") {
	LSPTestFixture fixture;
	fixture.initialize();

	// Code with no indentation - formatter should add proper indentation
	std::string code = R"(function main() int
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
	std::string code = R"(function test() int
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
	std::string code = R"(function main() int
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
	std::string code = R"(function main() int
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
function main() int
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

	std::string code = R"(function foo() int
return 1
end 'foo'

function main() int
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

// =============================================================================
// Linked Editing Range Tests
// =============================================================================

TEST_CASE("LSP linkedEditingRange returns ranges for block labels", "[lsp][linkedEditing]") {
	LSPTestFixture fixture;
	fixture.initialize();

	std::string code = R"(function main() int
	for i in range(0, 10) 'loop'
		var x = i
	end 'loop'
	return 0
end 'main')";
	fixture.openDocument("file:///test.maxon", code);

	// Request linked editing on the 'loop' label at line 1
	json linkedEditParams = {
		{"textDocument", {{"uri", "file:///test.maxon"}}},
		{"position", {{"line", 1}, {"character", 24}}}};  // position on 'loop'
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
		REQUIRE(ranges.size() == 2);  // One for start label, one for end label
	}
}

TEST_CASE("LSP linkedEditingRange returns ranges for function names", "[lsp][linkedEditing]") {
	LSPTestFixture fixture;
	fixture.initialize();

	std::string code = R"(function myFunction() int
	return 42
end 'myFunction')";
	fixture.openDocument("file:///test.maxon", code);

	// Request linked editing on the function name "myFunction" at line 0
	// "function myFunction" - myFunction starts at character 9
	json linkedEditParams = {
		{"textDocument", {{"uri", "file:///test.maxon"}}},
		{"position", {{"line", 0}, {"character", 12}}}};  // position on 'myFunction'
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
	REQUIRE(ranges.size() == 2);  // Function name and end label

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
