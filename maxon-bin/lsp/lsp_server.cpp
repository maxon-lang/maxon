#include "lsp_server.h"
#include "features/code_actions.h"
#include "features/completion.h"
#include "features/definition.h"
#include "features/document_symbols.h"
#include "features/folding.h"
#include "features/formatting.h"
#include "features/hover.h"
#include "features/linked_editing.h"
#include "features/references.h"
#include "features/rename.h"
#include "features/semantic_tokens.h"
#include "features/signature_help.h"
#include "json_rpc.h"
#include <chrono>
#include <filesystem>
#include <fstream>
#include <sstream>

namespace maxon_lsp {

using json = maxon::lsp::json;
namespace lsp = maxon::lsp;

// ============================================================================
// Constructor
// ============================================================================

LSPServer::LSPServer(std::unique_ptr<lsp::Transport> transport)
	: transport_(std::move(transport)) {
	registerHandlers();
}

// ============================================================================
// Handler Registration
// ============================================================================

void LSPServer::registerHandlers() {
	// Lifecycle requests
	requestHandlers_["initialize"] = [this](const json &params) {
		return handleInitialize(params);
	};
	requestHandlers_["shutdown"] = [this](const json &params) {
		return handleShutdown(params);
	};

	// Lifecycle notifications
	notificationHandlers_["initialized"] = [this](const json &params) {
		handleInitialized(params);
	};
	notificationHandlers_["exit"] = [this](const json &params) {
		handleExit(params);
	};

	// Document synchronization notifications
	notificationHandlers_["textDocument/didOpen"] = [this](const json &params) {
		handleDidOpen(params);
	};
	notificationHandlers_["textDocument/didChange"] = [this](const json &params) {
		handleDidChange(params);
	};
	notificationHandlers_["textDocument/didClose"] = [this](const json &params) {
		handleDidClose(params);
	};
	notificationHandlers_["textDocument/didSave"] = [this](const json &params) {
		handleDidSave(params);
	};

	// Language feature requests
	requestHandlers_["textDocument/completion"] = [this](const json &params) {
		return handleCompletion(params);
	};
	requestHandlers_["textDocument/hover"] = [this](const json &params) {
		return handleHover(params);
	};
	requestHandlers_["textDocument/signatureHelp"] = [this](const json &params) {
		return handleSignatureHelp(params);
	};
	requestHandlers_["textDocument/definition"] = [this](const json &params) {
		return handleDefinition(params);
	};
	requestHandlers_["textDocument/references"] = [this](const json &params) {
		return handleReferences(params);
	};
	requestHandlers_["textDocument/documentSymbol"] = [this](const json &params) {
		return handleDocumentSymbol(params);
	};
	requestHandlers_["textDocument/prepareRename"] = [this](const json &params) {
		return handlePrepareRename(params);
	};
	requestHandlers_["textDocument/rename"] = [this](const json &params) {
		return handleRename(params);
	};
	requestHandlers_["textDocument/codeAction"] = [this](const json &params) {
		return handleCodeAction(params);
	};
	requestHandlers_["textDocument/formatting"] = [this](const json &params) {
		return handleFormatting(params);
	};
	requestHandlers_["textDocument/foldingRange"] = [this](const json &params) {
		return handleFoldingRange(params);
	};
	requestHandlers_["textDocument/linkedEditingRange"] = [this](const json &params) {
		return handleLinkedEditingRange(params);
	};
	requestHandlers_["textDocument/semanticTokens/full"] = [this](const json &params) {
		return handleSemanticTokensFull(params);
	};
}

// ============================================================================
// Main Message Loop
// ============================================================================

int LSPServer::run() {
	while (transport_->isConnected()) {
		// Read the next message from the transport
		auto messageOpt = transport_->readMessage();
		if (!messageOpt.has_value()) {
			// EOF or connection closed
			break;
		}

		const lsp::JsonRpcMessage &message = messageOpt.value();

		if (message.isRequest()) {
			// Handle request (has id, expects response)
			const std::string &method = message.method.value();
			const json &params = message.params.value_or(json::object());

			// Look up the handler
			auto it = requestHandlers_.find(method);
			if (it != requestHandlers_.end()) {
				try {
					// Call the handler and send the response
					json result = it->second(params);
					lsp::JsonRpcMessage response = lsp::JsonRpcMessage::createResponse(
						message.id.value(), result);
					transport_->writeMessage(response);
				} catch (const std::exception &e) {
					// Send internal error response
					lsp::JsonRpcMessage errorResponse = lsp::JsonRpcMessage::createErrorResponse(
						message.id.value(),
						lsp::ErrorCode::InternalError,
						std::string("Internal error: ") + e.what());
					transport_->writeMessage(errorResponse);
				}
			} else {
				// Method not found
				lsp::JsonRpcMessage errorResponse = lsp::JsonRpcMessage::createErrorResponse(
					message.id.value(),
					lsp::ErrorCode::MethodNotFound,
					"Method not found: " + method);
				transport_->writeMessage(errorResponse);
			}
		} else if (message.isNotification()) {
			// Handle notification (no id, no response)
			const std::string &method = message.method.value();
			const json &params = message.params.value_or(json::object());

			// Look up the handler
			auto it = notificationHandlers_.find(method);
			if (it != notificationHandlers_.end()) {
				try {
					it->second(params);
				} catch (const std::exception &e) {
					// Notifications don't send error responses, just log
					// In a production server, we would log this error
					(void)e; // Suppress unused variable warning
				}
			}
			// Unknown notifications are silently ignored per LSP spec
		}

		// Check for shutdown + exit sequence
		if (shutdownRequested_) {
			// After shutdown is requested, we wait for exit notification
			// The exit handler will break the loop by closing the transport
		}
	}

	// Return 0 if shutdown was properly requested, 1 otherwise
	return shutdownRequested_ ? 0 : 1;
}

// ============================================================================
// Lifecycle Handlers
// ============================================================================

json LSPServer::handleInitialize(const json &params) {
	// Extract workspace root from params
	if (params.contains("rootUri") && !params["rootUri"].is_null()) {
		workspaceRoot_ = uriToPath(params["rootUri"].get<std::string>());
	} else if (params.contains("rootPath") && !params["rootPath"].is_null()) {
		// Deprecated but still supported
		workspaceRoot_ = params["rootPath"].get<std::string>();
	}

	// Extract client capabilities
	if (params.contains("capabilities")) {
		const json &caps = params["capabilities"];

		// Check workspace capabilities
		if (caps.contains("workspace")) {
			const json &workspace = caps["workspace"];
			if (workspace.contains("workspaceFolders")) {
				supportsWorkspaceFolders_ = workspace["workspaceFolders"].get<bool>();
			}
			if (workspace.contains("workspaceEdit") &&
				workspace["workspaceEdit"].contains("documentChanges")) {
				supportsDocumentChanges_ = workspace["workspaceEdit"]["documentChanges"].get<bool>();
			}
		}
	}

	// Load stdlib from workspace if available
	if (!workspaceRoot_.empty()) {
		std::filesystem::path stdlibPath = std::filesystem::path(workspaceRoot_) / "stdlib";
		if (std::filesystem::exists(stdlibPath)) {
			stdlib_ = loadStdlib(stdlibPath.string());
		}
	}

	// Build the initialize result
	json result;
	result["capabilities"] = lsp::toJson(buildCapabilities());
	result["serverInfo"] = {
		{"name", "maxon-lsp"},
		{"version", "0.1.0"}};

	initialized_ = true;
	return result;
}

void LSPServer::handleInitialized(const json & /*params*/) {
	// Analyze all stdlib files and publish diagnostics for them
	// This allows users to see errors in stdlib files without opening them
	if (!workspaceRoot_.empty()) {
		std::filesystem::path stdlibPath = std::filesystem::path(workspaceRoot_) / "stdlib";
		if (std::filesystem::exists(stdlibPath)) {
			analyzeStdlibFiles(stdlibPath.string());
		}
	}
}

json LSPServer::handleShutdown(const json & /*params*/) {
	shutdownRequested_ = true;
	// Return null as per LSP spec
	return nullptr;
}

void LSPServer::handleExit(const json & /*params*/) {
	// Close the transport to exit the message loop
	// The exit code depends on whether shutdown was called first
	// This is handled in run()
}

// ============================================================================
// Document Synchronization Handlers
// ============================================================================

void LSPServer::handleDidOpen(const json &params) {
	lsp::DidOpenTextDocumentParams openParams = lsp::didOpenTextDocumentParamsFromJson(params);

	documentManager_.openDocument(
		openParams.textDocument.uri,
		openParams.textDocument.languageId,
		openParams.textDocument.version,
		openParams.textDocument.text);

	// Trigger analysis
	analyzeDocument(openParams.textDocument.uri);
}

void LSPServer::handleDidChange(const json &params) {
	lsp::DidChangeTextDocumentParams changeParams = lsp::didChangeTextDocumentParamsFromJson(params);

	documentManager_.updateDocument(
		changeParams.textDocument.uri,
		changeParams.textDocument.version,
		changeParams.contentChanges);

	// Trigger analysis
	analyzeDocument(changeParams.textDocument.uri);
}

void LSPServer::handleDidClose(const json &params) {
	lsp::DidCloseTextDocumentParams closeParams = lsp::didCloseTextDocumentParamsFromJson(params);

	documentManager_.closeDocument(closeParams.textDocument.uri);

	// Clear diagnostics for the closed document
	publishDiagnostics(closeParams.textDocument.uri, {});
}

void LSPServer::handleDidSave(const json &params) {
	lsp::DidSaveTextDocumentParams saveParams = lsp::didSaveTextDocumentParamsFromJson(params);

	// If the client included the text, update the document
	if (saveParams.text.has_value()) {
		auto docOpt = documentManager_.getDocument(saveParams.textDocument.uri);
		if (docOpt.has_value()) {
			Document *doc = docOpt.value();
			documentManager_.replaceDocument(
				saveParams.textDocument.uri,
				doc->version,
				saveParams.text.value());
		}
	}

	// Re-analyze on save
	analyzeDocument(saveParams.textDocument.uri);
}

// ============================================================================
// Analysis and Diagnostics
// ============================================================================

void LSPServer::analyzeDocument(const std::string &uri) {
	// Get the document
	auto docOpt = documentManager_.getDocument(uri);
	if (!docOpt.has_value()) {
		return;
	}

	Document *doc = docOpt.value();

	// Convert URI to file path for the analyzer
	std::string filePath = uriToPath(uri);

	// For .test files, truncate content at "---" separator (marks end of Maxon code)
	std::string content = doc->content;
	if (filePath.size() >= 5 && filePath.substr(filePath.size() - 5) == ".test") {
		size_t sepPos = content.find("\n---");
		if (sepPos != std::string::npos) {
			content = content.substr(0, sepPos);
		}
	}

	// Measure analysis time
	auto startTime = std::chrono::steady_clock::now();

	// Run analysis with stdlib support
	LSPAnalysisResult result = analyzeForLSP(content, filePath, stdlib_);

	auto endTime = std::chrono::steady_clock::now();
	auto duration = std::chrono::duration_cast<std::chrono::milliseconds>(endTime - startTime);

	// Create analysis cache entry
	AnalysisCache cache;
	cache.ast = std::move(result.ast);
	cache.symbols = std::move(result.symbols);
	cache.parseErrors = std::move(result.parseErrors);
	cache.semanticErrors = std::move(result.semanticErrors);
	cache.variables = std::move(result.variables);
	cache.functions = std::move(result.functions);
	cache.structs = std::move(result.structs);
	cache.interfaces = std::move(result.interfaces);
	cache.version = doc->version;
	cache.analysisTimeMs = duration.count();

	// Convert errors to diagnostics
	std::vector<lsp::Diagnostic> diagnostics;

	for (const ParseError &error : cache.parseErrors) {
		diagnostics.push_back(parseErrorToDiagnostic(error));
	}

	for (const SemanticError &error : cache.semanticErrors) {
		diagnostics.push_back(semanticErrorToDiagnostic(error));
	}

	// Update the cache
	documentManager_.setAnalysis(uri, std::move(cache));

	// Publish diagnostics
	publishDiagnostics(uri, diagnostics);
}

void LSPServer::publishDiagnostics(const std::string &uri,
								   const std::vector<lsp::Diagnostic> &diagnostics) {
	lsp::PublishDiagnosticsParams params;
	params.uri = uri;
	params.diagnostics = diagnostics;

	// Get document version if available
	auto docOpt = documentManager_.getDocument(uri);
	if (docOpt.has_value()) {
		params.version = docOpt.value()->version;
	}

	sendNotification("textDocument/publishDiagnostics", lsp::toJson(params));
}

void LSPServer::analyzeStdlibFiles(const std::string &stdlibPath) {
	// Find all .maxon files in the stdlib directory
	std::vector<std::string> stdlibFiles;
	for (const auto &entry : std::filesystem::recursive_directory_iterator(stdlibPath)) {
		if (entry.is_regular_file() && entry.path().extension() == ".maxon") {
			stdlibFiles.push_back(entry.path().string());
		}
	}

	// Analyze each stdlib file and publish diagnostics
	for (const auto &filePath : stdlibFiles) {
		// Read the file content
		std::ifstream file(filePath);
		if (!file) {
			continue;
		}

		std::stringstream buffer;
		buffer << file.rdbuf();
		std::string content = buffer.str();

		// Convert file path to URI
		std::string uri = pathToUri(filePath);

		// Open the document in the document manager (so analyzeDocument can find it)
		documentManager_.openDocument(uri, "maxon", 0, content);

		// Analyze the document
		analyzeDocument(uri);
	}
}

// ============================================================================
// Helper Methods
// ============================================================================

void LSPServer::sendNotification(const std::string &method, const json &params) {
	lsp::JsonRpcMessage notification = lsp::JsonRpcMessage::createNotification(method, params);
	transport_->writeMessage(notification);
}

void LSPServer::shutdown() {
	shutdownRequested_ = true;
}

lsp::ServerCapabilities LSPServer::buildCapabilities() {
	lsp::ServerCapabilities caps;

	// Text document sync - incremental updates
	lsp::TextDocumentSyncOptions syncOptions;
	syncOptions.openClose = true;
	syncOptions.change = lsp::TextDocumentSyncKind::Incremental;
	syncOptions.save = lsp::TextDocumentSyncOptions::SaveOptions{true};
	caps.textDocumentSync = syncOptions;

	// Completion provider with trigger characters
	lsp::CompletionOptions completionOptions;
	completionOptions.triggerCharacters = {".", "'"};
	completionOptions.resolveProvider = false;
	caps.completionProvider = completionOptions;

	// Hover support
	caps.hoverProvider = true;

	// Signature help with trigger characters
	lsp::SignatureHelpOptions sigHelpOptions;
	sigHelpOptions.triggerCharacters = {"(", ","};
	caps.signatureHelpProvider = sigHelpOptions;

	// Go to definition
	caps.definitionProvider = true;

	// Find references
	caps.referencesProvider = true;

	// Document symbols (outline)
	caps.documentSymbolProvider = true;

	// Rename with prepare support
	lsp::RenameOptions renameOptions;
	renameOptions.prepareProvider = true;
	caps.renameProvider = renameOptions;

	// Code actions
	caps.codeActionProvider = true;

	// Document formatting
	caps.documentFormattingProvider = true;

	// Folding ranges
	caps.foldingRangeProvider = true;

	// Linked editing (for block labels)
	caps.linkedEditingRangeProvider = true;

	// Semantic tokens (for block identifier colorization)
	lsp::SemanticTokensOptions semanticTokensOptions;
	semanticTokensOptions.legend = SemanticTokensProvider::getLegend();
	semanticTokensOptions.full = true;	 // Support full document tokens
	semanticTokensOptions.range = false; // Don't support range queries
	caps.semanticTokensProvider = semanticTokensOptions;

	return caps;
}

lsp::Diagnostic LSPServer::parseErrorToDiagnostic(const ParseError &error) {
	lsp::Diagnostic diag;

	// Convert 1-based line/column to 0-based
	int startLine = error.line > 0 ? error.line - 1 : 0;
	int startChar = error.column > 0 ? error.column - 1 : 0;
	int endLine = error.endLine > 0 ? error.endLine - 1 : startLine;
	int endChar = error.endColumn > 0 ? error.endColumn - 1 : startChar;

	// Ensure end is at least at start
	if (endLine < startLine || (endLine == startLine && endChar <= startChar)) {
		endChar = startChar + 1;
	}

	diag.range = lsp::Range(startLine, startChar, endLine, endChar);
	diag.message = error.message;
	diag.source = "maxon";

	// Convert severity (1=Error, 2=Warning, 3=Info, 4=Hint)
	switch (error.severity) {
	case 1:
		diag.severity = lsp::DiagnosticSeverity::Error;
		break;
	case 2:
		diag.severity = lsp::DiagnosticSeverity::Warning;
		break;
	case 3:
		diag.severity = lsp::DiagnosticSeverity::Information;
		break;
	case 4:
		diag.severity = lsp::DiagnosticSeverity::Hint;
		break;
	default:
		diag.severity = lsp::DiagnosticSeverity::Error;
		break;
	}

	return diag;
}

lsp::Diagnostic LSPServer::semanticErrorToDiagnostic(const SemanticError &error) {
	lsp::Diagnostic diag;

	// Convert 1-based line/column to 0-based
	int startLine = error.line > 0 ? error.line - 1 : 0;
	int startChar = error.column > 0 ? error.column - 1 : 0;

	// Semantic errors typically don't have end positions, so we mark a single position
	diag.range = lsp::Range(startLine, startChar, startLine, startChar + 1);
	diag.message = error.message;
	diag.source = "maxon";

	// Set error code if available
	if (!error.code.empty()) {
		diag.code = error.code;
	}

	// Convert severity (1=Error, 2=Warning)
	switch (error.severity) {
	case 1:
		diag.severity = lsp::DiagnosticSeverity::Error;
		break;
	case 2:
		diag.severity = lsp::DiagnosticSeverity::Warning;
		break;
	default:
		diag.severity = lsp::DiagnosticSeverity::Error;
		break;
	}

	return diag;
}

// ============================================================================
// Language Feature Handlers
// ============================================================================

json LSPServer::handleCompletion(const json &params) {
	auto completionParams = lsp::completionParamsFromJson(params);
	const std::string &uri = completionParams.textDocument.uri;

	auto docOpt = documentManager_.getDocument(uri);
	if (!docOpt.has_value()) {
		return json::array();
	}

	Document *doc = docOpt.value();
	const AnalysisCache *cache = documentManager_.getAnalysis(uri);

	CompletionProvider provider;
	auto result = provider.getCompletions(*doc, completionParams.position, cache, stdlib_);

	return lsp::toJson(result);
}

json LSPServer::handleHover(const json &params) {
	auto hoverParams = lsp::hoverParamsFromJson(params);
	const std::string &uri = hoverParams.textDocument.uri;

	auto docOpt = documentManager_.getDocument(uri);
	if (!docOpt.has_value()) {
		return nullptr;
	}

	Document *doc = docOpt.value();
	const AnalysisCache *cache = documentManager_.getAnalysis(uri);

	HoverProvider provider;
	auto result = provider.getHover(*doc, hoverParams.position, cache, stdlib_);

	if (result.has_value()) {
		return lsp::toJson(result.value());
	}
	return nullptr;
}

json LSPServer::handleSignatureHelp(const json &params) {
	auto sigHelpParams = lsp::signatureHelpParamsFromJson(params);
	const std::string &uri = sigHelpParams.textDocument.uri;

	auto docOpt = documentManager_.getDocument(uri);
	if (!docOpt.has_value()) {
		return nullptr;
	}

	Document *doc = docOpt.value();
	const AnalysisCache *cache = documentManager_.getAnalysis(uri);

	SignatureHelpProvider provider;
	auto result = provider.getSignatureHelp(*doc, sigHelpParams.position, cache, stdlib_);

	if (result.has_value()) {
		return lsp::toJson(result.value());
	}
	return nullptr;
}

json LSPServer::handleDefinition(const json &params) {
	auto defParams = lsp::definitionParamsFromJson(params);
	const std::string &uri = defParams.textDocument.uri;

	auto docOpt = documentManager_.getDocument(uri);
	if (!docOpt.has_value()) {
		return nullptr;
	}

	Document *doc = docOpt.value();
	const AnalysisCache *cache = documentManager_.getAnalysis(uri);

	DefinitionProvider provider;
	auto result = provider.getDefinition(*doc, defParams.position, cache, stdlib_, workspaceRoot_);

	if (!result.has_value()) {
		return nullptr;
	}

	// Return Location or array of LocationLinks
	return std::visit([](auto &&arg) -> json {
		using T = std::decay_t<decltype(arg)>;
		if constexpr (std::is_same_v<T, lsp::Location>) {
			return lsp::toJson(arg);
		} else {
			json arr = json::array();
			for (const auto &link : arg) {
				arr.push_back(lsp::toJson(link));
			}
			return arr;
		}
	},
					  result.value());
}

json LSPServer::handleReferences(const json &params) {
	auto refParams = lsp::referenceParamsFromJson(params);
	const std::string &uri = refParams.textDocument.uri;

	auto docOpt = documentManager_.getDocument(uri);
	if (!docOpt.has_value()) {
		return json::array();
	}

	Document *doc = docOpt.value();
	const AnalysisCache *cache = documentManager_.getAnalysis(uri);

	bool includeDeclaration = refParams.context.includeDeclaration;

	ReferencesProvider provider;
	auto locations = provider.findReferences(*doc, refParams.position, includeDeclaration, cache, workspaceRoot_);

	json result = json::array();
	for (const auto &loc : locations) {
		result.push_back(lsp::toJson(loc));
	}
	return result;
}

json LSPServer::handleDocumentSymbol(const json &params) {
	auto symbolParams = lsp::documentSymbolParamsFromJson(params);
	const std::string &uri = symbolParams.textDocument.uri;

	auto docOpt = documentManager_.getDocument(uri);
	if (!docOpt.has_value()) {
		return json::array();
	}

	Document *doc = docOpt.value();
	const AnalysisCache *cache = documentManager_.getAnalysis(uri);

	DocumentSymbolsProvider provider;
	auto symbols = provider.getDocumentSymbols(*doc, cache);

	json result = json::array();
	for (const auto &sym : symbols) {
		result.push_back(lsp::toJson(sym));
	}
	return result;
}

json LSPServer::handlePrepareRename(const json &params) {
	auto posParams = lsp::textDocumentPositionParamsFromJson(params);
	const std::string &uri = posParams.textDocument.uri;

	auto docOpt = documentManager_.getDocument(uri);
	if (!docOpt.has_value()) {
		return nullptr;
	}

	Document *doc = docOpt.value();
	const AnalysisCache *cache = documentManager_.getAnalysis(uri);

	RenameProvider provider;
	auto result = provider.prepareRename(*doc, posParams.position, cache);

	if (result.has_value()) {
		return lsp::toJson(result.value());
	}
	return nullptr;
}

json LSPServer::handleRename(const json &params) {
	auto renameParams = lsp::renameParamsFromJson(params);
	const std::string &uri = renameParams.textDocument.uri;

	auto docOpt = documentManager_.getDocument(uri);
	if (!docOpt.has_value()) {
		return nullptr;
	}

	Document *doc = docOpt.value();
	const AnalysisCache *cache = documentManager_.getAnalysis(uri);

	RenameProvider provider;
	auto result = provider.rename(*doc, renameParams.position, renameParams.newName, cache, workspaceRoot_);

	if (result.has_value()) {
		return lsp::toJson(result.value());
	}
	return nullptr;
}

json LSPServer::handleCodeAction(const json &params) {
	auto codeActionParams = lsp::codeActionParamsFromJson(params);
	const std::string &uri = codeActionParams.textDocument.uri;

	auto docOpt = documentManager_.getDocument(uri);
	if (!docOpt.has_value()) {
		return json::array();
	}

	Document *doc = docOpt.value();
	const AnalysisCache *cache = documentManager_.getAnalysis(uri);

	CodeActionsProvider provider;
	auto actions = provider.getCodeActions(*doc, codeActionParams.range, codeActionParams.context, cache);

	json result = json::array();
	for (const auto &action : actions) {
		result.push_back(lsp::toJson(action));
	}
	return result;
}

json LSPServer::handleFormatting(const json &params) {
	auto formatParams = lsp::documentFormattingParamsFromJson(params);
	const std::string &uri = formatParams.textDocument.uri;

	auto docOpt = documentManager_.getDocument(uri);
	if (!docOpt.has_value()) {
		return json::array();
	}

	Document *doc = docOpt.value();
	const AnalysisCache *cache = documentManager_.getAnalysis(uri);

	FormattingProvider provider;
	auto edits = provider.formatDocument(*doc, formatParams.options, cache);

	json result = json::array();
	for (const auto &edit : edits) {
		result.push_back(lsp::toJson(edit));
	}
	return result;
}

json LSPServer::handleFoldingRange(const json &params) {
	auto foldingParams = lsp::foldingRangeParamsFromJson(params);
	const std::string &uri = foldingParams.textDocument.uri;

	auto docOpt = documentManager_.getDocument(uri);
	if (!docOpt.has_value()) {
		return json::array();
	}

	Document *doc = docOpt.value();
	const AnalysisCache *cache = documentManager_.getAnalysis(uri);

	FoldingRangeProvider provider;
	auto ranges = provider.getFoldingRanges(*doc, cache);

	json result = json::array();
	for (const auto &range : ranges) {
		result.push_back(lsp::toJson(range));
	}
	return result;
}

json LSPServer::handleLinkedEditingRange(const json &params) {
	auto linkedParams = lsp::linkedEditingRangeParamsFromJson(params);
	const std::string &uri = linkedParams.textDocument.uri;

	auto docOpt = documentManager_.getDocument(uri);
	if (!docOpt.has_value()) {
		return nullptr;
	}

	Document *doc = docOpt.value();
	const AnalysisCache *cache = documentManager_.getAnalysis(uri);

	LinkedEditingProvider provider;
	auto result = provider.getLinkedEditingRanges(*doc, linkedParams.position, cache);

	if (result.has_value()) {
		return lsp::toJson(result.value());
	}
	return nullptr;
}

json LSPServer::handleSemanticTokensFull(const json &params) {
	auto semanticParams = lsp::semanticTokensParamsFromJson(params);
	const std::string &uri = semanticParams.textDocument.uri;

	auto docOpt = documentManager_.getDocument(uri);
	if (!docOpt.has_value()) {
		return nullptr;
	}

	Document *doc = docOpt.value();
	const AnalysisCache *cache = documentManager_.getAnalysis(uri);

	SemanticTokensProvider provider;
	auto result = provider.getSemanticTokens(*doc, cache);

	if (result.has_value()) {
		return lsp::toJson(result.value());
	}
	return nullptr;
}

} // namespace maxon_lsp
