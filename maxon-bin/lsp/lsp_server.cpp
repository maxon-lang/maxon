#include "lsp_server.h"
#include "../file_utils.h"
#include "../lexer.h"
#include "../parser.h"
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
#include <algorithm>
#include <chrono>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <sstream>

namespace maxon_lsp {

using json = maxon::lsp::json;
namespace lsp = maxon::lsp;

// Normalizes a file path for consistent comparison across different sources
static std::string normalizeFilePath(const std::string &path) {
	std::string result = path;
#ifdef _WIN32
	std::transform(result.begin(), result.end(), result.begin(),
				   [](unsigned char c) { return std::tolower(c); });
	std::replace(result.begin(), result.end(), '/', '\\');
#endif
	return result;
}

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

	// Custom Maxon requests
	requestHandlers_["maxon/generateIR"] = [this](const json &params) {
		return handleGenerateIR(params);
	};
	requestHandlers_["maxon/generateAsm"] = [this](const json &params) {
		return handleGenerateAsm(params);
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
	const std::string &uri = changeParams.textDocument.uri;

	documentManager_.updateDocument(
		uri,
		changeParams.textDocument.version,
		changeParams.contentChanges);

	// Check if this is a stdlib file - if so, reload stdlib and re-analyze dependents
	bool isStdlib = isStdlibFile(uri);
	if (isStdlib) {
		reloadStdlib();
		// Re-analyze the changed stdlib file first
		analyzeDocument(uri);
		// Then re-analyze all non-stdlib documents that might depend on it
		reanalyzeNonStdlibDocuments();
	} else {
		// Regular file - just analyze it
		analyzeDocument(uri);
	}
}

void LSPServer::handleDidClose(const json &params) {
	lsp::DidCloseTextDocumentParams closeParams = lsp::didCloseTextDocumentParamsFromJson(params);

	documentManager_.closeDocument(closeParams.textDocument.uri);

	// Clear diagnostics for the closed document
	publishDiagnostics(closeParams.textDocument.uri, {});
}

void LSPServer::handleDidSave(const json &params) {
	lsp::DidSaveTextDocumentParams saveParams = lsp::didSaveTextDocumentParamsFromJson(params);
	const std::string &uri = saveParams.textDocument.uri;

	// If the client included the text, update the document
	if (saveParams.text.has_value()) {
		auto docOpt = documentManager_.getDocument(uri);
		if (docOpt.has_value()) {
			Document *doc = docOpt.value();
			documentManager_.replaceDocument(
				uri,
				doc->version,
				saveParams.text.value());
		}
	}

	// Check if this is a stdlib file - if so, reload stdlib and re-analyze dependents
	if (isStdlibFile(uri)) {
		reloadStdlib();
		// Re-analyze the saved stdlib file first
		analyzeDocument(uri);
		// Then re-analyze all non-stdlib documents that might depend on it
		reanalyzeNonStdlibDocuments();
	} else {
		// Invalidate project cache when a file is saved (exported symbols may have changed)
		std::string filePath = uriToPath(uri);
		std::string projectRoot = findProjectRoot(filePath);
		if (!projectRoot.empty()) {
			invalidateProjectSymbols(projectRoot);
		}

		// Regular file - just analyze it
		analyzeDocument(uri);
	}
}

// ============================================================================
// Analysis and Diagnostics
// ============================================================================

// Find the project root by walking up directories until we find build.maxon
std::string LSPServer::findProjectRoot(const std::string &filePath) {
	try {
		std::filesystem::path current = std::filesystem::absolute(filePath);
		if (std::filesystem::is_regular_file(current)) {
			current = current.parent_path();
		}

		// Walk up the directory tree looking for build.maxon
		while (!current.empty() && current.has_parent_path()) {
			std::filesystem::path buildMaxon = current / "build.maxon";
			if (std::filesystem::exists(buildMaxon)) {
				return normalizeFilePath(current.string());
			}

			// Don't go above the workspace root
			if (!workspaceRoot_.empty()) {
				std::string normalizedCurrent = normalizeFilePath(current.string());
				std::string normalizedWorkspace = normalizeFilePath(workspaceRoot_);
				if (normalizedCurrent == normalizedWorkspace) {
					break;
				}
			}

			std::filesystem::path parent = current.parent_path();
			if (parent == current) {
				break; // Reached root
			}
			current = parent;
		}
	} catch (...) {
		// Ignore errors during directory traversal
	}

	return ""; // No project root found
}

// Get cached project symbols, loading them if necessary
StdlibSymbols &LSPServer::getProjectSymbols(const std::string &projectRoot) {
	auto it = projectCache_.find(projectRoot);
	if (it != projectCache_.end() && it->second.symbolsLoaded) {
		return it->second.symbols;
	}

	// Create or update project info
	ProjectInfo &project = projectCache_[projectRoot];
	project.rootPath = projectRoot;
	project.symbols = StdlibSymbols{};
	project.files.clear();

	try {
		// Recursively find all .maxon files in the project
		for (const auto &entry :
			 std::filesystem::recursive_directory_iterator(projectRoot)) {
			if (!entry.is_regular_file())
				continue;
			if (entry.path().extension() != ".maxon")
				continue;
			// Skip build.maxon itself
			if (entry.path().filename() == "build.maxon")
				continue;

			std::string siblingPath =
				normalizeFilePath(std::filesystem::absolute(entry.path()).string());

			// Skip stdlib files
			if (!workspaceRoot_.empty()) {
				std::filesystem::path stdlibPath =
					std::filesystem::path(workspaceRoot_) / "stdlib";
				if (std::filesystem::exists(stdlibPath)) {
					auto absStdlib = std::filesystem::absolute(stdlibPath);
					auto relative =
						std::filesystem::path(siblingPath).lexically_relative(absStdlib);
					if (!relative.empty() && relative.native()[0] != '.') {
						continue; // File is in stdlib, skip it
					}
				}
			}

			project.files.insert(siblingPath);

			// Read file content - check document manager first for open files
			std::string source;
			std::string siblingUri = pathToUri(siblingPath);
			auto docOpt = documentManager_.getDocument(siblingUri);
			if (docOpt.has_value()) {
				source = docOpt.value()->content;
			} else {
				std::ifstream file(siblingPath);
				if (!file)
					continue;
				std::stringstream buffer;
				buffer << file.rdbuf();
				source = buffer.str();
			}

			// Parse and extract exported symbols
			try {
				Lexer lexer(source);
				TokenStream stream = lexer.tokenize_stream();
				Parser parser(std::move(stream));
				std::string fileNamespace = deriveNamespace(siblingPath);
				parser.setDefaultNamespace(fileNamespace);
				auto program = parser.parse();

				// Extract only exported symbols
				auto symbols = extractSymbolsFromAST(program.get(), source, true);

				for (auto &sym : symbols) {
					sym.filePath = siblingPath;
					if (sym.kind == "function" || sym.kind == "method") {
						project.symbols.functions.push_back(std::move(sym));
					} else if (sym.kind == "struct") {
						project.symbols.structs.push_back(std::move(sym));
					} else if (sym.kind == "enum") {
						project.symbols.enums.push_back(std::move(sym));
					} else if (sym.kind == "interface") {
						project.symbols.interfaces.push_back(std::move(sym));
					}
				}
			} catch (...) {
				// Skip files that fail to parse
				continue;
			}
		}
	} catch (...) {
		// Ignore errors during project symbol discovery
	}

	project.symbolsLoaded = true;
	return project.symbols;
}

// Invalidate cached symbols for a project
void LSPServer::invalidateProjectSymbols(const std::string &projectRoot) {
	auto it = projectCache_.find(projectRoot);
	if (it != projectCache_.end()) {
		it->second.symbolsLoaded = false;
	}
}

// Load exported symbols from project files (uses project root if available)
StdlibSymbols LSPServer::loadProjectSymbols(const std::string &filePath) {
	// Try to find project root (directory with build.maxon)
	std::string projectRoot = findProjectRoot(filePath);

	if (!projectRoot.empty()) {
		// Use cached project symbols (includes all files recursively)
		StdlibSymbols &cached = getProjectSymbols(projectRoot);

		// Return a copy excluding the current file's symbols
		StdlibSymbols projectSymbols;
		std::string normalizedCurrentPath = normalizeFilePath(
			std::filesystem::absolute(filePath).string());

		for (const auto &sym : cached.functions) {
			if (normalizeFilePath(sym.filePath) != normalizedCurrentPath) {
				projectSymbols.functions.push_back(sym);
			}
		}
		for (const auto &sym : cached.structs) {
			if (normalizeFilePath(sym.filePath) != normalizedCurrentPath) {
				projectSymbols.structs.push_back(sym);
			}
		}
		for (const auto &sym : cached.enums) {
			if (normalizeFilePath(sym.filePath) != normalizedCurrentPath) {
				projectSymbols.enums.push_back(sym);
			}
		}
		for (const auto &sym : cached.interfaces) {
			if (normalizeFilePath(sym.filePath) != normalizedCurrentPath) {
				projectSymbols.interfaces.push_back(sym);
			}
		}

		return projectSymbols;
	}

	// Fallback: no project root found, load symbols from same directory only
	StdlibSymbols projectSymbols;

	try {
		std::filesystem::path currentFile = std::filesystem::absolute(filePath);
		std::filesystem::path currentDir = currentFile.parent_path();
		std::string normalizedCurrentPath = normalizeFilePath(currentFile.string());

		// Find all .maxon files in the same directory (non-recursive)
		for (const auto &entry : std::filesystem::directory_iterator(currentDir)) {
			if (!entry.is_regular_file())
				continue;
			if (entry.path().extension() != ".maxon")
				continue;

			// Skip the current file
			std::string siblingPath = std::filesystem::absolute(entry.path()).string();
			if (normalizeFilePath(siblingPath) == normalizedCurrentPath)
				continue;

			// Skip stdlib files
			if (!workspaceRoot_.empty()) {
				std::filesystem::path stdlibPath = std::filesystem::path(workspaceRoot_) / "stdlib";
				if (std::filesystem::exists(stdlibPath)) {
					auto absStdlib = std::filesystem::absolute(stdlibPath);
					auto relative = std::filesystem::path(siblingPath).lexically_relative(absStdlib);
					if (!relative.empty() && relative.native()[0] != '.') {
						continue; // File is in stdlib, skip it
					}
				}
			}

			// Read file content - check document manager first for open files
			std::string source;
			std::string siblingUri = pathToUri(siblingPath);
			auto docOpt = documentManager_.getDocument(siblingUri);
			if (docOpt.has_value()) {
				source = docOpt.value()->content;
			} else {
				std::ifstream file(siblingPath);
				if (!file)
					continue;
				std::stringstream buffer;
				buffer << file.rdbuf();
				source = buffer.str();
			}

			// Parse and extract exported symbols
			try {
				Lexer lexer(source);
				TokenStream stream = lexer.tokenize_stream();
				Parser parser(std::move(stream));
				std::string fileNamespace = deriveNamespace(siblingPath);
				parser.setDefaultNamespace(fileNamespace);
				auto program = parser.parse();

				// Extract only exported symbols
				auto symbols = extractSymbolsFromAST(program.get(), source, true);

				for (auto &sym : symbols) {
					sym.filePath = siblingPath;
					if (sym.kind == "function" || sym.kind == "method") {
						projectSymbols.functions.push_back(std::move(sym));
					} else if (sym.kind == "struct") {
						projectSymbols.structs.push_back(std::move(sym));
					} else if (sym.kind == "enum") {
						projectSymbols.enums.push_back(std::move(sym));
					} else if (sym.kind == "interface") {
						projectSymbols.interfaces.push_back(std::move(sym));
					}
				}
			} catch (...) {
				// Skip files that fail to parse
				continue;
			}
		}
	} catch (...) {
		// Ignore errors during project symbol discovery
	}

	return projectSymbols;
}

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

	// For stdlib files, all sibling symbols are already in stdlib_ - no need to load project symbols
	// This avoids expensive O(n²) parsing when analyzing stdlib files
	StdlibSymbols combinedSymbols = stdlib_;
	if (!isStdlibFile(uri)) {
		// Load symbols from sibling project files
		StdlibSymbols projectSymbols = loadProjectSymbols(filePath);

		// Merge project symbols into combined symbols
		for (auto &sym : projectSymbols.functions) {
			combinedSymbols.functions.push_back(std::move(sym));
		}
		for (auto &sym : projectSymbols.structs) {
			combinedSymbols.structs.push_back(std::move(sym));
		}
		for (auto &sym : projectSymbols.enums) {
			combinedSymbols.enums.push_back(std::move(sym));
		}
		for (auto &sym : projectSymbols.interfaces) {
			combinedSymbols.interfaces.push_back(std::move(sym));
		}
	}

	// Run analysis with combined stdlib + project symbols
	LSPAnalysisResult result = analyzeForLSP(content, filePath, combinedSymbols);

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
	cache.enums = std::move(result.enums);
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

bool LSPServer::isStdlibFile(const std::string &uri) const {
	if (workspaceRoot_.empty()) {
		return false;
	}
	std::string filePath = uriToPath(uri);
	std::filesystem::path stdlibPath = std::filesystem::path(workspaceRoot_) / "stdlib";
	try {
		// Check if the file path starts with the stdlib path
		std::filesystem::path absFilePath = std::filesystem::absolute(filePath);
		std::filesystem::path absStdlibPath = std::filesystem::absolute(stdlibPath);
		// Use lexically_relative to check if file is under stdlib
		auto relative = absFilePath.lexically_relative(absStdlibPath);
		// If the relative path starts with "..", the file is not under stdlib
		return !relative.empty() && relative.native()[0] != '.';
	} catch (...) {
		return false;
	}
}

void LSPServer::reloadStdlib() {
	if (workspaceRoot_.empty()) {
		return;
	}
	std::filesystem::path stdlibPath = std::filesystem::path(workspaceRoot_) / "stdlib";
	if (std::filesystem::exists(stdlibPath)) {
		// Use content provider that checks document manager for open files
		stdlib_ = loadStdlibWithContentProvider(stdlibPath.string(),
												[this](const std::string &filePath) -> std::optional<std::string> {
													// Convert file path to URI and check if document is open
													std::string uri = pathToUri(filePath);
													auto docOpt = documentManager_.getDocument(uri);
													if (docOpt.has_value()) {
														// Return in-memory content
														return docOpt.value()->content;
													}
													// Return nullopt to indicate file should be read from disk
													return std::nullopt;
												});
	}
}

void LSPServer::reanalyzeNonStdlibDocuments() {
	// Get all open document URIs and re-analyze non-stdlib ones
	auto openDocuments = documentManager_.getOpenDocumentUris();
	for (const auto &uri : openDocuments) {
		if (!isStdlibFile(uri)) {
			analyzeDocument(uri);
		}
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
	AnalysisCache *cache = documentManager_.getAnalysis(uri);

	// If current cache has parse errors, supplement missing type info from last good cache
	// This allows completions to work while typing incomplete code
	if (cache && cache->hasParseErrors()) {
		const AnalysisCache *lastGood = documentManager_.getLastGoodAnalysis(uri);
		if (lastGood) {
			// Supplement enums if missing
			if (cache->enums.empty() && !lastGood->enums.empty()) {
				cache->enums = lastGood->enums;
			}
			// Supplement variables if missing
			if (cache->variables.empty() && !lastGood->variables.empty()) {
				cache->variables = lastGood->variables;
			}
			// Supplement structs if missing
			if (cache->structs.empty() && !lastGood->structs.empty()) {
				cache->structs = lastGood->structs;
			}
		}
	}

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
	auto result = provider.getSemanticTokens(*doc, cache, stdlib_);

	if (result.has_value()) {
		return lsp::toJson(result.value());
	}
	return nullptr;
}

// ============================================================================
// Custom Maxon Request Handlers
// ============================================================================

json LSPServer::handleGenerateIR(const json &params) {
	// Extract parameters
	// Can accept either a URI to an open document or inline source text
	std::string source;
	std::string filename = "compiler_explorer.maxon";
	bool optimize = false;

	if (params.contains("textDocument") && params["textDocument"].contains("uri")) {
		// Use content from an open document
		std::string uri = params["textDocument"]["uri"].get<std::string>();
		auto docOpt = documentManager_.getDocument(uri);
		if (!docOpt.has_value()) {
			json error;
			error["ir"] = "";
			error["errors"] = json::array();
			error["errors"].push_back({{"message", "Document not found: " + uri},
									   {"line", 1},
									   {"column", 1}});
			return error;
		}
		source = docOpt.value()->content;
		filename = uriToPath(uri);
	} else if (params.contains("source")) {
		// Use inline source text
		source = params["source"].get<std::string>();
		if (params.contains("filename")) {
			filename = params["filename"].get<std::string>();
		}
	} else {
		json error;
		error["ir"] = "";
		error["errors"] = json::array();
		error["errors"].push_back({{"message", "Either textDocument.uri or source must be provided"},
								   {"line", 1},
								   {"column", 1}});
		return error;
	}

	if (params.contains("optimize")) {
		optimize = params["optimize"].get<bool>();
	}

	// Generate IR using the compiler API
	IRGenerationResult irResult = generateIRForLSP(source, filename, optimize, stdlib_);

	// Build response
	json response;
	response["ir"] = irResult.ir;
	response["errors"] = json::array();

	// Add parse errors
	for (const auto &err : irResult.parseErrors) {
		response["errors"].push_back({{"message", err.message},
									  {"line", err.line},
									  {"column", err.column},
									  {"type", "parse"}});
	}

	// Add semantic errors
	for (const auto &err : irResult.semanticErrors) {
		response["errors"].push_back({{"message", err.message},
									  {"line", err.line},
									  {"column", err.column},
									  {"type", "semantic"}});
	}

	return response;
}

json LSPServer::handleGenerateAsm(const json &params) {
	std::string source;
	std::string filename = "compiler_explorer.maxon";
	bool optimize = false;

	// Accept either a document URI or inline source text
	if (params.contains("textDocument") && params["textDocument"].contains("uri")) {
		std::string uri = params["textDocument"]["uri"].get<std::string>();
		auto docOpt = documentManager_.getDocument(uri);
		if (!docOpt.has_value()) {
			json error;
			error["assembly"] = "";
			error["errors"] = json::array();
			error["errors"].push_back({{"message", "Document not found: " + uri},
									   {"line", 1},
									   {"column", 1}});
			return error;
		}
		source = docOpt.value()->content;
		filename = uriToPath(uri);
	} else if (params.contains("source")) {
		// Use inline source text
		source = params["source"].get<std::string>();
		if (params.contains("filename")) {
			filename = params["filename"].get<std::string>();
		}
	} else {
		json error;
		error["assembly"] = "";
		error["errors"] = json::array();
		error["errors"].push_back({{"message", "Either textDocument.uri or source must be provided"},
								   {"line", 1},
								   {"column", 1}});
		return error;
	}

	if (params.contains("optimize")) {
		optimize = params["optimize"].get<bool>();
	}

	// Generate assembly using the compiler API
	AsmGenerationResult asmResult = generateAsmForLSP(source, filename, optimize, stdlib_);

	// Build response
	json response;
	response["assembly"] = asmResult.assembly;
	response["errors"] = json::array();

	// Add parse errors
	for (const auto &err : asmResult.parseErrors) {
		response["errors"].push_back({{"message", err.message},
									  {"line", err.line},
									  {"column", err.column},
									  {"type", "parse"}});
	}

	// Add semantic errors
	for (const auto &err : asmResult.semanticErrors) {
		response["errors"].push_back({{"message", err.message},
									  {"line", err.line},
									  {"column", err.column},
									  {"type", "semantic"}});
	}

	return response;
}

} // namespace maxon_lsp
