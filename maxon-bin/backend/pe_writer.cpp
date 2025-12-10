#include "pe_writer.h"
#include <algorithm>
#include <cstring>
#include <ctime>
#include <fstream>
#include <stdexcept>

namespace backend {

// Helper to copy a section name into a fixed-size buffer (avoids strncpy deprecation warning)
static void copySectionName(char (&dest)[8], const char *src) {
	std::memset(dest, 0, 8);
	size_t len = std::strlen(src);
	if (len > 8)
		len = 8;
	std::memcpy(dest, src, len);
}

// Helper to patch a RIP-relative displacement in code
// instrRva is the RVA where the 4-byte displacement starts
// targetRva is the RVA of the target address
// The displacement is relative to the instruction end (instrRva + 4)
static void patchRipRelativeDisplacement(std::vector<uint8_t> &data, uint32_t offset,
                                          uint32_t instrRva, uint32_t targetRva) {
	int32_t disp = static_cast<int32_t>(targetRva - (instrRva + 4));
	if (offset + 3 < data.size()) {
		data[offset + 0] = static_cast<uint8_t>(disp & 0xFF);
		data[offset + 1] = static_cast<uint8_t>((disp >> 8) & 0xFF);
		data[offset + 2] = static_cast<uint8_t>((disp >> 16) & 0xFF);
		data[offset + 3] = static_cast<uint8_t>((disp >> 24) & 0xFF);
	}
}

PeWriter::PeWriter()
	: subsystem(IMAGE_SUBSYSTEM_WINDOWS_CUI),
	  imageBase(0x140000000), // Default for 64-bit Windows executables
	  entryPointRva(0) {
}

void PeWriter::setSubsystem(uint16_t sub) {
	subsystem = sub;
}

void PeWriter::setImageBase(uint64_t base) {
	imageBase = base;
}

void PeWriter::setEntryPoint(uint32_t rva) {
	entryPointRva = rva;
}

uint32_t PeWriter::addSection(const std::string &name, uint32_t characteristics,
							  const std::vector<uint8_t> &data, uint32_t virtualSize) {
	PeSection section;
	section.name = name;
	section.characteristics = characteristics;
	section.data = data;
	section.virtualSize = virtualSize > 0 ? virtualSize : static_cast<uint32_t>(data.size());
	section.virtualAddress = 0; // Set during layout
	section.rawDataOffset = 0;	// Set during layout

	uint32_t index = static_cast<uint32_t>(sections.size());
	sectionNameMap[name] = index;
	sections.push_back(section);
	return index;
}

uint32_t PeWriter::addTextSection(const std::vector<uint8_t> &code) {
	return addSection(".text",
					  IMAGE_SCN_CNT_CODE | IMAGE_SCN_MEM_EXECUTE | IMAGE_SCN_MEM_READ,
					  code);
}

uint32_t PeWriter::addDataSection(const std::vector<uint8_t> &data) {
	return addSection(".data",
					  IMAGE_SCN_CNT_INITIALIZED_DATA | IMAGE_SCN_MEM_READ | IMAGE_SCN_MEM_WRITE,
					  data);
}

uint32_t PeWriter::addRdataSection(const std::vector<uint8_t> &data) {
	return addSection(".rdata",
					  IMAGE_SCN_CNT_INITIALIZED_DATA | IMAGE_SCN_MEM_READ,
					  data);
}

uint32_t PeWriter::addBssSection(uint32_t size) {
	return addSection(".bss",
					  IMAGE_SCN_CNT_UNINITIALIZED_DATA | IMAGE_SCN_MEM_READ | IMAGE_SCN_MEM_WRITE,
					  std::vector<uint8_t>(), size);
}

void PeWriter::addImport(const std::string &dllName, const std::string &functionName) {
	// Find existing DLL entry or create new one
	for (auto &imp : imports) {
		if (imp.dllName == dllName) {
			// Check if function already exists
			if (std::find(imp.functions.begin(), imp.functions.end(), functionName) == imp.functions.end()) {
				imp.functions.push_back(functionName);
			}
			return;
		}
	}

	PeImport newImport;
	newImport.dllName = dllName;
	newImport.functions.push_back(functionName);
	imports.push_back(newImport);
}

void PeWriter::addSymbol(const std::string &name, uint32_t rva, uint32_t size,
						 bool isFunction, int32_t sectionIndex) {
	PeSymbol sym;
	sym.name = name;
	sym.rva = rva;
	sym.size = size;
	sym.isFunction = isFunction;
	sym.sectionIndex = sectionIndex;
	symbols.push_back(sym);
}

void PeWriter::addRelocation(uint32_t rva, uint16_t type) {
	PeRelocation rel;
	rel.rva = rva;
	rel.type = type;
	baseRelocations.push_back(rel);
}

void PeWriter::addImportRelocation(uint32_t codeOffset, const std::string &dllName,
								   const std::string &funcName) {
	ImportCallReloc reloc;
	reloc.codeOffset = codeOffset;
	reloc.dllName = dllName;
	reloc.funcName = funcName;
	importCallRelocs.push_back(reloc);
}

void PeWriter::addDataRelocation(uint32_t codeOffset, uint32_t dataOffset) {
	DataReloc reloc;
	reloc.codeOffset = codeOffset;
	reloc.dataOffset = dataOffset;
	dataRelocs.push_back(reloc);
}

uint32_t PeWriter::getImportRva(const std::string &dllName,
								const std::string &funcName) const {
	std::string key = dllName + "!" + funcName;
	auto it = importRvaMap.find(key);
	if (it != importRvaMap.end()) {
		return it->second;
	}

	// If not yet populated (before write()), calculate expected position
	// Import directory starts after all user sections
	// Layout: .text at 0x1000, then other sections, then .idata
	uint32_t importBaseRva = SECTION_ALIGNMENT; // First section starts at 0x1000
	for (const auto &sec : sections) {
		importBaseRva = alignTo(importBaseRva + sec.virtualSize, SECTION_ALIGNMENT);
	}

	// Import directory layout:
	// - Import descriptors: (numDlls + 1) * 20 bytes
	// - IAT: 8 bytes per function + 8 null terminator per DLL
	// - INT: same as IAT
	// - Strings

	size_t numDlls = imports.size();
	size_t descriptorTableSize = (numDlls + 1) * sizeof(ImportDescriptor);

	// IAT starts right after descriptors
	uint32_t currentIatRva = importBaseRva + static_cast<uint32_t>(descriptorTableSize);

	// Find the function in imports
	for (const auto &imp : imports) {
		for (const auto &func : imp.functions) {
			if (imp.dllName == dllName && func == funcName) {
				importRvaMap[key] = currentIatRva;
				return currentIatRva;
			}
			currentIatRva += 8; // 64-bit IAT entry
		}
		currentIatRva += 8; // Null terminator for this DLL
	}

	return 0;
}

int32_t PeWriter::getSectionIndex(const std::string &name) const {
	auto it = sectionNameMap.find(name);
	if (it == sectionNameMap.end()) {
		return -1;
	}
	return static_cast<int32_t>(it->second);
}

void PeWriter::calculateLayout() {
	// Calculate headers size
	uint32_t dosHeaderSize = sizeof(DosHeader);
	uint32_t coffHeaderSize = sizeof(CoffHeader);
	uint32_t optHeaderSize = sizeof(OptionalHeader64);
	uint32_t sectionHeadersSize = static_cast<uint32_t>(sections.size()) * sizeof(SectionHeader);

	// Add space for import directory section if needed
	if (!imports.empty()) {
		sectionHeadersSize += sizeof(SectionHeader);
	}
	// Add space for relocation section if needed
	if (!baseRelocations.empty()) {
		sectionHeadersSize += sizeof(SectionHeader);
	}

	uint32_t headersSize = dosHeaderSize + coffHeaderSize + optHeaderSize + sectionHeadersSize;
	headersSize = alignTo(headersSize, FILE_ALIGNMENT);

	// First section starts at first aligned address after headers
	uint32_t currentRva = alignTo(headersSize, SECTION_ALIGNMENT);
	uint32_t currentFileOffset = headersSize;

	for (auto &sec : sections) {
		sec.virtualAddress = currentRva;
		sec.rawDataOffset = currentFileOffset;

		// Virtual size is the actual size in memory
		// Raw data size is file-aligned
		uint32_t rawDataSize = alignTo(static_cast<uint32_t>(sec.data.size()), FILE_ALIGNMENT);

		currentRva = alignTo(currentRva + sec.virtualSize, SECTION_ALIGNMENT);
		if (!sec.data.empty()) {
			currentFileOffset += rawDataSize;
		}
	}
}

std::vector<uint8_t> PeWriter::buildImportDirectory(uint32_t baseRva) {
	std::vector<uint8_t> data;

	if (imports.empty()) {
		return data;
	}

	// Calculate sizes
	size_t numDlls = imports.size();
	size_t descriptorTableSize = (numDlls + 1) * sizeof(ImportDescriptor); // +1 for null terminator

	// Each DLL needs an INT and IAT, each entry is 8 bytes (64-bit), plus null terminator
	size_t iatSize = 0;
	for (const auto &imp : imports) {
		iatSize += (imp.functions.size() + 1) * 8; // +1 for null terminator per DLL
	}

	// Calculate string table offset (after descriptors and IAT)
	size_t stringsOffset = descriptorTableSize + iatSize * 2; // INT and IAT

	// Calculate total size
	size_t totalSize = stringsOffset;
	for (const auto &imp : imports) {
		totalSize += imp.dllName.size() + 1;
		for (const auto &func : imp.functions) {
			totalSize += 2 + func.size() + 1; // 2-byte hint + name + null
			if (totalSize % 2)
				totalSize++; // Align to 2
		}
	}

	data.resize(alignTo(static_cast<uint32_t>(totalSize), 4), 0);

	// Build import descriptors
	uint32_t currentIatRva = baseRva + static_cast<uint32_t>(descriptorTableSize);
	uint32_t currentIntRva = currentIatRva + static_cast<uint32_t>(iatSize);
	uint32_t currentStringRva = baseRva + static_cast<uint32_t>(stringsOffset);

	size_t descOffset = 0;
	size_t iatOffset = descriptorTableSize;
	size_t intOffset = descriptorTableSize + iatSize;
	size_t stringOffset = stringsOffset;

	for (const auto &imp : imports) {
		ImportDescriptor desc = {};
		desc.OriginalFirstThunk = currentIntRva;
		desc.FirstThunk = currentIatRva;
		desc.Name = currentStringRva;

		std::memcpy(data.data() + descOffset, &desc, sizeof(desc));
		descOffset += sizeof(ImportDescriptor);

		// Write DLL name
		std::memcpy(data.data() + stringOffset, imp.dllName.c_str(), imp.dllName.size() + 1);
		currentStringRva += static_cast<uint32_t>(imp.dllName.size() + 1);
		stringOffset += imp.dllName.size() + 1;

		// Write function entries
		for (const auto &func : imp.functions) {
			// Store RVA for import lookup
			std::string key = imp.dllName + "!" + func;
			importRvaMap[key] = currentIatRva;

			// Align string offset
			if (stringOffset % 2) {
				stringOffset++;
				currentStringRva++;
			}

			// Write IAT entry (points to hint/name)
			uint64_t hintNameRva = currentStringRva;
			std::memcpy(data.data() + iatOffset, &hintNameRva, 8);
			iatOffset += 8;
			currentIatRva += 8;

			// Write INT entry (same as IAT)
			std::memcpy(data.data() + intOffset, &hintNameRva, 8);
			intOffset += 8;
			currentIntRva += 8;

			// Write hint (0) and name
			uint16_t hint = 0;
			std::memcpy(data.data() + stringOffset, &hint, 2);
			stringOffset += 2;
			std::memcpy(data.data() + stringOffset, func.c_str(), func.size() + 1);
			currentStringRva += 2 + static_cast<uint32_t>(func.size() + 1);
			stringOffset += func.size() + 1;
		}

		// Null terminator for this DLL's IAT/INT
		iatOffset += 8;
		intOffset += 8;
		currentIatRva += 8;
		currentIntRva += 8;
	}

	return data;
}

std::vector<uint8_t> PeWriter::buildRelocationDirectory(uint32_t baseRva) {
	std::vector<uint8_t> data;

	if (baseRelocations.empty()) {
		return data;
	}

	// Group relocations by page (4KB blocks)
	std::unordered_map<uint32_t, std::vector<PeRelocation>> pageRelocations;

	for (const auto &rel : baseRelocations) {
		uint32_t page = rel.rva & ~0xFFF;
		pageRelocations[page].push_back(rel);
	}

	// Build relocation blocks
	for (const auto &[page, relocs] : pageRelocations) {
		// Each block: header (8 bytes) + entries (2 bytes each)
		size_t numEntries = relocs.size();
		if (numEntries % 2)
			numEntries++; // Pad to 4-byte alignment

		size_t blockSize = 8 + numEntries * 2;
		size_t startOffset = data.size();
		data.resize(data.size() + blockSize, 0);

		BaseRelocationBlock block;
		block.VirtualAddress = page;
		block.SizeOfBlock = static_cast<uint32_t>(blockSize);

		std::memcpy(data.data() + startOffset, &block, sizeof(block));

		// Write entries
		size_t entryOffset = startOffset + 8;
		for (const auto &rel : relocs) {
			uint16_t offset = static_cast<uint16_t>(rel.rva - page);
			uint16_t entry = (rel.type << 12) | offset;
			std::memcpy(data.data() + entryOffset, &entry, 2);
			entryOffset += 2;
		}
	}

	return data;
}

bool PeWriter::write(const std::string &filename) {
	calculateLayout();

	// Build import directory if needed
	std::vector<uint8_t> importData;
	uint32_t importRva = 0;
	if (!imports.empty()) {
		// Find where to place import directory
		if (!sections.empty()) {
			auto &lastSec = sections.back();
			importRva = alignTo(lastSec.virtualAddress + lastSec.virtualSize, SECTION_ALIGNMENT);
		} else {
			importRva = SECTION_ALIGNMENT;
		}
		importData = buildImportDirectory(importRva);
	}

	// Build relocation directory if needed
	std::vector<uint8_t> relocData;
	uint32_t relocRva = 0;
	if (!baseRelocations.empty()) {
		if (!imports.empty()) {
			relocRva = alignTo(importRva + static_cast<uint32_t>(importData.size()), SECTION_ALIGNMENT);
		} else if (!sections.empty()) {
			auto &lastSec = sections.back();
			relocRva = alignTo(lastSec.virtualAddress + lastSec.virtualSize, SECTION_ALIGNMENT);
		} else {
			relocRva = SECTION_ALIGNMENT;
		}
		relocData = buildRelocationDirectory(relocRva);
	}

	// Calculate total number of sections
	uint32_t numSections = static_cast<uint32_t>(sections.size());
	if (!imports.empty())
		numSections++;
	if (!baseRelocations.empty())
		numSections++;

	// Calculate header sizes
	uint32_t dosStubSize = 64; // Minimal DOS stub
	uint32_t peHeaderOffset = dosStubSize;
	uint32_t coffHeaderSize = 4 + 20; // Signature + COFF header
	uint32_t optHeaderSize = sizeof(OptionalHeader64);
	uint32_t sectionHeadersSize = numSections * sizeof(SectionHeader);
	uint32_t headersSize = peHeaderOffset + coffHeaderSize + optHeaderSize + sectionHeadersSize;
	headersSize = alignTo(headersSize, FILE_ALIGNMENT);

	// Recalculate layout with proper header size
	uint32_t currentRva = alignTo(headersSize, SECTION_ALIGNMENT);
	uint32_t currentFileOffset = headersSize;

	for (auto &sec : sections) {
		sec.virtualAddress = currentRva;
		sec.rawDataOffset = sec.data.empty() ? 0 : currentFileOffset;

		uint32_t rawDataSize = alignTo(static_cast<uint32_t>(sec.data.size()), FILE_ALIGNMENT);
		currentRva = alignTo(currentRva + sec.virtualSize, SECTION_ALIGNMENT);
		if (!sec.data.empty()) {
			currentFileOffset += rawDataSize;
		}
	}

	// Update import/reloc RVAs based on actual layout
	if (!imports.empty()) {
		importRva = currentRva;
		importData = buildImportDirectory(importRva);
		currentRva = alignTo(currentRva + static_cast<uint32_t>(importData.size()), SECTION_ALIGNMENT);

		// Now patch import call relocations in the code section
		// After buildImportDirectory, importRvaMap is populated
		if (!sections.empty() && !importCallRelocs.empty()) {
			// Find the .text section
			for (auto &sec : sections) {
				if (sec.name == ".text") {
					for (const auto &reloc : importCallRelocs) {
						std::string key = reloc.dllName + "!" + reloc.funcName;
						auto iatIt = importRvaMap.find(key);
						if (iatIt != importRvaMap.end()) {
							uint32_t instrRva = sec.virtualAddress + reloc.codeOffset;
							patchRipRelativeDisplacement(sec.data, reloc.codeOffset, instrRva, iatIt->second);
						}
					}
					break;
				}
			}
		}
	}

	// Patch data section relocations (RIP-relative references to .data)
	if (!dataRelocs.empty()) {
		// Find .text and .data sections
		PeSection *textSec = nullptr;
		PeSection *dataSec = nullptr;
		for (auto &sec : sections) {
			if (sec.name == ".text")
				textSec = &sec;
			if (sec.name == ".data")
				dataSec = &sec;
		}

		if (textSec && dataSec) {
			for (const auto &reloc : dataRelocs) {
				uint32_t instrRva = textSec->virtualAddress + reloc.codeOffset;
				uint32_t dataRva = dataSec->virtualAddress + reloc.dataOffset;
				patchRipRelativeDisplacement(textSec->data, reloc.codeOffset, instrRva, dataRva);
			}
		}
	}

	if (!baseRelocations.empty()) {
		relocRva = currentRva;
		relocData = buildRelocationDirectory(relocRva);
		currentRva = alignTo(currentRva + static_cast<uint32_t>(relocData.size()), SECTION_ALIGNMENT);
	}

	uint32_t imageSize = currentRva;

	// Calculate code and data sizes
	uint32_t sizeOfCode = 0;
	uint32_t sizeOfInitData = 0;
	uint32_t sizeOfUninitData = 0;
	uint32_t baseOfCode = 0;

	for (const auto &sec : sections) {
		if (sec.characteristics & IMAGE_SCN_CNT_CODE) {
			sizeOfCode += alignTo(static_cast<uint32_t>(sec.data.size()), FILE_ALIGNMENT);
			if (baseOfCode == 0) {
				baseOfCode = sec.virtualAddress;
			}
		}
		if (sec.characteristics & IMAGE_SCN_CNT_INITIALIZED_DATA) {
			sizeOfInitData += alignTo(static_cast<uint32_t>(sec.data.size()), FILE_ALIGNMENT);
		}
		if (sec.characteristics & IMAGE_SCN_CNT_UNINITIALIZED_DATA) {
			sizeOfUninitData += sec.virtualSize;
		}
	}

	// Build output buffer
	std::vector<uint8_t> output;

	// Write DOS header
	DosHeader dos = {};
	dos.e_magic = DOS_MAGIC;
	dos.e_cblp = 0x90;
	dos.e_cp = 0x03;
	dos.e_cparhdr = 0x04;
	dos.e_maxalloc = 0xFFFF;
	dos.e_sp = 0xB8;
	dos.e_lfarlc = 0x40;
	dos.e_lfanew = peHeaderOffset;

	output.insert(output.end(),
				  reinterpret_cast<uint8_t *>(&dos),
				  reinterpret_cast<uint8_t *>(&dos) + sizeof(dos));

	// Pad to PE header offset
	output.resize(peHeaderOffset, 0);

	// Write PE signature
	uint32_t peSignature = PE_SIGNATURE;
	output.insert(output.end(),
				  reinterpret_cast<uint8_t *>(&peSignature),
				  reinterpret_cast<uint8_t *>(&peSignature) + 4);

	// Write COFF header
	CoffHeader coff = {};
	coff.Signature = 0; // Already written
	coff.Machine = IMAGE_FILE_MACHINE_AMD64;
	coff.NumberOfSections = static_cast<uint16_t>(numSections);
	coff.TimeDateStamp = static_cast<uint32_t>(std::time(nullptr));
	coff.PointerToSymbolTable = 0;
	coff.NumberOfSymbols = 0;
	coff.SizeOfOptionalHeader = sizeof(OptionalHeader64);
	coff.Characteristics = IMAGE_FILE_EXECUTABLE_IMAGE | IMAGE_FILE_LARGE_ADDRESS_AWARE;

	// Write COFF header (skip signature field since we wrote it separately)
	output.insert(output.end(),
				  reinterpret_cast<uint8_t *>(&coff.Machine),
				  reinterpret_cast<uint8_t *>(&coff) + sizeof(coff));

	// Write optional header
	OptionalHeader64 opt = {};
	opt.Magic = PE32PLUS_MAGIC;
	opt.MajorLinkerVersion = 14;
	opt.MinorLinkerVersion = 0;
	opt.SizeOfCode = sizeOfCode;
	opt.SizeOfInitializedData = sizeOfInitData;
	opt.SizeOfUninitializedData = sizeOfUninitData;
	opt.AddressOfEntryPoint = entryPointRva;
	opt.BaseOfCode = baseOfCode;
	opt.ImageBase = imageBase;
	opt.SectionAlignment = SECTION_ALIGNMENT;
	opt.FileAlignment = FILE_ALIGNMENT;
	opt.MajorOperatingSystemVersion = 6;
	opt.MinorOperatingSystemVersion = 0;
	opt.MajorImageVersion = 0;
	opt.MinorImageVersion = 0;
	opt.MajorSubsystemVersion = 6;
	opt.MinorSubsystemVersion = 0;
	opt.Win32VersionValue = 0;
	opt.SizeOfImage = imageSize;
	opt.SizeOfHeaders = headersSize;
	opt.CheckSum = 0;
	opt.Subsystem = subsystem;
	opt.DllCharacteristics = IMAGE_DLLCHARACTERISTICS_DYNAMIC_BASE |
							 IMAGE_DLLCHARACTERISTICS_NX_COMPAT |
							 IMAGE_DLLCHARACTERISTICS_TERMINAL_SERVER_AWARE;
	opt.SizeOfStackReserve = 0x100000; // 1MB
	opt.SizeOfStackCommit = 0x1000;	   // 4KB
	opt.SizeOfHeapReserve = 0x100000;  // 1MB
	opt.SizeOfHeapCommit = 0x1000;	   // 4KB
	opt.LoaderFlags = 0;
	opt.NumberOfRvaAndSizes = IMAGE_NUMBEROF_DIRECTORY_ENTRIES;

	// Set up data directories
	if (!imports.empty()) {
		opt.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT].VirtualAddress = importRva;
		opt.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT].Size =
			static_cast<uint32_t>((imports.size() + 1) * sizeof(ImportDescriptor));

		// IAT directory
		uint32_t iatRva = importRva + static_cast<uint32_t>((imports.size() + 1) * sizeof(ImportDescriptor));
		uint32_t iatSize = 0;
		for (const auto &imp : imports) {
			iatSize += static_cast<uint32_t>((imp.functions.size() + 1) * 8);
		}
		opt.DataDirectory[IMAGE_DIRECTORY_ENTRY_IAT].VirtualAddress = iatRva;
		opt.DataDirectory[IMAGE_DIRECTORY_ENTRY_IAT].Size = iatSize;
	}

	if (!baseRelocations.empty()) {
		opt.DataDirectory[IMAGE_DIRECTORY_ENTRY_BASERELOC].VirtualAddress = relocRva;
		opt.DataDirectory[IMAGE_DIRECTORY_ENTRY_BASERELOC].Size =
			static_cast<uint32_t>(relocData.size());
	}

	output.insert(output.end(),
				  reinterpret_cast<uint8_t *>(&opt),
				  reinterpret_cast<uint8_t *>(&opt) + sizeof(opt));

	// Write section headers
	currentFileOffset = headersSize;

	for (const auto &sec : sections) {
		SectionHeader shdr = {};
		copySectionName(shdr.Name, sec.name.c_str());
		shdr.VirtualSize = sec.virtualSize;
		shdr.VirtualAddress = sec.virtualAddress;
		shdr.SizeOfRawData = sec.data.empty() ? 0 : alignTo(static_cast<uint32_t>(sec.data.size()), FILE_ALIGNMENT);
		shdr.PointerToRawData = sec.data.empty() ? 0 : currentFileOffset;
		shdr.PointerToRelocations = 0;
		shdr.PointerToLinenumbers = 0;
		shdr.NumberOfRelocations = 0;
		shdr.NumberOfLinenumbers = 0;
		shdr.Characteristics = sec.characteristics;

		output.insert(output.end(),
					  reinterpret_cast<uint8_t *>(&shdr),
					  reinterpret_cast<uint8_t *>(&shdr) + sizeof(shdr));

		if (!sec.data.empty()) {
			currentFileOffset += shdr.SizeOfRawData;
		}
	}

	// Write import section header if needed
	if (!imports.empty()) {
		SectionHeader shdr = {};
		copySectionName(shdr.Name, ".idata");
		shdr.VirtualSize = static_cast<uint32_t>(importData.size());
		shdr.VirtualAddress = importRva;
		shdr.SizeOfRawData = alignTo(static_cast<uint32_t>(importData.size()), FILE_ALIGNMENT);
		shdr.PointerToRawData = currentFileOffset;
		shdr.Characteristics = IMAGE_SCN_CNT_INITIALIZED_DATA | IMAGE_SCN_MEM_READ;

		output.insert(output.end(),
					  reinterpret_cast<uint8_t *>(&shdr),
					  reinterpret_cast<uint8_t *>(&shdr) + sizeof(shdr));

		currentFileOffset += shdr.SizeOfRawData;
	}

	// Write relocation section header if needed
	if (!baseRelocations.empty()) {
		SectionHeader shdr = {};
		copySectionName(shdr.Name, ".reloc");
		shdr.VirtualSize = static_cast<uint32_t>(relocData.size());
		shdr.VirtualAddress = relocRva;
		shdr.SizeOfRawData = alignTo(static_cast<uint32_t>(relocData.size()), FILE_ALIGNMENT);
		shdr.PointerToRawData = currentFileOffset;
		shdr.Characteristics = IMAGE_SCN_CNT_INITIALIZED_DATA | IMAGE_SCN_MEM_READ |
							   IMAGE_SCN_MEM_DISCARDABLE;

		output.insert(output.end(),
					  reinterpret_cast<uint8_t *>(&shdr),
					  reinterpret_cast<uint8_t *>(&shdr) + sizeof(shdr));
	}

	// Pad headers to alignment
	output.resize(headersSize, 0);

	// Write section data
	for (const auto &sec : sections) {
		if (!sec.data.empty()) {
			size_t pos = output.size();
			uint32_t alignedSize = alignTo(static_cast<uint32_t>(sec.data.size()), FILE_ALIGNMENT);
			output.resize(pos + alignedSize, 0);
			std::memcpy(output.data() + pos, sec.data.data(), sec.data.size());
		}
	}

	// Write import data
	if (!importData.empty()) {
		size_t pos = output.size();
		uint32_t alignedSize = alignTo(static_cast<uint32_t>(importData.size()), FILE_ALIGNMENT);
		output.resize(pos + alignedSize, 0);
		std::memcpy(output.data() + pos, importData.data(), importData.size());
	}

	// Write relocation data
	if (!relocData.empty()) {
		size_t pos = output.size();
		uint32_t alignedSize = alignTo(static_cast<uint32_t>(relocData.size()), FILE_ALIGNMENT);
		output.resize(pos + alignedSize, 0);
		std::memcpy(output.data() + pos, relocData.data(), relocData.size());
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
