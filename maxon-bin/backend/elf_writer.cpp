#include "elf_writer.h"
#include <algorithm>
#include <cstring>
#include <fstream>
#include <stdexcept>

namespace backend {

ElfWriter::ElfWriter() : entryPoint(BASE_ADDR) {
	// Add null section (required as first section)
	ElfSection nullSection;
	nullSection.name = "";
	nullSection.type = SHT_NULL;
	nullSection.flags = 0;
	nullSection.addr = 0;
	nullSection.alignment = 0;
	nullSection.link = 0;
	nullSection.info = 0;
	nullSection.entsize = 0;
	sections.push_back(nullSection);

	// Add null symbol (required as first symbol)
	ElfSymbol nullSymbol;
	nullSymbol.name = "";
	nullSymbol.value = 0;
	nullSymbol.size = 0;
	nullSymbol.binding = STB_LOCAL;
	nullSymbol.type = STT_NOTYPE;
	nullSymbol.sectionIndex = SHN_UNDEF;
	symbols.push_back(nullSymbol);
}

void ElfWriter::setEntryPoint(uint64_t addr) {
	entryPoint = addr;
}

uint32_t ElfWriter::addSection(const std::string &name, uint32_t type, uint64_t flags,
							   const std::vector<uint8_t> &data, uint64_t alignment) {
	ElfSection section;
	section.name = name;
	section.type = type;
	section.flags = flags;
	section.addr = 0; // Will be set during layout
	section.data = data;
	section.alignment = alignment;
	section.link = 0;
	section.info = 0;
	section.entsize = 0;

	uint32_t index = static_cast<uint32_t>(sections.size());
	sectionNameMap[name] = index;
	sections.push_back(section);
	return index;
}

uint32_t ElfWriter::addTextSection(const std::vector<uint8_t> &code) {
	return addSection(".text", SHT_PROGBITS,
					  SHF_ALLOC | SHF_EXECINSTR, code, 16);
}

uint32_t ElfWriter::addDataSection(const std::vector<uint8_t> &data) {
	return addSection(".data", SHT_PROGBITS,
					  SHF_ALLOC | SHF_WRITE, data, 8);
}

uint32_t ElfWriter::addRodataSection(const std::vector<uint8_t> &data) {
	return addSection(".rodata", SHT_PROGBITS,
					  SHF_ALLOC, data, 8);
}

uint32_t ElfWriter::addBssSection(uint64_t size) {
	ElfSection section;
	section.name = ".bss";
	section.type = SHT_NOBITS;
	section.flags = SHF_ALLOC | SHF_WRITE;
	section.addr = 0;
	section.data.resize(0); // BSS has no data in file
	section.alignment = 8;
	section.link = 0;
	section.info = 0;
	section.entsize = 0;

	uint32_t index = static_cast<uint32_t>(sections.size());
	sectionNameMap[".bss"] = index;
	sections.push_back(section);

	// Store size in a different way - we'll handle this specially
	sections.back().addr = size; // Temporarily store size here

	return index;
}

void ElfWriter::addSymbol(const std::string &name, uint64_t value, uint64_t size,
						  uint8_t binding, uint8_t type, uint32_t sectionIndex) {
	ElfSymbol sym;
	sym.name = name;
	sym.value = value;
	sym.size = size;
	sym.binding = binding;
	sym.type = type;
	sym.sectionIndex = static_cast<uint16_t>(sectionIndex);

	if (!name.empty()) {
		symbolNameMap[name] = static_cast<uint32_t>(symbols.size());
	}
	symbols.push_back(sym);
}

void ElfWriter::addFunction(const std::string &name, uint64_t offset, uint64_t size) {
	int32_t textIdx = getSectionIndex(".text");
	if (textIdx < 0) {
		throw std::runtime_error("Cannot add function: .text section not found");
	}
	addSymbol(name, offset, size, STB_GLOBAL, STT_FUNC, textIdx);
}

void ElfWriter::addVariable(const std::string &name, uint64_t offset, uint64_t size) {
	int32_t dataIdx = getSectionIndex(".data");
	if (dataIdx < 0) {
		dataIdx = getSectionIndex(".rodata");
	}
	if (dataIdx < 0) {
		throw std::runtime_error("Cannot add variable: .data/.rodata section not found");
	}
	addSymbol(name, offset, size, STB_GLOBAL, STT_OBJECT, dataIdx);
}

void ElfWriter::addRelocation(uint32_t sectionIndex, uint64_t offset,
							  uint32_t type, const std::string &symbolName, int64_t addend) {
	if (sectionIndex >= sections.size()) {
		throw std::runtime_error("Invalid section index for relocation");
	}

	ElfRelocation rel;
	rel.offset = offset;
	rel.type = type;
	rel.symbolName = symbolName;
	rel.addend = addend;

	sections[sectionIndex].relocations.push_back(rel);
}

int32_t ElfWriter::getSectionIndex(const std::string &name) const {
	auto it = sectionNameMap.find(name);
	if (it == sectionNameMap.end()) {
		return -1;
	}
	return static_cast<int32_t>(it->second);
}

std::vector<uint8_t> ElfWriter::buildStringTable(
	const std::vector<std::string> &strings,
	std::unordered_map<std::string, uint32_t> &offsets) {

	std::vector<uint8_t> table;
	table.push_back(0); // First byte is null

	for (const auto &str : strings) {
		if (str.empty()) {
			offsets[str] = 0;
		} else {
			offsets[str] = static_cast<uint32_t>(table.size());
			table.insert(table.end(), str.begin(), str.end());
			table.push_back(0);
		}
	}

	return table;
}

bool ElfWriter::write(const std::string &filename) {
	std::vector<uint8_t> output;

	// Collect section names and symbol names for string tables
	std::vector<std::string> sectionNames;
	std::vector<std::string> symbolNames;

	for (const auto &sec : sections) {
		sectionNames.push_back(sec.name);
	}
	sectionNames.push_back(".shstrtab"); // Section header string table
	sectionNames.push_back(".strtab");	 // Symbol string table
	sectionNames.push_back(".symtab");	 // Symbol table

	for (const auto &sym : symbols) {
		symbolNames.push_back(sym.name);
	}

	// Build string tables
	std::unordered_map<std::string, uint32_t> shstrtabOffsets;
	std::vector<uint8_t> shstrtab = buildStringTable(sectionNames, shstrtabOffsets);

	std::unordered_map<std::string, uint32_t> strtabOffsets;
	std::vector<uint8_t> strtab = buildStringTable(symbolNames, strtabOffsets);

	// Calculate layout
	const uint64_t ehdrSize = sizeof(Elf64_Ehdr);
	const uint64_t phdrSize = sizeof(Elf64_Phdr);
	const uint64_t shdrSize = sizeof(Elf64_Shdr);

	// We'll have multiple program headers:
	// 1. PT_PHDR - for the program header table itself
	// 2. PT_LOAD - for code (.text)
	// 3. PT_LOAD - for data (.data, .rodata)
	// 4. PT_GNU_STACK - for stack permissions
	uint32_t numPhdrs = 4;

	// Headers come first
	uint64_t phdrOffset = ehdrSize;
	uint64_t headersEnd = ehdrSize + numPhdrs * phdrSize;

	// Page size for alignment
	const uint64_t PAGE_SIZE = 0x1000;

	// Section data starts after headers, aligned to page
	uint64_t currentOffset = align(headersEnd, PAGE_SIZE);
	uint64_t currentVaddr = BASE_ADDR + currentOffset;

	// Track loadable segments
	struct SegmentInfo {
		uint64_t fileOffset;
		uint64_t vaddr;
		uint64_t fileSize;
		uint64_t memSize;
		uint32_t flags;
	};

	std::vector<SegmentInfo> loadSegments;

	// Text segment (code)
	SegmentInfo textSegment = {currentOffset, currentVaddr, 0, 0, PF_R | PF_X};
	uint64_t textStart = currentOffset;

	for (auto &sec : sections) {
		if ((sec.flags & SHF_EXECINSTR) && sec.type == SHT_PROGBITS) {
			currentOffset = align(currentOffset, sec.alignment);
			currentVaddr = align(currentVaddr, sec.alignment);

			sec.addr = currentVaddr;
			// We'll set file offset later

			currentOffset += sec.data.size();
			currentVaddr += sec.data.size();
		}
	}

	textSegment.fileSize = currentOffset - textStart;
	textSegment.memSize = textSegment.fileSize;
	if (textSegment.fileSize > 0) {
		loadSegments.push_back(textSegment);
	}

	// Align to next page for data
	currentOffset = align(currentOffset, PAGE_SIZE);
	currentVaddr = align(currentVaddr, PAGE_SIZE);

	// Data segment (writable data)
	SegmentInfo dataSegment = {currentOffset, currentVaddr, 0, 0, PF_R | PF_W};
	uint64_t dataStart = currentOffset;
	uint64_t bssSize = 0;

	for (auto &sec : sections) {
		if ((sec.flags & SHF_WRITE) && sec.type == SHT_PROGBITS) {
			currentOffset = align(currentOffset, sec.alignment);
			currentVaddr = align(currentVaddr, sec.alignment);

			sec.addr = currentVaddr;

			currentOffset += sec.data.size();
			currentVaddr += sec.data.size();
		}
	}

	// Read-only data
	for (auto &sec : sections) {
		if ((sec.flags & SHF_ALLOC) && !(sec.flags & SHF_WRITE) &&
			!(sec.flags & SHF_EXECINSTR) && sec.type == SHT_PROGBITS) {
			currentOffset = align(currentOffset, sec.alignment);
			currentVaddr = align(currentVaddr, sec.alignment);

			sec.addr = currentVaddr;

			currentOffset += sec.data.size();
			currentVaddr += sec.data.size();
		}
	}

	dataSegment.fileSize = currentOffset - dataStart;

	// BSS (uninitialized data - doesn't take file space)
	for (auto &sec : sections) {
		if (sec.type == SHT_NOBITS) {
			currentVaddr = align(currentVaddr, sec.alignment);
			uint64_t size = sec.addr; // We stored size here temporarily
			sec.addr = currentVaddr;
			currentVaddr += size;
			bssSize += size;
		}
	}

	dataSegment.memSize = dataSegment.fileSize + bssSize;
	if (dataSegment.fileSize > 0 || bssSize > 0) {
		loadSegments.push_back(dataSegment);
	}

	// Non-loadable sections come after
	currentOffset = align(currentOffset, 8);

	// Add string tables and symbol table as sections
	uint32_t shstrtabIdx = static_cast<uint32_t>(sections.size());
	{
		ElfSection shstrtabSec;
		shstrtabSec.name = ".shstrtab";
		shstrtabSec.type = SHT_STRTAB;
		shstrtabSec.flags = 0;
		shstrtabSec.addr = 0;
		shstrtabSec.data = shstrtab;
		shstrtabSec.alignment = 1;
		shstrtabSec.link = 0;
		shstrtabSec.info = 0;
		shstrtabSec.entsize = 0;
		sections.push_back(shstrtabSec);
	}

	uint32_t strtabIdx = static_cast<uint32_t>(sections.size());
	{
		ElfSection strtabSec;
		strtabSec.name = ".strtab";
		strtabSec.type = SHT_STRTAB;
		strtabSec.flags = 0;
		strtabSec.addr = 0;
		strtabSec.data = strtab;
		strtabSec.alignment = 1;
		strtabSec.link = 0;
		strtabSec.info = 0;
		strtabSec.entsize = 0;
		sections.push_back(strtabSec);
	}

	// Build symbol table
	std::vector<uint8_t> symtabData;
	uint32_t firstGlobal = 0;

	// Sort symbols: locals first, then globals
	std::vector<ElfSymbol> sortedSymbols;
	for (const auto &sym : symbols) {
		if (sym.binding == STB_LOCAL) {
			sortedSymbols.push_back(sym);
		}
	}
	firstGlobal = static_cast<uint32_t>(sortedSymbols.size());
	for (const auto &sym : symbols) {
		if (sym.binding != STB_LOCAL) {
			sortedSymbols.push_back(sym);
		}
	}

	for (const auto &sym : sortedSymbols) {
		Elf64_Sym elfSym;
		elfSym.st_name = strtabOffsets[sym.name];
		elfSym.st_info = ELF64_ST_INFO(sym.binding, sym.type);
		elfSym.st_other = STV_DEFAULT;
		elfSym.st_shndx = sym.sectionIndex;
		elfSym.st_value = sym.value;
		if (sym.type == STT_FUNC || sym.type == STT_OBJECT) {
			// Adjust value to virtual address
			if (sym.sectionIndex < sections.size()) {
				elfSym.st_value += sections[sym.sectionIndex].addr;
			}
		}
		elfSym.st_size = sym.size;

		const uint8_t *ptr = reinterpret_cast<const uint8_t *>(&elfSym);
		symtabData.insert(symtabData.end(), ptr, ptr + sizeof(Elf64_Sym));
	}

	uint32_t symtabIdx = static_cast<uint32_t>(sections.size());
	{
		ElfSection symtabSec;
		symtabSec.name = ".symtab";
		symtabSec.type = SHT_SYMTAB;
		symtabSec.flags = 0;
		symtabSec.addr = 0;
		symtabSec.data = symtabData;
		symtabSec.alignment = 8;
		symtabSec.link = strtabIdx;
		symtabSec.info = firstGlobal;
		symtabSec.entsize = sizeof(Elf64_Sym);
		sections.push_back(symtabSec);
	}

	// Calculate section offsets for non-loadable sections
	for (size_t i = shstrtabIdx; i < sections.size(); ++i) {
		currentOffset = align(currentOffset, sections[i].alignment);
		// Store offset temporarily (we'll use it when writing)
		currentOffset += sections[i].data.size();
	}

	// Section header table comes at the end
	uint64_t shdrOffset = align(currentOffset, 8);

	// Find entry point (look for _start or main)
	uint64_t entryAddr = BASE_ADDR;
	for (const auto &sym : symbols) {
		if (sym.name == "_start" || sym.name == "main") {
			if (sym.sectionIndex < sections.size()) {
				entryAddr = sections[sym.sectionIndex].addr + sym.value;
				break;
			}
		}
	}

	// Now write everything
	output.resize(static_cast<size_t>(shdrOffset + sections.size() * shdrSize));
	std::fill(output.begin(), output.end(), 0);

	// Write ELF header
	Elf64_Ehdr ehdr = {};
	ehdr.e_ident[0] = ELFMAG0;
	ehdr.e_ident[1] = ELFMAG1;
	ehdr.e_ident[2] = ELFMAG2;
	ehdr.e_ident[3] = ELFMAG3;
	ehdr.e_ident[4] = ELFCLASS64;
	ehdr.e_ident[5] = ELFDATA2LSB;
	ehdr.e_ident[6] = EV_CURRENT;
	ehdr.e_ident[7] = ELFOSABI_NONE;
	ehdr.e_type = ET_EXEC;
	ehdr.e_machine = EM_X86_64;
	ehdr.e_version = EV_CURRENT;
	ehdr.e_entry = entryAddr;
	ehdr.e_phoff = phdrOffset;
	ehdr.e_shoff = shdrOffset;
	ehdr.e_flags = 0;
	ehdr.e_ehsize = sizeof(Elf64_Ehdr);
	ehdr.e_phentsize = sizeof(Elf64_Phdr);
	ehdr.e_phnum = numPhdrs;
	ehdr.e_shentsize = sizeof(Elf64_Shdr);
	ehdr.e_shnum = static_cast<uint16_t>(sections.size());
	ehdr.e_shstrndx = static_cast<uint16_t>(shstrtabIdx);

	std::memcpy(output.data(), &ehdr, sizeof(ehdr));

	// Write program headers
	size_t phdrPos = static_cast<size_t>(phdrOffset);

	// PT_PHDR
	{
		Elf64_Phdr phdr = {};
		phdr.p_type = PT_PHDR;
		phdr.p_flags = PF_R;
		phdr.p_offset = phdrOffset;
		phdr.p_vaddr = BASE_ADDR + phdrOffset;
		phdr.p_paddr = phdr.p_vaddr;
		phdr.p_filesz = numPhdrs * phdrSize;
		phdr.p_memsz = phdr.p_filesz;
		phdr.p_align = 8;
		std::memcpy(output.data() + phdrPos, &phdr, sizeof(phdr));
		phdrPos += sizeof(phdr);
	}

	// PT_LOAD segments
	for (const auto &seg : loadSegments) {
		Elf64_Phdr phdr = {};
		phdr.p_type = PT_LOAD;
		phdr.p_flags = seg.flags;
		phdr.p_offset = seg.fileOffset;
		phdr.p_vaddr = seg.vaddr;
		phdr.p_paddr = seg.vaddr;
		phdr.p_filesz = seg.fileSize;
		phdr.p_memsz = seg.memSize;
		phdr.p_align = PAGE_SIZE;
		std::memcpy(output.data() + phdrPos, &phdr, sizeof(phdr));
		phdrPos += sizeof(phdr);
	}

	// Fill remaining with PT_NULL or PT_GNU_STACK
	while (phdrPos < phdrOffset + numPhdrs * phdrSize) {
		Elf64_Phdr phdr = {};
		phdr.p_type = PT_GNU_STACK;
		phdr.p_flags = PF_R | PF_W; // Non-executable stack
		phdr.p_align = 8;
		std::memcpy(output.data() + phdrPos, &phdr, sizeof(phdr));
		phdrPos += sizeof(phdr);
	}

	// Write section data
	currentOffset = align(headersEnd, PAGE_SIZE);

	// Write loadable sections
	for (auto &sec : sections) {
		if ((sec.flags & SHF_ALLOC) && sec.type == SHT_PROGBITS) {
			currentOffset = align(currentOffset, sec.alignment);
			if (!sec.data.empty()) {
				// Resize if needed
				if (currentOffset + sec.data.size() > output.size()) {
					output.resize(static_cast<size_t>(currentOffset + sec.data.size()));
				}
				std::memcpy(output.data() + currentOffset, sec.data.data(), sec.data.size());
			}
			currentOffset += sec.data.size();
		}
	}

	// Align for data
	currentOffset = align(currentOffset, PAGE_SIZE);

	for (auto &sec : sections) {
		if ((sec.flags & SHF_WRITE) && sec.type == SHT_PROGBITS) {
			currentOffset = align(currentOffset, sec.alignment);
			if (!sec.data.empty()) {
				if (currentOffset + sec.data.size() > output.size()) {
					output.resize(static_cast<size_t>(currentOffset + sec.data.size()));
				}
				std::memcpy(output.data() + currentOffset, sec.data.data(), sec.data.size());
			}
			currentOffset += sec.data.size();
		}
	}

	// Read-only data
	for (auto &sec : sections) {
		if ((sec.flags & SHF_ALLOC) && !(sec.flags & SHF_WRITE) &&
			!(sec.flags & SHF_EXECINSTR) && sec.type == SHT_PROGBITS) {
			currentOffset = align(currentOffset, sec.alignment);
			if (!sec.data.empty()) {
				if (currentOffset + sec.data.size() > output.size()) {
					output.resize(static_cast<size_t>(currentOffset + sec.data.size()));
				}
				std::memcpy(output.data() + currentOffset, sec.data.data(), sec.data.size());
			}
			currentOffset += sec.data.size();
		}
	}

	// Non-loadable sections
	currentOffset = align(currentOffset, 8);
	std::vector<uint64_t> sectionOffsets(sections.size(), 0);

	for (size_t i = 0; i < sections.size(); ++i) {
		auto &sec = sections[i];
		if (!(sec.flags & SHF_ALLOC) && sec.type != SHT_NULL && sec.type != SHT_NOBITS) {
			currentOffset = align(currentOffset, sec.alignment > 0 ? sec.alignment : 1);
			sectionOffsets[i] = currentOffset;
			if (!sec.data.empty()) {
				if (currentOffset + sec.data.size() > output.size()) {
					output.resize(static_cast<size_t>(currentOffset + sec.data.size()));
				}
				std::memcpy(output.data() + currentOffset, sec.data.data(), sec.data.size());
			}
			currentOffset += sec.data.size();
		}
	}

	// Write section headers at the end
	if (shdrOffset + sections.size() * shdrSize > output.size()) {
		output.resize(static_cast<size_t>(shdrOffset + sections.size() * shdrSize));
	}

	size_t shdrPos = static_cast<size_t>(shdrOffset);
	uint64_t runningOffset = align(headersEnd, PAGE_SIZE);

	for (size_t i = 0; i < sections.size(); ++i) {
		const auto &sec = sections[i];
		Elf64_Shdr shdr = {};

		shdr.sh_name = shstrtabOffsets.count(sec.name) ? shstrtabOffsets[sec.name] : 0;
		shdr.sh_type = sec.type;
		shdr.sh_flags = sec.flags;
		shdr.sh_addr = sec.addr;

		if (sec.type == SHT_NULL) {
			shdr.sh_offset = 0;
			shdr.sh_size = 0;
		} else if (sec.type == SHT_NOBITS) {
			shdr.sh_offset = 0;
			shdr.sh_size = sec.addr; // We stored size in addr
		} else if (sec.flags & SHF_ALLOC) {
			runningOffset = align(runningOffset, sec.alignment > 0 ? sec.alignment : 1);
			shdr.sh_offset = runningOffset;
			shdr.sh_size = sec.data.size();
			runningOffset += sec.data.size();

			if (sec.flags & SHF_EXECINSTR) {
				// After text, align to page
			}
		} else {
			shdr.sh_offset = sectionOffsets[i];
			shdr.sh_size = sec.data.size();
		}

		shdr.sh_link = sec.link;
		shdr.sh_info = sec.info;
		shdr.sh_addralign = sec.alignment;
		shdr.sh_entsize = sec.entsize;

		std::memcpy(output.data() + shdrPos, &shdr, sizeof(shdr));
		shdrPos += sizeof(shdr);
	}

	// Write to file
	std::ofstream file(filename, std::ios::binary);
	if (!file) {
		return false;
	}

	file.write(reinterpret_cast<const char *>(output.data()),
			   static_cast<std::streamsize>(output.size()));

	return file.good();
}

} // namespace backend
