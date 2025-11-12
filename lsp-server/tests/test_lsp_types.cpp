#include "../include/lsp_types.h"
#include <cassert>
#include <iostream>

void test_position_structure() {
    std::cout << "Testing Position structure..." << std::endl;
    
    lsp::Position pos;
    pos.line = 10;
    pos.character = 5;
    
    assert(pos.line == 10);
    assert(pos.character == 5);
    
    std::cout << "✓ Position structure works" << std::endl;
}

void test_range_structure() {
    std::cout << "Testing Range structure..." << std::endl;
    
    lsp::Range range;
    range.start.line = 0;
    range.start.character = 0;
    range.end.line = 5;
    range.end.character = 10;
    
    assert(range.start.line == 0);
    assert(range.end.line == 5);
    
    std::cout << "✓ Range structure works" << std::endl;
}

void test_diagnostic_structure() {
    std::cout << "Testing Diagnostic structure..." << std::endl;
    
    lsp::Diagnostic diag;
    diag.range.start = {0, 0};
    diag.range.end = {0, 5};
    diag.message = "Test error";
    diag.severity = 1; // Error
    diag.source = "test";
    
    assert(diag.message == "Test error");
    assert(diag.severity == 1);
    assert(diag.source == "test");
    
    std::cout << "✓ Diagnostic structure works" << std::endl;
}

void test_completion_item_structure() {
    std::cout << "Testing CompletionItem structure..." << std::endl;
    
    lsp::CompletionItem item;
    item.label = "testFunction";
    item.kind = 3; // Function
    item.detail = "A test function";
    item.documentation = "Documentation for test function";
    
    assert(item.label == "testFunction");
    assert(item.kind == 3);
    assert(item.detail == "A test function");
    
    std::cout << "✓ CompletionItem structure works" << std::endl;
}

void test_location_structure() {
    std::cout << "Testing Location structure..." << std::endl;
    
    lsp::Location loc;
    loc.uri = "file:///test.maxon";
    loc.range.start = {10, 5};
    loc.range.end = {10, 15};
    
    assert(loc.uri == "file:///test.maxon");
    assert(loc.range.start.line == 10);
    
    std::cout << "✓ Location structure works" << std::endl;
}

void test_hover_structure() {
    std::cout << "Testing Hover structure..." << std::endl;
    
    lsp::Hover hover;
    hover.contents = "Hover information";
    hover.range = lsp::Range{{5, 0}, {5, 10}};
    
    assert(hover.contents == "Hover information");
    assert(hover.range.has_value());
    assert(hover.range->start.line == 5);
    
    std::cout << "✓ Hover structure works" << std::endl;
}

void test_symbol_information_structure() {
    std::cout << "Testing SymbolInformation structure..." << std::endl;
    
    lsp::SymbolInformation symbol;
    symbol.name = "myFunction";
    symbol.kind = 12; // Function
    symbol.location.uri = "file:///test.maxon";
    symbol.location.range.start = {0, 0};
    symbol.location.range.end = {10, 0};
    
    assert(symbol.name == "myFunction");
    assert(symbol.kind == 12);
    
    std::cout << "✓ SymbolInformation structure works" << std::endl;
}

void test_position_comparison() {
    std::cout << "Testing position comparison..." << std::endl;
    
    lsp::Position pos1{5, 10};
    lsp::Position pos2{5, 10};
    lsp::Position pos3{5, 15};
    lsp::Position pos4{6, 10};
    
    assert(pos1.line == pos2.line && pos1.character == pos2.character);
    assert(pos1.line == pos3.line && pos1.character != pos3.character);
    assert(pos1.line != pos4.line);
    
    std::cout << "✓ Position comparison works" << std::endl;
}

int main() {
    std::cout << "Running LSP Types Tests...\n" << std::endl;
    
    try {
        test_position_structure();
        test_range_structure();
        test_diagnostic_structure();
        test_completion_item_structure();
        test_location_structure();
        test_hover_structure();
        test_symbol_information_structure();
        test_position_comparison();
        
        std::cout << "\n✓ All LSP Types tests passed!" << std::endl;
        return 0;
    } catch (const std::exception& e) {
        std::cerr << "\n✗ Test failed: " << e.what() << std::endl;
        return 1;
    }
}
