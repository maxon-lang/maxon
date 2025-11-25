/**
 * Unit tests for Phase 4: Executable Generation (ELF & PE)
 *
 * Tests the ELF and PE writers for correct executable format generation.
 */

#include "../../lsp-server/tests/catch_amalgamated.hpp"
#include "../backend/elf_writer.h"
#include "../backend/pe_writer.h"
#include <cstring>
#include <fstream>

using namespace backend;

//==============================================================================
// ELF Writer Tests
//==============================================================================

TEST_CASE("ElfWriter basic construction", "[elf][writer]") {
	ElfWriter writer;

	// Just verify construction doesn't crash
	REQUIRE(writer.getSectionIndex(".text") == -1); // No .text yet
}

TEST_CASE("ElfWriter add sections", "[elf][writer]") {
	ElfWriter writer;

	SECTION("add text section") {
		std::vector<uint8_t> code = {0x48, 0xC7, 0xC0, 0x00, 0x00, 0x00, 0x00, // mov rax, 0
									 0xC3};									   // ret
		uint32_t idx = writer.addTextSection(code);
		REQUIRE(idx > 0); // Index 0 is null section
		REQUIRE(writer.getSectionIndex(".text") == static_cast<int32_t>(idx));
	}

	SECTION("add data section") {
		std::vector<uint8_t> data = {0x01, 0x02, 0x03, 0x04};
		uint32_t idx = writer.addDataSection(data);
		REQUIRE(idx > 0);
		REQUIRE(writer.getSectionIndex(".data") == static_cast<int32_t>(idx));
	}

	SECTION("add rodata section") {
		std::vector<uint8_t> rodata = {'H', 'e', 'l', 'l', 'o', '\0'};
		uint32_t idx = writer.addRodataSection(rodata);
		REQUIRE(idx > 0);
		REQUIRE(writer.getSectionIndex(".rodata") == static_cast<int32_t>(idx));
	}

	SECTION("add bss section") {
		uint32_t idx = writer.addBssSection(1024);
		REQUIRE(idx > 0);
		REQUIRE(writer.getSectionIndex(".bss") == static_cast<int32_t>(idx));
	}
}

TEST_CASE("ElfWriter add symbols", "[elf][writer]") {
	ElfWriter writer;

	// Add text section first
	std::vector<uint8_t> code = {0xC3}; // ret
	uint32_t textIdx = writer.addTextSection(code);

	// Add data section for variable
	std::vector<uint8_t> data = {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00};
	(void)writer.addDataSection(data); // Result not needed, just creates section

	// Add function symbol (name, offset, size)
	writer.addFunction("main", 0, 1);

	// Add variable symbol (name, offset, size)
	writer.addVariable("global_var", 0, 8);

	// Add with full parameters (name, value, size, binding, type, section)
	writer.addSymbol("custom_sym", 0, 4, STB_GLOBAL, STT_OBJECT, textIdx);

	// Should not crash
	REQUIRE(true);
}

TEST_CASE("ElfWriter write minimal executable", "[elf][writer]") {
	ElfWriter writer;

	// Create a minimal program: mov rax, 60 (exit syscall); xor rdi, rdi; syscall
	std::vector<uint8_t> code = {
		0x48, 0xC7, 0xC0, 0x3C, 0x00, 0x00, 0x00, // mov rax, 60
		0x48, 0x31, 0xFF,						  // xor rdi, rdi
		0x0F, 0x05								  // syscall
	};

	writer.addTextSection(code);
	writer.addFunction("_start", 0, code.size());
	writer.setEntryPoint(0x401000); // Typical Linux executable entry

	// Write to temp file
	std::string filename = "test_elf_output.elf";
	bool success = writer.write(filename);

	// Clean up
	std::remove(filename.c_str());

	REQUIRE(success);
}

TEST_CASE("ElfWriter ELF header constants", "[elf][constants]") {
	// Verify ELF magic numbers
	REQUIRE(ELFMAG0 == 0x7f);
	REQUIRE(ELFMAG1 == 'E');
	REQUIRE(ELFMAG2 == 'L');
	REQUIRE(ELFMAG3 == 'F');

	// Verify class constants
	REQUIRE(ELFCLASS64 == 2);

	// Verify machine type
	REQUIRE(EM_X86_64 == 62);

	// Verify section flags
	REQUIRE(SHF_WRITE == 0x1);
	REQUIRE(SHF_ALLOC == 0x2);
	REQUIRE(SHF_EXECINSTR == 0x4);

	// Verify program header flags
	REQUIRE(PF_X == 0x1);
	REQUIRE(PF_W == 0x2);
	REQUIRE(PF_R == 0x4);
}

TEST_CASE("ElfWriter multiple sections", "[elf][writer]") {
	ElfWriter writer;

	// Add multiple sections
	std::vector<uint8_t> code = {0xC3};
	std::vector<uint8_t> data = {0x42, 0x00, 0x00, 0x00};
	std::vector<uint8_t> rodata = {'T', 'e', 's', 't', '\0'};

	writer.addTextSection(code);
	writer.addDataSection(data);
	writer.addRodataSection(rodata);
	writer.addBssSection(256);

	// Verify all sections exist
	REQUIRE(writer.getSectionIndex(".text") > 0);
	REQUIRE(writer.getSectionIndex(".data") > 0);
	REQUIRE(writer.getSectionIndex(".rodata") > 0);
	REQUIRE(writer.getSectionIndex(".bss") > 0);

	// All should have different indices
	REQUIRE(writer.getSectionIndex(".text") != writer.getSectionIndex(".data"));
	REQUIRE(writer.getSectionIndex(".data") != writer.getSectionIndex(".rodata"));
}

//==============================================================================
// PE Writer Tests
//==============================================================================

TEST_CASE("PeWriter basic construction", "[pe][writer]") {
	PeWriter writer;

	// Just verify construction doesn't crash
	REQUIRE(writer.getSectionIndex(".text") == -1); // No .text yet
}

TEST_CASE("PeWriter add sections", "[pe][writer]") {
	PeWriter writer;

	SECTION("add text section") {
		std::vector<uint8_t> code = {0x48, 0xC7, 0xC0, 0x00, 0x00, 0x00, 0x00, // mov rax, 0
									 0xC3};									   // ret
		uint32_t idx = writer.addTextSection(code);
		REQUIRE(idx == 0); // First section
		REQUIRE(writer.getSectionIndex(".text") == 0);
	}

	SECTION("add data section") {
		std::vector<uint8_t> data = {0x01, 0x02, 0x03, 0x04};
		uint32_t idx = writer.addDataSection(data);
		REQUIRE(idx == 0);
		REQUIRE(writer.getSectionIndex(".data") == 0);
	}

	SECTION("add rdata section") {
		std::vector<uint8_t> rdata = {'H', 'e', 'l', 'l', 'o', '\0'};
		uint32_t idx = writer.addRdataSection(rdata);
		REQUIRE(idx == 0);
		REQUIRE(writer.getSectionIndex(".rdata") == 0);
	}

	SECTION("add bss section") {
		uint32_t idx = writer.addBssSection(1024);
		REQUIRE(idx == 0);
		REQUIRE(writer.getSectionIndex(".bss") == 0);
	}
}

TEST_CASE("PeWriter set properties", "[pe][writer]") {
	PeWriter writer;

	SECTION("set subsystem") {
		writer.setSubsystem(IMAGE_SUBSYSTEM_WINDOWS_CUI);
		// Should not crash
		REQUIRE(true);
	}

	SECTION("set image base") {
		writer.setImageBase(0x140000000ULL);
		// Should not crash
		REQUIRE(true);
	}

	SECTION("set entry point") {
		writer.setEntryPoint(0x1000);
		// Should not crash
		REQUIRE(true);
	}
}

TEST_CASE("PeWriter add imports", "[pe][writer]") {
	PeWriter writer;

	// Add imports from kernel32.dll
	writer.addImport("kernel32.dll", "ExitProcess");
	writer.addImport("kernel32.dll", "GetStdHandle");
	writer.addImport("kernel32.dll", "WriteFile");

	// Add imports from another DLL
	writer.addImport("msvcrt.dll", "printf");

	// Should not crash
	REQUIRE(true);
}

TEST_CASE("PeWriter add symbols", "[pe][writer]") {
	PeWriter writer;

	// Add text section first
	std::vector<uint8_t> code = {0xC3}; // ret
	writer.addTextSection(code);

	// Add symbol
	writer.addSymbol("main", 0x1000, 1, true, 0);

	// Should not crash
	REQUIRE(true);
}

TEST_CASE("PeWriter add relocations", "[pe][writer]") {
	PeWriter writer;

	// Add text section
	std::vector<uint8_t> code(64, 0x90); // NOPs
	writer.addTextSection(code);

	// Add relocation
	writer.addRelocation(0x1008, IMAGE_REL_BASED_DIR64);

	// Should not crash
	REQUIRE(true);
}

TEST_CASE("PeWriter write minimal executable", "[pe][writer]") {
	PeWriter writer;

	// Create a minimal Windows program
	// sub rsp, 40 ; shadow space
	// mov ecx, 0  ; exit code
	// call [ExitProcess]
	std::vector<uint8_t> code = {
		0x48,
		0x83,
		0xEC,
		0x28, // sub rsp, 40
		0x31,
		0xC9, // xor ecx, ecx
		0xFF,
		0x15,
		0x00,
		0x00,
		0x00,
		0x00, // call [rip+0] (placeholder)
	};

	writer.addTextSection(code);
	writer.setEntryPoint(0x1000);
	writer.setSubsystem(IMAGE_SUBSYSTEM_WINDOWS_CUI);
	writer.addImport("kernel32.dll", "ExitProcess");

	// Write to temp file
	std::string filename = "test_pe_output.exe";
	bool success = writer.write(filename);

	// Clean up
	std::remove(filename.c_str());

	REQUIRE(success);
}

TEST_CASE("PeWriter PE header constants", "[pe][constants]") {
	// Verify DOS magic
	REQUIRE(DOS_MAGIC == 0x5A4D);

	// Verify PE signature
	REQUIRE(PE_SIGNATURE == 0x00004550);

	// Verify machine type
	REQUIRE(IMAGE_FILE_MACHINE_AMD64 == 0x8664);

	// Verify optional header magic
	REQUIRE(PE32PLUS_MAGIC == 0x20b);

	// Verify subsystem types
	REQUIRE(IMAGE_SUBSYSTEM_WINDOWS_CUI == 3);
	REQUIRE(IMAGE_SUBSYSTEM_WINDOWS_GUI == 2);

	// Verify section characteristics
	REQUIRE(IMAGE_SCN_CNT_CODE == 0x00000020);
	REQUIRE(IMAGE_SCN_MEM_EXECUTE == 0x20000000);
	REQUIRE(IMAGE_SCN_MEM_READ == 0x40000000);
	REQUIRE(IMAGE_SCN_MEM_WRITE == 0x80000000);
}

TEST_CASE("PeWriter multiple sections", "[pe][writer]") {
	PeWriter writer;

	// Add multiple sections
	std::vector<uint8_t> code = {0xC3};
	std::vector<uint8_t> data = {0x42, 0x00, 0x00, 0x00};
	std::vector<uint8_t> rdata = {'T', 'e', 's', 't', '\0'};

	writer.addTextSection(code);
	writer.addDataSection(data);
	writer.addRdataSection(rdata);
	writer.addBssSection(256);

	// Verify all sections exist
	REQUIRE(writer.getSectionIndex(".text") >= 0);
	REQUIRE(writer.getSectionIndex(".data") >= 0);
	REQUIRE(writer.getSectionIndex(".rdata") >= 0);
	REQUIRE(writer.getSectionIndex(".bss") >= 0);
}

//==============================================================================
// Cross-Platform Comparison Tests
//==============================================================================

TEST_CASE("ELF vs PE section naming", "[elf][pe][comparison]") {
	ElfWriter elfWriter;
	PeWriter peWriter;

	std::vector<uint8_t> code = {0xC3};
	std::vector<uint8_t> data = {0x00};

	// ELF uses .rodata, PE uses .rdata
	elfWriter.addRodataSection(data);
	peWriter.addRdataSection(data);

	REQUIRE(elfWriter.getSectionIndex(".rodata") >= 0);
	REQUIRE(peWriter.getSectionIndex(".rdata") >= 0);
}
