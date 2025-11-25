#pragma once

#include <cstdint>
#include <string>
#include <unordered_map>
#include <vector>

namespace backend {

//==============================================================================
// PE Header Constants
//==============================================================================

// DOS header signature
constexpr uint16_t DOS_MAGIC = 0x5A4D; // "MZ"

// PE signature
constexpr uint32_t PE_SIGNATURE = 0x00004550; // "PE\0\0"

// Machine types
constexpr uint16_t IMAGE_FILE_MACHINE_AMD64 = 0x8664;

// Characteristics
constexpr uint16_t IMAGE_FILE_EXECUTABLE_IMAGE = 0x0002;
constexpr uint16_t IMAGE_FILE_LARGE_ADDRESS_AWARE = 0x0020;
constexpr uint16_t IMAGE_FILE_DEBUG_STRIPPED = 0x0200;

// Optional header magic
constexpr uint16_t PE32_MAGIC = 0x10b;
constexpr uint16_t PE32PLUS_MAGIC = 0x20b;

// Subsystem types
constexpr uint16_t IMAGE_SUBSYSTEM_UNKNOWN = 0;
constexpr uint16_t IMAGE_SUBSYSTEM_NATIVE = 1;
constexpr uint16_t IMAGE_SUBSYSTEM_WINDOWS_GUI = 2;
constexpr uint16_t IMAGE_SUBSYSTEM_WINDOWS_CUI = 3; // Console

// DLL characteristics
constexpr uint16_t IMAGE_DLLCHARACTERISTICS_HIGH_ENTROPY_VA = 0x0020;
constexpr uint16_t IMAGE_DLLCHARACTERISTICS_DYNAMIC_BASE = 0x0040;
constexpr uint16_t IMAGE_DLLCHARACTERISTICS_NX_COMPAT = 0x0100;
constexpr uint16_t IMAGE_DLLCHARACTERISTICS_NO_SEH = 0x0400;
constexpr uint16_t IMAGE_DLLCHARACTERISTICS_TERMINAL_SERVER_AWARE = 0x8000;

// Section characteristics
constexpr uint32_t IMAGE_SCN_CNT_CODE = 0x00000020;
constexpr uint32_t IMAGE_SCN_CNT_INITIALIZED_DATA = 0x00000040;
constexpr uint32_t IMAGE_SCN_CNT_UNINITIALIZED_DATA = 0x00000080;
constexpr uint32_t IMAGE_SCN_MEM_EXECUTE = 0x20000000;
constexpr uint32_t IMAGE_SCN_MEM_READ = 0x40000000;
constexpr uint32_t IMAGE_SCN_MEM_WRITE = 0x80000000;

// Data directory indices
constexpr uint32_t IMAGE_DIRECTORY_ENTRY_EXPORT = 0;
constexpr uint32_t IMAGE_DIRECTORY_ENTRY_IMPORT = 1;
constexpr uint32_t IMAGE_DIRECTORY_ENTRY_RESOURCE = 2;
constexpr uint32_t IMAGE_DIRECTORY_ENTRY_EXCEPTION = 3;
constexpr uint32_t IMAGE_DIRECTORY_ENTRY_SECURITY = 4;
constexpr uint32_t IMAGE_DIRECTORY_ENTRY_BASERELOC = 5;
constexpr uint32_t IMAGE_DIRECTORY_ENTRY_DEBUG = 6;
constexpr uint32_t IMAGE_DIRECTORY_ENTRY_ARCHITECTURE = 7;
constexpr uint32_t IMAGE_DIRECTORY_ENTRY_GLOBALPTR = 8;
constexpr uint32_t IMAGE_DIRECTORY_ENTRY_TLS = 9;
constexpr uint32_t IMAGE_DIRECTORY_ENTRY_LOAD_CONFIG = 10;
constexpr uint32_t IMAGE_DIRECTORY_ENTRY_BOUND_IMPORT = 11;
constexpr uint32_t IMAGE_DIRECTORY_ENTRY_IAT = 12;
constexpr uint32_t IMAGE_DIRECTORY_ENTRY_DELAY_IMPORT = 13;
constexpr uint32_t IMAGE_DIRECTORY_ENTRY_COM_DESCRIPTOR = 14;
constexpr uint32_t IMAGE_NUMBEROF_DIRECTORY_ENTRIES = 16;

// Relocation types
constexpr uint16_t IMAGE_REL_BASED_ABSOLUTE = 0;
constexpr uint16_t IMAGE_REL_BASED_HIGH = 1;
constexpr uint16_t IMAGE_REL_BASED_LOW = 2;
constexpr uint16_t IMAGE_REL_BASED_HIGHLOW = 3;
constexpr uint16_t IMAGE_REL_BASED_DIR64 = 10;

// Additional section characteristics
constexpr uint32_t IMAGE_SCN_MEM_DISCARDABLE = 0x02000000;

//==============================================================================
// PE Structures
//==============================================================================

#pragma pack(push, 1)

struct DosHeader {
	uint16_t e_magic;	 // Magic number (MZ)
	uint16_t e_cblp;	 // Bytes on last page
	uint16_t e_cp;		 // Pages in file
	uint16_t e_crlc;	 // Relocations
	uint16_t e_cparhdr;	 // Size of header in paragraphs
	uint16_t e_minalloc; // Minimum extra paragraphs
	uint16_t e_maxalloc; // Maximum extra paragraphs
	uint16_t e_ss;		 // Initial SS value
	uint16_t e_sp;		 // Initial SP value
	uint16_t e_csum;	 // Checksum
	uint16_t e_ip;		 // Initial IP value
	uint16_t e_cs;		 // Initial CS value
	uint16_t e_lfarlc;	 // File address of relocation table
	uint16_t e_ovno;	 // Overlay number
	uint16_t e_res[4];	 // Reserved
	uint16_t e_oemid;	 // OEM identifier
	uint16_t e_oeminfo;	 // OEM information
	uint16_t e_res2[10]; // Reserved
	uint32_t e_lfanew;	 // File address of PE header
};

struct CoffHeader {
	uint32_t Signature;
	uint16_t Machine;
	uint16_t NumberOfSections;
	uint32_t TimeDateStamp;
	uint32_t PointerToSymbolTable;
	uint32_t NumberOfSymbols;
	uint16_t SizeOfOptionalHeader;
	uint16_t Characteristics;
};

struct DataDirectory {
	uint32_t VirtualAddress;
	uint32_t Size;
};

struct OptionalHeader64 {
	uint16_t Magic;
	uint8_t MajorLinkerVersion;
	uint8_t MinorLinkerVersion;
	uint32_t SizeOfCode;
	uint32_t SizeOfInitializedData;
	uint32_t SizeOfUninitializedData;
	uint32_t AddressOfEntryPoint;
	uint32_t BaseOfCode;
	uint64_t ImageBase;
	uint32_t SectionAlignment;
	uint32_t FileAlignment;
	uint16_t MajorOperatingSystemVersion;
	uint16_t MinorOperatingSystemVersion;
	uint16_t MajorImageVersion;
	uint16_t MinorImageVersion;
	uint16_t MajorSubsystemVersion;
	uint16_t MinorSubsystemVersion;
	uint32_t Win32VersionValue;
	uint32_t SizeOfImage;
	uint32_t SizeOfHeaders;
	uint32_t CheckSum;
	uint16_t Subsystem;
	uint16_t DllCharacteristics;
	uint64_t SizeOfStackReserve;
	uint64_t SizeOfStackCommit;
	uint64_t SizeOfHeapReserve;
	uint64_t SizeOfHeapCommit;
	uint32_t LoaderFlags;
	uint32_t NumberOfRvaAndSizes;
	DataDirectory DataDirectory[IMAGE_NUMBEROF_DIRECTORY_ENTRIES];
};

struct SectionHeader {
	char Name[8];
	uint32_t VirtualSize;
	uint32_t VirtualAddress;
	uint32_t SizeOfRawData;
	uint32_t PointerToRawData;
	uint32_t PointerToRelocations;
	uint32_t PointerToLinenumbers;
	uint16_t NumberOfRelocations;
	uint16_t NumberOfLinenumbers;
	uint32_t Characteristics;
};

struct ImportDescriptor {
	uint32_t OriginalFirstThunk; // RVA to INT (Import Name Table)
	uint32_t TimeDateStamp;
	uint32_t ForwarderChain;
	uint32_t Name;		 // RVA to DLL name
	uint32_t FirstThunk; // RVA to IAT (Import Address Table)
};

struct BaseRelocationBlock {
	uint32_t VirtualAddress;
	uint32_t SizeOfBlock;
	// Followed by type/offset entries
};

#pragma pack(pop)

//==============================================================================
// High-Level Structures
//==============================================================================

struct PeImport {
	std::string dllName;
	std::vector<std::string> functions;
};

struct PeRelocation {
	uint32_t rva;  // RVA of the location to relocate
	uint16_t type; // Relocation type
};

struct PeSection {
	std::string name;
	uint32_t characteristics;
	std::vector<uint8_t> data;
	uint32_t virtualSize;	 // Size in memory (can be larger than data for BSS)
	uint32_t virtualAddress; // Set during layout
	uint32_t rawDataOffset;	 // Set during layout

	std::vector<PeRelocation> relocations;
};

struct PeSymbol {
	std::string name;
	uint32_t rva;
	uint32_t size;
	bool isFunction;
	int32_t sectionIndex;
};

//==============================================================================
// PE Writer
//==============================================================================

class PeWriter {
  public:
	PeWriter();

	// Set subsystem (console or GUI)
	void setSubsystem(uint16_t subsystem);

	// Set image base address
	void setImageBase(uint64_t base);

	// Set entry point RVA
	void setEntryPoint(uint32_t rva);

	// Add a section
	uint32_t addSection(const std::string &name, uint32_t characteristics,
						const std::vector<uint8_t> &data, uint32_t virtualSize = 0);

	// Add code section (.text)
	uint32_t addTextSection(const std::vector<uint8_t> &code);

	// Add data section (.data)
	uint32_t addDataSection(const std::vector<uint8_t> &data);

	// Add read-only data section (.rdata)
	uint32_t addRdataSection(const std::vector<uint8_t> &data);

	// Add BSS section (.bss)
	uint32_t addBssSection(uint32_t size);

	// Add an import
	void addImport(const std::string &dllName, const std::string &functionName);

	// Add a symbol (for debugging/exports)
	void addSymbol(const std::string &name, uint32_t rva, uint32_t size,
				   bool isFunction, int32_t sectionIndex);

	// Add a base relocation
	void addRelocation(uint32_t rva, uint16_t type = IMAGE_REL_BASED_DIR64);

	// Add an import call relocation (for patching indirect calls through IAT)
	void addImportRelocation(uint32_t codeOffset, const std::string &dllName,
							 const std::string &funcName);

	// Get RVA for an imported function (after layout)
	uint32_t getImportRva(const std::string &dllName, const std::string &funcName) const;

	// Write the PE file
	bool write(const std::string &filename);

	// Get section index by name
	int32_t getSectionIndex(const std::string &name) const;

  private:
	std::vector<PeSection> sections;
	std::vector<PeImport> imports;
	std::vector<PeSymbol> symbols;
	std::vector<PeRelocation> baseRelocations;
	std::unordered_map<std::string, uint32_t> sectionNameMap;

	// Import function RVA lookup (populated after buildImportDirectory)
	mutable std::unordered_map<std::string, uint32_t> importRvaMap;

	// Import call relocations (code offset -> import info)
	struct ImportCallReloc {
		uint32_t codeOffset; // Offset in .text section
		std::string dllName;
		std::string funcName;
	};
	std::vector<ImportCallReloc> importCallRelocs;

	uint16_t subsystem;
	uint64_t imageBase;
	uint32_t entryPointRva;

	// Alignment values
	static constexpr uint32_t SECTION_ALIGNMENT = 0x1000; // 4KB
	static constexpr uint32_t FILE_ALIGNMENT = 0x200;	  // 512 bytes

	// Build import directory
	std::vector<uint8_t> buildImportDirectory(uint32_t baseRva);

	// Build base relocation directory
	std::vector<uint8_t> buildRelocationDirectory(uint32_t baseRva);

	// Calculate file layout
	void calculateLayout();

	// Align values
	static uint32_t alignTo(uint32_t value, uint32_t alignment) {
		return (value + alignment - 1) & ~(alignment - 1);
	}
};

} // namespace backend
