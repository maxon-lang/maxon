/**
 * MIR Code Generator - Output Generation
 *
 * Handles IR output, assembly generation, and executable creation.
 */

#include "../codegen_ng.h"
#include "../compiler_stats.h"
#include "../file_utils.h"
#include "../mir/mir_parser.h"
#include "../mir/optimizer.h"

#include "../backend/regalloc.h"
#include "../backend/x86_codegen.h"
#include "../backend/x86_disassembler.h"

#include <fstream>
#include <iostream>

#ifdef _WIN32
#include "../backend/pe_writer.h"
#else
#include "../backend/elf_writer.h"
#endif

//==============================================================================
// IR Output
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

void MIRCodeGenerator::writeAsmToFile(const std::string &filename) {
	logProgress("Generating assembly file: " + filename);

	// Run PHI elimination (required before x86 codegen)
	mir::PhiEliminationPass phiElim;
	phiElim.run(*module);

	// Determine calling convention based on target
	bool isWindows = (module->targetTriple.find("windows") != std::string::npos);
	backend::CallingConv cc = isWindows ? backend::CallingConv::Win64 : backend::CallingConv::SysV64;

	// Generate x86-64 code
	backend::X86CodeGen x86gen(cc);
	x86gen.generate(module.get());

	// Disassemble to Intel syntax assembly
	backend::X86Disassembler disasm;
	std::string assembly = disasm.disassembleWithSymbols(x86gen.getFunctionCodes());

	// Write to file
	std::ofstream file(filename);
	if (!file.is_open()) {
		throw std::runtime_error("Failed to open file for writing: " + filename);
	}
	file << assembly;
	file.close();

	logProgress("Assembly file written");
}

void MIRCodeGenerator::writeObjectFile(const std::string &filename) {
	(void)filename;
	throw std::runtime_error("Object file generation not yet implemented - use writeExecutable instead");
}

//==============================================================================
// Runtime Library Location
//==============================================================================

static std::string findRuntimeFile(const std::string &filename) {
	std::string exeDir = getExecutableDirectory();
	std::string path = exeDir + "/" + filename;

	std::ifstream f(path);
	if (f.good()) {
		return path;
	}

	path = exeDir + "/../maxon-runtime/" + filename;
	f = std::ifstream(path);
	if (f.good()) {
		return path;
	}

	path = filename;
	f = std::ifstream(path);
	if (f.good()) {
		return path;
	}

	return "";
}

//==============================================================================
// Executable Generation
//==============================================================================

void MIRCodeGenerator::writeExecutable(const std::string &exeFile) {
	if (verboseLevel >= 1) {
		std::cout << "Generating executable: " << exeFile << std::endl;
	}

	bool isWindows = (module->targetTriple.find("windows") != std::string::npos);

	// Helper lambda to merge a runtime module into the current module
	auto mergeRuntime = [this](const std::string &runtimeMirPath) {
		auto runtimeResult = mir::MIRParser::parseFile(runtimeMirPath);
		if (!runtimeResult.errors.empty()) {
			std::cerr << "Warning: Failed to parse runtime library at line " << runtimeResult.errors[0].line
					  << ": " << runtimeResult.errors[0].message << std::endl;
			return;
		}
		if (!runtimeResult.module) {
			if (verboseLevel >= 1) {
				std::cerr << "Warning: Runtime module is null after parsing: " << runtimeMirPath << std::endl;
			}
			return;
		}

		// Merge globals
		for (auto &global : runtimeResult.module->globals) {
			bool found = false;
			for (const auto &existing : module->globals) {
				if (existing->name == global->name) {
					found = true;
					break;
				}
			}
			if (!found) {
				module->globals.push_back(std::move(global));
			}
		}

		// First pass: Add external declarations
		for (auto &func : runtimeResult.module->functions) {
			if (func->isExternal) {
				if (!module->getFunction(func->name)) {
					module->functions.push_back(std::move(func));
				}
			}
		}

		// Second pass: Replace declarations with definitions
		for (auto &func : runtimeResult.module->functions) {
			if (!func)
				continue;
			if (func->isExternal)
				continue;

			mir::MIRFunction *existing = module->getFunction(func->name);
			if (existing && existing->isExternal) {
				for (auto it = module->functions.begin(); it != module->functions.end(); ++it) {
					if ((*it)->name == func->name) {
						*it = std::move(func);
						break;
					}
				}
			} else if (!existing) {
				module->functions.push_back(std::move(func));
			}
		}
	};

	// Load and merge runtime library
	std::string platformRuntimePath = isWindows ? "runtime_windows.mir" : "runtime_linux.mir";
	std::string platformMirPath = findRuntimeFile(platformRuntimePath);

	if (!platformMirPath.empty()) {
		if (verboseLevel >= 2) {
			std::cout << "[MIR] Loading runtime: " << platformMirPath << std::endl;
		}
		mergeRuntime(platformMirPath);
	} else if (verboseLevel >= 1) {
		std::cerr << "Warning: Runtime not found: " << platformRuntimePath << std::endl;
	}

	// Update calleeFunc pointers in all call instructions
	for (auto &func : module->functions) {
		for (auto &block : func->basicBlocks) {
			for (auto &inst : block->instructions) {
				if (inst->opcode == mir::MIROpcode::Call && !inst->calleeName.empty()) {
					inst->calleeFunc = module->getFunction(inst->calleeName);
				}
			}
		}
	}

	// Run dead code elimination after merging runtime
	mir::DeadCodeEliminationPass dce;
	dce.setVerboseLevel(verboseLevel);
	dce.run(*module);

	// Run PHI elimination
	mir::PhiEliminationPass phiElim;
	phiElim.setVerboseLevel(verboseLevel);
	phiElim.run(*module);

	// Generate x86-64 code
	backend::CallingConv cc = isWindows ? backend::CallingConv::Win64 : backend::CallingConv::SysV64;
	backend::X86CodeGen x86gen(cc);
	x86gen.generate(module.get());

	const auto &functionCodes = x86gen.getFunctionCodes();
	const auto &dataSection = x86gen.getDataSection();
	const auto &globalOffsets = x86gen.getGlobalOffsets();

	// Collect all code into single buffer
	std::vector<uint8_t> codeBuffer;
	std::unordered_map<std::string, size_t> functionOffsets;

	struct AdjustedRelocation {
		backend::Relocation reloc;
		size_t funcBaseOffset;
		std::string funcName;
	};
	std::vector<AdjustedRelocation> allRelocations;

	for (const auto &func : functionCodes) {
		size_t funcOffset = codeBuffer.size();
		functionOffsets[func.name] = funcOffset;
		codeBuffer.insert(codeBuffer.end(), func.code.begin(), func.code.end());

		for (const auto &reloc : func.relocations) {
			allRelocations.push_back({reloc, funcOffset, func.name});
		}
	}

	// Apply relocations
	std::vector<std::pair<size_t, std::string>> importRelocations;
	std::vector<std::pair<size_t, size_t>> dataRelocations;

	for (const auto &adj : allRelocations) {
		const auto &reloc = adj.reloc;

		if (reloc.type == backend::Relocation::Type::FunctionCall) {
			auto targetIt = functionOffsets.find(reloc.symbolName);
			if (targetIt != functionOffsets.end()) {
				size_t patchOffset = adj.funcBaseOffset + reloc.offset;
				size_t callEnd = patchOffset + 4;
				int32_t rel = static_cast<int32_t>(targetIt->second - callEnd);

				codeBuffer[patchOffset + 0] = static_cast<uint8_t>(rel & 0xFF);
				codeBuffer[patchOffset + 1] = static_cast<uint8_t>((rel >> 8) & 0xFF);
				codeBuffer[patchOffset + 2] = static_cast<uint8_t>((rel >> 16) & 0xFF);
				codeBuffer[patchOffset + 3] = static_cast<uint8_t>((rel >> 24) & 0xFF);
			}
		} else if (reloc.type == backend::Relocation::Type::ImportCall) {
			size_t patchOffset = adj.funcBaseOffset + reloc.offset;
			importRelocations.push_back({patchOffset, reloc.symbolName});
		} else if (reloc.type == backend::Relocation::Type::FunctionAddress) {
			auto targetIt = functionOffsets.find(reloc.symbolName);
			if (targetIt != functionOffsets.end()) {
				size_t patchOffset = adj.funcBaseOffset + reloc.offset;
				size_t instrEnd = patchOffset + 4;
				int32_t rel = static_cast<int32_t>(targetIt->second - instrEnd);

				codeBuffer[patchOffset + 0] = static_cast<uint8_t>(rel & 0xFF);
				codeBuffer[patchOffset + 1] = static_cast<uint8_t>((rel >> 8) & 0xFF);
				codeBuffer[patchOffset + 2] = static_cast<uint8_t>((rel >> 16) & 0xFF);
				codeBuffer[patchOffset + 3] = static_cast<uint8_t>((rel >> 24) & 0xFF);
			}
		} else if (reloc.type == backend::Relocation::Type::GlobalRef) {
			size_t patchOffset = adj.funcBaseOffset + reloc.offset;
			size_t dataOffset = 0;

			if (reloc.symbolName.substr(0, 6) == ".const") {
				dataOffset = std::stoull(reloc.symbolName.substr(6));
			} else {
				auto it = globalOffsets.find(reloc.symbolName);
				if (it != globalOffsets.end()) {
					dataOffset = it->second;
				}
			}
			dataRelocations.push_back({patchOffset, dataOffset});
		}
	}

#ifdef _WIN32
	writeWindowsExecutable(exeFile, codeBuffer, dataSection, functionOffsets, importRelocations, dataRelocations);
#else
	writeLinuxExecutable(exeFile, codeBuffer, dataSection, functionOffsets);
#endif
}

//==============================================================================
// Windows Import Table
//==============================================================================

static const std::pair<const char *, const char *> WINDOWS_IMPORTS[] = {
	{"kernel32.dll", "ExitProcess"},
	{"kernel32.dll", "GetProcessHeap"},
	{"kernel32.dll", "HeapAlloc"},
	{"kernel32.dll", "HeapFree"},
	{"kernel32.dll", "HeapReAlloc"},
	{"kernel32.dll", "GetStdHandle"},
	{"kernel32.dll", "CreateFileA"},
	{"kernel32.dll", "ReadFile"},
	{"kernel32.dll", "WriteFile"},
	{"kernel32.dll", "GetFileSize"},
	{"kernel32.dll", "CloseHandle"},
	{"kernel32.dll", "CreateProcessA"},
	{"kernel32.dll", "TerminateProcess"},
	{"kernel32.dll", "GetExitCodeProcess"},
	{"kernel32.dll", "WaitForSingleObject"},
	{"kernel32.dll", "WaitForMultipleObjects"},
	{"kernel32.dll", "GetCurrentProcessId"},
	{"kernel32.dll", "ResumeThread"},
	{"kernel32.dll", "CreateJobObjectA"},
	{"kernel32.dll", "SetInformationJobObject"},
	{"kernel32.dll", "AssignProcessToJobObject"},
	{"kernel32.dll", "CreateFileMappingA"},
	{"kernel32.dll", "OpenFileMappingA"},
	{"kernel32.dll", "MapViewOfFile"},
	{"kernel32.dll", "UnmapViewOfFile"},
	{"kernel32.dll", "CreateSemaphoreA"},
	{"kernel32.dll", "OpenSemaphoreA"},
	{"kernel32.dll", "ReleaseSemaphore"},
	{"kernel32.dll", "GetLastError"},
	{"kernel32.dll", "SetErrorMode"},
	{"kernel32.dll", "GetCommandLineA"},
	{"kernel32.dll", "GetCommandLineW"},
	{"kernel32.dll", "GetEnvironmentVariableA"},
	{"kernel32.dll", "SetEnvironmentVariableA"},
	{"kernel32.dll", "WideCharToMultiByte"},
	{"kernel32.dll", "LocalFree"},
	{"shell32.dll", "CommandLineToArgvW"},
	{"kernel32.dll", "LoadLibraryA"},
	{"kernel32.dll", "GetProcAddress"},
	{"kernel32.dll", "FreeLibrary"},
	{"kernel32.dll", "FindFirstFileA"},
	{"kernel32.dll", "FindNextFileA"},
	{"kernel32.dll", "FindClose"},
	{"kernel32.dll", "GetFileAttributesA"},
};

#ifdef _WIN32
void MIRCodeGenerator::writeWindowsExecutable(
	const std::string &exeFile,
	std::vector<uint8_t> &code,
	const std::vector<uint8_t> &data,
	const std::unordered_map<std::string, size_t> &funcOffsets,
	const std::vector<std::pair<size_t, std::string>> &importRelocs,
	const std::vector<std::pair<size_t, size_t>> &dataRelocs) {

	backend::PeWriter pe;

	std::unordered_map<std::string, std::string> importMapping;
	for (const auto &[dll, func] : WINDOWS_IMPORTS) {
		pe.addImport(dll, func);
		importMapping[func] = std::string(dll) + "!" + func;
	}

	uint32_t textSection = pe.addTextSection(code);
	if (!data.empty()) {
		pe.addDataSection(data);
	}

	auto startIt = funcOffsets.find("_start");
	if (startIt == funcOffsets.end()) {
		throw std::runtime_error("Entry point _start not found");
	}
	pe.setEntryPoint(0x1000 + static_cast<uint32_t>(startIt->second));

	for (const auto &[offset, symbolName] : importRelocs) {
		auto mappingIt = importMapping.find(symbolName);
		if (mappingIt == importMapping.end()) {
			throw std::runtime_error("Import symbol '" + symbolName + "' not found");
		}

		size_t bangPos = mappingIt->second.find('!');
		pe.addImportRelocation(
			static_cast<uint32_t>(offset),
			mappingIt->second.substr(0, bangPos),
			mappingIt->second.substr(bangPos + 1));
	}

	for (const auto &[codeOffset, dataOffset] : dataRelocs) {
		pe.addDataRelocation(static_cast<uint32_t>(codeOffset), static_cast<uint32_t>(dataOffset));
	}

	for (const auto &[name, offset] : funcOffsets) {
		size_t nextOffset = code.size();
		for (const auto &[otherName, otherOffset] : funcOffsets) {
			if (otherOffset > offset && otherOffset < nextOffset) {
				nextOffset = otherOffset;
			}
		}
		pe.addSymbol(name, static_cast<uint32_t>(offset),
					 static_cast<uint32_t>(nextOffset - offset), true, textSection);
	}

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

	uint32_t textSection = elf.addTextSection(code);
	(void)textSection;

	if (!data.empty()) {
		elf.addDataSection(data);
	}

	auto startIt = funcOffsets.find("_start");
	if (startIt != funcOffsets.end()) {
		elf.setEntryPoint(0x400000 + startIt->second);
	} else {
		throw std::runtime_error("Entry point _start not found");
	}

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

	if (!elf.write(exeFile)) {
		throw std::runtime_error("Failed to write ELF executable");
	}

	if (verboseLevel >= 1) {
		std::cout << "Executable written: " << exeFile << std::endl;
	}
}
#endif
