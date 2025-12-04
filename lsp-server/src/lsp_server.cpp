#include "lsp_server.h"
#include "lexer.h"
#include <filesystem>
#include <fstream>
#include <iostream>
#include <sstream>

#ifdef _WIN32
#include <windows.h>
#else
#include <limits.h>
#include <unistd.h>
#endif

namespace fs = std::filesystem;

// Helper function to URL decode a string
static std::string urlDecode(const std::string &str) {
	std::string result;
	for (size_t i = 0; i < str.length(); ++i) {
		if (str[i] == '%' && i + 2 < str.length()) {
			// Convert %XX to character
			std::string hex = str.substr(i + 1, 2);
			char ch = static_cast<char>(std::stoi(hex, nullptr, 16));
			result += ch;
			i += 2;
		} else if (str[i] == '+') {
			result += ' ';
		} else {
			result += str[i];
		}
	}
	return result;
}

// Helper function to get executable directory
static std::string getExecutableDirectory() {
#ifdef _WIN32
	char path[MAX_PATH];
	GetModuleFileNameA(NULL, path, MAX_PATH);
	std::string exePath(path);
	size_t lastSlash = exePath.find_last_of("\\/");
	return lastSlash != std::string::npos ? exePath.substr(0, lastSlash) : "";
#else
	char path[PATH_MAX];
	ssize_t count = readlink("/proc/self/exe", path, PATH_MAX);
	if (count != -1) {
		path[count] = '\0';
		std::string exePath(path);
		size_t lastSlash = exePath.find_last_of('/');
		return lastSlash != std::string::npos ? exePath.substr(0, lastSlash) : "";
	}
	return "";
#endif
}

// Helper function to convert file:// URI to filesystem path
static std::string uriToPath(const std::string &uri) {
	std::string path = uri;
	if (path.substr(0, 7) == "file://") {
		path = path.substr(7);
	}
	path = urlDecode(path);
	// On Windows, remove leading slash from /C:/...
	if (path.size() > 2 && path[0] == '/' && path[2] == ':') {
		path = path.substr(1);
	}
	return path;
}

LspServer::LspServer()
	: rpcHandler(std::make_unique<JsonRpcHandler>()),
	  docManager(std::make_unique<DocumentManager>()),
	  analyzer(std::make_unique<Analyzer>()),
	  formatter(std::make_unique<Formatter>()),
	  initialized(false) {

	// Register request handlers
	rpcHandler->registerRequestHandler("initialize",
									   [this](const json &params) { return handleInitialize(params); });
	rpcHandler->registerRequestHandler("shutdown",
									   [this](const json &params) { return handleShutdown(params); });
	rpcHandler->registerRequestHandler("textDocument/completion",
									   [this](const json &params) { return handleCompletion(params); });
	rpcHandler->registerRequestHandler("textDocument/hover",
									   [this](const json &params) { return handleHover(params); });
	rpcHandler->registerRequestHandler("textDocument/definition",
									   [this](const json &params) { return handleDefinition(params); });
	rpcHandler->registerRequestHandler("textDocument/documentSymbol",
									   [this](const json &params) { return handleDocumentSymbol(params); });
	rpcHandler->registerRequestHandler("textDocument/codeAction",
									   [this](const json &params) { return handleCodeAction(params); });
	rpcHandler->registerRequestHandler("textDocument/rename",
									   [this](const json &params) { return handleRename(params); });
	rpcHandler->registerRequestHandler("textDocument/linkedEditingRange",
									   [this](const json &params) { return handleLinkedEditingRange(params); });
	rpcHandler->registerRequestHandler("textDocument/formatting",
									   [this](const json &params) { return handleFormatting(params); });
	rpcHandler->registerRequestHandler("textDocument/rangeFormatting",
									   [this](const json &params) { return handleRangeFormatting(params); });
	rpcHandler->registerRequestHandler("textDocument/semanticTokens/full",
									   [this](const json &params) { return handleSemanticTokensFull(params); });

	// Register notification handlers
	rpcHandler->registerNotificationHandler("initialized",
											[this](const json &params) { handleInitialized(params); });
	rpcHandler->registerNotificationHandler("textDocument/didOpen",
											[this](const json &params) { handleDidOpen(params); });
	rpcHandler->registerNotificationHandler("textDocument/didChange",
											[this](const json &params) { handleDidChange(params); });
	rpcHandler->registerNotificationHandler("textDocument/didClose",
											[this](const json &params) { handleDidClose(params); });
	rpcHandler->registerNotificationHandler("textDocument/didSave",
											[this](const json &params) { handleDidSave(params); });
	rpcHandler->registerNotificationHandler("exit",
											[this](const json &params) { handleExit(params); });
	rpcHandler->registerNotificationHandler("workspace/didChangeWatchedFiles",
											[this](const json &params) { handleDidChangeWatchedFiles(params); });
}

void LspServer::run() {
	std::string line;
	int contentLength = 0;

	while (true) {
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

			if (std::cin.gcount() == contentLength) {
				rpcHandler->processMessage(content);
			}

			contentLength = 0;
		}

		if (std::cin.eof()) {
			break;
		}
	}
}

json LspServer::handleInitialize(const json &params) {
	if (params.contains("rootUri")) {
		rootUri = params["rootUri"].get<std::string>();
	}

	// Initialize stdlib function cache
	// First, try to find stdlib relative to the executable location
	// This works when maxon-lsp-server.exe is in project_root/bin/
	std::string exeDir = getExecutableDirectory();

	bool stdlibInitialized = false;

	if (!exeDir.empty()) {
		fs::path stdlibPath = fs::path(exeDir).parent_path() / "stdlib";

		if (fs::exists(stdlibPath) && fs::is_directory(stdlibPath)) {
			analyzer->initializeStdlib(stdlibPath.string());
			stdlibInitialized = true;
		}
	}

	// If stdlib not found relative to exe, try workspace root as fallback
	if (!stdlibInitialized && !rootUri.empty()) {
		// Convert file:// URI to path and look for stdlib directory
		std::string path = rootUri;
		if (path.substr(0, 7) == "file://") {
			path = path.substr(7);
		}

		// URL decode the path (e.g., %3A -> :)
		path = urlDecode(path);

		// On Windows, remove leading slash from /C:/...
		if (path.size() > 2 && path[0] == '/' && path[2] == ':') {
			path = path.substr(1);
		}

		fs::path stdlibPath = fs::path(path) / "stdlib";

		if (fs::exists(stdlibPath) && fs::is_directory(stdlibPath)) {
			analyzer->initializeStdlib(stdlibPath.string());
		}
	}

	json capabilities = {
		{"capabilities", {{"textDocumentSync", {{"openClose", true}, {"change", 2}, {"save", {{"includeText", false}}}}}, // Incremental sync (2)
						  {"completionProvider", {{"resolveProvider", false}, {"triggerCharacters", json::array({".", ":"})}}},
						  {"hoverProvider", true},
						  {"definitionProvider", true},
						  {"documentSymbolProvider", true},
						  {"codeActionProvider", {{"codeActionKinds", json::array({"quickfix"})}}},
						  {"renameProvider", true},
						  {"linkedEditingRangeProvider", true},
						  {"documentFormattingProvider", true},
						  {"documentRangeFormattingProvider", true},
						  {"semanticTokensProvider", {{"legend", {{"tokenTypes", json::array({"namespace", "type", "class", "enum", "interface", "struct", "typeParameter", "parameter", "variable", "property", "enumMember", "event", "function", "method", "macro", "keyword", "modifier", "comment", "string", "number", "regexp", "operator", "label"})}, {"tokenModifiers", json::array({"declaration", "definition", "readonly", "static", "deprecated", "abstract", "async", "modification", "documentation", "defaultLibrary", "level0", "level1", "level2", "level3", "level4", "level5"})}}}, {"full", true}}}}},
		{"serverInfo", {{"name", "maxon-lsp"}, {"version", "0.1.0"}}}};

	initialized = true;
	return capabilities;
}

json LspServer::handleShutdown(const json &params) {
	return nullptr;
}

json LspServer::handleCompletion(const json &params) {
	std::string uri = params["textDocument"]["uri"].get<std::string>();
	int line = params["position"]["line"].get<int>();
	int character = params["position"]["character"].get<int>();

	auto doc = docManager->getDocument(uri);
	if (!doc) {
		return json::array();
	}

	lsp::Position pos{line, character};
	auto completions = analyzer->getCompletions(doc, pos);

	json items = json::array();
	for (const auto &comp : completions) {
		json item = {
			{"label", comp.label},
			{"kind", static_cast<int>(comp.kind)},
			{"detail", comp.detail}};

		// Add documentation if present
		if (!comp.documentation.empty()) {
			item["documentation"] = comp.documentation;
		}

		// Add insertText if present
		if (comp.insertText.has_value()) {
			item["insertText"] = comp.insertText.value();
		}

		items.push_back(item);
	}

	return items;
}

json LspServer::handleHover(const json &params) {
	std::string uri = params["textDocument"]["uri"].get<std::string>();
	int line = params["position"]["line"].get<int>();
	int character = params["position"]["character"].get<int>();

	auto doc = docManager->getDocument(uri);
	if (!doc) {
		return nullptr;
	}

	lsp::Position pos{line, character};
	auto hover = analyzer->getHover(doc, pos);

	if (hover.has_value()) {
		return {
			{"contents", {{"kind", "markdown"}, {"value", hover->contents}}}};
	}

	return nullptr;
}

json LspServer::handleDefinition(const json &params) {
	std::string uri = params["textDocument"]["uri"].get<std::string>();
	int line = params["position"]["line"].get<int>();
	int character = params["position"]["character"].get<int>();

	auto doc = docManager->getDocument(uri);
	if (!doc) {
		return nullptr;
	}

	lsp::Position pos{line, character};
	auto location = analyzer->getDefinition(doc, pos);

	if (location.has_value()) {
		return {
			{"uri", location->uri},
			{"range", {{"start", {{"line", location->range.start.line}, {"character", location->range.start.character}}}, {"end", {{"line", location->range.end.line}, {"character", location->range.end.character}}}}}};
	}

	return nullptr;
}

json LspServer::handleDocumentSymbol(const json &params) {
	std::string uri = params["textDocument"]["uri"].get<std::string>();

	auto doc = docManager->getDocument(uri);
	if (!doc) {
		return json::array();
	}

	auto symbols = analyzer->getSymbols(doc);

	json result = json::array();
	for (const auto &sym : symbols) {
		json item = {
			{"name", sym.name},
			{"kind", static_cast<int>(sym.kind)},
			{"location", {{"uri", sym.location.uri}, {"range", {{"start", {{"line", sym.location.range.start.line}, {"character", sym.location.range.start.character}}}, {"end", {{"line", sym.location.range.end.line}, {"character", sym.location.range.end.character}}}}}}}};
		result.push_back(item);
	}

	return result;
}

void LspServer::handleInitialized(const json &params) {
	// Server is now initialized - register file watchers
	registerFileWatchers();
}

void LspServer::registerFileWatchers() {
	// Register file watchers for stdlib files using dynamic registration
	// This allows us to be notified when stdlib files change

	json registrations = json::array();

	// Create a file watcher registration for stdlib .maxon files
	json fileWatcherRegistration = {
		{"id", "stdlib-watcher"},
		{"method", "workspace/didChangeWatchedFiles"},
		{"registerOptions", {
			{"watchers", json::array({
				{{"globPattern", "**/stdlib/**/*.maxon"}},
				{{"globPattern", "**/stdlib/**/*.mx"}}
			})}
		}}
	};

	registrations.push_back(fileWatcherRegistration);

	json registerParams = {
		{"registrations", registrations}
	};

	// Send the registration request to the client
	rpcHandler->sendRequest("client/registerCapability", registerParams);
}

void LspServer::handleDidChangeWatchedFiles(const json &params) {
	if (!params.contains("changes") || !params["changes"].is_array()) {
		return;
	}

	bool stdlibChanged = false;

	for (const auto &change : params["changes"]) {
		if (!change.contains("uri")) {
			continue;
		}

		std::string uri = change["uri"].get<std::string>();
		std::string filePath = uriToPath(uri);

		// Check if this is a stdlib file
		if (analyzer->isStdlibFile(filePath)) {
			stdlibChanged = true;
			// Change type: 1 = created, 2 = changed, 3 = deleted
			int changeType = change.contains("type") ? change["type"].get<int>() : 2;
			(void)changeType; // Currently we reload the entire stdlib regardless of change type
		}
	}

	if (stdlibChanged) {
		// Reload stdlib and invalidate all document caches
		analyzer->reloadStdlib();
		analyzer->invalidateAllDocumentCaches();

		// Re-analyze all open documents
		reanalyzeAllOpenDocuments();
	}
}

void LspServer::reanalyzeAllOpenDocuments() {
	// Get all open document URIs and re-analyze them
	// This is called after stdlib changes to update diagnostics with new stdlib symbols

	auto uris = docManager->getAllDocumentUris();
	for (const auto &uri : uris) {
		auto doc = docManager->getDocument(uri);
		if (doc) {
			auto diagnostics = analyzer->analyze(doc);
			publishDiagnostics(uri, diagnostics);
		}
	}
}

void LspServer::handleDidOpen(const json &params) {
	std::string uri = params["textDocument"]["uri"].get<std::string>();
	std::string text = params["textDocument"]["text"].get<std::string>();
	int version = params["textDocument"]["version"].get<int>();

	docManager->openDocument(uri, text, version);

	// Analyze and send diagnostics
	auto doc = docManager->getDocument(uri);
	if (doc) {
		auto diagnostics = analyzer->analyze(doc);
		publishDiagnostics(uri, diagnostics);
	}
}

void LspServer::handleDidChange(const json &params) {
	std::string uri = params["textDocument"]["uri"].get<std::string>();
	int version = params["textDocument"]["version"].get<int>();

	if (params.contains("contentChanges") && params["contentChanges"].is_array() &&
		params["contentChanges"].size() > 0) {

		// Process all content changes (could be incremental or full)
		for (const auto &change : params["contentChanges"]) {
			std::string text = change["text"].get<std::string>();

			if (change.contains("range")) {
				// Incremental change - apply the change to the existing document
				lsp::Range range;
				range.start.line = change["range"]["start"]["line"].get<int>();
				range.start.character = change["range"]["start"]["character"].get<int>();
				range.end.line = change["range"]["end"]["line"].get<int>();
				range.end.character = change["range"]["end"]["character"].get<int>();

				docManager->applyIncrementalChange(uri, range, text, version);
			} else {
				// Full document replacement
				docManager->updateDocument(uri, text, version);
			}
		}

		// Analyze and send diagnostics
		auto doc = docManager->getDocument(uri);
		if (doc) {
			auto diagnostics = analyzer->analyze(doc);
			publishDiagnostics(uri, diagnostics);
		}
	}
}

void LspServer::handleDidClose(const json &params) {
	std::string uri = params["textDocument"]["uri"].get<std::string>();

	// Clear diagnostics for the closed document
	publishDiagnostics(uri, {});

	docManager->closeDocument(uri);
}

void LspServer::handleDidSave(const json &params) {
	std::string uri = params["textDocument"]["uri"].get<std::string>();

	// Check if this is a stdlib file and reload it if so
	std::string filePath = uriToPath(uri);
	if (analyzer->isStdlibFile(filePath)) {
		analyzer->reloadStdlibFile(filePath);
	}

	// Re-analyze on save
	auto doc = docManager->getDocument(uri);
	if (doc) {
		auto diagnostics = analyzer->analyze(doc);
		publishDiagnostics(uri, diagnostics);
	}
}

json LspServer::handleCodeAction(const json &params) {
	std::string uri = params["textDocument"]["uri"].get<std::string>();
	lsp::Range range;
	range.start.line = params["range"]["start"]["line"].get<int>();
	range.start.character = params["range"]["start"]["character"].get<int>();
	range.end.line = params["range"]["end"]["line"].get<int>();
	range.end.character = params["range"]["end"]["character"].get<int>();

	json actions = json::array();

	// Check if diagnostics are provided in the context
	if (params.contains("context") && params["context"].contains("diagnostics")) {
		auto diagnostics = params["context"]["diagnostics"];

		for (const auto &diag : diagnostics) {
			// Only provide quick fixes for warnings
			if (diag["severity"].get<int>() == 2 && diag.contains("code")) {
				std::string code = diag["code"].get<std::string>();

				if (code == "unused-variable") {
					// Extract the variable name from the message
					std::string message = diag["message"].get<std::string>();
					size_t start = message.find("'");
					size_t end = message.find("'", start + 1);

					if (start != std::string::npos && end != std::string::npos) {
						std::string varName = message.substr(start + 1, end - start - 1);

						// Get the document to find the variable declaration
						auto doc = docManager->getDocument(uri);
						if (doc) {
							lsp::Range diagRange;
							diagRange.start.line = diag["range"]["start"]["line"].get<int>();
							diagRange.start.character = diag["range"]["start"]["character"].get<int>();
							diagRange.end.line = diag["range"]["end"]["line"].get<int>();
							diagRange.end.character = diag["range"]["end"]["character"].get<int>();

							// Find the line containing the variable declaration
							std::istringstream stream(doc->text);
							std::string line;
							int lineNum = 0;
							lsp::Range deleteRange;
							bool found = false;

							while (std::getline(stream, line)) {
								if (lineNum == diagRange.start.line) {
									// This is the line with the variable declaration
									deleteRange.start.line = lineNum;
									deleteRange.start.character = 0;
									deleteRange.end.line = lineNum + 1;
									deleteRange.end.character = 0;
									found = true;
									break;
								}
								lineNum++;
							}

							if (found) {
								// Create a quick fix to remove the unused variable
								json removeAction = {
									{"title", "Remove unused variable '" + varName + "'"},
									{"kind", "quickfix"},
									{"diagnostics", json::array({diag})},
									{"edit", {{"changes", {{uri, json::array({{{"range", {{"start", {{"line", deleteRange.start.line}, {"character", deleteRange.start.character}}}, {"end", {{"line", deleteRange.end.line}, {"character", deleteRange.end.character}}}}}, {"newText", ""}}})}}}}}};

								actions.push_back(removeAction);
							}
						}
					}
				} else if (code == "unnecessary-qualified-name") {
					// Extract the qualified name and unqualified name from the message
					std::string message = diag["message"].get<std::string>();
					size_t qualStart = message.find("'");
					size_t qualEnd = message.find("'", qualStart + 1);
					size_t unqualStart = message.find("'", qualEnd + 1);
					size_t unqualEnd = message.find("'", unqualStart + 1);

					if (qualStart != std::string::npos && qualEnd != std::string::npos &&
						unqualStart != std::string::npos && unqualEnd != std::string::npos) {
						std::string qualifiedName = message.substr(qualStart + 1, qualEnd - qualStart - 1);
						std::string unqualifiedName = message.substr(unqualStart + 1, unqualEnd - unqualStart - 1);

						// Get the document to find the qualified name usage
						auto doc = docManager->getDocument(uri);
						if (doc) {
							lsp::Range diagRange;
							diagRange.start.line = diag["range"]["start"]["line"].get<int>();
							diagRange.start.character = diag["range"]["start"]["character"].get<int>();
							diagRange.end.line = diag["range"]["end"]["line"].get<int>();
							diagRange.end.character = diag["range"]["end"]["character"].get<int>();

							// Find the qualified name in the document
							std::istringstream stream(doc->text);
							std::string line;
							int lineNum = 0;
							lsp::Range replaceRange;
							bool found = false;

							// Qualified names already use dot notation
							std::string sourceQualifiedName = qualifiedName;

							while (std::getline(stream, line)) {
								if (lineNum == diagRange.start.line) {
									// Find the qualified name in this line
									size_t qualPos = line.find(sourceQualifiedName);
									if (qualPos != std::string::npos) {
										replaceRange.start.line = lineNum;
										replaceRange.start.character = qualPos;
										replaceRange.end.line = lineNum;
										replaceRange.end.character = qualPos + sourceQualifiedName.length();
										found = true;
										break;
									}
								}
								lineNum++;
							}

							if (found) {
								// Create a quick fix to replace qualified name with unqualified name
								json simplifyAction = {
									{"title", "Use unqualified name '" + unqualifiedName + "'"},
									{"kind", "quickfix"},
									{"diagnostics", json::array({diag})},
									{"edit", {{"changes", {{uri, json::array({{{"range", {{"start", {{"line", replaceRange.start.line}, {"character", replaceRange.start.character}}}, {"end", {{"line", replaceRange.end.line}, {"character", replaceRange.end.character}}}}}, {"newText", unqualifiedName}}})}}}}}};

								actions.push_back(simplifyAction);
							}
						}
					}
				}
			}
		}
	}

	return actions;
}

json LspServer::handleRename(const json &params) {
	std::string uri = params["textDocument"]["uri"].get<std::string>();
	lsp::Position pos;
	pos.line = params["position"]["line"].get<int>();
	pos.character = params["position"]["character"].get<int>();
	std::string newName = params["newName"].get<std::string>();

	auto doc = docManager->getDocument(uri);
	if (!doc) {
		return nullptr;
	}

	auto workspaceEdit = analyzer->getRename(doc, pos, newName);

	if (!workspaceEdit) {
		return nullptr;
	}

	// Convert WorkspaceEdit to JSON
	json changes = json::object();
	for (const auto &pair : workspaceEdit->changes) {
		json edits = json::array();
		for (const auto &edit : pair.second) {
			edits.push_back({{"range", {{"start", {{"line", edit.range.start.line}, {"character", edit.range.start.character}}}, {"end", {{"line", edit.range.end.line}, {"character", edit.range.end.character}}}}},
							 {"newText", edit.newText}});
		}
		changes[pair.first] = edits;
	}

	return {{"changes", changes}};
}

json LspServer::handleLinkedEditingRange(const json &params) {
	std::string uri = params["textDocument"]["uri"].get<std::string>();
	lsp::Position pos;
	pos.line = params["position"]["line"].get<int>();
	pos.character = params["position"]["character"].get<int>();

	auto doc = docManager->getDocument(uri);
	if (!doc) {
		return nullptr;
	}

	auto ranges = analyzer->getLinkedEditingRanges(doc, pos);

	if (!ranges) {
		return nullptr;
	}

	// Convert ranges to JSON
	json rangesJson = json::array();
	for (const auto &range : *ranges) {
		rangesJson.push_back({{"start", {{"line", range.start.line}, {"character", range.start.character}}},
							  {"end", {{"line", range.end.line}, {"character", range.end.character}}}});
	}

	return {{"ranges", rangesJson}};
}

void LspServer::handleExit(const json &params) {
	std::exit(0);
}

json LspServer::handleFormatting(const json &params) {
	std::string uri = params["textDocument"]["uri"].get<std::string>();

	auto doc = docManager->getDocument(uri);
	if (!doc) {
		return json::array();
	}

	// Get formatting options from the request
	bool insertSpaces = true; // Default to spaces
	int tabSize = 4;		  // Default tab size

	if (params.contains("options")) {
		const auto &options = params["options"];
		if (options.contains("insertSpaces")) {
			insertSpaces = options["insertSpaces"].get<bool>();
		}
		if (options.contains("tabSize")) {
			tabSize = options["tabSize"].get<int>();
		}
	}

	// Format the document
	auto edits = formatter->formatDocument(doc->text, insertSpaces, tabSize);

	// Convert edits to LSP format
	json result = json::array();
	for (const auto &edit : edits) {
		result.push_back({{"range", {{"start", {{"line", edit.range.start.line}, {"character", edit.range.start.character}}}, {"end", {{"line", edit.range.end.line}, {"character", edit.range.end.character}}}}},
						  {"newText", edit.newText}});
	}

	return result;
}

json LspServer::handleRangeFormatting(const json &params) {
	std::string uri = params["textDocument"]["uri"].get<std::string>();

	// Parse the range
	lsp::Range range;
	range.start.line = params["range"]["start"]["line"].get<int>();
	range.start.character = params["range"]["start"]["character"].get<int>();
	range.end.line = params["range"]["end"]["line"].get<int>();
	range.end.character = params["range"]["end"]["character"].get<int>();

	auto doc = docManager->getDocument(uri);
	if (!doc) {
		return json::array();
	}

	// Get formatting options from the request
	bool insertSpaces = true; // Default to spaces
	int tabSize = 4;		  // Default tab size

	if (params.contains("options")) {
		const auto &options = params["options"];
		if (options.contains("insertSpaces")) {
			insertSpaces = options["insertSpaces"].get<bool>();
		}
		if (options.contains("tabSize")) {
			tabSize = options["tabSize"].get<int>();
		}
	}

	// Format the range
	auto edits = formatter->formatRange(doc->text, range, insertSpaces, tabSize);

	// Convert edits to LSP format
	json result = json::array();
	for (const auto &edit : edits) {
		result.push_back({{"range", {{"start", {{"line", edit.range.start.line}, {"character", edit.range.start.character}}}, {"end", {{"line", edit.range.end.line}, {"character", edit.range.end.character}}}}},
						  {"newText", edit.newText}});
	}

	return result;
}

json LspServer::handleSemanticTokensFull(const json &params) {
	std::string uri = params["textDocument"]["uri"].get<std::string>();
	std::cerr << "[DEBUG] Semantic tokens requested for: " << uri << std::endl;

	auto doc = docManager->getDocument(uri);
	if (!doc) {
		std::cerr << "[DEBUG] Document not found for semantic tokens" << std::endl;
		return {{"data", json::array()}};
	}

	// Parse the document to get tokens
	std::vector<Token> tokens = tokenize(doc->text);

	// Track nesting depth for block identifiers
	int currentDepth = 0;
	std::vector<int> depthStack; // Stack to track depths

	// Map block identifier names to their depths (for break/continue target matching)
	std::map<std::string, int> blockIdDepths;

	// Encode semantic tokens (delta-encoded format)
	std::vector<uint32_t> data;
	int prevLine = 0;
	int prevChar = 0;

	// Token type index for "label" (index 22 in our legend)
	const int BLOCK_ID_TOKEN_TYPE = 22;

	// Modifier indices (level0=10, level1=11, ..., level5=15)
	const int LEVEL0_MODIFIER = 10;

	// Helper lambda to add a semantic token
	auto addToken = [&](int line, int column, int length, int depth) {
		int level = depth % 6;
		int modifierBitMask = (1 << (LEVEL0_MODIFIER + level));

		int tokenLine = line - 1;	  // Convert to 0-based
		int tokenColumn = column - 1; // Convert to 0-based

		int deltaLine = tokenLine - prevLine;
		int deltaChar = (deltaLine == 0) ? (tokenColumn - prevChar) : tokenColumn;

		data.push_back(deltaLine);
		data.push_back(deltaChar);
		data.push_back(length);
		data.push_back(BLOCK_ID_TOKEN_TYPE);
		data.push_back(modifierBitMask);

		prevLine = tokenLine;
		prevChar = tokenColumn;
	};

	bool lastWasBlockOpener = false; // Track if previous token opens/closes a block
	bool lastWasEnd = false;
	bool lastWasBreakOrContinue = false;
	bool lastWasElse = false; // Track if previous token was 'else'

	for (size_t i = 0; i < tokens.size(); ++i) {
		const Token &token = tokens[i];

		// Track nesting depth based on keywords
		if (token.type == TokenType::KEYWORD) {
			if (token.value == "function" || token.value == "struct") {
				// Function and struct names are implied block identifiers
				// Look for the name (next IDENTIFIER token)
				if (i + 1 < tokens.size() && tokens[i + 1].type == TokenType::IDENTIFIER) {
					const Token &name = tokens[i + 1];
					// Add semantic token for the name at current depth
					addToken(name.line, name.column, name.value.length(), currentDepth);
					// Store the block ID name and its depth for break/continue targeting
					blockIdDepths[name.value] = currentDepth;
				}

				// Now increment depth for body
				depthStack.push_back(currentDepth);
				currentDepth++;
				lastWasBlockOpener = false;
				lastWasEnd = false;
				lastWasElse = false;
			} else if (token.value == "if" || token.value == "while" || token.value == "for") {
				// These blocks have explicit block IDs
				// Push current depth (this is the depth of the block identifier itself)
				depthStack.push_back(currentDepth);
				lastWasBlockOpener = true;
				lastWasEnd = false;
				lastWasElse = false;
				// Note: We increment currentDepth AFTER processing the block ID
			} else if (token.value == "else") {
				// else continues the same block - use same depth as the if
				// Don't push to stack since we're not starting a new nesting level
				lastWasBlockOpener = false;
				lastWasEnd = false;
				lastWasElse = true;
			} else if (token.value == "end") {
				// Closing a block - the BLOCK_ID should be at the same depth as when it opened
				// So we set lastWasEnd but don't pop the stack yet
				lastWasBlockOpener = false;
				lastWasEnd = true;
				lastWasElse = false;
				// Note: We pop the stack AFTER processing the closing block ID
			} else if (token.value == "break" || token.value == "continue") {
				// Set flag to handle the following BLOCK_ID as a target label
				lastWasBreakOrContinue = true;
			} else {
				// Any other keyword (return, var, let, etc.) - reset else flag
				// This handles single-line else like "else return 1"
				lastWasElse = false;
			}
			// Don't reset lastWasBlockOpener/lastWasEnd here - wait for BLOCK_ID
		}

		// Highlight BLOCK_ID tokens
		if (token.type == TokenType::BLOCK_ID) {
			if (lastWasBreakOrContinue) {
				// This is a target label in break/continue - color it the same as the matching block
				auto it = blockIdDepths.find(token.value);
				if (it != blockIdDepths.end()) {
					addToken(token.line, token.column, token.value.length() + 2, it->second);
				}
			} else if (lastWasElse) {
				// else block identifier - use the depth of the matching if block
				auto it = blockIdDepths.find(token.value);
				if (it != blockIdDepths.end()) {
					addToken(token.line, token.column, token.value.length() + 2, it->second);
				} else {
					// Fallback: use current depth - 1 (one level up from inside the if body)
					int depth = currentDepth > 0 ? currentDepth - 1 : 0;
					addToken(token.line, token.column, token.value.length() + 2, depth);
				}
				// Don't increment currentDepth - else continues at the same level
			} else if (lastWasBlockOpener) {
				// Opening block identifier - use current depth
				addToken(token.line, token.column, token.value.length() + 2, currentDepth);

				// Store the block ID name and its depth for break/continue targeting
				blockIdDepths[token.value] = currentDepth;

				// Now increment depth for the block's body
				currentDepth++;
			} else if (lastWasEnd) {
				// Closing block identifier - use the depth from the stack (same as opening)
				int depth = depthStack.empty() ? 0 : depthStack.back();
				addToken(token.line, token.column, token.value.length() + 2, depth);

				// Now pop the stack to restore depth
				if (!depthStack.empty()) {
					currentDepth = depthStack.back();
					depthStack.pop_back();
				}
			}

			// Reset all flags after processing any BLOCK_ID
			lastWasBlockOpener = false;
			lastWasEnd = false;
			lastWasBreakOrContinue = false;
			lastWasElse = false;
		}
	}

	std::cerr << "[DEBUG] Generated " << (data.size() / 5) << " semantic tokens" << std::endl;
	return {{"data", data}};
}

void LspServer::publishDiagnostics(const std::string &uri, const std::vector<lsp::Diagnostic> &diagnostics) {
	json diags = json::array();

	for (const auto &diag : diagnostics) {
		json item = {
			{"range", {{"start", {{"line", diag.range.start.line}, {"character", diag.range.start.character}}}, {"end", {{"line", diag.range.end.line}, {"character", diag.range.end.character}}}}},
			{"severity", diag.severity},
			{"message", diag.message}};

		if (diag.source.has_value()) {
			item["source"] = diag.source.value();
		}

		if (diag.code.has_value()) {
			item["code"] = diag.code.value();
		}

		diags.push_back(item);
	}

	json params = {
		{"uri", uri},
		{"diagnostics", diags}};

	rpcHandler->sendNotification("textDocument/publishDiagnostics", params);
}
