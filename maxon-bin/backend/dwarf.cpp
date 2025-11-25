#include "dwarf.h"
#include <algorithm>

namespace backend {

DwarfGenerator::DwarfGenerator() {
	compileUnit.language = DW_LANG_lo_user; // Custom language
}

void DwarfGenerator::setCompileUnit(const std::string &name, const std::string &compDir,
									const std::string &producer) {
	compileUnit.name = name;
	compileUnit.compDir = compDir;
	compileUnit.producer = producer;
}

void DwarfGenerator::setCodeRange(uint64_t lowPC, uint64_t highPC) {
	compileUnit.lowPC = lowPC;
	compileUnit.highPC = highPC;
}

uint32_t DwarfGenerator::addSourceFile(const std::string &filename,
									   const std::string &directory) {
	SourceFile file;
	file.filename = filename;
	file.directory = directory.empty() ? compileUnit.compDir : directory;
	file.modTime = 0;
	file.size = 0;

	compileUnit.files.push_back(file);
	return static_cast<uint32_t>(compileUnit.files.size()); // 1-based
}

uint32_t DwarfGenerator::addBaseType(const std::string &name, uint32_t byteSize,
									 uint8_t encoding) {
	DebugType type;
	type.name = name;
	type.byteSize = byteSize;
	type.encoding = encoding;
	type.isPointer = false;
	type.pointeeType = 0;
	type.dieOffset = 0; // Set during generation

	uint32_t index = static_cast<uint32_t>(compileUnit.types.size());
	typeNameMap[name] = index;
	compileUnit.types.push_back(type);
	return index;
}

void DwarfGenerator::addStandardTypes() {
	// Add common base types
	addBaseType("i32", 4, DW_ATE_signed);
	addBaseType("i64", 8, DW_ATE_signed);
	addBaseType("u32", 4, DW_ATE_unsigned);
	addBaseType("u64", 8, DW_ATE_unsigned);
	addBaseType("f32", 4, DW_ATE_float);
	addBaseType("f64", 8, DW_ATE_float);
	addBaseType("bool", 1, DW_ATE_boolean);
	addBaseType("void", 0, DW_ATE_unsigned);
	addBaseType("ptr", 8, DW_ATE_address);
}

uint32_t DwarfGenerator::getTypeIndex(const std::string &name) const {
	auto it = typeNameMap.find(name);
	if (it != typeNameMap.end()) {
		return it->second;
	}
	return 0; // Default to first type (void-like)
}

void DwarfGenerator::addFunction(const std::string &name, uint64_t lowPC, uint64_t highPC,
								 uint32_t returnType, uint32_t fileIndex, uint32_t line) {
	DebugFunction func;
	func.name = name;
	func.lowPC = lowPC;
	func.highPC = highPC;
	func.returnTypeIndex = returnType;
	func.declFile = fileIndex;
	func.declLine = line;

	compileUnit.functions.push_back(func);
}

void DwarfGenerator::addParameter(const std::string &name, uint32_t typeIndex,
								  int32_t frameOffset, uint32_t fileIndex, uint32_t line) {
	if (compileUnit.functions.empty())
		return;

	DebugVariable var;
	var.name = name;
	var.typeIndex = typeIndex;
	var.frameOffset = frameOffset;
	var.declFile = fileIndex;
	var.declLine = line;
	var.isParameter = true;

	compileUnit.functions.back().parameters.push_back(var);
}

void DwarfGenerator::addLocalVariable(const std::string &name, uint32_t typeIndex,
									  int32_t frameOffset, uint32_t fileIndex, uint32_t line) {
	if (compileUnit.functions.empty())
		return;

	DebugVariable var;
	var.name = name;
	var.typeIndex = typeIndex;
	var.frameOffset = frameOffset;
	var.declFile = fileIndex;
	var.declLine = line;
	var.isParameter = false;

	compileUnit.functions.back().locals.push_back(var);
}

void DwarfGenerator::addLineEntry(uint64_t address, uint32_t line, uint32_t column,
								  uint32_t fileIndex, bool isStmt, bool prologueEnd,
								  bool epilogueBegin) {
	if (compileUnit.functions.empty())
		return;

	LineEntry entry;
	entry.address = address;
	entry.line = line;
	entry.column = column;
	entry.fileIndex = fileIndex;
	entry.isStmt = isStmt;
	entry.prologueEnd = prologueEnd;
	entry.epilogueBegin = epilogueBegin;

	compileUnit.functions.back().lineTable.push_back(entry);
}

void DwarfGenerator::generate() {
	// Clear output buffers
	debugInfo.clear();
	debugAbbrev.clear();
	debugLine.clear();
	debugStr.clear();
	debugAranges.clear();
	stringOffsets.clear();
	nextAbbrevCode = 1;

	// Add empty string at offset 0
	debugStr.push_back(0);
	stringOffsets[""] = 0;

	// Generate sections in order
	generateAbbrev();
	generateInfo();
	generateLine();
	generateAranges();
}

//==============================================================================
// Helper Functions
//==============================================================================

void DwarfGenerator::writeULEB128(std::vector<uint8_t> &out, uint64_t value) {
	do {
		uint8_t byte = value & 0x7f;
		value >>= 7;
		if (value != 0) {
			byte |= 0x80;
		}
		out.push_back(byte);
	} while (value != 0);
}

void DwarfGenerator::writeSLEB128(std::vector<uint8_t> &out, int64_t value) {
	bool more = true;
	while (more) {
		uint8_t byte = value & 0x7f;
		value >>= 7;

		bool signBit = (byte & 0x40) != 0;
		if ((value == 0 && !signBit) || (value == -1 && signBit)) {
			more = false;
		} else {
			byte |= 0x80;
		}
		out.push_back(byte);
	}
}

void DwarfGenerator::writeString(std::vector<uint8_t> &out, const std::string &str) {
	out.insert(out.end(), str.begin(), str.end());
	out.push_back(0);
}

void DwarfGenerator::write8(std::vector<uint8_t> &out, uint8_t value) {
	out.push_back(value);
}

void DwarfGenerator::write16(std::vector<uint8_t> &out, uint16_t value) {
	out.push_back(value & 0xff);
	out.push_back((value >> 8) & 0xff);
}

void DwarfGenerator::write32(std::vector<uint8_t> &out, uint32_t value) {
	out.push_back(value & 0xff);
	out.push_back((value >> 8) & 0xff);
	out.push_back((value >> 16) & 0xff);
	out.push_back((value >> 24) & 0xff);
}

void DwarfGenerator::write64(std::vector<uint8_t> &out, uint64_t value) {
	write32(out, static_cast<uint32_t>(value));
	write32(out, static_cast<uint32_t>(value >> 32));
}

uint32_t DwarfGenerator::addString(const std::string &str) {
	auto it = stringOffsets.find(str);
	if (it != stringOffsets.end()) {
		return it->second;
	}

	uint32_t offset = static_cast<uint32_t>(debugStr.size());
	debugStr.insert(debugStr.end(), str.begin(), str.end());
	debugStr.push_back(0);
	stringOffsets[str] = offset;
	return offset;
}

//==============================================================================
// Abbreviation Table Generation
//==============================================================================

void DwarfGenerator::generateAbbrev() {
	// Abbreviation 1: Compile Unit
	writeULEB128(debugAbbrev, 1); // Abbreviation code
	writeULEB128(debugAbbrev, DW_TAG_compile_unit);
	write8(debugAbbrev, DW_CHILDREN_yes);
	// Attributes
	writeULEB128(debugAbbrev, DW_AT_producer);
	writeULEB128(debugAbbrev, DW_FORM_strp);
	writeULEB128(debugAbbrev, DW_AT_language);
	writeULEB128(debugAbbrev, DW_FORM_data2);
	writeULEB128(debugAbbrev, DW_AT_name);
	writeULEB128(debugAbbrev, DW_FORM_strp);
	writeULEB128(debugAbbrev, DW_AT_comp_dir);
	writeULEB128(debugAbbrev, DW_FORM_strp);
	writeULEB128(debugAbbrev, DW_AT_low_pc);
	writeULEB128(debugAbbrev, DW_FORM_addr);
	writeULEB128(debugAbbrev, DW_AT_high_pc);
	writeULEB128(debugAbbrev, DW_FORM_data8);
	writeULEB128(debugAbbrev, DW_AT_stmt_list);
	writeULEB128(debugAbbrev, DW_FORM_sec_offset);
	writeULEB128(debugAbbrev, 0); // End of attributes
	writeULEB128(debugAbbrev, 0);

	// Abbreviation 2: Base Type
	writeULEB128(debugAbbrev, 2);
	writeULEB128(debugAbbrev, DW_TAG_base_type);
	write8(debugAbbrev, DW_CHILDREN_no);
	writeULEB128(debugAbbrev, DW_AT_name);
	writeULEB128(debugAbbrev, DW_FORM_strp);
	writeULEB128(debugAbbrev, DW_AT_byte_size);
	writeULEB128(debugAbbrev, DW_FORM_data1);
	writeULEB128(debugAbbrev, DW_AT_encoding);
	writeULEB128(debugAbbrev, DW_FORM_data1);
	writeULEB128(debugAbbrev, 0);
	writeULEB128(debugAbbrev, 0);

	// Abbreviation 3: Subprogram (function) with children
	writeULEB128(debugAbbrev, 3);
	writeULEB128(debugAbbrev, DW_TAG_subprogram);
	write8(debugAbbrev, DW_CHILDREN_yes);
	writeULEB128(debugAbbrev, DW_AT_name);
	writeULEB128(debugAbbrev, DW_FORM_strp);
	writeULEB128(debugAbbrev, DW_AT_low_pc);
	writeULEB128(debugAbbrev, DW_FORM_addr);
	writeULEB128(debugAbbrev, DW_AT_high_pc);
	writeULEB128(debugAbbrev, DW_FORM_data8);
	writeULEB128(debugAbbrev, DW_AT_type);
	writeULEB128(debugAbbrev, DW_FORM_ref4);
	writeULEB128(debugAbbrev, DW_AT_frame_base);
	writeULEB128(debugAbbrev, DW_FORM_exprloc);
	writeULEB128(debugAbbrev, DW_AT_decl_file);
	writeULEB128(debugAbbrev, DW_FORM_data1);
	writeULEB128(debugAbbrev, DW_AT_decl_line);
	writeULEB128(debugAbbrev, DW_FORM_data4);
	writeULEB128(debugAbbrev, DW_AT_external);
	writeULEB128(debugAbbrev, DW_FORM_flag_present);
	writeULEB128(debugAbbrev, 0);
	writeULEB128(debugAbbrev, 0);

	// Abbreviation 4: Subprogram without children
	writeULEB128(debugAbbrev, 4);
	writeULEB128(debugAbbrev, DW_TAG_subprogram);
	write8(debugAbbrev, DW_CHILDREN_no);
	writeULEB128(debugAbbrev, DW_AT_name);
	writeULEB128(debugAbbrev, DW_FORM_strp);
	writeULEB128(debugAbbrev, DW_AT_low_pc);
	writeULEB128(debugAbbrev, DW_FORM_addr);
	writeULEB128(debugAbbrev, DW_AT_high_pc);
	writeULEB128(debugAbbrev, DW_FORM_data8);
	writeULEB128(debugAbbrev, DW_AT_type);
	writeULEB128(debugAbbrev, DW_FORM_ref4);
	writeULEB128(debugAbbrev, DW_AT_frame_base);
	writeULEB128(debugAbbrev, DW_FORM_exprloc);
	writeULEB128(debugAbbrev, DW_AT_decl_file);
	writeULEB128(debugAbbrev, DW_FORM_data1);
	writeULEB128(debugAbbrev, DW_AT_decl_line);
	writeULEB128(debugAbbrev, DW_FORM_data4);
	writeULEB128(debugAbbrev, DW_AT_external);
	writeULEB128(debugAbbrev, DW_FORM_flag_present);
	writeULEB128(debugAbbrev, 0);
	writeULEB128(debugAbbrev, 0);

	// Abbreviation 5: Formal Parameter
	writeULEB128(debugAbbrev, 5);
	writeULEB128(debugAbbrev, DW_TAG_formal_parameter);
	write8(debugAbbrev, DW_CHILDREN_no);
	writeULEB128(debugAbbrev, DW_AT_name);
	writeULEB128(debugAbbrev, DW_FORM_strp);
	writeULEB128(debugAbbrev, DW_AT_type);
	writeULEB128(debugAbbrev, DW_FORM_ref4);
	writeULEB128(debugAbbrev, DW_AT_location);
	writeULEB128(debugAbbrev, DW_FORM_exprloc);
	writeULEB128(debugAbbrev, DW_AT_decl_file);
	writeULEB128(debugAbbrev, DW_FORM_data1);
	writeULEB128(debugAbbrev, DW_AT_decl_line);
	writeULEB128(debugAbbrev, DW_FORM_data4);
	writeULEB128(debugAbbrev, 0);
	writeULEB128(debugAbbrev, 0);

	// Abbreviation 6: Variable
	writeULEB128(debugAbbrev, 6);
	writeULEB128(debugAbbrev, DW_TAG_variable);
	write8(debugAbbrev, DW_CHILDREN_no);
	writeULEB128(debugAbbrev, DW_AT_name);
	writeULEB128(debugAbbrev, DW_FORM_strp);
	writeULEB128(debugAbbrev, DW_AT_type);
	writeULEB128(debugAbbrev, DW_FORM_ref4);
	writeULEB128(debugAbbrev, DW_AT_location);
	writeULEB128(debugAbbrev, DW_FORM_exprloc);
	writeULEB128(debugAbbrev, DW_AT_decl_file);
	writeULEB128(debugAbbrev, DW_FORM_data1);
	writeULEB128(debugAbbrev, DW_AT_decl_line);
	writeULEB128(debugAbbrev, DW_FORM_data4);
	writeULEB128(debugAbbrev, 0);
	writeULEB128(debugAbbrev, 0);

	// End of abbreviations
	write8(debugAbbrev, 0);
}

//==============================================================================
// Debug Info Generation
//==============================================================================

void DwarfGenerator::generateInfo() {
	// Reserve space for header (will fill in later)
	size_t headerStart = debugInfo.size();
	write32(debugInfo, 0); // unit_length (placeholder)
	write16(debugInfo, 4); // DWARF version 4
	write32(debugInfo, 0); // debug_abbrev_offset
	write8(debugInfo, 8);  // address_size (64-bit)

	(void)headerStart; // Reserved for future use

	// Compile Unit DIE (abbreviation 1)
	writeULEB128(debugInfo, 1);
	write32(debugInfo, addString(compileUnit.producer));		// DW_AT_producer
	write16(debugInfo, compileUnit.language);					// DW_AT_language
	write32(debugInfo, addString(compileUnit.name));			// DW_AT_name
	write32(debugInfo, addString(compileUnit.compDir));			// DW_AT_comp_dir
	write64(debugInfo, compileUnit.lowPC);						// DW_AT_low_pc
	write64(debugInfo, compileUnit.highPC - compileUnit.lowPC); // DW_AT_high_pc (offset)
	write32(debugInfo, 0);										// DW_AT_stmt_list (offset to .debug_line)

	// Track type DIE offsets
	std::vector<uint32_t> typeOffsets;

	// Base Type DIEs (abbreviation 2)
	for (auto &type : compileUnit.types) {
		type.dieOffset = static_cast<uint32_t>(debugInfo.size() - headerStart);
		typeOffsets.push_back(type.dieOffset);

		writeULEB128(debugInfo, 2);
		write32(debugInfo, addString(type.name));				// DW_AT_name
		write8(debugInfo, static_cast<uint8_t>(type.byteSize)); // DW_AT_byte_size
		write8(debugInfo, type.encoding);						// DW_AT_encoding
	}

	// Function DIEs
	for (const auto &func : compileUnit.functions) {
		bool hasChildren = !func.parameters.empty() || !func.locals.empty();
		writeULEB128(debugInfo, hasChildren ? 3 : 4); // Abbreviation code

		write32(debugInfo, addString(func.name));	  // DW_AT_name
		write64(debugInfo, func.lowPC);				  // DW_AT_low_pc
		write64(debugInfo, func.highPC - func.lowPC); // DW_AT_high_pc

		// Return type reference
		uint32_t typeOffset = 0;
		if (func.returnTypeIndex < typeOffsets.size()) {
			typeOffset = typeOffsets[func.returnTypeIndex];
		}
		write32(debugInfo, typeOffset); // DW_AT_type

		// Frame base: DW_OP_call_frame_cfa
		writeULEB128(debugInfo, 1); // Expression length
		write8(debugInfo, DW_OP_call_frame_cfa);

		write8(debugInfo, static_cast<uint8_t>(func.declFile)); // DW_AT_decl_file
		write32(debugInfo, func.declLine);						// DW_AT_decl_line
		// DW_AT_external is flag_present, no value needed

		if (hasChildren) {
			// Parameters (abbreviation 5)
			for (const auto &param : func.parameters) {
				writeULEB128(debugInfo, 5);
				write32(debugInfo, addString(param.name)); // DW_AT_name

				uint32_t paramTypeOffset = 0;
				if (param.typeIndex < typeOffsets.size()) {
					paramTypeOffset = typeOffsets[param.typeIndex];
				}
				write32(debugInfo, paramTypeOffset); // DW_AT_type

				// Location: DW_OP_fbreg(offset)
				std::vector<uint8_t> locExpr;
				locExpr.push_back(DW_OP_fbreg);
				writeSLEB128(locExpr, param.frameOffset);
				writeULEB128(debugInfo, locExpr.size());
				debugInfo.insert(debugInfo.end(), locExpr.begin(), locExpr.end());

				write8(debugInfo, static_cast<uint8_t>(param.declFile)); // DW_AT_decl_file
				write32(debugInfo, param.declLine);						 // DW_AT_decl_line
			}

			// Local variables (abbreviation 6)
			for (const auto &local : func.locals) {
				writeULEB128(debugInfo, 6);
				write32(debugInfo, addString(local.name)); // DW_AT_name

				uint32_t localTypeOffset = 0;
				if (local.typeIndex < typeOffsets.size()) {
					localTypeOffset = typeOffsets[local.typeIndex];
				}
				write32(debugInfo, localTypeOffset); // DW_AT_type

				// Location
				std::vector<uint8_t> locExpr;
				locExpr.push_back(DW_OP_fbreg);
				writeSLEB128(locExpr, local.frameOffset);
				writeULEB128(debugInfo, locExpr.size());
				debugInfo.insert(debugInfo.end(), locExpr.begin(), locExpr.end());

				write8(debugInfo, static_cast<uint8_t>(local.declFile)); // DW_AT_decl_file
				write32(debugInfo, local.declLine);						 // DW_AT_decl_line
			}

			// End of children
			write8(debugInfo, 0);
		}
	}

	// End of compile unit children
	write8(debugInfo, 0);

	// Update unit length
	uint32_t unitLength = static_cast<uint32_t>(debugInfo.size() - headerStart - 4);
	debugInfo[headerStart] = unitLength & 0xff;
	debugInfo[headerStart + 1] = (unitLength >> 8) & 0xff;
	debugInfo[headerStart + 2] = (unitLength >> 16) & 0xff;
	debugInfo[headerStart + 3] = (unitLength >> 24) & 0xff;
}

//==============================================================================
// Line Number Program Generation
//==============================================================================

void DwarfGenerator::generateLine() {
	// Collect all line entries from all functions
	std::vector<LineEntry> allLines;
	for (const auto &func : compileUnit.functions) {
		allLines.insert(allLines.end(), func.lineTable.begin(), func.lineTable.end());
	}

	// Sort by address
	std::sort(allLines.begin(), allLines.end(),
			  [](const LineEntry &a, const LineEntry &b) {
				  return a.address < b.address;
			  });

	// Line number program header
	size_t headerStart = debugLine.size();
	write32(debugLine, 0); // unit_length (placeholder)
	write16(debugLine, 4); // DWARF version 4
	size_t headerLengthPos = debugLine.size();
	write32(debugLine, 0); // header_length (placeholder)

	size_t prologueStart = debugLine.size();

	// Standard header fields
	write8(debugLine, 1);						 // minimum_instruction_length
	write8(debugLine, 1);						 // maximum_operations_per_instruction (DWARF4)
	write8(debugLine, 1);						 // default_is_stmt
	write8(debugLine, static_cast<uint8_t>(-5)); // line_base
	write8(debugLine, 14);						 // line_range
	write8(debugLine, 13);						 // opcode_base

	// Standard opcode lengths
	write8(debugLine, 0); // DW_LNS_copy
	write8(debugLine, 1); // DW_LNS_advance_pc
	write8(debugLine, 1); // DW_LNS_advance_line
	write8(debugLine, 1); // DW_LNS_set_file
	write8(debugLine, 1); // DW_LNS_set_column
	write8(debugLine, 0); // DW_LNS_negate_stmt
	write8(debugLine, 0); // DW_LNS_set_basic_block
	write8(debugLine, 0); // DW_LNS_const_add_pc
	write8(debugLine, 1); // DW_LNS_fixed_advance_pc
	write8(debugLine, 0); // DW_LNS_set_prologue_end
	write8(debugLine, 0); // DW_LNS_set_epilogue_begin
	write8(debugLine, 1); // DW_LNS_set_isa

	// Include directories (empty for now)
	write8(debugLine, 0); // End of include directories

	// File names
	for (const auto &file : compileUnit.files) {
		writeString(debugLine, file.filename);
		writeULEB128(debugLine, 0); // Directory index
		writeULEB128(debugLine, file.modTime);
		writeULEB128(debugLine, file.size);
	}
	write8(debugLine, 0); // End of file names

	// Update header length
	uint32_t headerLength = static_cast<uint32_t>(debugLine.size() - prologueStart);
	debugLine[headerLengthPos] = headerLength & 0xff;
	debugLine[headerLengthPos + 1] = (headerLength >> 8) & 0xff;
	debugLine[headerLengthPos + 2] = (headerLength >> 16) & 0xff;
	debugLine[headerLengthPos + 3] = (headerLength >> 24) & 0xff;

	// Line number program
	if (!allLines.empty()) {
		uint64_t currentAddr = 0;
		uint32_t currentLine = 1;
		uint32_t currentFile = 1;

		for (const auto &entry : allLines) {
			// Set address (extended opcode)
			write8(debugLine, 0);		// Extended opcode indicator
			writeULEB128(debugLine, 9); // Length (1 + 8)
			write8(debugLine, DW_LNE_set_address);
			write64(debugLine, entry.address);
			currentAddr = entry.address;
			(void)currentAddr; // Reserved for future delta calculations

			// Set file if changed
			if (entry.fileIndex != currentFile) {
				write8(debugLine, DW_LNS_set_file);
				writeULEB128(debugLine, entry.fileIndex);
				currentFile = entry.fileIndex;
			}

			// Advance line
			int32_t lineDelta = static_cast<int32_t>(entry.line) -
								static_cast<int32_t>(currentLine);
			if (lineDelta != 0) {
				write8(debugLine, DW_LNS_advance_line);
				writeSLEB128(debugLine, lineDelta);
			}
			currentLine = entry.line;

			// Set column
			if (entry.column > 0) {
				write8(debugLine, DW_LNS_set_column);
				writeULEB128(debugLine, entry.column);
			}

			// Prologue end / epilogue begin
			if (entry.prologueEnd) {
				write8(debugLine, DW_LNS_set_prologue_end);
			}
			if (entry.epilogueBegin) {
				write8(debugLine, DW_LNS_set_epilogue_begin);
			}

			// Copy row to matrix
			write8(debugLine, DW_LNS_copy);
		}

		// End sequence
		write8(debugLine, 0);
		writeULEB128(debugLine, 1);
		write8(debugLine, DW_LNE_end_sequence);
	}

	// Update unit length
	uint32_t unitLength = static_cast<uint32_t>(debugLine.size() - headerStart - 4);
	debugLine[headerStart] = unitLength & 0xff;
	debugLine[headerStart + 1] = (unitLength >> 8) & 0xff;
	debugLine[headerStart + 2] = (unitLength >> 16) & 0xff;
	debugLine[headerStart + 3] = (unitLength >> 24) & 0xff;
}

//==============================================================================
// Address Ranges Generation
//==============================================================================

void DwarfGenerator::generateAranges() {
	// .debug_aranges header
	size_t headerStart = debugAranges.size();
	write32(debugAranges, 0); // unit_length (placeholder)
	write16(debugAranges, 2); // DWARF version
	write32(debugAranges, 0); // debug_info_offset
	write8(debugAranges, 8);  // address_size
	write8(debugAranges, 0);  // segment_selector_size

	// Padding to align to 2*address_size
	while ((debugAranges.size() - headerStart) % 16 != 0) {
		write8(debugAranges, 0);
	}

	// Address range descriptors
	for (const auto &func : compileUnit.functions) {
		write64(debugAranges, func.lowPC);
		write64(debugAranges, func.highPC - func.lowPC);
	}

	// Terminating entry
	write64(debugAranges, 0);
	write64(debugAranges, 0);

	// Update unit length
	uint32_t unitLength = static_cast<uint32_t>(debugAranges.size() - headerStart - 4);
	debugAranges[headerStart] = unitLength & 0xff;
	debugAranges[headerStart + 1] = (unitLength >> 8) & 0xff;
	debugAranges[headerStart + 2] = (unitLength >> 16) & 0xff;
	debugAranges[headerStart + 3] = (unitLength >> 24) & 0xff;
}

} // namespace backend
