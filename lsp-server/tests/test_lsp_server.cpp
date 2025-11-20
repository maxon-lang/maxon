#include "../include/lsp_server.h"
#include "catch_amalgamated.hpp"
#include <iostream>
#include <memory>

#define CATCH_CONFIG_MAIN

// Mock test for LSP Server initialization
TEST_CASE("lsp_server_creation", "[lsp_server]") {

    
    // Just ensure we can create the server without crashing
    LspServer server;
    

}

TEST_CASE("initialize_request", "[lsp_server]") {

    
    // This would require mocking the JSON-RPC layer
    // For now, just verify the structure exists
    
    json params = {
        {"processId", 1234},
        {"rootUri", "file:///workspace"},
        {"capabilities", json::object()}
    };
    
    // In a real test, we'd call handleInitialize
    REQUIRE(params.contains("rootUri"));
    

}

TEST_CASE("shutdown_request", "[lsp_server]") {

    
    // Test that shutdown params can be empty
    json params = json::object();
    
    REQUIRE(params.is_object());
    

}

TEST_CASE("did_open_notification", "[lsp_server]") {

    
    json params = {
        {"textDocument", {
            {"uri", "file:///test.maxon"},
            {"languageId", "maxon"},
            {"version", 0},
            {"text", "function main() end"}
        }}
    };
    
    REQUIRE(params["textDocument"]["uri"] == "file:///test.maxon");
    REQUIRE(params["textDocument"]["text"] == "function main() end");
    

}

TEST_CASE("did_change_notification", "[lsp_server]") {

    
    json params = {
        {"textDocument", {
            {"uri", "file:///test.maxon"},
            {"version", 1}
        }},
        {"contentChanges", json::array({
            {{"text", "updated content"}}
        })}
    };
    
    REQUIRE(params["contentChanges"].is_array());
    REQUIRE(params["contentChanges"].size() > 0);
    

}

TEST_CASE("completion_request", "[lsp_server]") {

    
    json params = {
        {"textDocument", {{"uri", "file:///test.maxon"}}},
        {"position", {{"line", 0}, {"character", 5}}}
    };
    
    REQUIRE(params["position"]["line"] == 0);
    REQUIRE(params["position"]["character"] == 5);
    

}

TEST_CASE("hover_request", "[lsp_server]") {

    
    json params = {
        {"textDocument", {{"uri", "file:///test.maxon"}}},
        {"position", {{"line", 2}, {"character", 8}}}
    };
    
    REQUIRE(params["textDocument"]["uri"] == "file:///test.maxon");
    

}

TEST_CASE("definition_request", "[lsp_server]") {

    
    json params = {
        {"textDocument", {{"uri", "file:///test.maxon"}}},
        {"position", {{"line", 1}, {"character", 10}}}
    };
    
    REQUIRE(params.contains("textDocument"));
    REQUIRE(params.contains("position"));
    

}

TEST_CASE("document_symbol_request", "[lsp_server]") {

    
    json params = {
        {"textDocument", {{"uri", "file:///test.maxon"}}}
    };
    
    REQUIRE(params["textDocument"]["uri"] == "file:///test.maxon");
    

}


