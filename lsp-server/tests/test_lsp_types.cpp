#include "../include/lsp_types.h"
#include "catch_amalgamated.hpp"
#include <iostream>

#define CATCH_CONFIG_MAIN

TEST_CASE("position_structure", "[lsp_types]") {

    
    lsp::Position pos;
    pos.line = 10;
    pos.character = 5;
    
    

}

TEST_CASE("range_structure", "[lsp_types]") {

    
    lsp::Range range;
    range.start.line = 0;
    range.start.character = 0;
    range.end.line = 5;
    range.end.character = 10;
    
    

}

TEST_CASE("diagnostic_structure", "[lsp_types]") {

    
    lsp::Diagnostic diag;
    diag.range.start = {0, 0};
    diag.range.end = {0, 5};
    diag.message = "Test error";
    diag.severity = 1; // Error
    diag.source = "test";
    
    REQUIRE(diag.message == "Test error");
    REQUIRE(diag.severity == 1);
    REQUIRE(diag.source == "test");
    

}

TEST_CASE("completion_item_structure", "[lsp_types]") {

    
    lsp::CompletionItem item;
    item.label = "testFunction";
    item.kind = lsp::CompletionItemKind::Function;
    item.detail = "A test function";
    item.documentation = "Documentation for test function";
    
    REQUIRE(item.label == "testFunction");
    REQUIRE(item.kind == lsp::CompletionItemKind::Function);
    REQUIRE(item.detail == "A test function");
    

}

TEST_CASE("location_structure", "[lsp_types]") {

    
    lsp::Location loc;
    loc.uri = "file:///test.maxon";
    loc.range.start = {10, 5};
    loc.range.end = {10, 15};
    
    REQUIRE(loc.uri == "file:///test.maxon");
    REQUIRE(loc.range.start.line == 10);
    

}

TEST_CASE("hover_structure", "[lsp_types]") {

    
    lsp::Hover hover;
    hover.contents = "Hover information";
    hover.range = lsp::Range{{5, 0}, {5, 10}};
    
    REQUIRE(hover.contents == "Hover information");
    REQUIRE(hover.range.has_value());
    REQUIRE(hover.range->start.line == 5);
    

}

TEST_CASE("symbol_information_structure", "[lsp_types]") {

    
    lsp::SymbolInformation symbol;
    symbol.name = "myFunction";
    symbol.kind = lsp::SymbolKind::Function;
    symbol.location.uri = "file:///test.maxon";
    symbol.location.range.start = {0, 0};
    symbol.location.range.end = {10, 0};
    
    REQUIRE(symbol.name == "myFunction");
    REQUIRE(symbol.kind == lsp::SymbolKind::Function);
    

}

TEST_CASE("position_comparison", "[lsp_types]") {

    
    lsp::Position pos1{5, 10};
    lsp::Position pos2{5, 10};
    lsp::Position pos3{5, 15};
    lsp::Position pos4{6, 10};
    
    REQUIRE((pos1.line == pos2.line && pos1.character == pos2.character));
    REQUIRE((pos1.line == pos3.line && pos1.character != pos3.character));
    REQUIRE(pos1.line != pos4.line);
    

}
