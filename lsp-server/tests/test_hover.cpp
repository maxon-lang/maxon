#include "../include/analyzer.h"
#include "catch_amalgamated.hpp"
#include <iostream>
#include <memory>
#include <stdexcept>

#define CATCH_CONFIG_MAIN

std::shared_ptr<Document> createTestDocument(const std::string &text) {
	return std::make_shared<Document>("file:///test.maxon", text, 0);
}

TEST_CASE("hover on 'let' variable") {
	Analyzer analyzer;
	auto doc = createTestDocument(
		"function test() int\n"
		"    let x = 42\n"
		"    return x\n"
		"end 'test'");

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
		"end 'test'");

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
		"end 'test'");

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
		"end 'add'");

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

TEST_CASE("hover on user_function", "[hover]") {

	Analyzer analyzer;
	auto doc = createTestDocument(
		"function multiply(x int, y int) int\n"
		"    return x * y\n"
		"end 'multiply'\n"
		"\n"
		"function main() int\n"
		"    return multiply(3, 4)\n"
		"end 'main'");

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
		"end 'test'");

	analyzer.analyze(doc);

	// Hover over 'return' keyword
	lsp::Position pos{1, 4};
	auto hover = analyzer.getHover(doc, pos);

	REQUIRE(hover.has_value());
	REQUIRE(hover->contents.find("return") != std::string::npos);
	// Check for documentation text (from KeywordEntry description)
	REQUIRE(hover->contents.find("Return from function") != std::string::npos);
}

TEST_CASE("hover on struct_keyword", "[hover]") {

	Analyzer analyzer;
	auto doc = createTestDocument(
		"struct Point\n"
		"    x int\n"
		"    y int\n"
		"end 'Point'");

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
		"end 'test'");

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
		"end 'test'");

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

TEST_CASE("hover on numeric_literals", "[hover]") {
	Analyzer analyzer;
	auto doc = createTestDocument(
		"function test() int\n"
		"    var i = 42\n"
		"    var f = 3.14\n"
		"    var b = 255b\n"
		"    return 0\n"
		"end 'test'");

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

TEST_CASE("hover on compiler_intrinsic", "[hover]") {
	Analyzer analyzer;
	auto doc = createTestDocument(
		"function test() int\n"
		"    var cs cstring\n"
		"    return __cstring_write_stdout(cs)\n"
		"end 'test'");

	analyzer.analyze(doc);

	// Hover over '__cstring_write_stdout' intrinsic
	lsp::Position pos{2, 15};
	auto hover = analyzer.getHover(doc, pos);

	REQUIRE(hover.has_value());
	REQUIRE(hover->contents.find("function") != std::string::npos);
	REQUIRE(hover->contents.find("__cstring_write_stdout") != std::string::npos);
	REQUIRE(hover->contents.find("cstring") != std::string::npos);
	REQUIRE(hover->contents.find("int") != std::string::npos);
	REQUIRE(hover->contents.find("compiler intrinsic") != std::string::npos);
}

TEST_CASE("hover on string_intrinsic", "[hover]") {
	Analyzer analyzer;
	auto doc = createTestDocument(
		"function test() int\n"
		"    var s = \"hello\"\n"
		"    return __string_len(s)\n"
		"end 'test'");

	analyzer.analyze(doc);

	// Hover over '__string_len' intrinsic
	lsp::Position pos{2, 15};
	auto hover = analyzer.getHover(doc, pos);

	REQUIRE(hover.has_value());
	REQUIRE(hover->contents.find("function") != std::string::npos);
	REQUIRE(hover->contents.find("__string_len") != std::string::npos);
	REQUIRE(hover->contents.find("int") != std::string::npos);
	REQUIRE(hover->contents.find("compiler intrinsic") != std::string::npos);
}

TEST_CASE("hover on struct field in method", "[hover]") {
	Analyzer analyzer;
	// Use a simple struct with methods that only read fields (no assignment)
	auto doc = createTestDocument(
		"struct Counter\n"
		"    var _count int\n"
		"    var _capacity int\n"
		"\n"
		"    function getCount() int\n"
		"        return _count\n"
		"    end 'getCount'\n"
		"\n"
		"    function getCapacity() int\n"
		"        return _capacity\n"
		"    end 'getCapacity'\n"
		"end 'Counter'");

	analyzer.analyze(doc);

	// Hover over '_count' in return _count
	// Line 5 is: "        return _count"
	// _count starts at column 15
	lsp::Position pos{5, 15}; // Line 5, char 15 (on '_count')
	auto hover = analyzer.getHover(doc, pos);

	REQUIRE(hover.has_value());
	// Should at least show the identifier name
	REQUIRE(hover->contents.find("_count") != std::string::npos);
	// Note: Full field resolution inside methods requires more semantic analysis
	// For now, we just verify hover returns something useful
}

TEST_CASE("hover on map method", "[hover]") {
	Analyzer analyzer;
	analyzer.initializeStdlib("../../../stdlib");

	auto doc = createTestDocument(
		"function main() int\n"
		"    var m = map from int to int\n"
		"    m.insert(1, 10)\n"
		"    return 0\n"
		"end 'main'");

	analyzer.analyze(doc);

	// Hover over 'insert' in m.insert(1, 10)
	// Line 2 is: "    m.insert(1, 10)"
	// 'insert' starts at column 6
	lsp::Position pos{2, 8}; // Line 2, char 8 (on 'insert')
	auto hover = analyzer.getHover(doc, pos);

	REQUIRE(hover.has_value());
	// Should show method info, not just "Identifier"
	INFO("Hover contents: " << hover->contents);
	REQUIRE(hover->contents.find("Identifier") == std::string::npos);
	// Should show it's a function/method
	REQUIRE((hover->contents.find("function") != std::string::npos ||
			 hover->contents.find("method") != std::string::npos ||
			 hover->contents.find("Method") != std::string::npos));
	REQUIRE(hover->contents.find("insert") != std::string::npos);
}
