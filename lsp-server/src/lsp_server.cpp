#include "lsp_server.h"
#include <iostream>
#include <sstream>
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
      formatter(std::make_unique<Formatter>()),
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
    rpcHandler->registerRequestHandler("textDocument/codeAction", 
        [this](const json& params) { return handleCodeAction(params); });
    rpcHandler->registerRequestHandler("textDocument/rename", 
        [this](const json& params) { return handleRename(params); });
    rpcHandler->registerRequestHandler("textDocument/linkedEditingRange", 
        [this](const json& params) { return handleLinkedEditingRange(params); });
    rpcHandler->registerRequestHandler("textDocument/formatting", 
        [this](const json& params) { return handleFormatting(params); });
    rpcHandler->registerRequestHandler("textDocument/rangeFormatting", 
        [this](const json& params) { return handleRangeFormatting(params); });
    
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
            {"documentSymbolProvider", true},
            {"codeActionProvider", {
                {"codeActionKinds", json::array({"quickfix"})}
            }},
            {"renameProvider", true},
            {"linkedEditingRangeProvider", true},
            {"documentFormattingProvider", true},
            {"documentRangeFormattingProvider", true}
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
            {"kind", static_cast<int>(comp.kind)},
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
            {"kind", static_cast<int>(sym.kind)},
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
    
    // Clear diagnostics for the closed document
    publishDiagnostics(uri, {});
    
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

json LspServer::handleCodeAction(const json& params) {
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
        
        for (const auto& diag : diagnostics) {
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
                                    {"edit", {
                                        {"changes", {
                                            {uri, json::array({
                                                {
                                                    {"range", {
                                                        {"start", {
                                                            {"line", deleteRange.start.line},
                                                            {"character", deleteRange.start.character}
                                                        }},
                                                        {"end", {
                                                            {"line", deleteRange.end.line},
                                                            {"character", deleteRange.end.character}
                                                        }}
                                                    }},
                                                    {"newText", ""}
                                                }
                                            })}
                                        }}
                                    }}
                                };
                                
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
                                    {"edit", {
                                        {"changes", {
                                            {uri, json::array({
                                                {
                                                    {"range", {
                                                        {"start", {
                                                            {"line", replaceRange.start.line},
                                                            {"character", replaceRange.start.character}
                                                        }},
                                                        {"end", {
                                                            {"line", replaceRange.end.line},
                                                            {"character", replaceRange.end.character}
                                                        }}
                                                    }},
                                                    {"newText", unqualifiedName}
                                                }
                                            })}
                                        }}
                                    }}
                                };
                                
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

json LspServer::handleRename(const json& params) {
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
    for (const auto& pair : workspaceEdit->changes) {
        json edits = json::array();
        for (const auto& edit : pair.second) {
            edits.push_back({
                {"range", {
                    {"start", {
                        {"line", edit.range.start.line},
                        {"character", edit.range.start.character}
                    }},
                    {"end", {
                        {"line", edit.range.end.line},
                        {"character", edit.range.end.character}
                    }}
                }},
                {"newText", edit.newText}
            });
        }
        changes[pair.first] = edits;
    }
    
    return {{"changes", changes}};
}

json LspServer::handleLinkedEditingRange(const json& params) {
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
    for (const auto& range : *ranges) {
        rangesJson.push_back({
            {"start", {
                {"line", range.start.line},
                {"character", range.start.character}
            }},
            {"end", {
                {"line", range.end.line},
                {"character", range.end.character}
            }}
        });
    }
    
    return {{"ranges", rangesJson}};
}

void LspServer::handleExit(const json& params) {
    std::exit(0);
}



json LspServer::handleFormatting(const json& params) {
    std::string uri = params["textDocument"]["uri"].get<std::string>();
    
    auto doc = docManager->getDocument(uri);
    if (!doc) {
        return json::array();
    }
    
    // Get formatting options from the request
    bool insertSpaces = true;  // Default to spaces
    int tabSize = 4;           // Default tab size
    
    if (params.contains("options")) {
        const auto& options = params["options"];
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
    for (const auto& edit : edits) {
        result.push_back({
            {"range", {
                {"start", {
                    {"line", edit.range.start.line},
                    {"character", edit.range.start.character}
                }},
                {"end", {
                    {"line", edit.range.end.line},
                    {"character", edit.range.end.character}
                }}
            }},
            {"newText", edit.newText}
        });
    }
    
    return result;
}

json LspServer::handleRangeFormatting(const json& params) {
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
    bool insertSpaces = true;  // Default to spaces
    int tabSize = 4;           // Default tab size
    
    if (params.contains("options")) {
        const auto& options = params["options"];
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
    for (const auto& edit : edits) {
        result.push_back({
            {"range", {
                {"start", {
                    {"line", edit.range.start.line},
                    {"character", edit.range.start.character}
                }},
                {"end", {
                    {"line", edit.range.end.line},
                    {"character", edit.range.end.character}
                }}
            }},
            {"newText", edit.newText}
        });
    }
    
    return result;
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
        
        if (diag.code.has_value()) {
            item["code"] = diag.code.value();
        }
        
        diags.push_back(item);
    }
    
    json params = {
        {"uri", uri},
        {"diagnostics", diags}
    };
    
    rpcHandler->sendNotification("textDocument/publishDiagnostics", params);
}
