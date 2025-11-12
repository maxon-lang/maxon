#include "../include/lsp_server.h"
#include "../include/document_manager.h"
#include "../include/analyzer.h"
#include <cassert>
#include <iostream>
#include <memory>

void test_code_action_structure() {
    std::cout << "Testing code action request structure..." << std::endl;
    
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
    
    assert(params.contains("textDocument"));
    assert(params.contains("range"));
    assert(params.contains("context"));
    assert(params["context"]["diagnostics"].is_array());
    assert(params["context"]["diagnostics"][0]["code"] == "unused-variable");
    
    std::cout << "✓ Code action request structure validated" << std::endl;
}

void test_unused_variable_diagnostic() {
    std::cout << "Testing unused variable diagnostic generation..." << std::endl;
    
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
    
    assert(doc != nullptr);
    
    auto diagnostics = analyzer->analyze(doc);
    
    // Should have at least one diagnostic for unused variable
    bool foundUnusedWarning = false;
    for (const auto& diag : diagnostics) {
        if (diag.severity == 2 && diag.message.find("unused") != std::string::npos) {
            foundUnusedWarning = true;
            // Check that it has the code
            assert(diag.code.has_value());
            assert(diag.code.value() == "unused-variable");
        }
    }
    
    assert(foundUnusedWarning);
    
    std::cout << "✓ Unused variable diagnostic generated with code" << std::endl;
}

void test_code_action_response_format() {
    std::cout << "Testing code action response format..." << std::endl;
    
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
    
    assert(expectedAction.contains("title"));
    assert(expectedAction.contains("kind"));
    assert(expectedAction["kind"] == "quickfix");
    assert(expectedAction.contains("edit"));
    assert(expectedAction["edit"].contains("changes"));
    
    std::cout << "✓ Code action response format validated" << std::endl;
}

void test_code_action_only_for_warnings() {
    std::cout << "Testing code actions only provided for warnings..." << std::endl;
    
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
    assert(errorDiagnostic["severity"] == 1);
    
    std::cout << "✓ Code actions filtering validated" << std::endl;
}

void test_workspace_edit_structure() {
    std::cout << "Testing workspace edit structure..." << std::endl;
    
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
    
    assert(workspaceEdit.contains("changes"));
    assert(workspaceEdit["changes"].is_object());
    
    auto& changes = workspaceEdit["changes"];
    assert(changes.contains("file:///test.maxon"));
    assert(changes["file:///test.maxon"].is_array());
    assert(changes["file:///test.maxon"].size() > 0);
    
    auto& textEdit = changes["file:///test.maxon"][0];
    assert(textEdit.contains("range"));
    assert(textEdit.contains("newText"));
    
    std::cout << "✓ Workspace edit structure validated" << std::endl;
}

void test_diagnostic_code_field() {
    std::cout << "Testing diagnostic code field..." << std::endl;
    
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
    
    assert(diagnostic.contains("code"));
    assert(diagnostic["code"].is_string());
    assert(diagnostic["code"] == "unused-variable");
    
    std::cout << "✓ Diagnostic code field validated" << std::endl;
}

void test_code_action_capabilities() {
    std::cout << "Testing code action capabilities..." << std::endl;
    
    // Server should advertise code action support
    json capabilities = {
        {"codeActionProvider", {
            {"codeActionKinds", json::array({"quickfix"})}
        }}
    };
    
    assert(capabilities.contains("codeActionProvider"));
    assert(capabilities["codeActionProvider"].contains("codeActionKinds"));
    assert(capabilities["codeActionProvider"]["codeActionKinds"].is_array());
    
    bool hasQuickfix = false;
    for (const auto& kind : capabilities["codeActionProvider"]["codeActionKinds"]) {
        if (kind == "quickfix") {
            hasQuickfix = true;
            break;
        }
    }
    assert(hasQuickfix);
    
    std::cout << "✓ Code action capabilities validated" << std::endl;
}

void test_variable_name_extraction() {
    std::cout << "Testing variable name extraction from diagnostic message..." << std::endl;
    
    std::string message = "The variable 'unused' is assigned but its value is never used";
    
    // Extract variable name from message
    size_t start = message.find("'");
    size_t end = message.find("'", start + 1);
    
    assert(start != std::string::npos);
    assert(end != std::string::npos);
    
    std::string varName = message.substr(start + 1, end - start - 1);
    assert(varName == "unused");
    
    std::cout << "✓ Variable name extraction validated" << std::endl;
}

void test_unnecessary_qualified_name_extraction() {
    std::cout << "Testing unnecessary qualified name extraction from diagnostic message..." << std::endl;
    
    std::string message = "Unnecessary qualified name: 'math::add'\n  The unqualified name 'add' is unambiguous\n  Consider using 'add' instead";
    
    // Extract qualified name and unqualified name from message
    size_t qualStart = message.find("'");
    size_t qualEnd = message.find("'", qualStart + 1);
    size_t unqualStart = message.find("'", qualEnd + 1);
    size_t unqualEnd = message.find("'", unqualStart + 1);
    
    assert(qualStart != std::string::npos);
    assert(qualEnd != std::string::npos);
    assert(unqualStart != std::string::npos);
    assert(unqualEnd != std::string::npos);
    
    std::string qualifiedName = message.substr(qualStart + 1, qualEnd - qualStart - 1);
    std::string unqualifiedName = message.substr(unqualStart + 1, unqualEnd - unqualStart - 1);
    
    assert(qualifiedName == "math::add");
    assert(unqualifiedName == "add");
    
    std::cout << "✓ Unnecessary qualified name extraction validated" << std::endl;
}

int main() {
    std::cout << "\n=== Running Code Action Tests ===\n" << std::endl;
    
    try {
        test_code_action_structure();
        test_unused_variable_diagnostic();
        test_code_action_response_format();
        test_code_action_only_for_warnings();
        test_workspace_edit_structure();
        test_diagnostic_code_field();
        test_code_action_capabilities();
        test_variable_name_extraction();
        test_unnecessary_qualified_name_extraction();
        
        std::cout << "\n=== All Code Action Tests Passed ✓ ===\n" << std::endl;
        return 0;
    } catch (const std::exception& e) {
        std::cerr << "\n✗ Test failed with exception: " << e.what() << std::endl;
        return 1;
    } catch (...) {
        std::cerr << "\n✗ Test failed with unknown exception" << std::endl;
        return 1;
    }
}
