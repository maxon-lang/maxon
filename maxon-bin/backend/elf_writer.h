#pragma once

#include <cstdint>
#include <string>
#include <unordered_map>
#include <vector>

namespace backend {

//==============================================================================
// ELF Header Constants
//==============================================================================

// ELF identification
constexpr uint8_t ELFMAG0 = 0x7f;
constexpr uint8_t ELFMAG1 = 'E';
constexpr uint8_t ELFMAG2 = 'L';
constexpr uint8_t ELFMAG3 = 'F';

// ELF class
constexpr uint8_t ELFCLASSNONE = 0;
constexpr uint8_t ELFCLASS32 = 1;
constexpr uint8_t ELFCLASS64 = 2;

// ELF data encoding
constexpr uint8_t ELFDATANONE = 0;
constexpr uint8_t ELFDATA2LSB = 1; // Little endian
constexpr uint8_t ELFDATA2MSB = 2; // Big endian

// ELF version
constexpr uint8_t EV_CURRENT = 1;

// ELF OS/ABI
constexpr uint8_t ELFOSABI_NONE = 0;
constexpr uint8_t ELFOSABI_LINUX = 3;

// ELF type
constexpr uint16_t ET_NONE = 0;
constexpr uint16_t ET_REL = 1;	// Relocatable
constexpr uint16_t ET_EXEC = 2; // Executable
constexpr uint16_t ET_DYN = 3;	// Shared object
constexpr uint16_t ET_CORE = 4; // Core file

// Machine type
constexpr uint16_t EM_X86_64 = 62;

// Section types
constexpr uint32_t SHT_NULL = 0;
constexpr uint32_t SHT_PROGBITS = 1;
constexpr uint32_t SHT_SYMTAB = 2;
constexpr uint32_t SHT_STRTAB = 3;
constexpr uint32_t SHT_RELA = 4;
constexpr uint32_t SHT_HASH = 5;
constexpr uint32_t SHT_DYNAMIC = 6;
constexpr uint32_t SHT_NOTE = 7;
constexpr uint32_t SHT_NOBITS = 8;
constexpr uint32_t SHT_REL = 9;
constexpr uint32_t SHT_DYNSYM = 11;

// Section flags
constexpr uint64_t SHF_WRITE = 0x1;
constexpr uint64_t SHF_ALLOC = 0x2;
constexpr uint64_t SHF_EXECINSTR = 0x4;
constexpr uint64_t SHF_MERGE = 0x10;
constexpr uint64_t SHF_STRINGS = 0x20;

// Program header types
constexpr uint32_t PT_NULL = 0;
constexpr uint32_t PT_LOAD = 1;
constexpr uint32_t PT_DYNAMIC = 2;
constexpr uint32_t PT_INTERP = 3;
constexpr uint32_t PT_NOTE = 4;
constexpr uint32_t PT_PHDR = 6;
constexpr uint32_t PT_GNU_STACK = 0x6474e551;

// Program header flags
constexpr uint32_t PF_X = 0x1; // Execute
constexpr uint32_t PF_W = 0x2; // Write
constexpr uint32_t PF_R = 0x4; // Read

// Symbol binding
constexpr uint8_t STB_LOCAL = 0;
constexpr uint8_t STB_GLOBAL = 1;
constexpr uint8_t STB_WEAK = 2;

// Symbol types
constexpr uint8_t STT_NOTYPE = 0;
constexpr uint8_t STT_OBJECT = 1;
constexpr uint8_t STT_FUNC = 2;
constexpr uint8_t STT_SECTION = 3;
constexpr uint8_t STT_FILE = 4;

// Symbol visibility
constexpr uint8_t STV_DEFAULT = 0;
constexpr uint8_t STV_INTERNAL = 1;
constexpr uint8_t STV_HIDDEN = 2;
constexpr uint8_t STV_PROTECTED = 3;

// Special section indices
constexpr uint16_t SHN_UNDEF = 0;
constexpr uint16_t SHN_ABS = 0xfff1;
constexpr uint16_t SHN_COMMON = 0xfff2;

// Relocation types (x86-64)
constexpr uint32_t R_X86_64_NONE = 0;
constexpr uint32_t R_X86_64_64 = 1;
constexpr uint32_t R_X86_64_PC32 = 2;
constexpr uint32_t R_X86_64_GOT32 = 3;
constexpr uint32_t R_X86_64_PLT32 = 4;
constexpr uint32_t R_X86_64_32 = 10;
constexpr uint32_t R_X86_64_32S = 11;

//==============================================================================
// ELF Structures
//==============================================================================

#pragma pack(push, 1)

struct Elf64_Ehdr {
	uint8_t e_ident[16];  // ELF identification
	uint16_t e_type;	  // Object file type
	uint16_t e_machine;	  // Machine type
	uint32_t e_version;	  // Object file version
	uint64_t e_entry;	  // Entry point address
	uint64_t e_phoff;	  // Program header offset
	uint64_t e_shoff;	  // Section header offset
	uint32_t e_flags;	  // Processor-specific flags
	uint16_t e_ehsize;	  // ELF header size
	uint16_t e_phentsize; // Size of program header entry
	uint16_t e_phnum;	  // Number of program header entries
	uint16_t e_shentsize; // Size of section header entry
	uint16_t e_shnum;	  // Number of section header entries
	uint16_t e_shstrndx;  // Section name string table index
};

struct Elf64_Phdr {
	uint32_t p_type;   // Type of segment
	uint32_t p_flags;  // Segment flags
	uint64_t p_offset; // Offset in file
	uint64_t p_vaddr;  // Virtual address in memory
	uint64_t p_paddr;  // Physical address (reserved)
	uint64_t p_filesz; // Size of segment in file
	uint64_t p_memsz;  // Size of segment in memory
	uint64_t p_align;  // Alignment of segment
};

struct Elf64_Shdr {
	uint32_t sh_name;	   // Section name (string table index)
	uint32_t sh_type;	   // Section type
	uint64_t sh_flags;	   // Section flags
	uint64_t sh_addr;	   // Virtual address in memory
	uint64_t sh_offset;	   // Offset in file
	uint64_t sh_size;	   // Size of section
	uint32_t sh_link;	   // Link to another section
	uint32_t sh_info;	   // Additional section information
	uint64_t sh_addralign; // Section alignment
	uint64_t sh_entsize;   // Entry size if section holds table
};

struct Elf64_Sym {
	uint32_t st_name;  // Symbol name (string table index)
	uint8_t st_info;   // Symbol type and binding
	uint8_t st_other;  // Symbol visibility
	uint16_t st_shndx; // Section index
	uint64_t st_value; // Symbol value
	uint64_t st_size;  // Symbol size
};

struct Elf64_Rela {
	uint64_t r_offset; // Location to apply relocation
	uint64_t r_info;   // Relocation type and symbol index
	int64_t r_addend;  // Addend
};

#pragma pack(pop)

// Helper macros for symbol info
inline uint8_t ELF64_ST_INFO(uint8_t bind, uint8_t type) {
	return (bind << 4) | (type & 0xf);
}

inline uint8_t ELF64_ST_BIND(uint8_t info) {
	return info >> 4;
}

inline uint8_t ELF64_ST_TYPE(uint8_t info) {
	return info & 0xf;
}

// Helper macros for relocation info
inline uint64_t ELF64_R_INFO(uint32_t sym, uint32_t type) {
	return ((uint64_t)sym << 32) | type;
}

inline uint32_t ELF64_R_SYM(uint64_t info) {
	return (uint32_t)(info >> 32);
}

inline uint32_t ELF64_R_TYPE(uint64_t info) {
	return (uint32_t)(info & 0xffffffff);
}

//==============================================================================
// High-Level Structures
//==============================================================================

struct ElfSymbol {
	std::string name;
	uint64_t value; // Address/offset
	uint64_t size;
	uint8_t binding;	   // STB_LOCAL, STB_GLOBAL, etc.
	uint8_t type;		   // STT_FUNC, STT_OBJECT, etc.
	uint16_t sectionIndex; // Section this symbol belongs to
};

struct ElfRelocation {
	uint64_t offset;		// Where to apply relocation
	uint32_t type;			// Relocation type
	std::string symbolName; // Symbol being referenced
	int64_t addend;
};

struct ElfSection {
	std::string name;
	uint32_t type;
	uint64_t flags;
	uint64_t addr; // Virtual address
	std::vector<uint8_t> data;
	uint64_t alignment;
	uint32_t link;
	uint32_t info;
	uint64_t entsize;

	// For sections with relocations
	std::vector<ElfRelocation> relocations;
};

//==============================================================================
// ELF Writer
//==============================================================================

class ElfWriter {
  public:
	ElfWriter();

	// Set the entry point address
	void setEntryPoint(uint64_t addr);

	// Add a section
	uint32_t addSection(const std::string &name, uint32_t type, uint64_t flags,
						const std::vector<uint8_t> &data, uint64_t alignment = 1);

	// Add code section (.text)
	uint32_t addTextSection(const std::vector<uint8_t> &code);

	// Add data section (.data)
	uint32_t addDataSection(const std::vector<uint8_t> &data);

	// Add read-only data section (.rodata)
	uint32_t addRodataSection(const std::vector<uint8_t> &data);

	// Add BSS section (.bss) - uninitialized data
	uint32_t addBssSection(uint64_t size);

	// Add a symbol
	void addSymbol(const std::string &name, uint64_t value, uint64_t size,
				   uint8_t binding, uint8_t type, uint32_t sectionIndex);

	// Add a global function symbol
	void addFunction(const std::string &name, uint64_t offset, uint64_t size);

	// Add a global variable symbol
	void addVariable(const std::string &name, uint64_t offset, uint64_t size);

	// Add a relocation
	void addRelocation(uint32_t sectionIndex, uint64_t offset,
					   uint32_t type, const std::string &symbolName, int64_t addend = 0);

	// Write the ELF file
	bool write(const std::string &filename);

	// Get section index by name
	int32_t getSectionIndex(const std::string &name) const;

  private:
	std::vector<ElfSection> sections;
	std::vector<ElfSymbol> symbols;
	std::unordered_map<std::string, uint32_t> sectionNameMap;
	std::unordered_map<std::string, uint32_t> symbolNameMap;
	uint64_t entryPoint;

	// Base virtual address for the executable
	static constexpr uint64_t BASE_ADDR = 0x400000;

	// Build string table from section/symbol names
	std::vector<uint8_t> buildStringTable(const std::vector<std::string> &strings,
										  std::unordered_map<std::string, uint32_t> &offsets);

	// Calculate file layout
	void calculateLayout(uint64_t &currentOffset, uint64_t &currentVaddr);

	// Resolve relocations
	void resolveRelocations(std::vector<uint8_t> &output);

	// Write program headers
	void writePhdr(std::vector<uint8_t> &output, const Elf64_Phdr &phdr);

	// Write section headers
	void writeShdr(std::vector<uint8_t> &output, const Elf64_Shdr &shdr);

	// Align a value
	static uint64_t align(uint64_t value, uint64_t alignment) {
		return (value + alignment - 1) & ~(alignment - 1);
	}
};

} // namespace backend
