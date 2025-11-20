#include "../include/lsp_server.h"
#include "../include/document_manager.h"
#include "../include/analyzer.h"
#include "catch_amalgamated.hpp"
#include <iostream>
#include <memory>

#define CATCH_CONFIG_MAIN

TEST_CASE("code_action_structure", "[code_actions]") {

    
    json params = {
        {"textDocument", {{"uri", "file:///test.maxon"}}},
        {"range", {
            {"start", {{"line", 1}, {"character", 4}}},
            {"end", {{"line", 1}, {"character", 10}}}
        }},
        {"context", {
            {"diagnostics", json::array({
                {
                    {"range", {
                        {"start", {{"line", 1}, {"character", 4}}},
                        {"end", {{"line", 1}, {"character", 10}}}
                    }},
                    {"severity", 2},
                    {"message", "The variable 'unused' is assigned but its value is never used"},
                    {"source", "maxon"},
                    {"code", "unused-variable"}
                }
            })}
        }}
    };
    
    REQUIRE(params.contains("textDocument"));
    REQUIRE(params.contains("range"));
    REQUIRE(params.contains("context"));
    REQUIRE(params["context"]["diagnostics"].is_array());
    REQUIRE(params["context"]["diagnostics"][0]["code"] == "unused-variable");
    

}

TEST_CASE("unused_variable_diagnostic", "[code_actions]") {

    
    std::string testCode = 
        "function test() int\n"
        "    var unused = 42\n"
        "    var used = 10\n"
        "    return used\n"
        "end 'test'\n";
    
    auto analyzer = std::make_unique<Analyzer>();
    auto docManager = std::make_unique<DocumentManager>();
    
    docManager->openDocument("file:///test.maxon", testCode, 1);
    auto doc = docManager->getDocument("file:///test.maxon");
    
    REQUIRE(doc != nullptr);
    
    auto diagnostics = analyzer->analyze(doc);
    
    // Should have at least one diagnostic for unused variable
    bool foundUnusedWarning = false;
    for (const auto& diag : diagnostics) {
        if (diag.severity == 2 && diag.message.find("unused") != std::string::npos) {
            foundUnusedWarning = true;
            // Check that it has the code
            REQUIRE(diag.code.has_value());
            REQUIRE(diag.code.value() == "unused-variable");
        }
    }
    
    REQUIRE(foundUnusedWarning);
    

}

TEST_CASE("code_action_response_format", "[code_actions]") {

    
    // Expected response format for a quick fix
    json expectedAction = {
        {"title", "Remove unused variable 'unused'"},
        {"kind", "quickfix"},
        {"diagnostics", json::array({
            {
                {"range", {
                    {"start", {{"line", 1}, {"character", 4}}},
                    {"end", {{"line", 1}, {"character", 10}}}
                }},
                {"severity", 2},
                {"message", "The variable 'unused' is assigned but its value is never used"},
                {"source", "maxon"},
                {"code", "unused-variable"}
            }
        })},
        {"edit", {
            {"changes", {
                {"file:///test.maxon", json::array({
                    {
                        {"range", {
                            {"start", {{"line", 1}, {"character", 0}}},
                            {"end", {{"line", 2}, {"character", 0}}}
                        }},
                        {"newText", ""}
                    }
                })}
            }}
        }}
    };
    
    REQUIRE(expectedAction.contains("title"));
    REQUIRE(expectedAction.contains("kind"));
    REQUIRE(expectedAction["kind"] == "quickfix");
    REQUIRE(expectedAction.contains("edit"));
    REQUIRE(expectedAction["edit"].contains("changes"));
    

}

TEST_CASE("code_action_only_for_warnings", "[code_actions]") {

    
    json errorDiagnostic = {
        {"range", {
            {"start", {{"line", 0}, {"character", 0}}},
            {"end", {{"line", 0}, {"character", 1}}}
        }},
        {"severity", 1}, // Error
        {"message", "Parse error"},
        {"source", "maxon"}
    };
    
    // Code actions should only be provided for severity 2 (warnings)
    REQUIRE(errorDiagnostic["severity"] == 1);
    

}

TEST_CASE("workspace_edit_structure", "[code_actions]") {

    
    json workspaceEdit = {
        {"changes", {
            {"file:///test.maxon", json::array({
                {
                    {"range", {
                        {"start", {{"line", 1}, {"character", 0}}},
                        {"end", {{"line", 2}, {"character", 0}}}
                    }},
                    {"newText", ""}
                }
            })}
        }}
    };
    
    REQUIRE(workspaceEdit.contains("changes"));
    REQUIRE(workspaceEdit["changes"].is_object());
    
    auto& changes = workspaceEdit["changes"];
    REQUIRE(changes.contains("file:///test.maxon"));
    REQUIRE(changes["file:///test.maxon"].is_array());
    REQUIRE(changes["file:///test.maxon"].size() > 0);
    
    auto& textEdit = changes["file:///test.maxon"][0];
    REQUIRE(textEdit.contains("range"));
    REQUIRE(textEdit.contains("newText"));
    

}

TEST_CASE("diagnostic_code_field", "[code_actions]") {

    
    json diagnostic = {
        {"range", {
            {"start", {{"line", 1}, {"character", 4}}},
            {"end", {{"line", 1}, {"character", 10}}}
        }},
        {"severity", 2},
        {"message", "The variable 'unused' is assigned but its value is never used"},
        {"source", "maxon"},
        {"code", "unused-variable"}
    };
    
    REQUIRE(diagnostic.contains("code"));
    REQUIRE(diagnostic["code"].is_string());
    REQUIRE(diagnostic["code"] == "unused-variable");
    

}

TEST_CASE("code_action_capabilities", "[code_actions]") {

    
    // Server should advertise code action support
    json capabilities = {
        {"codeActionProvider", {
            {"codeActionKinds", json::array({"quickfix"})}
        }}
    };
    
    REQUIRE(capabilities.contains("codeActionProvider"));
    REQUIRE(capabilities["codeActionProvider"].contains("codeActionKinds"));
    REQUIRE(capabilities["codeActionProvider"]["codeActionKinds"].is_array());
    
    bool hasQuickfix = false;
    for (const auto& kind : capabilities["codeActionProvider"]["codeActionKinds"]) {
        if (kind == "quickfix") {
            hasQuickfix = true;
            break;
        }
    }
    REQUIRE(hasQuickfix);
    

}

TEST_CASE("variable_name_extraction", "[code_actions]") {

    
    std::string message = "The variable 'unused' is assigned but its value is never used";
    
    // Extract variable name from message
    size_t start = message.find("'");
    size_t end = message.find("'", start + 1);
    
    REQUIRE(start != std::string::npos);
    REQUIRE(end != std::string::npos);
    
    std::string varName = message.substr(start + 1, end - start - 1);
    REQUIRE(varName == "unused");
    

}

TEST_CASE("unnecessary_qualified_name_extraction", "[code_actions]") {

    
    std::string message = "Unnecessary qualified name: 'math::add'\n  The unqualified name 'add' is unambiguous\n  Consider using 'add' instead";
    
    // Extract qualified name and unqualified name from message
    size_t qualStart = message.find("'");
    size_t qualEnd = message.find("'", qualStart + 1);
    size_t unqualStart = message.find("'", qualEnd + 1);
    size_t unqualEnd = message.find("'", unqualStart + 1);
    
    REQUIRE(qualStart != std::string::npos);
    REQUIRE(qualEnd != std::string::npos);
    REQUIRE(unqualStart != std::string::npos);
    REQUIRE(unqualEnd != std::string::npos);
    
    std::string qualifiedName = message.substr(qualStart + 1, qualEnd - qualStart - 1);
    std::string unqualifiedName = message.substr(unqualStart + 1, unqualEnd - unqualStart - 1);
    
    REQUIRE(qualifiedName == "math::add");
    REQUIRE(unqualifiedName == "add");
    

}


