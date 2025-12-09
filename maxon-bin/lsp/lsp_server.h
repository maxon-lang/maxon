#ifndef MAXON_LSP_SERVER_H
#define MAXON_LSP_SERVER_H

#include "../compiler_api.h"
#include "document_manager.h"
#include "lsp_json.h"
#include "lsp_types.h"
#include "transport.h"
#include <functional>
#include <map>
#include <memory>

namespace maxon_lsp {

/**
 * LSP Server implementation for the Maxon language.
 *
 * This class implements the Language Server Protocol, handling communication
 * with LSP clients (like VS Code) to provide language features such as
 * diagnostics, completion, hover, go-to-definition, etc.
 *
 * The server uses a Transport abstraction for communication, allowing it to
 * work with stdio, sockets, or other transport mechanisms.
 */
class LSPServer {
  public:
	explicit LSPServer(std::unique_ptr<maxon::lsp::Transport> transport);

	// Main entry point - runs the message loop
	// Returns 0 on clean shutdown, non-zero on error
	int run();

	// Request graceful shutdown
	void shutdown();

  private:
	std::unique_ptr<maxon::lsp::Transport> transport_;
	DocumentManager documentManager_;
	StdlibSymbols stdlib_;

	bool initialized_ = false;
	bool shutdownRequested_ = false;
	std::string workspaceRoot_;

	// Client capabilities
	bool supportsWorkspaceFolders_ = false;
	bool supportsDocumentChanges_ = false;

	// Request handlers (methods that return a result)
	using RequestHandler = std::function<maxon::lsp::json(const maxon::lsp::json &)>;
	// Notification handlers (methods that don't return a result)
	using NotificationHandler = std::function<void(const maxon::lsp::json &)>;

	std::map<std::string, RequestHandler> requestHandlers_;
	std::map<std::string, NotificationHandler> notificationHandlers_;

	// Register all request and notification handlers
	void registerHandlers();

	// =========================================================================
	// Lifecycle handlers
	// =========================================================================

	// Handle the "initialize" request
	maxon::lsp::json handleInitialize(const maxon::lsp::json &params);

	// Handle the "initialized" notification
	void handleInitialized(const maxon::lsp::json &params);

	// Handle the "shutdown" request
	maxon::lsp::json handleShutdown(const maxon::lsp::json &params);

	// Handle the "exit" notification
	void handleExit(const maxon::lsp::json &params);

	// =========================================================================
	// Document synchronization handlers
	// =========================================================================

	// Handle "textDocument/didOpen" notification
	void handleDidOpen(const maxon::lsp::json &params);

	// Handle "textDocument/didChange" notification
	void handleDidChange(const maxon::lsp::json &params);

	// Handle "textDocument/didClose" notification
	void handleDidClose(const maxon::lsp::json &params);

	// Handle "textDocument/didSave" notification
	void handleDidSave(const maxon::lsp::json &params);

	// =========================================================================
	// Language feature handlers
	// =========================================================================

	// Handle "textDocument/completion" request
	maxon::lsp::json handleCompletion(const maxon::lsp::json &params);

	// Handle "textDocument/hover" request
	maxon::lsp::json handleHover(const maxon::lsp::json &params);

	// Handle "textDocument/signatureHelp" request
	maxon::lsp::json handleSignatureHelp(const maxon::lsp::json &params);

	// Handle "textDocument/definition" request
	maxon::lsp::json handleDefinition(const maxon::lsp::json &params);

	// Handle "textDocument/references" request
	maxon::lsp::json handleReferences(const maxon::lsp::json &params);

	// Handle "textDocument/documentSymbol" request
	maxon::lsp::json handleDocumentSymbol(const maxon::lsp::json &params);

	// Handle "textDocument/prepareRename" request
	maxon::lsp::json handlePrepareRename(const maxon::lsp::json &params);

	// Handle "textDocument/rename" request
	maxon::lsp::json handleRename(const maxon::lsp::json &params);

	// Handle "textDocument/codeAction" request
	maxon::lsp::json handleCodeAction(const maxon::lsp::json &params);

	// Handle "textDocument/formatting" request
	maxon::lsp::json handleFormatting(const maxon::lsp::json &params);

	// Handle "textDocument/foldingRange" request
	maxon::lsp::json handleFoldingRange(const maxon::lsp::json &params);

	// Handle "textDocument/linkedEditingRange" request
	maxon::lsp::json handleLinkedEditingRange(const maxon::lsp::json &params);

	// Handle "textDocument/semanticTokens/full" request
	maxon::lsp::json handleSemanticTokensFull(const maxon::lsp::json &params);

	// =========================================================================
	// Custom Maxon requests
	// =========================================================================

	// Handle "maxon/generateIR" request - generate MIR for compiler explorer
	maxon::lsp::json handleGenerateIR(const maxon::lsp::json &params);

	// Handle "maxon/generateAsm" request - generate x86-64 assembly for compiler explorer
	maxon::lsp::json handleGenerateAsm(const maxon::lsp::json &params);

	// =========================================================================
	// Analysis and diagnostics
	// =========================================================================

	// Analyze a document and update the cache
	void analyzeDocument(const std::string &uri);

	// Analyze all stdlib files and publish diagnostics for them
	void analyzeStdlibFiles(const std::string &stdlibPath);

	// Reload stdlib symbols and re-analyze dependent documents
	void reloadStdlib();

	// Check if a URI is in the stdlib directory
	bool isStdlibFile(const std::string &uri) const;

	// Re-analyze all open non-stdlib documents
	void reanalyzeNonStdlibDocuments();

	// Publish diagnostics to the client
	void publishDiagnostics(const std::string &uri,
							const std::vector<maxon::lsp::Diagnostic> &diagnostics);

	// =========================================================================
	// Helper methods
	// =========================================================================

	// Send a notification to the client
	void sendNotification(const std::string &method, const maxon::lsp::json &params);

	// Build the server capabilities for the initialize response
	maxon::lsp::ServerCapabilities buildCapabilities();

	// Convert ParseError to LSP Diagnostic
	maxon::lsp::Diagnostic parseErrorToDiagnostic(const ParseError &error);

	// Convert SemanticError to LSP Diagnostic
	maxon::lsp::Diagnostic semanticErrorToDiagnostic(const SemanticError &error);
};

} // namespace maxon_lsp

#endif // MAXON_LSP_SERVER_H
