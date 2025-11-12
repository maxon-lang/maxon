#include "lsp_server.h"
#include <iostream>
#include <filesystem>
#include <fstream>

#ifdef _WIN32
#include <windows.h>
#else
#include <unistd.h>
#include <limits.h>
#endif

namespace fs = std::filesystem;

// Helper function to URL decode a string
static std::string urlDecode(const std::string& str) {
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
        {"capabilities", {
            {"textDocumentSync", 1}, // Full sync
            {"completionProvider", {
                {"resolveProvider", false},
                {"triggerCharacters", json::array({".", ":"})}
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
        
        // Add documentation if present
        if (!comp.documentation.empty()) {
            item["documentation"] = comp.documentation;
        }
        
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
