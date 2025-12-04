#include "../include/lsp_server.h"
#include "../include/document_manager.h"
#include "../include/analyzer.h"
#include "catch_amalgamated.hpp"
#include <iostream>
#include <memory>

#define CATCH_CONFIG_MAIN

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
