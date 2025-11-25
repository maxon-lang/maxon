#pragma once

#include <cstdint>
#include <string>
#include <unordered_map>
#include <vector>

namespace backend {

//==============================================================================
// DWARF Constants (DWARF version 4)
//==============================================================================

// Tag values
constexpr uint16_t DW_TAG_compile_unit = 0x11;
constexpr uint16_t DW_TAG_subprogram = 0x2e;
constexpr uint16_t DW_TAG_formal_parameter = 0x05;
constexpr uint16_t DW_TAG_variable = 0x34;
constexpr uint16_t DW_TAG_base_type = 0x24;
constexpr uint16_t DW_TAG_pointer_type = 0x0f;
constexpr uint16_t DW_TAG_array_type = 0x01;
constexpr uint16_t DW_TAG_structure_type = 0x13;
constexpr uint16_t DW_TAG_member = 0x0d;
constexpr uint16_t DW_TAG_lexical_block = 0x0b;
constexpr uint16_t DW_TAG_unspecified_parameters = 0x18;

// Child determination
constexpr uint8_t DW_CHILDREN_no = 0x00;
constexpr uint8_t DW_CHILDREN_yes = 0x01;

// Attribute names
constexpr uint16_t DW_AT_name = 0x03;
constexpr uint16_t DW_AT_stmt_list = 0x10;
constexpr uint16_t DW_AT_low_pc = 0x11;
constexpr uint16_t DW_AT_high_pc = 0x12;
constexpr uint16_t DW_AT_language = 0x13;
constexpr uint16_t DW_AT_comp_dir = 0x1b;
constexpr uint16_t DW_AT_producer = 0x25;
constexpr uint16_t DW_AT_decl_file = 0x3a;
constexpr uint16_t DW_AT_decl_line = 0x3b;
constexpr uint16_t DW_AT_type = 0x49;
constexpr uint16_t DW_AT_byte_size = 0x0b;
constexpr uint16_t DW_AT_encoding = 0x3e;
constexpr uint16_t DW_AT_frame_base = 0x40;
constexpr uint16_t DW_AT_location = 0x02;
constexpr uint16_t DW_AT_external = 0x3f;
constexpr uint16_t DW_AT_data_member_location = 0x38;

// Attribute forms
constexpr uint8_t DW_FORM_addr = 0x01;
constexpr uint8_t DW_FORM_data1 = 0x0b;
constexpr uint8_t DW_FORM_data2 = 0x05;
constexpr uint8_t DW_FORM_data4 = 0x06;
constexpr uint8_t DW_FORM_data8 = 0x07;
constexpr uint8_t DW_FORM_string = 0x08;
constexpr uint8_t DW_FORM_strp = 0x0e;
constexpr uint8_t DW_FORM_flag = 0x0c;
constexpr uint8_t DW_FORM_udata = 0x0f;
constexpr uint8_t DW_FORM_sdata = 0x0d;
constexpr uint8_t DW_FORM_sec_offset = 0x17;
constexpr uint8_t DW_FORM_ref4 = 0x13;
constexpr uint8_t DW_FORM_exprloc = 0x18;
constexpr uint8_t DW_FORM_flag_present = 0x19;

// Base type encodings
constexpr uint8_t DW_ATE_signed = 0x05;
constexpr uint8_t DW_ATE_unsigned = 0x07;
constexpr uint8_t DW_ATE_float = 0x04;
constexpr uint8_t DW_ATE_boolean = 0x02;
constexpr uint8_t DW_ATE_address = 0x01;

// Language codes
constexpr uint16_t DW_LANG_C = 0x02;
constexpr uint16_t DW_LANG_C_plus_plus = 0x04;
constexpr uint16_t DW_LANG_lo_user = 0x8000;

// Location expressions
constexpr uint8_t DW_OP_addr = 0x03;
constexpr uint8_t DW_OP_fbreg = 0x91;
constexpr uint8_t DW_OP_breg0 = 0x70;
constexpr uint8_t DW_OP_regx = 0x90;
constexpr uint8_t DW_OP_call_frame_cfa = 0x9c;

// Line number program opcodes
constexpr uint8_t DW_LNS_copy = 0x01;
constexpr uint8_t DW_LNS_advance_pc = 0x02;
constexpr uint8_t DW_LNS_advance_line = 0x03;
constexpr uint8_t DW_LNS_set_file = 0x04;
constexpr uint8_t DW_LNS_set_column = 0x05;
constexpr uint8_t DW_LNS_negate_stmt = 0x06;
constexpr uint8_t DW_LNS_set_basic_block = 0x07;
constexpr uint8_t DW_LNS_const_add_pc = 0x08;
constexpr uint8_t DW_LNS_fixed_advance_pc = 0x09;
constexpr uint8_t DW_LNS_set_prologue_end = 0x0a;
constexpr uint8_t DW_LNS_set_epilogue_begin = 0x0b;
constexpr uint8_t DW_LNS_set_isa = 0x0c;

// Extended opcodes
constexpr uint8_t DW_LNE_end_sequence = 0x01;
constexpr uint8_t DW_LNE_set_address = 0x02;
constexpr uint8_t DW_LNE_define_file = 0x03;
constexpr uint8_t DW_LNE_set_discriminator = 0x04;

//==============================================================================
// DWARF Data Structures
//==============================================================================

// Line number entry
struct LineEntry {
	uint64_t address;
	uint32_t line;
	uint32_t column;
	uint32_t fileIndex; // 1-based index into file table
	bool isStmt;
	bool prologueEnd;
	bool epilogueBegin;
};

// Source file info
struct SourceFile {
	std::string filename;
	std::string directory;
	uint64_t modTime;
	uint64_t size;
};

// Type info for debug
struct DebugType {
	std::string name;
	uint32_t byteSize;
	uint8_t encoding;
	uint32_t dieOffset; // Offset in .debug_info
	bool isPointer;
	uint32_t pointeeType; // For pointer types
};

// Variable info
struct DebugVariable {
	std::string name;
	uint32_t typeIndex;
	int32_t frameOffset; // Offset from frame base (negative for stack)
	uint32_t declFile;
	uint32_t declLine;
	bool isParameter;
};

// Function info
struct DebugFunction {
	std::string name;
	uint64_t lowPC;
	uint64_t highPC;
	uint32_t returnTypeIndex;
	uint32_t declFile;
	uint32_t declLine;
	std::vector<DebugVariable> parameters;
	std::vector<DebugVariable> locals;
	std::vector<LineEntry> lineTable;
};

// Compile unit info
struct DebugCompileUnit {
	std::string name;
	std::string compDir;
	std::string producer;
	uint16_t language;
	uint64_t lowPC;
	uint64_t highPC;
	std::vector<SourceFile> files;
	std::vector<DebugType> types;
	std::vector<DebugFunction> functions;
};

//==============================================================================
// DWARF Generator
//==============================================================================

class DwarfGenerator {
  public:
	DwarfGenerator();

	// Set compilation unit info
	void setCompileUnit(const std::string &name, const std::string &compDir,
						const std::string &producer = "Maxon Compiler 1.0");

	// Set code range
	void setCodeRange(uint64_t lowPC, uint64_t highPC);

	// Add a source file (returns 1-based file index)
	uint32_t addSourceFile(const std::string &filename, const std::string &directory = "");

	// Add a base type (returns type index)
	uint32_t addBaseType(const std::string &name, uint32_t byteSize, uint8_t encoding);

	// Add standard types
	void addStandardTypes();

	// Get type index for common types
	uint32_t getTypeIndex(const std::string &name) const;

	// Add a function
	void addFunction(const std::string &name, uint64_t lowPC, uint64_t highPC,
					 uint32_t returnType, uint32_t fileIndex, uint32_t line);

	// Add a parameter to the current function
	void addParameter(const std::string &name, uint32_t typeIndex, int32_t frameOffset,
					  uint32_t fileIndex, uint32_t line);

	// Add a local variable to the current function
	void addLocalVariable(const std::string &name, uint32_t typeIndex, int32_t frameOffset,
						  uint32_t fileIndex, uint32_t line);

	// Add a line entry to the current function
	void addLineEntry(uint64_t address, uint32_t line, uint32_t column, uint32_t fileIndex,
					  bool isStmt = true, bool prologueEnd = false, bool epilogueBegin = false);

	// Generate all DWARF sections
	void generate();

	// Get generated sections
	const std::vector<uint8_t> &getDebugInfo() const { return debugInfo; }
	const std::vector<uint8_t> &getDebugAbbrev() const { return debugAbbrev; }
	const std::vector<uint8_t> &getDebugLine() const { return debugLine; }
	const std::vector<uint8_t> &getDebugStr() const { return debugStr; }
	const std::vector<uint8_t> &getDebugAranges() const { return debugAranges; }

  private:
	DebugCompileUnit compileUnit;

	// Output buffers for each DWARF section
	std::vector<uint8_t> debugInfo;
	std::vector<uint8_t> debugAbbrev;
	std::vector<uint8_t> debugLine;
	std::vector<uint8_t> debugStr;
	std::vector<uint8_t> debugAranges;

	// String table for .debug_str
	std::unordered_map<std::string, uint32_t> stringOffsets;

	// Type name to index mapping
	std::unordered_map<std::string, uint32_t> typeNameMap;

	// Abbreviation code counter
	uint32_t nextAbbrevCode = 1;

	// Helper functions
	void writeULEB128(std::vector<uint8_t> &out, uint64_t value);
	void writeSLEB128(std::vector<uint8_t> &out, int64_t value);
	void writeString(std::vector<uint8_t> &out, const std::string &str);
	void write8(std::vector<uint8_t> &out, uint8_t value);
	void write16(std::vector<uint8_t> &out, uint16_t value);
	void write32(std::vector<uint8_t> &out, uint32_t value);
	void write64(std::vector<uint8_t> &out, uint64_t value);

	// Add string to .debug_str and return offset
	uint32_t addString(const std::string &str);

	// Generate individual sections
	void generateAbbrev();
	void generateInfo();
	void generateLine();
	void generateAranges();
};

} // namespace backend
