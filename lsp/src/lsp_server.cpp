#include "lsp_server.h"
#include <iostream>

LspServer::LspServer() 
    : rpcHandler(std::make_unique<JsonRpcHandler>()),
      docManager(std::make_unique<DocumentManager>()),
      analyzer(std::make_unique<Analyzer>()),
      initialized(false) {
    
    // Register request handlers
    rpcHandler->registerRequestHandler("initialize", 
        [this](const json& params) { return handleInitialize(params); });
    rpcHandler->registerRequestHandler("shutdown", 
        [this](const json& params) { return handleShutdown(params); });
    rpcHandler->registerRequestHandler("textDocument/completion", 
        [this](const json& params) { return handleCompletion(params); });
    rpcHandler->registerRequestHandler("textDocument/hover", 
        [this](const json& params) { return handleHover(params); });
    rpcHandler->registerRequestHandler("textDocument/definition", 
        [this](const json& params) { return handleDefinition(params); });
    rpcHandler->registerRequestHandler("textDocument/documentSymbol", 
        [this](const json& params) { return handleDocumentSymbol(params); });
    
    // Register notification handlers
    rpcHandler->registerNotificationHandler("initialized", 
        [this](const json& params) { handleInitialized(params); });
    rpcHandler->registerNotificationHandler("textDocument/didOpen", 
        [this](const json& params) { handleDidOpen(params); });
    rpcHandler->registerNotificationHandler("textDocument/didChange", 
        [this](const json& params) { handleDidChange(params); });
    rpcHandler->registerNotificationHandler("textDocument/didClose", 
        [this](const json& params) { handleDidClose(params); });
    rpcHandler->registerNotificationHandler("textDocument/didSave", 
        [this](const json& params) { handleDidSave(params); });
    rpcHandler->registerNotificationHandler("exit", 
        [this](const json& params) { handleExit(params); });
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

json LspServer::handleInitialize(const json& params) {
    if (params.contains("rootUri")) {
        rootUri = params["rootUri"].get<std::string>();
    }
    
    json capabilities = {
        {"capabilities", {
            {"textDocumentSync", 1}, // Full sync
            {"completionProvider", {
                {"resolveProvider", false},
                {"triggerCharacters", json::array()}
            }},
            {"hoverProvider", true},
            {"definitionProvider", true},
            {"documentSymbolProvider", true}
        }},
        {"serverInfo", {
            {"name", "maxon-lsp"},
            {"version", "0.1.0"}
        }}
    };
    
    initialized = true;
    return capabilities;
}

json LspServer::handleShutdown(const json& params) {
    return nullptr;
}

json LspServer::handleCompletion(const json& params) {
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
    for (const auto& comp : completions) {
        json item = {
            {"label", comp.label},
            {"kind", comp.kind},
            {"detail", comp.detail}
        };
        items.push_back(item);
    }
    
    return items;
}

json LspServer::handleHover(const json& params) {
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
            {"contents", {
                {"kind", "markdown"},
                {"value", hover->contents}
            }}
        };
    }
    
    return nullptr;
}

json LspServer::handleDefinition(const json& params) {
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
            {"range", {
                {"start", {
                    {"line", location->range.start.line},
                    {"character", location->range.start.character}
                }},
                {"end", {
                    {"line", location->range.end.line},
                    {"character", location->range.end.character}
                }}
            }}
        };
    }
    
    return nullptr;
}

json LspServer::handleDocumentSymbol(const json& params) {
    std::string uri = params["textDocument"]["uri"].get<std::string>();
    
    auto doc = docManager->getDocument(uri);
    if (!doc) {
        return json::array();
    }
    
    auto symbols = analyzer->getSymbols(doc);
    
    json result = json::array();
    for (const auto& sym : symbols) {
        json item = {
            {"name", sym.name},
            {"kind", sym.kind},
            {"location", {
                {"uri", sym.location.uri},
                {"range", {
                    {"start", {
                        {"line", sym.location.range.start.line},
                        {"character", sym.location.range.start.character}
                    }},
                    {"end", {
                        {"line", sym.location.range.end.line},
                        {"character", sym.location.range.end.character}
                    }}
                }}
            }}
        };
        result.push_back(item);
    }
    
    return result;
}

void LspServer::handleInitialized(const json& params) {
    // Server is now initialized
}

void LspServer::handleDidOpen(const json& params) {
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

void LspServer::handleDidChange(const json& params) {
    std::string uri = params["textDocument"]["uri"].get<std::string>();
    int version = params["textDocument"]["version"].get<int>();
    
    if (params.contains("contentChanges") && params["contentChanges"].is_array() && 
        params["contentChanges"].size() > 0) {
        std::string text = params["contentChanges"][0]["text"].get<std::string>();
        
        docManager->updateDocument(uri, text, version);
        
        // Analyze and send diagnostics
        auto doc = docManager->getDocument(uri);
        if (doc) {
            auto diagnostics = analyzer->analyze(doc);
            publishDiagnostics(uri, diagnostics);
        }
    }
}

void LspServer::handleDidClose(const json& params) {
    std::string uri = params["textDocument"]["uri"].get<std::string>();
    docManager->closeDocument(uri);
}

void LspServer::handleDidSave(const json& params) {
    std::string uri = params["textDocument"]["uri"].get<std::string>();
    
    // Re-analyze on save
    auto doc = docManager->getDocument(uri);
    if (doc) {
        auto diagnostics = analyzer->analyze(doc);
        publishDiagnostics(uri, diagnostics);
    }
}

void LspServer::handleExit(const json& params) {
    std::exit(0);
}

void LspServer::publishDiagnostics(const std::string& uri, const std::vector<lsp::Diagnostic>& diagnostics) {
    json diags = json::array();
    
    for (const auto& diag : diagnostics) {
        json item = {
            {"range", {
                {"start", {
                    {"line", diag.range.start.line},
                    {"character", diag.range.start.character}
                }},
                {"end", {
                    {"line", diag.range.end.line},
                    {"character", diag.range.end.character}
                }}
            }},
            {"severity", diag.severity},
            {"message", diag.message}
        };
        
        if (diag.source.has_value()) {
            item["source"] = diag.source.value();
        }
        
        diags.push_back(item);
    }
    
    json params = {
        {"uri", uri},
        {"diagnostics", diags}
    };
    
    rpcHandler->sendNotification("textDocument/publishDiagnostics", params);
}
