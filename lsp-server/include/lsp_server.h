#ifndef LSP_SERVER_H
#define LSP_SERVER_H

#include "json_rpc.h"
#include "document_manager.h"
#include "analyzer.h"
#include <memory>

class LspServer {
public:
    LspServer();
    void run();
    
private:
    std::unique_ptr<JsonRpcHandler> rpcHandler;
    std::unique_ptr<DocumentManager> docManager;
    std::unique_ptr<Analyzer> analyzer;
    
    bool initialized;
    std::string rootUri;
    
    // LSP Request handlers
    json handleInitialize(const json& params);
    json handleShutdown(const json& params);
    json handleCompletion(const json& params);
    json handleHover(const json& params);
    json handleDefinition(const json& params);
    json handleDocumentSymbol(const json& params);
    
    // LSP Notification handlers
    void handleInitialized(const json& params);
    void handleDidOpen(const json& params);
    void handleDidChange(const json& params);
    void handleDidClose(const json& params);
    void handleDidSave(const json& params);
    void handleExit(const json& params);
    
    // Helper methods
    void publishDiagnostics(const std::string& uri, const std::vector<lsp::Diagnostic>& diagnostics);
};

#endif // LSP_SERVER_H
