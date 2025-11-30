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
#include "../compiler_stats.h"
#include "../file_utils.h"
#include "../mir/mir_parser.h"
#include "../mir/optimizer.h"
#include <fstream>
#include <iostream>

#ifdef _WIN32
#include "../backend/coff_lib_reader.h"
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

void MIRCodeGenerator::optimize(CompilerStats *stats) {
	logProgress("Running optimization passes...");

	// Create standard optimization pipeline with verbosity
	mir::MIROptimizer optimizer = mir::MIROptimizer::createStandardPipeline(verboseLevel);

	// Run passes until convergence, collecting stats if requested
	optimizer.runPasses(*module, 10, stats);

	logProgress("Optimization complete");
}

void MIRCodeGenerator::runDeadCodeElimination() {
	logDetail("Running dead code elimination");
	mir::DeadCodeEliminationPass dce;
	dce.setVerboseLevel(verboseLevel);
	dce.run(*module);
}

size_t MIRCodeGenerator::getInstructionCount() const {
	size_t count = 0;
	for (const auto &func : module->functions) {
		if (!func->isExternal) {
			for (const auto &block : func->basicBlocks) {
				count += block->instructions.size();
			}
		}
	}
	return count;
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
		if (verboseLevel >= 2) {
			std::cout << "[MIR]   Parsed " << runtimeResult.module->functions.size() << " functions, "
					  << runtimeResult.module->globals.size() << " globals from runtime" << std::endl;
		}

		// Merge globals from runtime that we don't have
		for (auto &global : runtimeResult.module->globals) {
			bool found = false;
			for (const auto &existing : module->globals) {
				if (existing->name == global->name) {
					found = true;
					break;
				}
			}
			if (!found) {
				if (verboseLevel >= 2) {
					std::cout << "[MIR]     Adding runtime global '" << global->name << "'" << std::endl;
				}
				module->globals.push_back(std::move(global));
			}
		}

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
			if (verboseLevel >= 2) {
				std::cout << "[MIR]   Processing runtime function '" << func->name
						  << "' (existing=" << (existing ? "yes" : "no")
						  << ", isExternal=" << (existing && existing->isExternal ? "yes" : "no") << ")" << std::endl;
			}
			if (existing && existing->isExternal) {
				if (verboseLevel >= 2) {
					std::cout << "[MIR]     Replacing declaration '" << func->name << "' with runtime definition" << std::endl;
				}
				// Replace the declaration with the definition
				for (auto it = module->functions.begin(); it != module->functions.end(); ++it) {
					if ((*it)->name == func->name) {
						*it = std::move(func);
						break;
					}
				}
			} else if (!existing) {
				if (verboseLevel >= 3) {
					std::cout << "[MIR]     Adding new runtime function '" << func->name << "'" << std::endl;
				}
				// New function not in user module
				module->functions.push_back(std::move(func));
			}
			// If existing && !existing->isExternal, keep the user's definition
		}
	};

	// Step 0: Load and merge runtime library
	// Load the combined platform-specific runtime (includes both platform and common functions)
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
	// This is needed because we replaced some functions
	for (auto &func : module->functions) {
		for (auto &block : func->basicBlocks) {
			for (auto &inst : block->instructions) {
				if (inst->opcode == mir::MIROpcode::Call && !inst->calleeName.empty()) {
					// Re-lookup the function by name
					inst->calleeFunc = module->getFunction(inst->calleeName);
					if (verboseLevel >= 3 && inst->calleeFunc) {
						std::cout << "[MIR]   Call to " << inst->calleeName
								  << " -> isExternal=" << inst->calleeFunc->isExternal << std::endl;
					}
				}
			}
		}
	}

	// Run dead code elimination again after merging runtime library
	// This eliminates unused runtime functions
	if (verboseLevel >= 2) {
		std::cout << "  Running dead code elimination on merged module..." << std::endl;
	}
	mir::DeadCodeEliminationPass dce;
	dce.setVerboseLevel(verboseLevel);
	dce.run(*module);

	// Step 1: Run PHI elimination pass to convert SSA form to machine-ready form
	// This must happen after runtime merging but before x86 code generation
	if (verboseLevel >= 2) {
		std::cout << "  Running PHI elimination pass..." << std::endl;
	}
	mir::PhiEliminationPass phiElim;
	phiElim.run(*module);

	// Step 2: Generate x86-64 code from MIR
	backend::CallingConv cc = isWindows ? backend::CallingConv::Win64 : backend::CallingConv::SysV64;
	backend::X86CodeGen x86gen(cc);

	if (verboseLevel >= 2) {
		std::cout << "  Generating x86-64 code..." << std::endl;
	}

	x86gen.generate(module.get());

	const auto &functionCodes = x86gen.getFunctionCodes();
	const auto &dataSection = x86gen.getDataSection();
	const auto &globalOffsets = x86gen.getGlobalOffsets();

	if (verboseLevel >= 2) {
		std::cout << "  Generated " << functionCodes.size() << " functions" << std::endl;
		std::cout << "  Data section size: " << dataSection.size() << " bytes" << std::endl;
		std::cout << "  Global offsets: " << globalOffsets.size() << " entries" << std::endl;
		for (const auto &kv : globalOffsets) {
			std::cout << "    " << kv.first << " -> " << kv.second << std::endl;
		}
	}

	// Collect all code into single buffer and track function positions
	std::vector<uint8_t> codeBuffer;
	std::unordered_map<std::string, size_t> functionOffsets;

	// Collect all relocations with their adjusted offsets
	struct AdjustedRelocation {
		backend::Relocation reloc;
		size_t funcBaseOffset; // Offset of the function containing this relocation
		std::string funcName;  // Name of function containing this relocation
	};
	std::vector<AdjustedRelocation> allRelocations;

	for (const auto &func : functionCodes) {
		size_t funcOffset = codeBuffer.size();
		functionOffsets[func.name] = funcOffset;
		if (verboseLevel >= 3) {
			std::cout << "[FUNC] " << func.name << " at offset " << funcOffset << " (size " << func.code.size() << ")" << std::endl;
		}
		codeBuffer.insert(codeBuffer.end(), func.code.begin(), func.code.end());

		// Collect relocations with adjusted offsets
		for (const auto &reloc : func.relocations) {
			allRelocations.push_back({reloc, funcOffset, func.name});
		}
	}

#ifdef _WIN32
	// Link static libraries - extract and append code for each static lib function
	if (!staticLibPaths.empty()) {
		if (verboseLevel >= 1) {
			std::cout << "  Linking " << staticLibPaths.size() << " static library(ies)..." << std::endl;
		}

		// Collect functions needed from static libraries
		std::set<std::string> neededFunctions;
		for (const auto &[funcName, info] : externFunctions) {
			if (info.isStaticLib) {
				neededFunctions.insert(info.exportName);
			}
		}

		// Process each static library
		for (const std::string &libPath : staticLibPaths) {
			if (verboseLevel >= 2) {
				std::cout << "    Loading: " << libPath << std::endl;
			}

			backend::CoffLibReader libReader;
			if (!libReader.load(libPath)) {
				throw std::runtime_error("Failed to load static library: " + libPath + " (" + libReader.getError() + ")");
			}

			// Extract each needed function from this library
			for (const std::string &funcName : neededFunctions) {
				if (libReader.hasSymbol(funcName)) {
					if (verboseLevel >= 2) {
						std::cout << "      Extracting: " << funcName << std::endl;
					}

					backend::LibFunction libFunc = libReader.extractFunction(funcName);
					if (!libFunc.found) {
						throw std::runtime_error("Failed to extract function '" + funcName + "' from " + libPath + ": " + libReader.getError());
					}

					// Add function code to our code buffer
					size_t funcOffset = codeBuffer.size();
					functionOffsets[funcName] = funcOffset;
					codeBuffer.insert(codeBuffer.end(), libFunc.code.begin(), libFunc.code.end());

					if (verboseLevel >= 2) {
						std::cout << "        Added at offset " << funcOffset << " (" << libFunc.code.size() << " bytes, " << libFunc.relocations.size() << " relocs)" << std::endl;
					}

					// Process relocations from the static lib function
					for (const auto &reloc : libFunc.relocations) {
						backend::Relocation r;
						r.offset = reloc.offset;
						r.symbolName = reloc.symbolName;

						// Convert COFF relocation type to our internal type
						if (reloc.type == backend::IMAGE_REL_AMD64_REL32 ||
							reloc.type == backend::IMAGE_REL_AMD64_REL32_1 ||
							reloc.type == backend::IMAGE_REL_AMD64_REL32_2 ||
							reloc.type == backend::IMAGE_REL_AMD64_REL32_3 ||
							reloc.type == backend::IMAGE_REL_AMD64_REL32_4 ||
							reloc.type == backend::IMAGE_REL_AMD64_REL32_5) {
							// Check if it's a call to an import or internal function
							if (functionOffsets.find(reloc.symbolName) != functionOffsets.end()) {
								r.type = backend::Relocation::Type::FunctionCall;
							} else {
								// Assume it's an import - the symbol will be resolved from Windows imports
								r.type = backend::Relocation::Type::ImportCall;
							}
						} else if (reloc.type == backend::IMAGE_REL_AMD64_ADDR32NB) {
							r.type = backend::Relocation::Type::GlobalRef;
						} else if (reloc.type == backend::IMAGE_REL_AMD64_ADDR64) {
							r.type = backend::Relocation::Type::GlobalRef;
						} else {
							if (verboseLevel >= 1) {
								std::cerr << "Warning: Unknown COFF relocation type " << reloc.type << " for " << reloc.symbolName << std::endl;
							}
							continue;
						}

						allRelocations.push_back({r, funcOffset, funcName});
					}
				}
			}
		}
	}
#endif

	// Apply relocations for internal function calls and collect import/data relocations
	std::vector<std::pair<size_t, std::string>> importRelocations; // offset, symbolName
	std::vector<std::pair<size_t, size_t>> dataRelocations;		   // codeOffset, dataOffset

	for (const auto &adj : allRelocations) {
		const auto &reloc = adj.reloc;

		if (verboseLevel >= 3) {
			std::cout << "[RELOC] " << (reloc.type == backend::Relocation::Type::FunctionCall ? "FunctionCall" : reloc.type == backend::Relocation::Type::ImportCall ? "ImportCall"
																																									 : "Other")
					  << " " << reloc.symbolName << " at offset " << (adj.funcBaseOffset + reloc.offset)
					  << " (in " << adj.funcName << ")" << std::endl;
		}

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
			// RIP-relative reference to data section (e.g., float constants or named globals)
			size_t patchOffset = adj.funcBaseOffset + reloc.offset;
			size_t dataOffset = 0;

			// First check if it's a float constant (.constN format)
			if (reloc.symbolName.substr(0, 6) == ".const") {
				dataOffset = std::stoull(reloc.symbolName.substr(6));
			} else {
				// Look up named global in globalOffsets map
				auto it = globalOffsets.find(reloc.symbolName);
				if (it != globalOffsets.end()) {
					dataOffset = it->second;
				} else if (verboseLevel >= 1) {
					std::cerr << "Warning: GlobalRef to unknown global '" << reloc.symbolName << "'" << std::endl;
				}
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

//==============================================================================
// Windows Import Table
// Maps function names to their DLLs. This table is used to populate the PE
// import directory. When adding a new external function to runtime_windows.mir,
// add it here as well.
//==============================================================================

static const std::pair<const char *, const char *> WINDOWS_IMPORTS[] = {
	// Core runtime functions
	{"kernel32.dll", "ExitProcess"},
	{"kernel32.dll", "GetProcessHeap"},
	{"kernel32.dll", "HeapAlloc"},
	{"kernel32.dll", "HeapFree"},
	{"kernel32.dll", "HeapReAlloc"},
	{"kernel32.dll", "GetStdHandle"},
	{"kernel32.dll", "WriteFile"},
	{"kernel32.dll", "CloseHandle"},

	// Safe FFI - Process management
	{"kernel32.dll", "CreateProcessA"},
	{"kernel32.dll", "TerminateProcess"},
	{"kernel32.dll", "GetExitCodeProcess"},
	{"kernel32.dll", "WaitForSingleObject"},
	{"kernel32.dll", "WaitForMultipleObjects"},
	{"kernel32.dll", "GetCurrentProcessId"},
	{"kernel32.dll", "ResumeThread"},

	// Safe FFI - Job Objects (auto-terminate worker on parent exit)
	{"kernel32.dll", "CreateJobObjectA"},
	{"kernel32.dll", "SetInformationJobObject"},
	{"kernel32.dll", "AssignProcessToJobObject"},

	// Safe FFI - Shared memory
	{"kernel32.dll", "CreateFileMappingA"},
	{"kernel32.dll", "OpenFileMappingA"},
	{"kernel32.dll", "MapViewOfFile"},
	{"kernel32.dll", "UnmapViewOfFile"},

	// Safe FFI - Semaphores
	{"kernel32.dll", "CreateSemaphoreA"},
	{"kernel32.dll", "OpenSemaphoreA"},
	{"kernel32.dll", "ReleaseSemaphore"},

	// Safe FFI - Misc
	{"kernel32.dll", "GetLastError"},
	{"kernel32.dll", "SetErrorMode"},
	{"kernel32.dll", "GetCommandLineA"},
	{"kernel32.dll", "GetEnvironmentVariableA"},
	{"kernel32.dll", "SetEnvironmentVariableA"},

	// Dynamic library loading
	{"kernel32.dll", "LoadLibraryA"},
	{"kernel32.dll", "GetProcAddress"},
	{"kernel32.dll", "FreeLibrary"},
};

#ifdef _WIN32
void MIRCodeGenerator::writeWindowsExecutable(
	const std::string &exeFile,
	std::vector<uint8_t> &code, // non-const to allow patching
	const std::vector<uint8_t> &data,
	const std::unordered_map<std::string, size_t> &funcOffsets,
	const std::vector<std::pair<size_t, std::string>> &importRelocs,
	const std::vector<std::pair<size_t, size_t>> &dataRelocs) {

	backend::PeWriter pe;

	// Build import mapping from the static table
	std::unordered_map<std::string, std::string> importMapping;
	for (const auto &[dll, func] : WINDOWS_IMPORTS) {
		pe.addImport(dll, func);
		importMapping[func] = std::string(dll) + "!" + func;
	}

	// Add code and data sections
	uint32_t textSection = pe.addTextSection(code);
	if (!data.empty()) {
		pe.addDataSection(data);
	}

	// Set entry point
	auto startIt = funcOffsets.find("_start");
	if (startIt == funcOffsets.end()) {
		throw std::runtime_error("Entry point _start not found");
	}
	pe.setEntryPoint(0x1000 + static_cast<uint32_t>(startIt->second));

	// Register import relocations
	for (const auto &[offset, symbolName] : importRelocs) {
		auto mappingIt = importMapping.find(symbolName);
		if (mappingIt == importMapping.end()) {
			throw std::runtime_error(
				"Internal compiler error: Import symbol '" + symbolName +
				"' is called but not in WINDOWS_IMPORTS table. "
				"Add it to codegen_mir_output.cpp");
		}

		size_t bangPos = mappingIt->second.find('!');
		pe.addImportRelocation(
			static_cast<uint32_t>(offset),
			mappingIt->second.substr(0, bangPos),
			mappingIt->second.substr(bangPos + 1));
	}

	// Register data relocations
	for (const auto &[codeOffset, dataOffset] : dataRelocs) {
		pe.addDataRelocation(static_cast<uint32_t>(codeOffset), static_cast<uint32_t>(dataOffset));
	}

	// Add function symbols for debugging
	for (const auto &[name, offset] : funcOffsets) {
		// Calculate function size (distance to next function or end of code)
		size_t nextOffset = code.size();
		for (const auto &[otherName, otherOffset] : funcOffsets) {
			if (otherOffset > offset && otherOffset < nextOffset) {
				nextOffset = otherOffset;
			}
		}
		pe.addSymbol(name, static_cast<uint32_t>(offset),
					 static_cast<uint32_t>(nextOffset - offset), true, textSection);
	}

	// Write PE file
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
