/**
 * End-to-end test: Generate a simple Windows executable from MIR
 *
 * This test creates a minimal program that exits with code 42,
 * generates x86-64 code, and writes it as a PE executable.
 */

#include "../../lsp-server/tests/catch_amalgamated.hpp"
#include "../backend/pe_writer.h"
#include "../backend/x86_codegen.h"
#include "../backend/x86_encoding.h"
#include "../mir/mir.h"
#include "../mir/mir_builder.h"
#include <cstdlib>
#include <fstream>

#ifdef _WIN32
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#endif

using namespace backend;
using namespace mir;

// Helper function to run an executable and get its exit code
static int runAndGetExitCode(const std::string &exePath) {
#ifdef _WIN32
	STARTUPINFOA si = {sizeof(si)};
	PROCESS_INFORMATION pi;

	std::string cmdLine = exePath;
	if (CreateProcessA(nullptr, cmdLine.data(), nullptr, nullptr, FALSE, 0,
					   nullptr, nullptr, &si, &pi)) {
		WaitForSingleObject(pi.hProcess, INFINITE);
		DWORD exitCode;
		GetExitCodeProcess(pi.hProcess, &exitCode);
		CloseHandle(pi.hProcess);
		CloseHandle(pi.hThread);
		return static_cast<int>(exitCode);
	}
	return -1;
#else
	return std::system(exePath.c_str());
#endif
}

//==============================================================================
// Test: Manual x86 assembly -> PE executable
//==============================================================================

TEST_CASE("Generate minimal PE executable (manual assembly)", "[e2e][pe]") {
	// Create a minimal Windows console program that:
	// - Calls ExitProcess(42)
	//
	// Windows x64 calling convention:
	// - First arg in RCX
	// - Shadow space of 32 bytes required
	// - Call through IAT entry

	X86Encoder encoder;

	// sub rsp, 40  ; Allocate shadow space + align stack
	encoder.subRI64(X86Reg::RSP, 40);

	// mov ecx, 42  ; Exit code in first arg register
	encoder.movRI32(X86Reg::ECX, 42);

	// We need to call ExitProcess through the Import Address Table
	// For now, emit a placeholder call that we'll fix up
	// call [rip + offset] - the offset will be filled in by PE writer

	// call qword ptr [rip + 0] ; Placeholder - needs relocation
	// FF 15 00 00 00 00
	encoder.emit8(0xFF);
	encoder.emit8(0x15);
	size_t relocOffset = encoder.getCode().size();
	encoder.emit32(0); // Will be patched

	// int3 ; Should never reach here
	encoder.emit8(0xCC);

	auto code = encoder.getCode();

	INFO("Code size: " << code.size());
	INFO("Reloc offset: " << relocOffset);

	// Create PE file
	PeWriter pe;
	pe.setSubsystem(IMAGE_SUBSYSTEM_WINDOWS_CUI);
	pe.setImageBase(0x140000000ULL);

	// Add code section
	pe.addTextSection(code);

	// Add import for ExitProcess
	pe.addImport("kernel32.dll", "ExitProcess");

	// Set entry point (start of .text section, which is at RVA 0x1000)
	pe.setEntryPoint(0x1000);

	// Add relocation for the ExitProcess call
	// The call instruction is at offset 9 (after sub rsp, 40 and mov ecx, 42)
	// The displacement starts at offset 11 (after FF 15)
	// We need to patch this with the IAT entry for ExitProcess
	uint32_t exitProcessIAT = pe.getImportRva("kernel32.dll", "ExitProcess");

	INFO("ExitProcess IAT RVA: 0x" << std::hex << exitProcessIAT);

	// Patch the call target: RIP-relative addressing
	// RIP is calculated from AFTER the instruction, not after the displacement!
	// Call instruction is 6 bytes (FF 15 xx xx xx xx), ends at byte 15
	// So RIP = 0x1000 + relocOffset + 4 = 0x1000 + 15
	// Offset = IAT_RVA - RIP
	uint32_t ripAfterCall = 0x1000 + static_cast<uint32_t>(relocOffset) + 4;
	int32_t callOffset = static_cast<int32_t>(exitProcessIAT) - static_cast<int32_t>(ripAfterCall);

	INFO("RIP after call: 0x" << std::hex << ripAfterCall);
	INFO("Call offset: " << std::dec << callOffset << " (0x" << std::hex << callOffset << ")");

	// Patch in the correct offset
	std::vector<uint8_t> patchedCode = code;
	patchedCode[relocOffset + 0] = (callOffset >> 0) & 0xFF;
	patchedCode[relocOffset + 1] = (callOffset >> 8) & 0xFF;
	patchedCode[relocOffset + 2] = (callOffset >> 16) & 0xFF;
	patchedCode[relocOffset + 3] = (callOffset >> 24) & 0xFF;

	// Need to recreate PE with patched code
	PeWriter pe2;
	pe2.setSubsystem(IMAGE_SUBSYSTEM_WINDOWS_CUI);
	pe2.setImageBase(0x140000000ULL);
	pe2.addTextSection(patchedCode);
	pe2.addImport("kernel32.dll", "ExitProcess");
	pe2.setEntryPoint(0x1000);

	// Write the executable
	std::string exePath = "test_exit42.exe";
	bool writeSuccess = pe2.write(exePath);
	REQUIRE(writeSuccess);

	// Try to run it and check exit code
	int exitCode = runAndGetExitCode(exePath);

	// Clean up
	std::remove(exePath.c_str());

	// On Windows, system() returns the exit code
	// 42 is what we expect
	INFO("Exit code: " << exitCode);
	REQUIRE(exitCode == 42);
}

//==============================================================================
// Test: MIR -> x86 -> PE executable (full pipeline)
//==============================================================================

TEST_CASE("Generate PE from MIR (full pipeline)", "[e2e][mir][pe]") {
	// Create a simple MIR module
	MIRModule mod("test_exit");
	MIRBuilder builder(&mod);

	// Declare ExitProcess as external
	auto *exitProcess = builder.declareFunction(
		"ExitProcess", MIRType::getVoid(), {MIRType::getInt32()});
	exitProcess->isExternal = true;

	// Create main function that calls ExitProcess(42)
	(void)builder.createFunction("main", MIRType::getVoid(), {});
	auto *entry = builder.createBasicBlock("entry");
	builder.setInsertPoint(entry);

	auto *exitCode = builder.getInt32(42);
	builder.createCall("ExitProcess", MIRType::getVoid(), {exitCode});
	builder.createRetVoid();

	// Generate x86-64 code
	X86CodeGen codegen(CallingConv::Win64);
	codegen.generate(&mod);

	// Get generated code
	auto &funcCodes = codegen.getFunctionCodes();
	REQUIRE(funcCodes.size() >= 1);

	// Find the main function
	const FunctionCode *mainCode = nullptr;
	for (const auto &fc : funcCodes) {
		if (fc.name == "main") {
			mainCode = &fc;
			break;
		}
	}
	REQUIRE(mainCode != nullptr);
	REQUIRE(!mainCode->code.empty());

	// Log the generated code for debugging
	INFO("Generated " << mainCode->code.size() << " bytes of code for main");

	// For now, just verify we generated something reasonable
	// Full executable generation needs more relocation work
	REQUIRE(mainCode->code.size() > 5);
}
