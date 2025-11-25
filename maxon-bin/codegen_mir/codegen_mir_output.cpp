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
	// Create standard optimization pipeline
	mir::MIROptimizer optimizer = mir::MIROptimizer::createStandardPipeline();

	// Run passes until convergence
	optimizer.runPasses(*module);

	if (verboseLevel >= 1) {
		std::cout << "Optimization complete" << std::endl;
	}
}

void MIRCodeGenerator::runDeadCodeElimination() {
	mir::DeadCodeEliminationPass dce;
	dce.run(*module);
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

	// Collect all code into single buffer
	std::vector<uint8_t> codeBuffer;
	std::unordered_map<std::string, size_t> functionOffsets;

	for (const auto &func : functionCodes) {
		functionOffsets[func.name] = codeBuffer.size();
		codeBuffer.insert(codeBuffer.end(), func.code.begin(), func.code.end());
	}

	if (verboseLevel >= 2) {
		std::cout << "  Total code size: " << codeBuffer.size() << " bytes" << std::endl;
	}

	// Step 2: Write executable using PE or ELF writer
#ifdef _WIN32
	writeWindowsExecutable(exeFile, codeBuffer, dataSection, functionOffsets);
#else
	writeLinuxExecutable(exeFile, codeBuffer, dataSection, functionOffsets);
#endif
}

#ifdef _WIN32
void MIRCodeGenerator::writeWindowsExecutable(
	const std::string &exeFile,
	const std::vector<uint8_t> &code,
	const std::vector<uint8_t> &data,
	const std::unordered_map<std::string, size_t> &funcOffsets) {

	backend::PeWriter pe;

	// Add code section
	uint32_t textSection = pe.addTextSection(code);

	// Add data section if non-empty
	if (!data.empty()) {
		pe.addDataSection(data);
	}

	// Find _start function and set as entry point
	auto startIt = funcOffsets.find("_start");
	if (startIt != funcOffsets.end()) {
		// Entry point is RVA = section base + offset
		// PeWriter handles the section base internally
		pe.setEntryPoint(static_cast<uint32_t>(startIt->second));
	} else {
		throw std::runtime_error("Entry point _start not found");
	}

	// Add imports for Windows runtime functions
	pe.addImport("kernel32.dll", "ExitProcess");
	pe.addImport("kernel32.dll", "GetProcessHeap");
	pe.addImport("kernel32.dll", "HeapAlloc");
	pe.addImport("kernel32.dll", "HeapFree");
	pe.addImport("kernel32.dll", "GetStdHandle");
	pe.addImport("kernel32.dll", "WriteFile");

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
