#pragma once

#include <cstdint>
#include <string>
#include <unordered_map>
#include <vector>

namespace backend {

//==============================================================================
// COFF Library Reader
//
// Parses Windows static library (.lib) files to extract object code for
// external functions. Static libraries are archives containing COFF object
// files.
//==============================================================================

// COFF relocation types (AMD64)
constexpr uint16_t IMAGE_REL_AMD64_ADDR64 = 0x0001;
constexpr uint16_t IMAGE_REL_AMD64_ADDR32 = 0x0002;
constexpr uint16_t IMAGE_REL_AMD64_ADDR32NB = 0x0003;
constexpr uint16_t IMAGE_REL_AMD64_REL32 = 0x0004;
constexpr uint16_t IMAGE_REL_AMD64_REL32_1 = 0x0005;
constexpr uint16_t IMAGE_REL_AMD64_REL32_2 = 0x0006;
constexpr uint16_t IMAGE_REL_AMD64_REL32_3 = 0x0007;
constexpr uint16_t IMAGE_REL_AMD64_REL32_4 = 0x0008;
constexpr uint16_t IMAGE_REL_AMD64_REL32_5 = 0x0009;

// Symbol storage class
constexpr uint8_t IMAGE_SYM_CLASS_EXTERNAL = 2;
constexpr uint8_t IMAGE_SYM_CLASS_STATIC = 3;
constexpr uint8_t IMAGE_SYM_CLASS_FUNCTION = 101;

#pragma pack(push, 1)

// Archive member header (used in .lib files)
struct ArchiveMemberHeader {
	char name[16];
	char date[12];
	char userId[6];
	char groupId[6];
	char mode[8];
	char size[10];
	char endHeader[2]; // "`\n"
};

// COFF file header
struct CoffFileHeader {
	uint16_t machine;
	uint16_t numberOfSections;
	uint32_t timeDateStamp;
	uint32_t pointerToSymbolTable;
	uint32_t numberOfSymbols;
	uint16_t sizeOfOptionalHeader;
	uint16_t characteristics;
};

// COFF section header
struct CoffSectionHeader {
	char name[8];
	uint32_t virtualSize;
	uint32_t virtualAddress;
	uint32_t sizeOfRawData;
	uint32_t pointerToRawData;
	uint32_t pointerToRelocations;
	uint32_t pointerToLinenumbers;
	uint16_t numberOfRelocations;
	uint16_t numberOfLinenumbers;
	uint32_t characteristics;
};

// COFF relocation entry
struct CoffRelocation {
	uint32_t virtualAddress;
	uint32_t symbolTableIndex;
	uint16_t type;
};

// COFF symbol table entry
struct CoffSymbol {
	union {
		char shortName[8];
		struct {
			uint32_t zeros;
			uint32_t offset;
		} longName;
	} name;
	uint32_t value;
	int16_t sectionNumber;
	uint16_t type;
	uint8_t storageClass;
	uint8_t numberOfAuxSymbols;
};

#pragma pack(pop)

// Relocation info for a symbol from a static library
struct LibRelocation {
	uint32_t offset;		// Offset within the function's code
	std::string symbolName; // Symbol being referenced
	uint16_t type;			// COFF relocation type
};

// Extracted function from a static library
struct LibFunction {
	std::string name;
	std::vector<uint8_t> code;
	std::vector<LibRelocation> relocations;
	bool found = false;
};

class CoffLibReader {
  public:
	CoffLibReader();

	// Load a .lib file
	bool load(const std::string &path);

	// Check if a symbol exists in the library
	bool hasSymbol(const std::string &name) const;

	// Extract a function's code and relocations
	LibFunction extractFunction(const std::string &name);

	// Get last error message
	const std::string &getError() const { return errorMsg; }

  private:
	std::vector<uint8_t> libData;
	std::string errorMsg;

	// Symbol table: symbol name -> offset of archive member containing it
	std::unordered_map<std::string, size_t> symbolOffsets;

	// Parse the archive's first linker member (symbol directory)
	bool parseFirstLinkerMember(const uint8_t *data, size_t size);

	// Parse a COFF object file at the given offset
	LibFunction parseCoffObject(size_t memberOffset, const std::string &symbolName);

	// Read a string from COFF string table
	std::string readStringTableEntry(const uint8_t *stringTable, uint32_t offset);

	// Parse archive member size
	size_t parseMemberSize(const ArchiveMemberHeader *header);
};

} // namespace backend
