#include "coff_lib_reader.h"
#include "pe_writer.h" // For IMAGE_FILE_MACHINE_AMD64, IMAGE_SCN_CNT_CODE
#include <cstring>
#include <fstream>
#include <iostream>

namespace backend {

CoffLibReader::CoffLibReader() {}

bool CoffLibReader::load(const std::string &path) {
	std::ifstream file(path, std::ios::binary | std::ios::ate);
	if (!file) {
		errorMsg = "Failed to open file: " + path;
		return false;
	}

	size_t fileSize = static_cast<size_t>(file.tellg());
	file.seekg(0);

	libData.resize(fileSize);
	file.read(reinterpret_cast<char *>(libData.data()), fileSize);

	if (!file) {
		errorMsg = "Failed to read file: " + path;
		return false;
	}

	// Check archive signature "!<arch>\n"
	if (fileSize < 8 || std::memcmp(libData.data(), "!<arch>\n", 8) != 0) {
		errorMsg = "Not a valid archive file (missing !<arch> signature)";
		return false;
	}

	// Parse the first linker member to build symbol directory
	size_t offset = 8; // After signature
	if (offset + sizeof(ArchiveMemberHeader) > fileSize) {
		errorMsg = "Archive too small for member header";
		return false;
	}

	auto *firstHeader = reinterpret_cast<const ArchiveMemberHeader *>(libData.data() + offset);
	size_t memberSize = parseMemberSize(firstHeader);
	size_t dataOffset = offset + sizeof(ArchiveMemberHeader);

	// First linker member name is "/"
	if (firstHeader->name[0] == '/' && firstHeader->name[1] == ' ') {
		if (!parseFirstLinkerMember(libData.data() + dataOffset, memberSize)) {
			return false;
		}
	} else {
		errorMsg = "First archive member is not the linker member";
		return false;
	}

	return true;
}

size_t CoffLibReader::parseMemberSize(const ArchiveMemberHeader *header) {
	char sizeStr[11] = {0};
	std::memcpy(sizeStr, header->size, 10);
	return static_cast<size_t>(std::strtoul(sizeStr, nullptr, 10));
}

bool CoffLibReader::parseFirstLinkerMember(const uint8_t *data, size_t size) {
	if (size < 4) {
		errorMsg = "First linker member too small";
		return false;
	}

	// First linker member format:
	// - 4 bytes: number of symbols (big-endian)
	// - N * 4 bytes: offsets to archive members (big-endian)
	// - N null-terminated strings: symbol names

	uint32_t numSymbols = (static_cast<uint32_t>(data[0]) << 24) |
						  (static_cast<uint32_t>(data[1]) << 16) |
						  (static_cast<uint32_t>(data[2]) << 8) |
						  static_cast<uint32_t>(data[3]);

	if (size < 4 + numSymbols * 4) {
		errorMsg = "First linker member truncated";
		return false;
	}

	// Read offsets
	std::vector<uint32_t> offsets(numSymbols);
	const uint8_t *offsetPtr = data + 4;
	for (uint32_t i = 0; i < numSymbols; i++) {
		offsets[i] = (static_cast<uint32_t>(offsetPtr[0]) << 24) |
					 (static_cast<uint32_t>(offsetPtr[1]) << 16) |
					 (static_cast<uint32_t>(offsetPtr[2]) << 8) |
					 static_cast<uint32_t>(offsetPtr[3]);
		offsetPtr += 4;
	}

	// Read symbol names
	const char *namePtr = reinterpret_cast<const char *>(data + 4 + numSymbols * 4);
	const char *endPtr = reinterpret_cast<const char *>(data + size);

	for (uint32_t i = 0; i < numSymbols && namePtr < endPtr; i++) {
		std::string name(namePtr);
		symbolOffsets[name] = offsets[i];
		namePtr += name.length() + 1;
	}

	return true;
}

bool CoffLibReader::hasSymbol(const std::string &name) const {
	return symbolOffsets.find(name) != symbolOffsets.end();
}

LibFunction CoffLibReader::extractFunction(const std::string &name) {
	LibFunction result;
	result.name = name;

	auto it = symbolOffsets.find(name);
	if (it == symbolOffsets.end()) {
		errorMsg = "Symbol not found: " + name;
		return result;
	}

	return parseCoffObject(it->second, name);
}

std::string CoffLibReader::readStringTableEntry(const uint8_t *stringTable, uint32_t offset) {
	const char *str = reinterpret_cast<const char *>(stringTable + offset);
	return std::string(str);
}

LibFunction CoffLibReader::parseCoffObject(size_t memberOffset, const std::string &symbolName) {
	LibFunction result;
	result.name = symbolName;

	if (memberOffset + sizeof(ArchiveMemberHeader) > libData.size()) {
		errorMsg = "Member offset out of bounds";
		return result;
	}

	auto *header = reinterpret_cast<const ArchiveMemberHeader *>(libData.data() + memberOffset);
	[[maybe_unused]] size_t memberSize = parseMemberSize(header);
	size_t coffOffset = memberOffset + sizeof(ArchiveMemberHeader);

	if (coffOffset + sizeof(CoffFileHeader) > libData.size()) {
		errorMsg = "COFF header out of bounds";
		return result;
	}

	const uint8_t *coffData = libData.data() + coffOffset;
	auto *coffHeader = reinterpret_cast<const CoffFileHeader *>(coffData);

	// Verify machine type
	if (coffHeader->machine != IMAGE_FILE_MACHINE_AMD64) {
		errorMsg = "Unsupported machine type (not AMD64)";
		return result;
	}

	// Get string table (after symbol table)
	const uint8_t *stringTable = nullptr;
	if (coffHeader->pointerToSymbolTable > 0 && coffHeader->numberOfSymbols > 0) {
		stringTable = coffData + coffHeader->pointerToSymbolTable +
					  coffHeader->numberOfSymbols * sizeof(CoffSymbol);
	}

	// Parse section headers
	const CoffSectionHeader *sections = reinterpret_cast<const CoffSectionHeader *>(
		coffData + sizeof(CoffFileHeader) + coffHeader->sizeOfOptionalHeader);

	// Parse symbol table to find the target symbol
	const CoffSymbol *symbols = reinterpret_cast<const CoffSymbol *>(
		coffData + coffHeader->pointerToSymbolTable);

	int16_t targetSection = -1;
	[[maybe_unused]] uint32_t targetValue = 0;

	for (uint32_t i = 0; i < coffHeader->numberOfSymbols; i++) {
		const CoffSymbol &sym = symbols[i];

		// Get symbol name
		std::string symName;
		if (sym.name.longName.zeros == 0) {
			// Long name - offset into string table
			symName = readStringTableEntry(stringTable, sym.name.longName.offset);
		} else {
			// Short name (up to 8 chars)
			char shortName[9] = {0};
			std::memcpy(shortName, sym.name.shortName, 8);
			symName = shortName;
		}

		if (symName == symbolName) {
			targetSection = sym.sectionNumber;
			targetValue = sym.value;
			break;
		}

		// Skip auxiliary symbols
		i += sym.numberOfAuxSymbols;
	}

	if (targetSection <= 0) {
		errorMsg = "Symbol not found in object file: " + symbolName;
		return result;
	}

	// Get the section containing the symbol
	const CoffSectionHeader &section = sections[targetSection - 1];

	// Check if it's a code section
	if (!(section.characteristics & IMAGE_SCN_CNT_CODE)) {
		errorMsg = "Symbol is not in a code section";
		return result;
	}

	// Extract the code
	// For now, extract the entire section (proper handling would determine function size)
	const uint8_t *sectionData = coffData + section.pointerToRawData;
	result.code.assign(sectionData, sectionData + section.sizeOfRawData);

	// Extract relocations for this section
	if (section.numberOfRelocations > 0) {
		const CoffRelocation *relocs = reinterpret_cast<const CoffRelocation *>(
			coffData + section.pointerToRelocations);

		for (uint16_t i = 0; i < section.numberOfRelocations; i++) {
			const CoffRelocation &reloc = relocs[i];

			// Get the symbol name for this relocation
			const CoffSymbol &relocSym = symbols[reloc.symbolTableIndex];
			std::string relocSymName;
			if (relocSym.name.longName.zeros == 0) {
				relocSymName = readStringTableEntry(stringTable, relocSym.name.longName.offset);
			} else {
				char shortName[9] = {0};
				std::memcpy(shortName, relocSym.name.shortName, 8);
				relocSymName = shortName;
			}

			LibRelocation lr;
			lr.offset = reloc.virtualAddress;
			lr.symbolName = relocSymName;
			lr.type = reloc.type;
			result.relocations.push_back(lr);
		}
	}

	result.found = true;
	return result;
}

} // namespace backend
