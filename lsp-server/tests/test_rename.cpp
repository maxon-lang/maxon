#include "../include/analyzer.h"
#include "catch_amalgamated.hpp"
#include <iostream>
#include <memory>
#include <stdexcept>

#define CATCH_CONFIG_MAIN

std::shared_ptr<Document> createTestDocument(const std::string& text) {
    return std::make_shared<Document>("file:///test.maxon", text, 0);
}

TEST_CASE("rename_function_block_identifier", "[rename]") {

    
    Analyzer analyzer;
    auto doc = createTestDocument(
        "function myFunc() int\n"
        "    return 42\n"
        "end 'myFunc'"
    );
    
    // Position on the block identifier at end (line 2, col 5)
    lsp::Position pos{2, 5};
    std::string newName = "'newFunc'";
    
    auto edit = analyzer.getRename(doc, pos, newName);
    
    REQUIRE(edit.has_value());
    REQUIRE(edit->changes.size() == 1);
    REQUIRE(edit->changes.count("file:///test.maxon") == 1);
    
    auto& edits = edit->changes["file:///test.maxon"];
    REQUIRE(edits.size() == 1);
    REQUIRE(edits[0].newText == newName);
    REQUIRE(edits[0].range.start.line == 2);
    

}

TEST_CASE("rename_if_block_identifiers", "[rename]") {

    
    Analyzer analyzer;
    auto doc = createTestDocument(
        "function test() int\n"
        "    var x = 5\n"
        "    if x = 5 'REQUIRE'\n"
        "        return 1\n"
        "    else 'REQUIRE'\n"
        "        return 0\n"
        "    end 'REQUIRE'\n"
        "end 'test'"
    );
    
    // Position on the first 'REQUIRE' (line 2, col 14)
    lsp::Position pos{2, 14};
    std::string newName = "'verify'";
    
    auto edit = analyzer.getRename(doc, pos, newName);
    
    REQUIRE(edit.has_value());
    auto& edits = edit->changes["file:///test.maxon"];
    REQUIRE(edits.size() == 3);
    
    // Verify all three occurrences are renamed
    for (const auto& e : edits) {
        REQUIRE(e.newText == newName);
    }
    
    // REQUIRE that edits are on correct lines
    REQUIRE(edits[0].range.start.line == 2);
    REQUIRE(edits[1].range.start.line == 4);
    REQUIRE(edits[2].range.start.line == 6);
    

}

TEST_CASE("rename_while_block_identifiers", "[rename]") {

    
    Analyzer analyzer;
    auto doc = createTestDocument(
        "function test() int\n"
        "    var i = 0\n"
        "    while i < 10 'loop'\n"
        "        i = i + 1\n"
        "    end 'loop'\n"
        "    return i\n"
        "end 'test'"
    );
    
    // Position on end's 'loop' (line 4, col 9)
    lsp::Position pos{4, 9};
    std::string newName = "'iteration'";
    
    auto edit = analyzer.getRename(doc, pos, newName);
    
    REQUIRE(edit.has_value());
    auto& edits = edit->changes["file:///test.maxon"];
    REQUIRE(edits.size() == 2);
    
    for (const auto& e : edits) {
        REQUIRE(e.newText == newName);
    }
    

}

TEST_CASE("rename_namespace_block_identifiers", "[rename]") {

    
    Analyzer analyzer;
    auto doc = createTestDocument(
        "namespace utils 'utils'\n"
        "    function helper() int\n"
        "        return 42\n"
        "    end 'helper'\n"
        "end 'utils'"
    );
    
    // Position on first 'utils' (line 0, col 17)
    lsp::Position pos{0, 17};
    std::string newName = "'helpers'";
    
    auto edit = analyzer.getRename(doc, pos, newName);
    
    REQUIRE(edit.has_value());
    auto& edits = edit->changes["file:///test.maxon"];
    REQUIRE(edits.size() == 2);
    

}

TEST_CASE("rename_struct_block_identifiers", "[rename]") {

    
    Analyzer analyzer;
    auto doc = createTestDocument(
        "struct Point\n"
        "    x int\n"
        "    y int\n"
        "end 'Point'"
    );
    
    // Position on 'Point' at end (line 3, col 5)
    lsp::Position pos{3, 5};
    std::string newName = "'Vector2D'";
    
    auto edit = analyzer.getRename(doc, pos, newName);
    
    REQUIRE(edit.has_value());
    auto& edits = edit->changes["file:///test.maxon"];
    REQUIRE(edits.size() == 1);
    

}

TEST_CASE("rename_non_block_identifier_returns_null", "[rename]") {

    
    Analyzer analyzer;
    auto doc = createTestDocument(
        "function test() int\n"
        "    var x = 5\n"
        "    return x\n"
        "end 'test'"
    );
    
    // Position on 'var' keyword (line 1, col 4)
    lsp::Position pos{1, 4};
    std::string newName = "'newName'";
    
    auto edit = analyzer.getRename(doc, pos, newName);
    
    REQUIRE(!edit.has_value());
    

}

TEST_CASE("rename_nested_blocks", "[rename]") {

    
    Analyzer analyzer;
    auto doc = createTestDocument(
        "function test() int\n"
        "    var x = 0\n"
        "    while x < 10 'outer'\n"
        "        if x = 5 'inner'\n"
        "            x = x + 2\n"
        "        else 'inner'\n"
        "            x = x + 1\n"
        "        end 'inner'\n"
        "    end 'outer'\n"
        "    return x\n"
        "end 'test'"
    );
    
    // Rename the inner block identifier
    lsp::Position pos{3, 18}; // on 'inner'
    std::string newName = "'innerREQUIRE'";
    
    auto edit = analyzer.getRename(doc, pos, newName);
    
    REQUIRE(edit.has_value());
    auto& edits = edit->changes["file:///test.maxon"];
    REQUIRE(edits.size() == 3);
    
    // Verify only 'inner' identifiers are renamed, not 'outer'
    for (const auto& e : edits) {
        REQUIRE(e.newText == newName);
        REQUIRE((e.range.start.line >= 3 && e.range.start.line <= 7));
    }
    

}

TEST_CASE("rename_multiple_same_name_blocks", "[rename]") {

    
    Analyzer analyzer;
    auto doc = createTestDocument(
        "function test1() int\n"
        "    if true 'block'\n"
        "        return 1\n"
        "    end 'block'\n"
        "end 'test1'\n"
        "\n"
        "function test2() int\n"
        "    if false 'block'\n"
        "        return 0\n"
        "    end 'block'\n"
        "end 'test2'"
    );
    
    // Rename first 'block' (line 1, col 13)
    lsp::Position pos{1, 13};
    std::string newName = "'condition'";
    
    auto edit = analyzer.getRename(doc, pos, newName);
    
    REQUIRE(edit.has_value());
    auto& edits = edit->changes["file:///test.maxon"];
    
    // Should rename ALL occurrences of 'block' (2 in first function, 2 in second)
    REQUIRE(edits.size() == 4);
    

}

TEST_CASE("rename_struct_name", "[rename]") {

    
    Analyzer analyzer;
    auto doc = createTestDocument(
        "struct Point\n"
        "    x int\n"
        "    y int\n"
        "end 'Point'\n"
        "\n"
        "function test(p Point) int\n"
        "    var q Point\n"
        "    return 0\n"
        "end 'test'"
    );
    
    // Position on 'Point' in struct declaration (line 0, col 7)
    lsp::Position pos{0, 7};
    std::string newName = "Vector2D";
    
    auto edit = analyzer.getRename(doc, pos, newName);
    
    REQUIRE(edit.has_value());
    auto& edits = edit->changes["file:///test.maxon"];
    
    std::cout << "  Number of edits: " << edits.size() << std::endl;
    for (size_t i = 0; i < edits.size(); i++) {
        std::cout << "  Edit " << i << ": line " << edits[i].range.start.line 
                  << ", col " << edits[i].range.start.character 
                  << ", text: '" << edits[i].newText << "'" << std::endl;
    }
    
    // Should rename:
    // 1. struct Point (line 0)
    // 2. end 'Point' (line 3)
    // 3. function parameter Point (line 5)
    // 4. var q Point (line 6)
    REQUIRE(edits.size() >= 3);
    

}

TEST_CASE("rename_struct_with_array_usage", "[rename]") {

    
    Analyzer analyzer;
    auto doc = createTestDocument(
        "struct Item\n"
        "    value int\n"
        "end 'Item'\n"
        "\n"
        "function process(items []Item) int\n"
        "    return 0\n"
        "end 'process'"
    );
    
    // Position on 'Item' in struct declaration
    lsp::Position pos{0, 7};
    std::string newName = "Element";
    
    auto edit = analyzer.getRename(doc, pos, newName);
    
    REQUIRE(edit.has_value());
    auto& edits = edit->changes["file:///test.maxon"];
    
    // Should rename:
    // 1. struct Item
    // 2. end 'Item'
    // 3. []Item in parameter
    REQUIRE(edits.size() == 3);
    

}

TEST_CASE("rename_struct_with_literal_syntax", "[rename]") {

    
    Analyzer analyzer;
    auto doc = createTestDocument(
        "struct Point\n"
        "    x int\n"
        "    y int\n"
        "end 'Point'\n"
        "\n"
        "function test() int\n"
        "    var p = Point{x: 1, y: 2}\n"
        "    return 0\n"
        "end 'test'"
    );
    
    // Position on 'Point' in struct declaration
    lsp::Position pos{0, 7};
    std::string newName = "Vector";
    
    auto edit = analyzer.getRename(doc, pos, newName);
    
    REQUIRE(edit.has_value());
    auto& edits = edit->changes["file:///test.maxon"];
    
    std::cout << "  Number of edits: " << edits.size() << std::endl;
    for (size_t i = 0; i < edits.size(); i++) {
        std::cout << "  Edit " << i << ": line " << edits[i].range.start.line 
                  << ", col " << edits[i].range.start.character 
                  << ", text: '" << edits[i].newText << "'" << std::endl;
    }
    
    // Should rename:
    // 1. struct Point
    // 2. end 'Point'
    // 3. Point{...} in literal
    REQUIRE(edits.size() == 3);
    

}

TEST_CASE("linked_editing_struct_with_block_id", "[rename]") {

    
    Analyzer analyzer;
    auto doc = createTestDocument(
        "struct Point\n"
        "    x int\n"
        "    y int\n"
        "end 'Point'\n"
        "\n"
        "function test() Point\n"
        "    var p Point\n"
        "    return Point{x: 1, y: 2}\n"
        "end 'test'"
    );
    
    // Position on the struct name at declaration (line 0, col 7)
    lsp::Position pos{0, 7};
    
    auto ranges = analyzer.getLinkedEditingRanges(doc, pos);
    
    REQUIRE(ranges.has_value());
    auto& rangeList = ranges.value();
    
    std::cout << "  Number of ranges: " << rangeList.size() << std::endl;
    for (size_t i = 0; i < rangeList.size(); i++) {
        std::cout << "  Range " << i << ": line " << rangeList[i].start.line 
                  << ", col " << rangeList[i].start.character 
                  << " to " << rangeList[i].end.character << std::endl;
    }
    
    // Should include:
    // 1. struct Point (line 0)
    // 2. end 'Point' (line 3, the content inside quotes)
    // 3. return type Point (line 5)
    // 4. var p Point (line 6)
    // 5. Point{...} (line 7)
    REQUIRE(rangeList.size() == 5);
    
    // Verify each range is correct
    bool hasStructDecl = false;
    bool hasBlockId = false;
    bool hasReturnType = false;
    bool hasVarType = false;
    bool hasLiteral = false;
    
    for (const auto& range : rangeList) {
        if (range.start.line == 0 && range.start.character == 7) hasStructDecl = true;
        if (range.start.line == 3 && range.start.character == 5) hasBlockId = true; // Updated: now excludes opening quote
        if (range.start.line == 5 && range.start.character == 16) hasReturnType = true;
        if (range.start.line == 6 && range.start.character == 10) hasVarType = true;
        if (range.start.line == 7 && range.start.character == 11) hasLiteral = true;
    }
    
    REQUIRE(hasStructDecl);
    REQUIRE(hasBlockId);
    REQUIRE(hasReturnType);
    REQUIRE(hasVarType);
    REQUIRE(hasLiteral);

    

}

TEST_CASE("linked_editing_block_id_only", "[rename]") {

    
    Analyzer analyzer;
    auto doc = createTestDocument(
        "if 'condition'\n"
        "    var x = 1\n"
        "else 'condition'\n"
        "    var x = 2\n"
        "end 'condition'"
    );
    
    // Position on the if block identifier (line 0, col 4)
    lsp::Position pos{0, 4};
    
    auto ranges = analyzer.getLinkedEditingRanges(doc, pos);
    
    REQUIRE(ranges.has_value());
    auto& rangeList = ranges.value();
    
    // Should include all three 'condition' block identifiers
    REQUIRE(rangeList.size() == 3);
    

}

TEST_CASE("linked_editing_ranges_exclude_quotes", "[rename]") {

    
    Analyzer analyzer;
    auto doc = createTestDocument(
        "struct Point\n"
        "    x int\n"
        "    y int\n"
        "end 'Point'\n"
        "\n"
        "function test() Point\n"
        "    var p Point\n"
        "    return Point{x: 1, y: 2}\n"
        "end 'test'"
    );
    
    // Position on the struct name at declaration (line 0, col 7)
    lsp::Position pos{0, 7};
    
    auto ranges = analyzer.getLinkedEditingRanges(doc, pos);
    
    REQUIRE(ranges.has_value());
    auto& rangeList = ranges.value();
    
    // Find the block identifier range (line 3)
    lsp::Range* blockIdRange = nullptr;
    for (auto& range : rangeList) {
        if (range.start.line == 3) {
            blockIdRange = &range;
            break;
        }
    }
    
    REQUIRE(blockIdRange != nullptr);
    
    std::cout << "  Block ID range: line " << blockIdRange->start.line 
              << ", col " << blockIdRange->start.character 
              << " to " << blockIdRange->end.character << std::endl;
    
    // The block identifier is 'Point' on line 3
    // The text is: "end 'Point'"
    // Column positions: e(0) n(1) d(2) (3) '(4) P(5) o(6) i(7) n(8) t(9) '(10)
    // For linked editing, we want to select just "Point" (cols 5-9, exclusive end = 10)
    // NOT "'Point" (cols 4-9) which includes the leading quote
    
    REQUIRE(blockIdRange->start.character == 5);
    REQUIRE(blockIdRange->end.character == 10);
    

}

TEST_CASE("linked_editing_function_name_no_quotes", "[rename]") {

    
    Analyzer analyzer;
    auto doc = createTestDocument(
        "function myFunction() int\n"
        "    return 42\n"
        "end 'myFunction'"
    );
    
    // Position on the function name at declaration (line 0, col 9)
    lsp::Position pos{0, 9};
    
    auto ranges = analyzer.getLinkedEditingRanges(doc, pos);
    
    REQUIRE(ranges.has_value());
    auto& rangeList = ranges.value();
    
    std::cout << "  Number of ranges: " << rangeList.size() << std::endl;
    for (size_t i = 0; i < rangeList.size(); i++) {
        std::cout << "  Range " << i << ": line " << rangeList[i].start.line 
                  << ", col " << rangeList[i].start.character 
                  << " to " << rangeList[i].end.character << std::endl;
    }
    
    // Should include:
    // 1. function myFunction (line 0, cols 9-19)
    // 2. end 'myFunction' (line 2, cols 5-15, inside quotes)
    REQUIRE(rangeList.size() == 2);
    
    // Verify function name range
    bool hasFuncName = false;
    bool hasBlockId = false;
    
    for (const auto& range : rangeList) {
        if (range.start.line == 0 && range.start.character == 9 && range.end.character == 19) {
            hasFuncName = true;
        }
        if (range.start.line == 2 && range.start.character == 5 && range.end.character == 15) {
            hasBlockId = true;
        }
    }
    
    REQUIRE(hasFuncName);
    REQUIRE(hasBlockId);

    

}

TEST_CASE("linked_editing_while_loop_with_quotes", "[rename]") {

    
    Analyzer analyzer;
    auto doc = createTestDocument(
        "function test() int\n"
        "    var i = 0\n"
        "    while i < 10 'loop'\n"
        "        i = i + 1\n"
        "    end 'loop'\n"
        "    return i\n"
        "end 'test'"
    );
    
    // Position on the while block identifier (line 2, col 18 - inside 'loop')
    lsp::Position pos{2, 18};
    
    auto ranges = analyzer.getLinkedEditingRanges(doc, pos);
    
    REQUIRE(ranges.has_value());
    auto& rangeList = ranges.value();
    
    std::cout << "  Number of ranges: " << rangeList.size() << std::endl;
    for (size_t i = 0; i < rangeList.size(); i++) {
        std::cout << "  Range " << i << ": line " << rangeList[i].start.line 
                  << ", col " << rangeList[i].start.character 
                  << " to " << rangeList[i].end.character << std::endl;
    }
    
    // Should include:
    // 1. while i < 10 'loop' (line 2, cols 18-22, inside quotes)
    // 2. end 'loop' (line 4, cols 9-13, inside quotes)
    REQUIRE(rangeList.size() == 2);
    
    // Verify both ranges are for "loop" inside quotes
    bool hasWhileBlockId = false;
    bool hasEndBlockId = false;
    
    for (const auto& range : rangeList) {
        if (range.start.line == 2 && range.start.character == 18 && range.end.character == 22) {
            hasWhileBlockId = true;
        }
        if (range.start.line == 4 && range.start.character == 9 && range.end.character == 13) {
            hasEndBlockId = true;
        }
    }
    
    REQUIRE(hasWhileBlockId);
    REQUIRE(hasEndBlockId);

    

}

