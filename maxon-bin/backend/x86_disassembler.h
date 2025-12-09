#pragma once

#include "x86_codegen.h"
#include <string>
#include <unordered_map>
#include <vector>

namespace backend {

/**
 * X86 Disassembler
 *
 * Wrapper around Zydis to convert raw machine code bytes to Intel-syntax
 * assembly text. Used by the compiler explorer to show generated code.
 */
class X86Disassembler {
  public:
	X86Disassembler();
	~X86Disassembler();

	/**
	 * Disassemble raw bytes to Intel syntax assembly
	 * @param code Raw machine code bytes
	 * @param baseAddress Virtual address for the code (for branch targets)
	 * @return Multi-line string of assembly instructions
	 */
	std::string disassemble(const std::vector<uint8_t> &code, uint64_t baseAddress = 0);

	/**
	 * Disassemble multiple functions with labels and annotations
	 * @param functions Vector of FunctionCode structs from X86CodeGen
	 * @param functionOffsets Map from function name to its offset in the final binary
	 * @return Formatted assembly with function labels
	 */
	std::string disassembleWithSymbols(const std::vector<FunctionCode> &functions,
									   const std::unordered_map<std::string, size_t> &functionOffsets = {});

  private:
	void *decoder;	 // ZydisDecoder*
	void *formatter; // ZydisFormatter*
};

} // namespace backend
