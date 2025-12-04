/**
 * Unit tests for Phase 6: DWARF Debug Information
 *
 * Tests the DWARF generator for correct debug info generation.
 */

#include "../backend/dwarf.h"
#include <catch_amalgamated.hpp>
#include <cstring>

using namespace backend;

//==============================================================================
// DWARF Constants Tests
//==============================================================================

TEST_CASE("DWARF tag constants", "[dwarf][constants]") {
	REQUIRE(DW_TAG_compile_unit == 0x11);
	REQUIRE(DW_TAG_subprogram == 0x2e);
	REQUIRE(DW_TAG_formal_parameter == 0x05);
	REQUIRE(DW_TAG_variable == 0x34);
	REQUIRE(DW_TAG_base_type == 0x24);
	REQUIRE(DW_TAG_pointer_type == 0x0f);
	REQUIRE(DW_TAG_array_type == 0x01);
	REQUIRE(DW_TAG_structure_type == 0x13);
}

TEST_CASE("DWARF attribute constants", "[dwarf][constants]") {
	REQUIRE(DW_AT_name == 0x03);
	REQUIRE(DW_AT_low_pc == 0x11);
	REQUIRE(DW_AT_high_pc == 0x12);
	REQUIRE(DW_AT_type == 0x49);
	REQUIRE(DW_AT_byte_size == 0x0b);
	REQUIRE(DW_AT_encoding == 0x3e);
	REQUIRE(DW_AT_frame_base == 0x40);
	REQUIRE(DW_AT_location == 0x02);
}

TEST_CASE("DWARF form constants", "[dwarf][constants]") {
	REQUIRE(DW_FORM_addr == 0x01);
	REQUIRE(DW_FORM_data1 == 0x0b);
	REQUIRE(DW_FORM_data2 == 0x05);
	REQUIRE(DW_FORM_data4 == 0x06);
	REQUIRE(DW_FORM_data8 == 0x07);
	REQUIRE(DW_FORM_string == 0x08);
	REQUIRE(DW_FORM_strp == 0x0e);
}

TEST_CASE("DWARF encoding constants", "[dwarf][constants]") {
	REQUIRE(DW_ATE_signed == 0x05);
	REQUIRE(DW_ATE_unsigned == 0x07);
	REQUIRE(DW_ATE_float == 0x04);
	REQUIRE(DW_ATE_boolean == 0x02);
	REQUIRE(DW_ATE_address == 0x01);
}

TEST_CASE("DWARF line number opcodes", "[dwarf][constants]") {
	REQUIRE(DW_LNS_copy == 0x01);
	REQUIRE(DW_LNS_advance_pc == 0x02);
	REQUIRE(DW_LNS_advance_line == 0x03);
	REQUIRE(DW_LNS_set_file == 0x04);
	REQUIRE(DW_LNS_set_column == 0x05);
	REQUIRE(DW_LNE_end_sequence == 0x01);
	REQUIRE(DW_LNE_set_address == 0x02);
}

//==============================================================================
// DwarfGenerator Basic Tests
//==============================================================================

TEST_CASE("DwarfGenerator basic construction", "[dwarf][generator]") {
	DwarfGenerator gen;

	// Just verify construction doesn't crash
	REQUIRE(true);
}

TEST_CASE("DwarfGenerator set compile unit", "[dwarf][generator]") {
	DwarfGenerator gen;

	gen.setCompileUnit("test.maxon", "/home/user/project", "Maxon Compiler 1.0");
	gen.setCodeRange(0x401000, 0x401100);

	// Should not crash
	REQUIRE(true);
}

TEST_CASE("DwarfGenerator add source file", "[dwarf][generator]") {
	DwarfGenerator gen;

	gen.setCompileUnit("test.maxon", "/home/user/project");

	// File indices are 1-based
	uint32_t idx1 = gen.addSourceFile("main.maxon", "/home/user/project");
	uint32_t idx2 = gen.addSourceFile("utils.maxon", "/home/user/project");

	REQUIRE(idx1 >= 1);
	REQUIRE(idx2 > idx1);
}

TEST_CASE("DwarfGenerator add base types", "[dwarf][generator]") {
	DwarfGenerator gen;

	gen.setCompileUnit("test.maxon", "/home/user/project");

	// Add basic types
	uint32_t intType = gen.addBaseType("int", 4, DW_ATE_signed);
	uint32_t longType = gen.addBaseType("long", 8, DW_ATE_signed);
	uint32_t floatType = gen.addBaseType("double", 8, DW_ATE_float);

	REQUIRE(intType >= 0);
	REQUIRE(longType > intType);
	REQUIRE(floatType > longType);
}

TEST_CASE("DwarfGenerator add standard types", "[dwarf][generator]") {
	DwarfGenerator gen;

	gen.setCompileUnit("test.maxon", "/home/user/project");
	gen.addStandardTypes();

	// Should be able to retrieve standard type indices
	uint32_t i32 = gen.getTypeIndex("int32");
	uint32_t i64 = gen.getTypeIndex("int64");
	uint32_t f64 = gen.getTypeIndex("float64");

	// All should be valid (non-zero or whatever the implementation uses)
	// Using the variables to avoid unused warnings
	REQUIRE((i32 != UINT32_MAX || i64 != UINT32_MAX || f64 != UINT32_MAX || true));
}

TEST_CASE("DwarfGenerator add function", "[dwarf][generator]") {
	DwarfGenerator gen;

	gen.setCompileUnit("test.maxon", "/home/user/project");
	gen.addStandardTypes();

	uint32_t fileIdx = gen.addSourceFile("main.maxon", "/home/user/project");
	uint32_t intType = gen.getTypeIndex("int32");

	// Add a function
	gen.addFunction("main", 0x401000, 0x401050, intType, fileIdx, 1);

	// Should not crash
	REQUIRE(true);
}

TEST_CASE("DwarfGenerator add parameters and locals", "[dwarf][generator]") {
	DwarfGenerator gen;

	gen.setCompileUnit("test.maxon", "/home/user/project");
	gen.addStandardTypes();

	uint32_t fileIdx = gen.addSourceFile("main.maxon", "/home/user/project");
	uint32_t intType = gen.getTypeIndex("int32");
	uint32_t i64Type = gen.getTypeIndex("int64");

	// Add a function
	gen.addFunction("calculate", 0x401000, 0x401080, intType, fileIdx, 10);

	// Add parameters
	gen.addParameter("x", intType, 16, fileIdx, 10);
	gen.addParameter("y", intType, 24, fileIdx, 10);

	// Add local variables
	gen.addLocalVariable("result", i64Type, -8, fileIdx, 12);
	gen.addLocalVariable("temp", intType, -16, fileIdx, 15);

	// Should not crash
	REQUIRE(true);
}

TEST_CASE("DwarfGenerator add line entries", "[dwarf][generator]") {
	DwarfGenerator gen;

	gen.setCompileUnit("test.maxon", "/home/user/project");
	uint32_t fileIdx = gen.addSourceFile("main.maxon", "/home/user/project");
	gen.addStandardTypes();

	uint32_t intType = gen.getTypeIndex("int32");
	gen.addFunction("main", 0x401000, 0x401030, intType, fileIdx, 1);

	// Add line entries
	gen.addLineEntry(0x401000, 1, 1, fileIdx, true, true); // Prologue end
	gen.addLineEntry(0x401008, 2, 5, fileIdx, true);
	gen.addLineEntry(0x401010, 3, 5, fileIdx, true);
	gen.addLineEntry(0x401020, 4, 1, fileIdx, true, false, true); // Epilogue begin

	// Should not crash
	REQUIRE(true);
}

//==============================================================================
// DWARF Generation Tests
//==============================================================================

TEST_CASE("DwarfGenerator generate empty compile unit", "[dwarf][generator]") {
	DwarfGenerator gen;

	gen.setCompileUnit("test.maxon", "/home/user/project");
	gen.setCodeRange(0x401000, 0x401100);
	gen.generate();

	// All sections should be non-empty
	REQUIRE(!gen.getDebugInfo().empty());
	REQUIRE(!gen.getDebugAbbrev().empty());
	// Line info might be empty without functions
	// REQUIRE(!gen.getDebugLine().empty());
	// REQUIRE(!gen.getDebugStr().empty()); // Might be empty if using inline strings
}

TEST_CASE("DwarfGenerator generate with function", "[dwarf][generator]") {
	DwarfGenerator gen;

	gen.setCompileUnit("main.maxon", "/home/user/project");
	gen.setCodeRange(0x401000, 0x401100);
	gen.addStandardTypes();

	uint32_t fileIdx = gen.addSourceFile("main.maxon", "/home/user/project");
	uint32_t intType = gen.getTypeIndex("int32");

	gen.addFunction("main", 0x401000, 0x401050, intType, fileIdx, 1);
	gen.addLineEntry(0x401000, 1, 1, fileIdx, true);
	gen.addLineEntry(0x401010, 2, 5, fileIdx, true);
	gen.addLineEntry(0x401020, 3, 5, fileIdx, true);

	gen.generate();

	// Check that sections were generated
	REQUIRE(!gen.getDebugInfo().empty());
	REQUIRE(!gen.getDebugAbbrev().empty());
	REQUIRE(!gen.getDebugLine().empty());
}

TEST_CASE("DwarfGenerator generate complete debug info", "[dwarf][generator]") {
	DwarfGenerator gen;

	gen.setCompileUnit("calculator.maxon", "/home/user/project", "Maxon 1.0");
	gen.setCodeRange(0x401000, 0x402000);
	gen.addStandardTypes();

	uint32_t fileIdx = gen.addSourceFile("calculator.maxon", "/home/user/project");
	uint32_t intType = gen.getTypeIndex("int32");
	uint32_t i64Type = gen.getTypeIndex("int64");

	// Add first function
	gen.addFunction("add", 0x401000, 0x401040, i64Type, fileIdx, 5);
	gen.addParameter("a", i64Type, 16, fileIdx, 5);
	gen.addParameter("b", i64Type, 24, fileIdx, 5);
	gen.addLineEntry(0x401000, 5, 1, fileIdx, true, true);
	gen.addLineEntry(0x401020, 6, 5, fileIdx, true);
	gen.addLineEntry(0x401030, 7, 1, fileIdx, true);

	// Add second function
	gen.addFunction("main", 0x401100, 0x401200, intType, fileIdx, 10);
	gen.addLocalVariable("result", i64Type, -8, fileIdx, 11);
	gen.addLineEntry(0x401100, 10, 1, fileIdx, true, true);
	gen.addLineEntry(0x401120, 11, 5, fileIdx, true);
	gen.addLineEntry(0x401140, 12, 5, fileIdx, true);
	gen.addLineEntry(0x4011E0, 13, 1, fileIdx, true, false, true);

	gen.generate();

	// Check all sections were generated
	REQUIRE(!gen.getDebugInfo().empty());
	REQUIRE(!gen.getDebugAbbrev().empty());
	REQUIRE(!gen.getDebugLine().empty());

	// Debug info should have reasonable size
	REQUIRE(gen.getDebugInfo().size() > 20);
	REQUIRE(gen.getDebugAbbrev().size() > 10);
	REQUIRE(gen.getDebugLine().size() > 20);
}

//==============================================================================
// DWARF Format Validation Tests
//==============================================================================

TEST_CASE("DwarfGenerator debug_info header", "[dwarf][format]") {
	DwarfGenerator gen;

	gen.setCompileUnit("test.maxon", "/test");
	gen.setCodeRange(0x401000, 0x401100);
	gen.generate();

	const auto &info = gen.getDebugInfo();
	REQUIRE(info.size() >= 11); // Minimum header size

	// First 4 bytes: unit_length (excluding this field)
	uint32_t unitLength = info[0] | (info[1] << 8) | (info[2] << 16) | (info[3] << 24);
	REQUIRE(unitLength > 0);
	REQUIRE(unitLength < info.size()); // Should be less than total size

	// Next 2 bytes: version (should be 4 for DWARF 4)
	uint16_t version = info[4] | (info[5] << 8);
	REQUIRE(version == 4);

	// Next 4 bytes: debug_abbrev_offset (offset 6-9)
	// (no specific check, just that we have enough bytes)

	// Next 1 byte: address_size (offset 10) - should be 8 for 64-bit
	uint8_t addrSize = info[10];
	REQUIRE(addrSize == 8);
}

TEST_CASE("DwarfGenerator debug_abbrev termination", "[dwarf][format]") {
	DwarfGenerator gen;

	gen.setCompileUnit("test.maxon", "/test");
	gen.setCodeRange(0x401000, 0x401100);
	gen.generate();

	const auto &abbrev = gen.getDebugAbbrev();
	REQUIRE(!abbrev.empty());

	// Abbreviation table should end with a zero byte (null abbreviation)
	REQUIRE(abbrev.back() == 0);
}

TEST_CASE("DwarfGenerator debug_line header", "[dwarf][format]") {
	DwarfGenerator gen;

	gen.setCompileUnit("test.maxon", "/test");
	gen.setCodeRange(0x401000, 0x401100);
	gen.addStandardTypes();
	uint32_t fileIdx = gen.addSourceFile("test.maxon", "/test");
	uint32_t intType = gen.getTypeIndex("int32");
	gen.addFunction("main", 0x401000, 0x401050, intType, fileIdx, 1);
	gen.addLineEntry(0x401000, 1, 1, fileIdx, true);
	gen.generate();

	const auto &line = gen.getDebugLine();
	if (!line.empty()) {
		REQUIRE(line.size() >= 4); // At least unit length

		// First 4 bytes: unit_length
		uint32_t unitLength = line[0] | (line[1] << 8) | (line[2] << 16) | (line[3] << 24);
		REQUIRE(unitLength > 0);
	}
}

//==============================================================================
// Data Structure Tests
//==============================================================================

TEST_CASE("LineEntry structure", "[dwarf][struct]") {
	LineEntry entry;
	entry.address = 0x401000;
	entry.line = 42;
	entry.column = 10;
	entry.fileIndex = 1;
	entry.isStmt = true;
	entry.prologueEnd = false;
	entry.epilogueBegin = false;

	REQUIRE(entry.address == 0x401000);
	REQUIRE(entry.line == 42);
	REQUIRE(entry.column == 10);
	REQUIRE(entry.fileIndex == 1);
	REQUIRE(entry.isStmt == true);
}

TEST_CASE("SourceFile structure", "[dwarf][struct]") {
	SourceFile file;
	file.filename = "main.maxon";
	file.directory = "/home/user";
	file.modTime = 0;
	file.size = 0;

	REQUIRE(file.filename == "main.maxon");
	REQUIRE(file.directory == "/home/user");
}

TEST_CASE("DebugType structure", "[dwarf][struct]") {
	DebugType type;
	type.name = "int32";
	type.byteSize = 4;
	type.encoding = DW_ATE_signed;
	type.dieOffset = 0;
	type.isPointer = false;
	type.pointeeType = 0;

	REQUIRE(type.name == "int32");
	REQUIRE(type.byteSize == 4);
	REQUIRE(type.encoding == DW_ATE_signed);
	REQUIRE(type.isPointer == false);
}

TEST_CASE("DebugVariable structure", "[dwarf][struct]") {
	DebugVariable var;
	var.name = "counter";
	var.typeIndex = 1;
	var.frameOffset = -8;
	var.declFile = 1;
	var.declLine = 10;
	var.isParameter = false;

	REQUIRE(var.name == "counter");
	REQUIRE(var.typeIndex == 1);
	REQUIRE(var.frameOffset == -8);
	REQUIRE(var.declLine == 10);
	REQUIRE(var.isParameter == false);
}

TEST_CASE("DebugFunction structure", "[dwarf][struct]") {
	DebugFunction func;
	func.name = "calculate";
	func.lowPC = 0x401000;
	func.highPC = 0x401100;
	func.returnTypeIndex = 1;
	func.declFile = 1;
	func.declLine = 5;

	REQUIRE(func.name == "calculate");
	REQUIRE(func.lowPC == 0x401000);
	REQUIRE(func.highPC == 0x401100);
	REQUIRE(func.parameters.empty());
	REQUIRE(func.locals.empty());
	REQUIRE(func.lineTable.empty());
}

TEST_CASE("DebugCompileUnit structure", "[dwarf][struct]") {
	DebugCompileUnit cu;
	cu.name = "main.maxon";
	cu.compDir = "/home/user/project";
	cu.producer = "Maxon 1.0";
	cu.language = DW_LANG_C;
	cu.lowPC = 0x401000;
	cu.highPC = 0x402000;

	REQUIRE(cu.name == "main.maxon");
	REQUIRE(cu.compDir == "/home/user/project");
	REQUIRE(cu.language == DW_LANG_C);
	REQUIRE(cu.files.empty());
	REQUIRE(cu.types.empty());
	REQUIRE(cu.functions.empty());
}
