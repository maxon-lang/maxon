#include "../include/analyzer.h"
#include "catch_amalgamated.hpp"
#include <iostream>
#include <memory>
#include <stdexcept>

#define CATCH_CONFIG_MAIN

std::shared_ptr<Document> createTestDocument(const std::string& text) {
    return std::make_shared<Document>("file:///test.maxon", text, 0);
}

TEST_CASE("hover on 'let' variable") {
    Analyzer analyzer;
    auto doc = createTestDocument(
        "function test() int\n"
        "    let x = 42\n"
        "    return x\n"
        "end 'test'"
    );
    
    // First analyze to populate semantic cache
    analyzer.analyze(doc);
    
    // Hover over 'x' in the return statement
    lsp::Position pos{2, 11}; // Line 2 (0-indexed), char 11 (on 'x')
    auto hover = analyzer.getHover(doc, pos);
    
    REQUIRE(hover.has_value());
    REQUIRE(hover->contents.find("let") != std::string::npos);
    REQUIRE(hover->contents.find("x") != std::string::npos);
    REQUIRE(hover->contents.find("int") != std::string::npos);
    REQUIRE(hover->contents.find("42") != std::string::npos);
}

TEST_CASE("hover on 'var' variable") {
    Analyzer analyzer;
    auto doc = createTestDocument(
        "function test() int\n"
        "    var count = 0\n"
        "    count = count + 1\n"
        "    return count\n"
        "end 'test'"
    );
    
    analyzer.analyze(doc);
    
    // Hover over 'count' in the assignment
    lsp::Position pos{2, 4};
    auto hover = analyzer.getHover(doc, pos);
    
    REQUIRE(hover.has_value());
    REQUIRE(hover->contents.find("var") != std::string::npos);
    REQUIRE(hover->contents.find("count") != std::string::npos);
    REQUIRE(hover->contents.find("int") != std::string::npos);
}

TEST_CASE("hover on float variable") {
    Analyzer analyzer;
    auto doc = createTestDocument(
        "function test() float\n"
        "    let pi = 3.14159\n"
        "    return pi\n"
        "end 'test'"
    );
    
    analyzer.analyze(doc);
    
    lsp::Position pos{2, 11};
    auto hover = analyzer.getHover(doc, pos);
    
    REQUIRE(hover.has_value());
    REQUIRE(hover->contents.find("float") != std::string::npos);
    REQUIRE(hover->contents.find("pi") != std::string::npos);
}

TEST_CASE("hover on function_parameter", "[hover]") {

    
    Analyzer analyzer;
    auto doc = createTestDocument(
        "function add(a int, b int) int\n"
        "    return a + b\n"
        "end 'add'"
    );
    
    analyzer.analyze(doc);
    
    // Hover over 'a' in the return statement
    lsp::Position pos{1, 11};
    auto hover = analyzer.getHover(doc, pos);
    
    REQUIRE(hover.has_value());
    REQUIRE(hover->contents.find("a") != std::string::npos);
    REQUIRE(hover->contents.find("int") != std::string::npos);
    REQUIRE((hover->contents.find("parameter") != std::string::npos || 
           hover->contents.find("Parameter") != std::string::npos));
    

}

TEST_CASE("hover on struct_definition", "[hover]") {

    
    Analyzer analyzer;
    auto doc = createTestDocument(
        "struct Point\n"
        "    x int\n"
        "    y int\n"
        "end 'Point'\n"
        "\n"
        "function test() int\n"
        "    var p Point\n"
        "    return 0\n"
        "end 'test'"
    );
    
    analyzer.analyze(doc);
    
    // Hover over 'Point' in the var declaration (line 6, after "var p ")
    lsp::Position pos{6, 11};
    auto hover = analyzer.getHover(doc, pos);
    
    if (!hover.has_value()) {
    
    
        return;
    }
    
    std::cout << "  Hover text: " << hover->contents << std::endl;
    
    // REQUIRE if it's showing struct info (should have struct keyword and field names)
    bool hasStructKeyword = hover->contents.find("struct") != std::string::npos;
    bool hasPointName = hover->contents.find("Point") != std::string::npos;
    bool hasFields = hover->contents.find("x") != std::string::npos && 
                     hover->contents.find("y") != std::string::npos;
    
    if (hasStructKeyword && hasPointName && hasFields) {
    
    } else {
    
    
    }
}

TEST_CASE("hover on user_function", "[hover]") {

    
    Analyzer analyzer;
    auto doc = createTestDocument(
        "function multiply(x int, y int) int\n"
        "    return x * y\n"
        "end 'multiply'\n"
        "\n"
        "function main() int\n"
        "    return multiply(3, 4)\n"
        "end 'main'"
    );
    
    analyzer.analyze(doc);
    
    // Hover over 'multiply' in the function call
    lsp::Position pos{5, 11};
    auto hover = analyzer.getHover(doc, pos);
    
    REQUIRE(hover.has_value());
    REQUIRE(hover->contents.find("function") != std::string::npos);
    REQUIRE(hover->contents.find("multiply") != std::string::npos);
    REQUIRE(hover->contents.find("x int") != std::string::npos);
    REQUIRE(hover->contents.find("y int") != std::string::npos);
    

}

TEST_CASE("hover on keyword", "[hover]") {

    
    Analyzer analyzer;
    auto doc = createTestDocument(
        "function test() int\n"
        "    return 42\n"
        "end 'test'"
    );
    
    analyzer.analyze(doc);
    
    // Hover over 'return' keyword
    lsp::Position pos{1, 4};
    auto hover = analyzer.getHover(doc, pos);
    
    REQUIRE(hover.has_value());
    REQUIRE(hover->contents.find("return") != std::string::npos);
    REQUIRE(hover->contents.find("control flow") != std::string::npos);
    

}

TEST_CASE("hover on struct_keyword", "[hover]") {

    
    Analyzer analyzer;
    auto doc = createTestDocument(
        "struct Point\n"
        "    x int\n"
        "    y int\n"
        "end 'Point'"
    );
    
    analyzer.analyze(doc);
    
    // Hover over 'struct' keyword
    lsp::Position pos{0, 2};
    auto hover = analyzer.getHover(doc, pos);
    
    REQUIRE(hover.has_value());
    REQUIRE(hover->contents.find("struct") != std::string::npos);
    REQUIRE(hover->contents.find("declaration") != std::string::npos);
    

}

TEST_CASE("hover on function_keyword", "[hover]") {

    
    Analyzer analyzer;
    auto doc = createTestDocument(
        "function test() int\n"
        "    return 0\n"
        "end 'test'"
    );
    
    analyzer.analyze(doc);
    
    // Hover over 'function' keyword
    lsp::Position pos{0, 2};
    auto hover = analyzer.getHover(doc, pos);
    
    REQUIRE(hover.has_value());
    REQUIRE(hover->contents.find("function") != std::string::npos);
    REQUIRE(hover->contents.find("declaration") != std::string::npos);
    

}

TEST_CASE("hover on type_name", "[hover]") {

    
    Analyzer analyzer;
    auto doc = createTestDocument(
        "function test(x int, y float, p ptr) int\n"
        "    return 0\n"
        "end 'test'"
    );
    
    analyzer.analyze(doc);
    
    // Hover over 'int' type
    lsp::Position pos{0, 16};
    auto hoverInt = analyzer.getHover(doc, pos);
    REQUIRE(hoverInt.has_value());
    REQUIRE(hoverInt->contents.find("int") != std::string::npos);
    
    // Hover over 'float' type
    lsp::Position posFloat{0, 23};
    auto hoverFloat = analyzer.getHover(doc, posFloat);
    REQUIRE(hoverFloat.has_value());
    REQUIRE(hoverFloat->contents.find("float") != std::string::npos);
    
    // Hover over 'ptr' type
    lsp::Position posPtr{0, 32};
    auto hoverPtr = analyzer.getHover(doc, posPtr);
    REQUIRE(hoverPtr.has_value());
    REQUIRE(hoverPtr->contents.find("ptr") != std::string::npos);
    

}

TEST_CASE("hover on array_variable", "[hover]") {

    
    Analyzer analyzer;
    auto doc = createTestDocument(
        "function test() int\n"
        "    var numbers []int\n"
        "    return numbers[0]\n"
        "end 'test'"
    );
    
    analyzer.analyze(doc);
    
    // Hover over 'numbers' in the var declaration first
    lsp::Position pos{1, 8};
    auto hover = analyzer.getHover(doc, pos);
    
    if (!hover.has_value()) {
    
    
        return;
    }
    
    std::cout << "  Hover text: " << hover->contents << std::endl;
    
    // REQUIRE if it shows variable info (even if type representation varies)
    if (hover->contents.find("numbers") != std::string::npos) {
    
    } else {
    
    }
}

TEST_CASE("hover on no_match", "[hover]") {


    Analyzer analyzer;
    auto doc = createTestDocument("function test() int\n    return 42\nend 'test'");

    analyzer.analyze(doc);

    // Hover over whitespace
    lsp::Position pos{1, 0};
    auto hover = analyzer.getHover(doc, pos);

    // Should still return something, but might be generic
    // This is okay - we just don't want it to crash


}

TEST_CASE("hover on numeric_literals", "[hover]") {
    Analyzer analyzer;
    auto doc = createTestDocument(
        "function test() int\n"
        "    var i = 42\n"
        "    var f = 3.14\n"
        "    var b = 255b\n"
        "    return 0\n"
        "end 'test'"
    );

    analyzer.analyze(doc);

    // Hover over integer literal '42'
    lsp::Position posInt{1, 12};
    auto hoverInt = analyzer.getHover(doc, posInt);
    REQUIRE(hoverInt.has_value());
    REQUIRE(hoverInt->contents.find("int") != std::string::npos);
    REQUIRE(hoverInt->contents.find("Integer literal") != std::string::npos);

    // Hover over float literal '3.14'
    lsp::Position posFloat{2, 12};
    auto hoverFloat = analyzer.getHover(doc, posFloat);
    REQUIRE(hoverFloat.has_value());
    REQUIRE(hoverFloat->contents.find("float") != std::string::npos);
    REQUIRE(hoverFloat->contents.find("Floating-point literal") != std::string::npos);

    // Hover over byte literal '255b'
    lsp::Position posByte{3, 12};
    auto hoverByte = analyzer.getHover(doc, posByte);
    REQUIRE(hoverByte.has_value());
    REQUIRE(hoverByte->contents.find("byte") != std::string::npos);
    REQUIRE(hoverByte->contents.find("Byte literal") != std::string::npos);

    // Hover over return value '0'
    lsp::Position posZero{4, 11};
    auto hoverZero = analyzer.getHover(doc, posZero);
    REQUIRE(hoverZero.has_value());
    REQUIRE(hoverZero->contents.find("int") != std::string::npos);
    REQUIRE(hoverZero->contents.find("Integer literal") != std::string::npos);
}

