#include "../codegen.h"
#include <iostream>
#include <lld/Common/CommonLinkerContext.h>
#include <lld/Common/Driver.h>
#include <llvm/IR/LegacyPassManager.h>
#include <llvm/Linker/Linker.h>
#include <llvm/MC/TargetRegistry.h>
#include <llvm/Passes/PassBuilder.h>
#include <llvm/Support/FileSystem.h>
#include <llvm/Support/TargetSelect.h>
#include <llvm/Support/raw_ostream.h>
#include <llvm/Target/TargetMachine.h>
#include <llvm/Target/TargetOptions.h>
#include <llvm/Transforms/IPO/GlobalDCE.h>
#include <llvm/Transforms/InstCombine/InstCombine.h>
#include <llvm/Transforms/Scalar.h>
#include <llvm/Transforms/Scalar/GVN.h>
#include <llvm/Transforms/Utils.h>

#ifdef _WIN32
#include <windows.h>
#else
#include <unistd.h>
#include <linux/limits.h>
#endif

#ifdef _WIN32
// Forward declare COFF driver function (Windows)
namespace lld {
namespace coff {
bool link(llvm::ArrayRef<const char *> args, llvm::raw_ostream &stdoutOS,
		  llvm::raw_ostream &stderrOS, bool exitEarly, bool disableOutput);
}
} // namespace lld
#else
// Forward declare ELF driver function (Linux)
namespace lld {
namespace elf {
bool link(llvm::ArrayRef<const char *> args, llvm::raw_ostream &stdoutOS,
		  llvm::raw_ostream &stderrOS, bool exitEarly, bool disableOutput);
}
} // namespace lld
#endif

void CodeGenerator::optimize() {
	// Use LLVM's PassBuilder to run an O3-style pipeline over the module.
	llvm::LoopAnalysisManager loopAM;
	llvm::FunctionAnalysisManager funcAM;
	llvm::CGSCCAnalysisManager cgsccAM;
	llvm::ModuleAnalysisManager moduleAM;

	llvm::PassBuilder pb;

	pb.registerModuleAnalyses(moduleAM);
	pb.registerCGSCCAnalyses(cgsccAM);
	pb.registerFunctionAnalyses(funcAM);
	pb.registerLoopAnalyses(loopAM);
	pb.crossRegisterProxies(loopAM, funcAM, cgsccAM, moduleAM);

	llvm::ModulePassManager mpm = pb.buildPerModuleDefaultPipeline(llvm::OptimizationLevel::O3);
	mpm.run(*module, moduleAM);
}

void CodeGenerator::runDeadCodeElimination() {
	// Run minimal dead code elimination to remove unused internal functions
	// This ensures the linker's /OPT:REF can work effectively
	llvm::LoopAnalysisManager loopAM;
	llvm::FunctionAnalysisManager funcAM;
	llvm::CGSCCAnalysisManager cgsccAM;
	llvm::ModuleAnalysisManager moduleAM;

	llvm::PassBuilder pb;

	pb.registerModuleAnalyses(moduleAM);
	pb.registerCGSCCAnalyses(cgsccAM);
	pb.registerFunctionAnalyses(funcAM);
	pb.registerLoopAnalyses(loopAM);
	pb.crossRegisterProxies(loopAM, funcAM, cgsccAM, moduleAM);

	// Run only GlobalDCE pass to remove unused internal functions
	llvm::ModulePassManager mpm;
	mpm.addPass(llvm::GlobalDCEPass());
	mpm.run(*module, moduleAM);
}

void CodeGenerator::printIR() {
	module->print(llvm::outs(), nullptr);
}

void CodeGenerator::writeIRToFile(const std::string &filename) {
	std::error_code EC;
	llvm::raw_fd_ostream dest(filename, EC);

	if (EC) {
		throw std::runtime_error("Could not open file: " + EC.message());
	}

	module->print(dest, nullptr);
	dest.flush();
}

void CodeGenerator::writeObjectFile(const std::string &filename) {
	// Get target triple based on platform
#ifdef _WIN32
	llvm::Triple targetTriple("x86_64-pc-windows-msvc");
#else
	llvm::Triple targetTriple("x86_64-pc-linux-gnu");
#endif
	module->setTargetTriple(targetTriple);

	// Get target
	std::string error;
	auto target = llvm::TargetRegistry::lookupTarget(targetTriple.str(), error);

	if (!target) {
		throw std::runtime_error("Failed to lookup target: " + error);
	}

	// Configure target machine
	auto CPU = "generic";
	auto features = "";
	llvm::TargetOptions opt;
	auto targetMachine = target->createTargetMachine(
		targetTriple, CPU, features, opt, llvm::Reloc::PIC_, std::nullopt, llvm::CodeGenOptLevel::Aggressive);

	module->setDataLayout(targetMachine->createDataLayout());

	// Open output file
	std::error_code ec;
	llvm::raw_fd_ostream dest(filename, ec, llvm::sys::fs::OF_None);

	if (ec) {
		throw std::runtime_error("Could not open file: " + ec.message());
	}

	// Emit object file
	llvm::legacy::PassManager pass;
	auto fileType = llvm::CodeGenFileType::ObjectFile;

	if (targetMachine->addPassesToEmitFile(pass, dest, nullptr, fileType)) {
		throw std::runtime_error("TargetMachine can't emit a file of this type");
	}

	pass.run(*module);
	dest.flush();

	if (verboseLevel >= 2) {
		uint64_t fileSize = 0;
		llvm::sys::fs::file_size(filename, fileSize);
		std::cout << "Object file size: " << fileSize << " bytes" << std::endl;
	}
}

void CodeGenerator::writeExecutable(const std::string &exeFile, llvm::raw_ostream *errorStream) {
	// Get target triple based on platform
#ifdef _WIN32
	llvm::Triple targetTriple("x86_64-pc-windows-msvc");
#else
	llvm::Triple targetTriple("x86_64-pc-linux-gnu");
#endif
	module->setTargetTriple(targetTriple);

	// Get target
	std::string error;
	auto target = llvm::TargetRegistry::lookupTarget(targetTriple.str(), error);

	if (!target) {
		throw std::runtime_error("Failed to lookup target: " + error);
	}

	// Configure target machine
	auto CPU = "generic";
	auto features = "";
	llvm::TargetOptions opt;
	auto targetMachine = target->createTargetMachine(
		targetTriple, CPU, features, opt, llvm::Reloc::PIC_, std::nullopt, llvm::CodeGenOptLevel::Aggressive);

	module->setDataLayout(targetMachine->createDataLayout());

	// First, write to a temporary object file
#ifdef _WIN32
	std::string tempObjFile = exeFile + ".tmp.obj";
#else
	std::string tempObjFile = exeFile + ".tmp.o";
#endif
	std::error_code ec;
	llvm::raw_fd_ostream dest(tempObjFile, ec, llvm::sys::fs::OF_None);

	if (ec) {
		throw std::runtime_error("Could not create temporary object file: " + ec.message());
	}

	// Emit object file
	llvm::legacy::PassManager pass;
	auto fileType = llvm::CodeGenFileType::ObjectFile;

	if (targetMachine->addPassesToEmitFile(pass, dest, nullptr, fileType)) {
		throw std::runtime_error("TargetMachine can't emit a file of this type");
	}

	pass.run(*module);
	dest.flush();
	dest.close();

	// Use LLD as a library (in-process linking)
	if (verboseLevel >= 1) {
		std::cout << "Linking" << std::endl;
	}

	// Prepare arguments for LLD driver (platform-specific)
	std::vector<std::string> argStorage; // Store strings so pointers remain valid
	std::vector<const char *> lldArgs;

#ifdef _WIN32
	// Windows COFF linker arguments
	argStorage.push_back("lld-link"); // Program name
	argStorage.push_back("/NOLOGO");
	argStorage.push_back("/MACHINE:X64");
	argStorage.push_back("/SUBSYSTEM:CONSOLE");

	// Add debug info if enabled
	if (generateDebugInfo) {
		argStorage.push_back("/DEBUG");
	}

	// Add library path for Windows SDK (needed for kernel32.lib)
	argStorage.push_back("/LIBPATH:C:\\Program Files (x86)\\Windows Kits\\10\\Lib\\10.0.22621.0\\um\\x64");
	argStorage.push_back("/DEFAULTLIB:kernel32.lib");
	argStorage.push_back("/NODEFAULTLIB"); // Don't link default CRT libraries
	argStorage.push_back("/ENTRY:_start"); // Set entry point to _start (our minimal wrapper)

	// Size optimization flags (only when not generating debug info)
	if (!generateDebugInfo) {
		argStorage.push_back("/OPT:REF");			 // Remove unreferenced functions/data
		argStorage.push_back("/OPT:ICF");			 // Identical COMDAT folding
		argStorage.push_back("/MERGE:.rdata=.text"); // Merge read-only data into code section
	}

	// Output file
	argStorage.push_back("/OUT:" + exeFile);

	// Input object file (program)
	argStorage.push_back(tempObjFile);
#else
	// Linux ELF linker arguments
	argStorage.push_back("ld.lld"); // Program name
	argStorage.push_back("-o");
	argStorage.push_back(exeFile);

	// Entry point
	argStorage.push_back("--entry=_start");

	// Dynamic linker (for system calls, but we're self-contained)
	argStorage.push_back("-dynamic-linker");
	argStorage.push_back("/lib64/ld-linux-x86-64.so.2");

	// Input object file (program)
	argStorage.push_back(tempObjFile);

	// Note: We don't link libc since Maxon is self-contained via runtime.ll
#endif

	// Convert to const char* pointers
	for (const auto &arg : argStorage) {
		lldArgs.push_back(arg.c_str());
	}

	// Add Maxon runtime library
	// Find runtime object file in the same directory as the executable
	std::string execPath;
#ifdef _WIN32
	char buffer[MAX_PATH];
	GetModuleFileNameA(NULL, buffer, MAX_PATH);
	execPath = buffer;
	std::string runtimeExt = ".obj";
#else
	char buffer[PATH_MAX];
	ssize_t len = readlink("/proc/self/exe", buffer, sizeof(buffer) - 1);
	if (len != -1) {
		buffer[len] = '\0';
		execPath = buffer;
	} else {
		execPath = ".";
	}
	std::string runtimeExt = ".o";
#endif
	size_t lastSlash = execPath.find_last_of("\\/");
	std::string execDir = (lastSlash != std::string::npos) ? execPath.substr(0, lastSlash) : ".";
	std::string runtimeObj = execDir + "/runtime" + runtimeExt;

	// Check if runtime object exists
	if (llvm::sys::fs::exists(runtimeObj)) {
		argStorage.push_back(runtimeObj);
		if (verboseLevel >= 2) {
			std::cout << "  Linking with Maxon runtime library: " << runtimeObj << std::endl;
		}
	} else if (verboseLevel >= 2) {
		std::cout << "  Warning: Maxon runtime library not found at " << runtimeObj << std::endl;
	}

#ifdef _WIN32
	// Explicitly link required Windows libraries
	argStorage.push_back("kernel32.lib");
	argStorage.push_back("shell32.lib"); // For CommandLineToArgvW
#endif

	// Rebuild lldArgs with final pointers
	lldArgs.clear();
	for (const auto &arg : argStorage) {
		lldArgs.push_back(arg.c_str());
	}

	// Call LLD driver directly (in-process) - platform-specific
	llvm::raw_ostream &errStream = errorStream ? *errorStream : llvm::errs();
#ifdef _WIN32
	bool success = lld::coff::link(lldArgs, llvm::outs(), errStream, false, false);
#else
	bool success = lld::elf::link(lldArgs, llvm::outs(), errStream, false, false);
#endif

	// Clean up temporary object file
	llvm::sys::fs::remove(tempObjFile);

	if (!success) {
		throw std::runtime_error("LLD linking failed");
	}
}
