#include "../include/analyzer.h"
#include <iostream>
#include <memory>
#include <stdexcept>

// Helper to check conditions and throw on failure instead of using assert
#define CHECK(condition, message) \
    if (!(condition)) { \
        throw std::runtime_error(std::string("Check failed: ") + message); \
    }

std::shared_ptr<Document> createTestDocument(const std::string& text) {
    return std::make_shared<Document>("file:///test.maxon", text, 0);
}

void test_rename_function_block_identifier() {
    std::cout << "Testing rename of function block identifier..." << std::endl;
    
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
    
    CHECK(edit.has_value(), "rename should return workspace edit");
    CHECK(edit->changes.size() == 1, "should have changes for one document");
    CHECK(edit->changes.count("file:///test.maxon") == 1, "should have changes for test document");
    
    auto& edits = edit->changes["file:///test.maxon"];
    CHECK(edits.size() == 1, "should have exactly 1 edit (only end block)");
    CHECK(edits[0].newText == newName, "new text should match requested name");
    CHECK(edits[0].range.start.line == 2, "edit should be on line 2");
    
    std::cout << "✓ Function block identifier rename works" << std::endl;
}

void test_rename_if_block_identifiers() {
    std::cout << "Testing rename of if statement block identifiers..." << std::endl;
    
    Analyzer analyzer;
    auto doc = createTestDocument(
        "function test() int\n"
        "    var x = 5\n"
        "    if x = 5 'check'\n"
        "        return 1\n"
        "    else 'check'\n"
        "        return 0\n"
        "    end 'check'\n"
        "end 'test'"
    );
    
    // Position on the first 'check' (line 2, col 14)
    lsp::Position pos{2, 14};
    std::string newName = "'verify'";
    
    auto edit = analyzer.getRename(doc, pos, newName);
    
    CHECK(edit.has_value(), "rename should return workspace edit");
    auto& edits = edit->changes["file:///test.maxon"];
    CHECK(edits.size() == 3, "should have 3 edits (if, else, end)");
    
    // Verify all three occurrences are renamed
    for (const auto& e : edits) {
        CHECK(e.newText == newName, "all edits should have new name");
    }
    
    // Check that edits are on correct lines
    CHECK(edits[0].range.start.line == 2, "first edit on if line");
    CHECK(edits[1].range.start.line == 4, "second edit on else line");
    CHECK(edits[2].range.start.line == 6, "third edit on end line");
    
    std::cout << "✓ If statement block identifiers rename works" << std::endl;
}

void test_rename_while_block_identifiers() {
    std::cout << "Testing rename of while loop block identifiers..." << std::endl;
    
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
    
    CHECK(edit.has_value(), "rename should return workspace edit");
    auto& edits = edit->changes["file:///test.maxon"];
    CHECK(edits.size() == 2, "should have 2 edits (while, end)");
    
    for (const auto& e : edits) {
        CHECK(e.newText == newName, "all edits should have new name");
    }
    
    std::cout << "✓ While loop block identifiers rename works" << std::endl;
}

void test_rename_namespace_block_identifiers() {
    std::cout << "Testing rename of namespace block identifiers..." << std::endl;
    
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
    
    CHECK(edit.has_value(), "rename should return workspace edit");
    auto& edits = edit->changes["file:///test.maxon"];
    CHECK(edits.size() == 2, "should have 2 edits (namespace, end)");
    
    std::cout << "✓ Namespace block identifiers rename works" << std::endl;
}

void test_rename_struct_block_identifiers() {
    std::cout << "Testing rename of struct block identifiers..." << std::endl;
    
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
    
    CHECK(edit.has_value(), "rename should return workspace edit");
    auto& edits = edit->changes["file:///test.maxon"];
    CHECK(edits.size() == 1, "should have 1 edit (only end block)");
    
    std::cout << "✓ Struct block identifiers rename works" << std::endl;
}

void test_rename_non_block_identifier_returns_null() {
    std::cout << "Testing rename on non-block identifier returns null..." << std::endl;
    
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
    
    CHECK(!edit.has_value(), "rename on non-block identifier should return nullopt");
    
    std::cout << "✓ Rename on non-block identifier correctly returns null" << std::endl;
}

void test_rename_nested_blocks() {
    std::cout << "Testing rename of nested block identifiers..." << std::endl;
    
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
    std::string newName = "'innerCheck'";
    
    auto edit = analyzer.getRename(doc, pos, newName);
    
    CHECK(edit.has_value(), "rename should return workspace edit");
    auto& edits = edit->changes["file:///test.maxon"];
    CHECK(edits.size() == 3, "should have 3 edits for inner block only");
    
    // Verify only 'inner' identifiers are renamed, not 'outer'
    for (const auto& e : edits) {
        CHECK(e.newText == newName, "edits should have new name");
        CHECK(e.range.start.line >= 3 && e.range.start.line <= 7, 
              "edits should only be in inner block range");
    }
    
    std::cout << "✓ Nested block identifiers rename correctly" << std::endl;
}

void test_rename_multiple_same_name_blocks() {
    std::cout << "Testing rename with multiple blocks having same identifier..." << std::endl;
    
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
    
    CHECK(edit.has_value(), "rename should return workspace edit");
    auto& edits = edit->changes["file:///test.maxon"];
    
    // Should rename ALL occurrences of 'block' (2 in first function, 2 in second)
    CHECK(edits.size() == 4, "should rename all 4 occurrences of 'block'");
    
    std::cout << "✓ Multiple blocks with same name all get renamed" << std::endl;
}

void test_rename_struct_name() {
    std::cout << "Testing rename of struct name identifier..." << std::endl;
    
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
    
    CHECK(edit.has_value(), "rename should return workspace edit");
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
    CHECK(edits.size() >= 3, "should have at least 3 edits");
    
    std::cout << "✓ Struct name identifier rename works" << std::endl;
}

void test_rename_struct_with_array_usage() {
    std::cout << "Testing rename of struct used in array type..." << std::endl;
    
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
    
    CHECK(edit.has_value(), "rename should return workspace edit");
    auto& edits = edit->changes["file:///test.maxon"];
    
    // Should rename:
    // 1. struct Item
    // 2. end 'Item'
    // 3. []Item in parameter
    CHECK(edits.size() == 3, "should have 3 edits including array usage");
    
    std::cout << "✓ Struct name with array type usage rename works" << std::endl;
}

void test_rename_struct_with_literal_syntax() {
    std::cout << "Testing rename of struct used in literal syntax..." << std::endl;
    
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
    
    CHECK(edit.has_value(), "rename should return workspace edit");
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
    CHECK(edits.size() == 3, "should have 3 edits including struct literal");
    
    std::cout << "✓ Struct name with literal syntax rename works" << std::endl;
}

void test_linked_editing_struct_with_block_id() {
    std::cout << "Testing linked editing for struct name with matching block identifier..." << std::endl;
    
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
    
    CHECK(ranges.has_value(), "linked editing should return ranges");
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
    CHECK(rangeList.size() == 5, "should have 5 linked ranges");
    
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
    
    CHECK(hasStructDecl, "should have struct declaration range");
    CHECK(hasBlockId, "should have block identifier range");
    CHECK(hasReturnType, "should have return type range");
    CHECK(hasVarType, "should have variable type range");
    CHECK(hasLiteral, "should have literal range");

    
    std::cout << "✓ Linked editing for struct with block identifier works" << std::endl;
}

void test_linked_editing_block_id_only() {
    std::cout << "Testing linked editing for block identifier without struct..." << std::endl;
    
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
    
    CHECK(ranges.has_value(), "linked editing should return ranges");
    auto& rangeList = ranges.value();
    
    // Should include all three 'condition' block identifiers
    CHECK(rangeList.size() == 3, "should have 3 linked ranges");
    
    std::cout << "✓ Linked editing for block identifier only works" << std::endl;
}

void test_linked_editing_ranges_exclude_quotes() {
    std::cout << "Testing linked editing ranges exclude quotes from block identifiers..." << std::endl;
    
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
    
    CHECK(ranges.has_value(), "linked editing should return ranges");
    auto& rangeList = ranges.value();
    
    // Find the block identifier range (line 3)
    lsp::Range* blockIdRange = nullptr;
    for (auto& range : rangeList) {
        if (range.start.line == 3) {
            blockIdRange = &range;
            break;
        }
    }
    
    CHECK(blockIdRange != nullptr, "should have range for block identifier on line 3");
    
    std::cout << "  Block ID range: line " << blockIdRange->start.line 
              << ", col " << blockIdRange->start.character 
              << " to " << blockIdRange->end.character << std::endl;
    
    // The block identifier is 'Point' on line 3
    // The text is: "end 'Point'"
    // Column positions: e(0) n(1) d(2) (3) '(4) P(5) o(6) i(7) n(8) t(9) '(10)
    // For linked editing, we want to select just "Point" (cols 5-9, exclusive end = 10)
    // NOT "'Point" (cols 4-9) which includes the leading quote
    
    CHECK(blockIdRange->start.character == 5, "block ID range should start after opening quote");
    CHECK(blockIdRange->end.character == 10, "block ID range should end before closing quote");
    
    std::cout << "✓ Linked editing ranges correctly exclude quotes from block identifiers" << std::endl;
}

int main() {
    try {
        test_rename_function_block_identifier();
        test_rename_if_block_identifiers();
        test_rename_while_block_identifiers();
        test_rename_namespace_block_identifiers();
        test_rename_struct_block_identifiers();
        test_rename_non_block_identifier_returns_null();
        test_rename_nested_blocks();
        test_rename_multiple_same_name_blocks();
        test_rename_struct_name();
        test_rename_struct_with_array_usage();
        test_rename_struct_with_literal_syntax();
        test_linked_editing_struct_with_block_id();
        test_linked_editing_block_id_only();
        test_linked_editing_ranges_exclude_quotes();
        
        std::cout << "\n✅ All rename tests passed!" << std::endl;
        return 0;
    } catch (const std::exception& e) {
        std::cerr << "\n❌ Test failed: " << e.what() << std::endl;
        return 1;
    }
}
