/**
 * MIR Code Generator - Output Generation
 *
 * This file implements the output methods that compile MIR to x86-64 machine code
 * and produce executable files using the custom PE/ELF writers.
 */

#include "../backend/pe_writer.h"
#include "../backend/regalloc.h"
#include "../backend/x86_codegen.h"
#include "../codegen_mir.h"
#include "../file_utils.h"
#include "../mir/mir_parser.h"
#include "../mir/optimizer.h"
#include <fstream>
#include <iostream>

#ifdef _WIN32
#include "../backend/pe_writer.h"
#else
#include "../backend/elf_writer.h"
#endif

//==============================================================================
// IR Operations
//==============================================================================

void MIRCodeGenerator::printIR() {
	module->print();
}

void MIRCodeGenerator::writeIRToFile(const std::string &filename) {
	std::ofstream file(filename);
	if (!file) {
		throw std::runtime_error("Could not open file: " + filename);
	}

	file << module->toString();
	file.close();
}

//==============================================================================
// Optimization
//==============================================================================

void MIRCodeGenerator::optimize() {
	logProgress("Running optimization passes...");

	// Create standard optimization pipeline with verbosity
	mir::MIROptimizer optimizer = mir::MIROptimizer::createStandardPipeline(verboseLevel);

	// Run passes until convergence
	optimizer.runPasses(*module);

	logProgress("Optimization complete");
}

void MIRCodeGenerator::runDeadCodeElimination() {
	logDetail("Running dead code elimination");
	mir::DeadCodeEliminationPass dce;
	dce.setVerboseLevel(verboseLevel);
	dce.run(*module);
}

//==============================================================================
// Runtime Library Location
//==============================================================================

static std::string findRuntimeFile(const std::string &filename) {
	// Try relative to executable first
	std::string exeDir = getExecutableDirectory();
	std::string path = exeDir + "/" + filename;

	std::ifstream f(path);
	if (f.good()) {
		return path;
	}

	// Try maxon-runtime directory (for development)
	path = exeDir + "/../maxon-runtime/" + filename;
	f = std::ifstream(path);
	if (f.good()) {
		return path;
	}

	// Try current directory
	path = filename;
	f = std::ifstream(path);
	if (f.good()) {
		return path;
	}

	return ""; // Not found
}

//==============================================================================
// Object File Generation
//==============================================================================

void MIRCodeGenerator::writeObjectFile(const std::string &filename) {
	// This would generate an object file (.o / .obj) for later linking
	// For now, we go straight to executable generation
	throw std::runtime_error("Object file generation not yet implemented - use writeExecutable instead");
}

//==============================================================================
// Executable Generation
//==============================================================================

void MIRCodeGenerator::writeExecutable(const std::string &exeFile) {
	if (verboseLevel >= 1) {
		std::cout << "Generating executable: " << exeFile << std::endl;
	}

	// Determine calling convention based on target
	bool isWindows = (module->targetTriple.find("windows") != std::string::npos);

	// Helper lambda to merge a runtime module into the current module
	auto mergeRuntime = [this](const std::string &runtimeMirPath) {
		auto runtimeResult = mir::MIRParser::parseFile(runtimeMirPath);
		if (!runtimeResult.errors.empty()) {
			std::cerr << "Warning: Failed to parse runtime library: " << runtimeResult.errors[0].message << std::endl;
			return;
		}
		if (!runtimeResult.module)
			return;

		// First pass: Add external declarations from runtime that we don't have
		// (these are Windows API functions like ExitProcess, HeapAlloc, etc.)
		for (auto &func : runtimeResult.module->functions) {
			if (func->isExternal) {
				if (!module->getFunction(func->name)) {
					// Add external declaration
					module->functions.push_back(std::move(func));
				}
			}
		}

		// Second pass: Replace declarations with definitions from runtime
		for (auto &func : runtimeResult.module->functions) {
			if (!func)
				continue; // Already moved in first pass
			if (func->isExternal)
				continue; // Skip remaining externals

			// Check if we have a declaration for this function that we can replace
			mir::MIRFunction *existing = module->getFunction(func->name);
			if (existing && existing->isExternal) {
				// Replace the declaration with the definition
				for (auto it = module->functions.begin(); it != module->functions.end(); ++it) {
					if ((*it)->name == func->name) {
						*it = std::move(func);
						break;
					}
				}
			} else if (!existing) {
				// New function not in user module
				module->functions.push_back(std::move(func));
			}
			// If existing && !existing->isExternal, keep the user's definition
		}
	};

	// Step 0: Load and merge runtime libraries
	// First load platform-specific runtime (malloc, free, exit, etc.)
	std::string platformRuntimePath = isWindows ? "runtime_windows.mir" : "runtime_linux.mir";
	std::string platformMirPath = findRuntimeFile(platformRuntimePath);
	if (!platformMirPath.empty()) {
		mergeRuntime(platformMirPath);
	}

	// Then load platform-independent runtime (math functions like trunc, floor, sin, etc.)
	std::string commonMirPath = findRuntimeFile("runtime.mir");
	if (!commonMirPath.empty()) {
		mergeRuntime(commonMirPath);
	}

	// Update calleeFunc pointers in all call instructions
	// This is needed because we replaced some functions
	for (auto &func : module->functions) {
		for (auto &block : func->basicBlocks) {
			for (auto &inst : block->instructions) {
				if (inst->opcode == mir::MIROpcode::Call && !inst->calleeName.empty()) {
					// Re-lookup the function by name
					inst->calleeFunc = module->getFunction(inst->calleeName);
				}
			}
		}
	}

	// Step 1: Generate x86-64 code from MIR
	backend::CallingConv cc = isWindows ? backend::CallingConv::Win64 : backend::CallingConv::SysV64;
	backend::X86CodeGen x86gen(cc);

	if (verboseLevel >= 2) {
		std::cout << "  Generating x86-64 code..." << std::endl;
	}

	x86gen.generate(module.get());

	const auto &functionCodes = x86gen.getFunctionCodes();
	const auto &dataSection = x86gen.getDataSection();

	if (verboseLevel >= 2) {
		std::cout << "  Generated " << functionCodes.size() << " functions" << std::endl;
		std::cout << "  Data section size: " << dataSection.size() << " bytes" << std::endl;
	}

	// Collect all code into single buffer and track function positions
	std::vector<uint8_t> codeBuffer;
	std::unordered_map<std::string, size_t> functionOffsets;

	// Collect all relocations with their adjusted offsets
	struct AdjustedRelocation {
		backend::Relocation reloc;
		size_t funcBaseOffset; // Offset of the function containing this relocation
	};
	std::vector<AdjustedRelocation> allRelocations;

	for (const auto &func : functionCodes) {
		size_t funcOffset = codeBuffer.size();
		functionOffsets[func.name] = funcOffset;
		codeBuffer.insert(codeBuffer.end(), func.code.begin(), func.code.end());

		// Collect relocations with adjusted offsets
		for (const auto &reloc : func.relocations) {
			allRelocations.push_back({reloc, funcOffset});
		}
	}

	// Apply relocations for internal function calls and collect import/data relocations
	std::vector<std::pair<size_t, std::string>> importRelocations; // offset, symbolName
	std::vector<std::pair<size_t, size_t>> dataRelocations;		   // codeOffset, dataOffset

	for (const auto &adj : allRelocations) {
		const auto &reloc = adj.reloc;

		if (reloc.type == backend::Relocation::Type::FunctionCall) {
			auto targetIt = functionOffsets.find(reloc.symbolName);
			if (targetIt != functionOffsets.end()) {
				// This is an internal function call - patch the rel32
				size_t patchOffset = adj.funcBaseOffset + reloc.offset;
				size_t callEnd = patchOffset + 4; // After the rel32
				int32_t rel = static_cast<int32_t>(targetIt->second - callEnd);

				// Patch the 4-byte relative offset
				codeBuffer[patchOffset + 0] = static_cast<uint8_t>(rel & 0xFF);
				codeBuffer[patchOffset + 1] = static_cast<uint8_t>((rel >> 8) & 0xFF);
				codeBuffer[patchOffset + 2] = static_cast<uint8_t>((rel >> 16) & 0xFF);
				codeBuffer[patchOffset + 3] = static_cast<uint8_t>((rel >> 24) & 0xFF);
			}
			// External function calls that aren't found will fall through (shouldn't happen)
		} else if (reloc.type == backend::Relocation::Type::ImportCall) {
			// Indirect call through IAT - collect for later patching by PE writer
			size_t patchOffset = adj.funcBaseOffset + reloc.offset;
			importRelocations.push_back({patchOffset, reloc.symbolName});
		} else if (reloc.type == backend::Relocation::Type::GlobalRef) {
			// RIP-relative reference to data section (e.g., float constants)
			// Symbol name format: ".constN" where N is the offset in data section
			size_t patchOffset = adj.funcBaseOffset + reloc.offset;
			// Parse data offset from symbol name (e.g., ".const0" -> 0, ".const8" -> 8)
			size_t dataOffset = 0;
			if (reloc.symbolName.substr(0, 6) == ".const") {
				dataOffset = std::stoull(reloc.symbolName.substr(6));
			}
			dataRelocations.push_back({patchOffset, dataOffset});
		}
	}

	if (verboseLevel >= 2) {
		std::cout << "  Total code size: " << codeBuffer.size() << " bytes" << std::endl;
	}

	// Step 2: Write executable using PE or ELF writer
#ifdef _WIN32
	writeWindowsExecutable(exeFile, codeBuffer, dataSection, functionOffsets, importRelocations, dataRelocations);
#else
	writeLinuxExecutable(exeFile, codeBuffer, dataSection, functionOffsets, importRelocations);
#endif
}

#ifdef _WIN32
void MIRCodeGenerator::writeWindowsExecutable(
	const std::string &exeFile,
	std::vector<uint8_t> &code, // non-const to allow patching
	const std::vector<uint8_t> &data,
	const std::unordered_map<std::string, size_t> &funcOffsets,
	const std::vector<std::pair<size_t, std::string>> &importRelocs,
	const std::vector<std::pair<size_t, size_t>> &dataRelocs) {

	backend::PeWriter pe;

	// Add imports for Windows runtime functions FIRST (before sections)
	// so we know the IAT layout
	pe.addImport("kernel32.dll", "ExitProcess");
	pe.addImport("kernel32.dll", "GetProcessHeap");
	pe.addImport("kernel32.dll", "HeapAlloc");
	pe.addImport("kernel32.dll", "HeapFree");
	pe.addImport("kernel32.dll", "GetStdHandle");
	pe.addImport("kernel32.dll", "WriteFile");

	// Map of symbol names to their DLL!function format
	std::unordered_map<std::string, std::string> importMapping = {
		{"ExitProcess", "kernel32.dll!ExitProcess"},
		{"GetProcessHeap", "kernel32.dll!GetProcessHeap"},
		{"HeapAlloc", "kernel32.dll!HeapAlloc"},
		{"HeapFree", "kernel32.dll!HeapFree"},
		{"GetStdHandle", "kernel32.dll!GetStdHandle"},
		{"WriteFile", "kernel32.dll!WriteFile"},
	};

	// Patch import call relocations in code buffer
	// For each import call, we need to compute the RIP-relative offset to the IAT entry
	// This will be patched after the PE layout is finalized
	// For now, store the relocation info for the PE writer to handle

	// Add code section (with potentially modified code)
	uint32_t textSection = pe.addTextSection(code);

	// Add data section if non-empty
	if (!data.empty()) {
		pe.addDataSection(data);
	}

	// Find _start function and set as entry point
	auto startIt = funcOffsets.find("_start");
	if (startIt != funcOffsets.end()) {
		// Entry point is RVA = section base (0x1000) + offset within code
		uint32_t entryRva = 0x1000 + static_cast<uint32_t>(startIt->second);
		pe.setEntryPoint(entryRva);
	} else {
		throw std::runtime_error("Entry point _start not found");
	}

	// Register import relocations with PE writer
	for (const auto &[offset, symbolName] : importRelocs) {
		auto mappingIt = importMapping.find(symbolName);
		if (mappingIt != importMapping.end()) {
			// Parse DLL!func format
			size_t bangPos = mappingIt->second.find('!');
			std::string dllName = mappingIt->second.substr(0, bangPos);
			std::string funcName = mappingIt->second.substr(bangPos + 1);

			// Add import relocation - PE writer will patch this
			pe.addImportRelocation(static_cast<uint32_t>(offset), dllName, funcName);
		}
	}

	// Register data section relocations with PE writer (for float constants, etc.)
	for (const auto &[codeOffset, dataOffset] : dataRelocs) {
		pe.addDataRelocation(static_cast<uint32_t>(codeOffset), static_cast<uint32_t>(dataOffset));
	}

	// Add symbols for debugging
	for (const auto &[name, offset] : funcOffsets) {
		// Estimate function size (until next function or end of section)
		uint32_t size = 0;
		size_t nextOffset = code.size();
		for (const auto &[otherName, otherOffset] : funcOffsets) {
			if (otherOffset > offset && otherOffset < nextOffset) {
				nextOffset = otherOffset;
			}
		}
		size = static_cast<uint32_t>(nextOffset - offset);

		pe.addSymbol(name, static_cast<uint32_t>(offset), size, true, textSection);
	}

	// Write the PE file
	if (!pe.write(exeFile)) {
		throw std::runtime_error("Failed to write PE executable");
	}

	if (verboseLevel >= 1) {
		std::cout << "Executable written: " << exeFile << std::endl;
	}
}
#endif

#ifndef _WIN32
void MIRCodeGenerator::writeLinuxExecutable(
	const std::string &exeFile,
	const std::vector<uint8_t> &code,
	const std::vector<uint8_t> &data,
	const std::unordered_map<std::string, size_t> &funcOffsets) {

	backend::ElfWriter elf;

	// Add code section
	uint32_t textSection = elf.addTextSection(code);

	// Add data section if non-empty
	if (!data.empty()) {
		elf.addDataSection(data);
	}

	// Find _start function and set as entry point
	auto startIt = funcOffsets.find("_start");
	if (startIt != funcOffsets.end()) {
		// ElfWriter BASE_ADDR is 0x400000
		// Entry point = BASE_ADDR + offset in .text (after ELF header)
		// The actual calculation depends on ElfWriter's layout
		elf.setEntryPoint(0x400000 + startIt->second);
	} else {
		throw std::runtime_error("Entry point _start not found");
	}

	// Add function symbols
	for (const auto &[name, offset] : funcOffsets) {
		size_t nextOffset = code.size();
		for (const auto &[otherName, otherOffset] : funcOffsets) {
			if (otherOffset > offset && otherOffset < nextOffset) {
				nextOffset = otherOffset;
			}
		}
		uint64_t size = nextOffset - offset;
		elf.addFunction(name, offset, size);
	}

	// Write the ELF file
	if (!elf.write(exeFile)) {
		throw std::runtime_error("Failed to write ELF executable");
	}

	if (verboseLevel >= 1) {
		std::cout << "Executable written: " << exeFile << std::endl;
	}
}
#endif
