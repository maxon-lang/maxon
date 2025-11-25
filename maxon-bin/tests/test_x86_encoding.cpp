/**
 * Unit tests for Phase 2: x86-64 Instruction Encoding
 *
 * Tests the X86Encoder class for correct machine code generation.
 */

#include "../../lsp-server/tests/catch_amalgamated.hpp"
#include "../backend/x86_encoding.h"

using namespace backend;

//==============================================================================
// Helper Functions
//==============================================================================

// Check if two byte vectors are equal
void requireBytesEqual(const std::vector<uint8_t> &actual,
					   std::initializer_list<uint8_t> expected) {
	std::vector<uint8_t> exp(expected);
	REQUIRE(actual.size() == exp.size());
	for (size_t i = 0; i < actual.size(); ++i) {
		INFO("Byte index: " << i);
		REQUIRE(actual[i] == exp[i]);
	}
}

//==============================================================================
// Register Utilities Tests
//==============================================================================

TEST_CASE("x86 register utilities", "[x86][encoding]") {
	SECTION("extended register detection") {
		REQUIRE_FALSE(isExtendedReg(X86Reg::RAX));
		REQUIRE_FALSE(isExtendedReg(X86Reg::RDI));
		REQUIRE(isExtendedReg(X86Reg::R8));
		REQUIRE(isExtendedReg(X86Reg::R15));
	}

	SECTION("register index extraction") {
		REQUIRE(regIndex(X86Reg::RAX) == 0);
		REQUIRE(regIndex(X86Reg::RCX) == 1);
		REQUIRE(regIndex(X86Reg::RDX) == 2);
		REQUIRE(regIndex(X86Reg::RBX) == 3);
		REQUIRE(regIndex(X86Reg::RSP) == 4);
		REQUIRE(regIndex(X86Reg::RBP) == 5);
		REQUIRE(regIndex(X86Reg::RSI) == 6);
		REQUIRE(regIndex(X86Reg::RDI) == 7);
		REQUIRE(regIndex(X86Reg::R8) == 0);
		REQUIRE(regIndex(X86Reg::R15) == 7);
	}
}

//==============================================================================
// Basic Emission Tests
//==============================================================================

TEST_CASE("x86 byte emission", "[x86][encoding]") {
	X86Encoder enc;

	SECTION("emit8") {
		enc.emit8(0x90); // NOP
		requireBytesEqual(enc.getCode(), {0x90});
	}

	SECTION("emit16") {
		enc.emit16(0x1234);
		requireBytesEqual(enc.getCode(), {0x34, 0x12}); // Little-endian
	}

	SECTION("emit32") {
		enc.emit32(0xDEADBEEF);
		requireBytesEqual(enc.getCode(), {0xEF, 0xBE, 0xAD, 0xDE});
	}

	SECTION("emit64") {
		enc.emit64(0x123456789ABCDEF0ULL);
		requireBytesEqual(enc.getCode(), {0xF0, 0xDE, 0xBC, 0x9A, 0x78, 0x56, 0x34, 0x12});
	}
}

//==============================================================================
// MOV Instruction Tests
//==============================================================================

TEST_CASE("x86 MOV reg,reg", "[x86][encoding][mov]") {
	X86Encoder enc;

	SECTION("mov rax, rbx (64-bit)") {
		enc.movRR64(X86Reg::RAX, X86Reg::RBX);
		// REX.W + MOV r/m64, r64
		// 48 89 D8 = mov rax, rbx
		requireBytesEqual(enc.getCode(), {0x48, 0x89, 0xD8});
	}

	SECTION("mov r8, r9 (64-bit extended)") {
		enc.movRR64(X86Reg::R8, X86Reg::R9);
		// REX.WRB + MOV r/m64, r64
		// 4D 89 C8 = mov r8, r9
		requireBytesEqual(enc.getCode(), {0x4D, 0x89, 0xC8});
	}

	SECTION("mov eax, ebx (32-bit)") {
		enc.movRR32(X86Reg::EAX, X86Reg::EBX);
		// 89 D8 = mov eax, ebx (no REX needed)
		requireBytesEqual(enc.getCode(), {0x89, 0xD8});
	}
}

TEST_CASE("x86 MOV reg,imm", "[x86][encoding][mov]") {
	X86Encoder enc;

	SECTION("mov rax, imm64") {
		enc.movRI64(X86Reg::RAX, 0x123456789ABCDEF0ULL);
		// REX.W + MOV r64, imm64 (B8+rd io)
		// 48 B8 <8 bytes>
		auto &code = enc.getCode();
		REQUIRE(code.size() == 10);
		REQUIRE(code[0] == 0x48); // REX.W
		REQUIRE(code[1] == 0xB8); // MOV RAX, imm64
	}

	SECTION("mov r10, imm64") {
		// Use a value that requires full 64-bit encoding (> 0x7FFFFFFF)
		enc.movRI64(X86Reg::R10, 0x123456789ABCDEF0ULL);
		auto &code = enc.getCode();
		REQUIRE(code[0] == 0x49); // REX.WB
		REQUIRE(code[1] == 0xBA); // MOV R10 (B8 + 2)
	}

	SECTION("mov r10, small imm64 uses sign-extended form") {
		enc.movRI64(X86Reg::R10, 42);
		auto &code = enc.getCode();
		REQUIRE(code[0] == 0x49); // REX.WB
		REQUIRE(code[1] == 0xC7); // MOV r/m64, imm32 (sign-extended)
	}

	SECTION("mov eax, imm32") {
		enc.movRI32(X86Reg::EAX, 0x12345678);
		// B8 + rd <4 bytes>
		auto &code = enc.getCode();
		REQUIRE(code.size() == 5);
		REQUIRE(code[0] == 0xB8);
		REQUIRE(code[1] == 0x78);
		REQUIRE(code[4] == 0x12);
	}
}

TEST_CASE("x86 MOV memory operations", "[x86][encoding][mov]") {
	X86Encoder enc;

	SECTION("mov rax, [rbx]") {
		enc.movRM64(X86Reg::RAX, X86Mem(X86Reg::RBX));
		// REX.W + 8B /r
		// 48 8B 03
		requireBytesEqual(enc.getCode(), {0x48, 0x8B, 0x03});
	}

	SECTION("mov [rbx], rax") {
		enc.movMR64(X86Mem(X86Reg::RBX), X86Reg::RAX);
		// REX.W + 89 /r
		// 48 89 03
		requireBytesEqual(enc.getCode(), {0x48, 0x89, 0x03});
	}

	SECTION("mov rax, [rbx+8]") {
		enc.movRM64(X86Reg::RAX, X86Mem(X86Reg::RBX, 8));
		// 48 8B 43 08 (disp8)
		requireBytesEqual(enc.getCode(), {0x48, 0x8B, 0x43, 0x08});
	}

	SECTION("mov rax, [rbp+0]") {
		// RBP/R13 needs special encoding (SIB byte or disp8)
		enc.movRM64(X86Reg::RAX, X86Mem(X86Reg::RBP, 0));
		// Should use mod=01 with disp8=0
		auto &code = enc.getCode();
		REQUIRE(code.size() >= 4);
	}
}

//==============================================================================
// Arithmetic Tests
//==============================================================================

TEST_CASE("x86 ADD instructions", "[x86][encoding][add]") {
	X86Encoder enc;

	SECTION("add rax, rbx") {
		enc.addRR64(X86Reg::RAX, X86Reg::RBX);
		// 48 01 D8
		requireBytesEqual(enc.getCode(), {0x48, 0x01, 0xD8});
	}

	SECTION("add eax, ebx") {
		enc.addRR32(X86Reg::EAX, X86Reg::EBX);
		// 01 D8
		requireBytesEqual(enc.getCode(), {0x01, 0xD8});
	}

	SECTION("add rax, imm32") {
		enc.addRI64(X86Reg::RAX, 100);
		auto &code = enc.getCode();
		REQUIRE(code[0] == 0x48); // REX.W
	}
}

TEST_CASE("x86 SUB instructions", "[x86][encoding][sub]") {
	X86Encoder enc;

	SECTION("sub rax, rbx") {
		enc.subRR64(X86Reg::RAX, X86Reg::RBX);
		// 48 29 D8
		requireBytesEqual(enc.getCode(), {0x48, 0x29, 0xD8});
	}

	SECTION("sub rsp, imm32") {
		enc.subRI64(X86Reg::RSP, 32);
		auto &code = enc.getCode();
		REQUIRE(code[0] == 0x48); // REX.W
	}
}

TEST_CASE("x86 IMUL instructions", "[x86][encoding][imul]") {
	X86Encoder enc;

	SECTION("imul rax, rbx") {
		enc.imulRR64(X86Reg::RAX, X86Reg::RBX);
		// 48 0F AF C3
		requireBytesEqual(enc.getCode(), {0x48, 0x0F, 0xAF, 0xC3});
	}

	SECTION("imul eax, ebx") {
		enc.imulRR32(X86Reg::EAX, X86Reg::EBX);
		// 0F AF C3
		requireBytesEqual(enc.getCode(), {0x0F, 0xAF, 0xC3});
	}
}

TEST_CASE("x86 DIV/IDIV instructions", "[x86][encoding][div]") {
	X86Encoder enc;

	SECTION("idiv rbx") {
		enc.idivR64(X86Reg::RBX);
		// 48 F7 FB
		requireBytesEqual(enc.getCode(), {0x48, 0xF7, 0xFB});
	}

	SECTION("cqo (sign extend RAX to RDX:RAX)") {
		enc.cqo();
		// 48 99
		requireBytesEqual(enc.getCode(), {0x48, 0x99});
	}

	SECTION("cdq (sign extend EAX to EDX:EAX)") {
		enc.cdq();
		// 99
		requireBytesEqual(enc.getCode(), {0x99});
	}
}

//==============================================================================
// Bitwise Operations Tests
//==============================================================================

TEST_CASE("x86 bitwise instructions", "[x86][encoding][bitwise]") {
	X86Encoder enc;

	SECTION("and rax, rbx") {
		enc.andRR64(X86Reg::RAX, X86Reg::RBX);
		requireBytesEqual(enc.getCode(), {0x48, 0x21, 0xD8});
	}

	SECTION("or rax, rbx") {
		enc.orRR64(X86Reg::RAX, X86Reg::RBX);
		requireBytesEqual(enc.getCode(), {0x48, 0x09, 0xD8});
	}

	SECTION("xor rax, rbx") {
		enc.xorRR64(X86Reg::RAX, X86Reg::RBX);
		requireBytesEqual(enc.getCode(), {0x48, 0x31, 0xD8});
	}

	SECTION("xor eax, eax (common zero pattern)") {
		enc.xorRR32(X86Reg::EAX, X86Reg::EAX);
		requireBytesEqual(enc.getCode(), {0x31, 0xC0});
	}
}

TEST_CASE("x86 shift instructions", "[x86][encoding][shift]") {
	X86Encoder enc;

	SECTION("shl rax, 4") {
		enc.shlR64_imm(X86Reg::RAX, 4);
		auto &code = enc.getCode();
		REQUIRE(code[0] == 0x48); // REX.W
	}

	SECTION("shr rax, cl") {
		enc.shrR64_CL(X86Reg::RAX);
		auto &code = enc.getCode();
		REQUIRE(code[0] == 0x48); // REX.W
	}

	SECTION("sar rax, cl") {
		enc.sarR64_CL(X86Reg::RAX);
		auto &code = enc.getCode();
		REQUIRE(code[0] == 0x48); // REX.W
	}
}

//==============================================================================
// Comparison Tests
//==============================================================================

TEST_CASE("x86 CMP instructions", "[x86][encoding][cmp]") {
	X86Encoder enc;

	SECTION("cmp rax, rbx") {
		enc.cmpRR64(X86Reg::RAX, X86Reg::RBX);
		requireBytesEqual(enc.getCode(), {0x48, 0x39, 0xD8});
	}

	SECTION("cmp eax, 0") {
		enc.cmpRI32(X86Reg::EAX, 0);
		auto &code = enc.getCode();
		// 83 F8 00 or 3D 00 00 00 00
		REQUIRE(!code.empty());
	}
}

TEST_CASE("x86 SETcc instructions", "[x86][encoding][setcc]") {
	X86Encoder enc;

	SECTION("sete al") {
		enc.setcc(X86Cond::E, X86Reg::AL);
		// 0F 94 C0
		requireBytesEqual(enc.getCode(), {0x0F, 0x94, 0xC0});
	}

	SECTION("setne al") {
		enc.setcc(X86Cond::NE, X86Reg::AL);
		// 0F 95 C0
		requireBytesEqual(enc.getCode(), {0x0F, 0x95, 0xC0});
	}

	SECTION("setl al") {
		enc.setcc(X86Cond::L, X86Reg::AL);
		// 0F 9C C0
		requireBytesEqual(enc.getCode(), {0x0F, 0x9C, 0xC0});
	}

	SECTION("setg al") {
		enc.setcc(X86Cond::G, X86Reg::AL);
		// 0F 9F C0
		requireBytesEqual(enc.getCode(), {0x0F, 0x9F, 0xC0});
	}
}

//==============================================================================
// Control Flow Tests
//==============================================================================

TEST_CASE("x86 JMP instructions", "[x86][encoding][jmp]") {
	X86Encoder enc;

	SECTION("jmp rel32") {
		enc.jmpRel32(0x12345678);
		// E9 <4 bytes>
		auto &code = enc.getCode();
		REQUIRE(code.size() == 5);
		REQUIRE(code[0] == 0xE9);
	}

	SECTION("jmp rax") {
		enc.jmpR(X86Reg::RAX);
		// FF E0
		requireBytesEqual(enc.getCode(), {0xFF, 0xE0});
	}
}

TEST_CASE("x86 Jcc instructions", "[x86][encoding][jcc]") {
	X86Encoder enc;

	SECTION("je rel32") {
		enc.jccRel32(X86Cond::E, 0x100);
		// 0F 84 <4 bytes>
		auto &code = enc.getCode();
		REQUIRE(code.size() == 6);
		REQUIRE(code[0] == 0x0F);
		REQUIRE(code[1] == 0x84);
	}

	SECTION("jne rel32") {
		enc.jccRel32(X86Cond::NE, 0x100);
		auto &code = enc.getCode();
		REQUIRE(code[1] == 0x85);
	}

	SECTION("jl rel32") {
		enc.jccRel32(X86Cond::L, 0x100);
		auto &code = enc.getCode();
		REQUIRE(code[1] == 0x8C);
	}
}

TEST_CASE("x86 CALL instructions", "[x86][encoding][call]") {
	X86Encoder enc;

	SECTION("call rel32") {
		enc.callRel32(0x1000);
		// E8 <4 bytes>
		auto &code = enc.getCode();
		REQUIRE(code.size() == 5);
		REQUIRE(code[0] == 0xE8);
	}

	SECTION("call rax") {
		enc.callR(X86Reg::RAX);
		// FF D0
		requireBytesEqual(enc.getCode(), {0xFF, 0xD0});
	}

	SECTION("ret") {
		enc.ret();
		// C3
		requireBytesEqual(enc.getCode(), {0xC3});
	}
}

//==============================================================================
// Stack Operations Tests
//==============================================================================

TEST_CASE("x86 stack instructions", "[x86][encoding][stack]") {
	X86Encoder enc;

	SECTION("push rax") {
		enc.pushR(X86Reg::RAX);
		// 50
		requireBytesEqual(enc.getCode(), {0x50});
	}

	SECTION("push r15") {
		enc.pushR(X86Reg::R15);
		// 41 57
		requireBytesEqual(enc.getCode(), {0x41, 0x57});
	}

	SECTION("pop rax") {
		enc.popR(X86Reg::RAX);
		// 58
		requireBytesEqual(enc.getCode(), {0x58});
	}

	SECTION("pop r15") {
		enc.popR(X86Reg::R15);
		// 41 5F
		requireBytesEqual(enc.getCode(), {0x41, 0x5F});
	}
}

//==============================================================================
// SSE2 Floating-Point Tests
//==============================================================================

TEST_CASE("x86 SSE2 MOVSD", "[x86][encoding][sse2]") {
	X86Encoder enc;

	SECTION("movsd xmm0, xmm1") {
		enc.movsdRR(X86Reg::XMM0, X86Reg::XMM1);
		// F2 0F 10 C1
		requireBytesEqual(enc.getCode(), {0xF2, 0x0F, 0x10, 0xC1});
	}

	SECTION("movsd xmm0, [rbx]") {
		enc.movsdRM(X86Reg::XMM0, X86Mem(X86Reg::RBX));
		// F2 0F 10 03
		requireBytesEqual(enc.getCode(), {0xF2, 0x0F, 0x10, 0x03});
	}

	SECTION("movsd [rbx], xmm0") {
		enc.movsdMR(X86Mem(X86Reg::RBX), X86Reg::XMM0);
		// F2 0F 11 03
		requireBytesEqual(enc.getCode(), {0xF2, 0x0F, 0x11, 0x03});
	}
}

TEST_CASE("x86 SSE2 arithmetic", "[x86][encoding][sse2]") {
	X86Encoder enc;

	SECTION("addsd xmm0, xmm1") {
		enc.addsdRR(X86Reg::XMM0, X86Reg::XMM1);
		// F2 0F 58 C1
		requireBytesEqual(enc.getCode(), {0xF2, 0x0F, 0x58, 0xC1});
	}

	SECTION("subsd xmm0, xmm1") {
		enc.subsdRR(X86Reg::XMM0, X86Reg::XMM1);
		// F2 0F 5C C1
		requireBytesEqual(enc.getCode(), {0xF2, 0x0F, 0x5C, 0xC1});
	}

	SECTION("mulsd xmm0, xmm1") {
		enc.mulsdRR(X86Reg::XMM0, X86Reg::XMM1);
		// F2 0F 59 C1
		requireBytesEqual(enc.getCode(), {0xF2, 0x0F, 0x59, 0xC1});
	}

	SECTION("divsd xmm0, xmm1") {
		enc.divsdRR(X86Reg::XMM0, X86Reg::XMM1);
		// F2 0F 5E C1
		requireBytesEqual(enc.getCode(), {0xF2, 0x0F, 0x5E, 0xC1});
	}
}

TEST_CASE("x86 SSE2 comparison", "[x86][encoding][sse2]") {
	X86Encoder enc;

	SECTION("ucomisd xmm0, xmm1") {
		enc.ucomisdRR(X86Reg::XMM0, X86Reg::XMM1);
		// 66 0F 2E C1
		requireBytesEqual(enc.getCode(), {0x66, 0x0F, 0x2E, 0xC1});
	}

	SECTION("xorpd xmm0, xmm0 (zero register)") {
		enc.xorpdRR(X86Reg::XMM0, X86Reg::XMM0);
		// 66 0F 57 C0
		requireBytesEqual(enc.getCode(), {0x66, 0x0F, 0x57, 0xC0});
	}
}

TEST_CASE("x86 SSE2 conversions", "[x86][encoding][sse2]") {
	X86Encoder enc;

	SECTION("cvtsi2sd xmm0, rax (int64 to double)") {
		enc.cvtsi2sdRR64(X86Reg::XMM0, X86Reg::RAX);
		// F2 REX.W 0F 2A C0
		auto &code = enc.getCode();
		REQUIRE(code.size() >= 4);
	}

	SECTION("cvttsd2si rax, xmm0 (double to int64)") {
		enc.cvttsd2siRR64(X86Reg::RAX, X86Reg::XMM0);
		// F2 REX.W 0F 2C C0
		auto &code = enc.getCode();
		REQUIRE(code.size() >= 4);
	}
}

//==============================================================================
// Fixup Tests
//==============================================================================

TEST_CASE("x86 fixup calculations", "[x86][encoding][fixup]") {
	X86Encoder enc;

	SECTION("calcRel32") {
		// From address 100, jump to address 200
		// rel32 = target - (from + 5) for JMP
		// But calcRel32 just does target - from
		int32_t rel = X86Encoder::calcRel32(100, 200);
		REQUIRE(rel == 100);

		rel = X86Encoder::calcRel32(200, 100);
		REQUIRE(rel == -100);
	}

	SECTION("patchRel32") {
		enc.emit8(0xE9); // JMP
		enc.emit32(0);	 // Placeholder

		// Patch the rel32 at offset 1
		enc.patchRel32(1, 0x12345678);

		auto &code = enc.getCode();
		REQUIRE(code[1] == 0x78);
		REQUIRE(code[2] == 0x56);
		REQUIRE(code[3] == 0x34);
		REQUIRE(code[4] == 0x12);
	}
}

//==============================================================================
// LEA Tests
//==============================================================================

TEST_CASE("x86 LEA instruction", "[x86][encoding][lea]") {
	X86Encoder enc;

	SECTION("lea rax, [rbx+rcx*4+8]") {
		enc.lea64(X86Reg::RAX, X86Mem(X86Reg::RBX, X86Reg::RCX, 4, 8));
		auto &code = enc.getCode();
		REQUIRE(code[0] == 0x48); // REX.W
		REQUIRE(code[1] == 0x8D); // LEA
	}

	SECTION("lea rax, [rsp+16]") {
		enc.lea64(X86Reg::RAX, X86Mem(X86Reg::RSP, 16));
		auto &code = enc.getCode();
		REQUIRE(code[0] == 0x48); // REX.W
		REQUIRE(code[1] == 0x8D); // LEA
	}
}
