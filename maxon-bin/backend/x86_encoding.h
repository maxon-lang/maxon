#pragma once

#include <cstdint>
#include <vector>

namespace backend {

//==============================================================================
// x86-64 Register Definitions
//==============================================================================

enum class X86Reg : uint8_t {
	// 64-bit general purpose registers
	RAX = 0,
	RCX = 1,
	RDX = 2,
	RBX = 3,
	RSP = 4,
	RBP = 5,
	RSI = 6,
	RDI = 7,
	R8 = 8,
	R9 = 9,
	R10 = 10,
	R11 = 11,
	R12 = 12,
	R13 = 13,
	R14 = 14,
	R15 = 15,

	// 32-bit general purpose registers (same encoding, different REX)
	EAX = 0,
	ECX = 1,
	EDX = 2,
	EBX = 3,
	ESP = 4,
	EBP = 5,
	ESI = 6,
	EDI = 7,
	R8D = 8,
	R9D = 9,
	R10D = 10,
	R11D = 11,
	R12D = 12,
	R13D = 13,
	R14D = 14,
	R15D = 15,

	// 8-bit registers (for i8 and bool operations)
	AL = 0,
	CL = 1,
	DL = 2,
	BL = 3,
	SPL = 4,
	BPL = 5,
	SIL = 6,
	DIL = 7,
	R8B = 8,
	R9B = 9,
	R10B = 10,
	R11B = 11,
	R12B = 12,
	R13B = 13,
	R14B = 14,
	R15B = 15,

	// SSE registers (for floating-point)
	XMM0 = 0,
	XMM1 = 1,
	XMM2 = 2,
	XMM3 = 3,
	XMM4 = 4,
	XMM5 = 5,
	XMM6 = 6,
	XMM7 = 7,
	XMM8 = 8,
	XMM9 = 9,
	XMM10 = 10,
	XMM11 = 11,
	XMM12 = 12,
	XMM13 = 13,
	XMM14 = 14,
	XMM15 = 15,

	// Special values
	None = 255,
};

// Register file information
inline bool isExtendedReg(X86Reg reg) {
	return static_cast<uint8_t>(reg) >= 8 && static_cast<uint8_t>(reg) <= 15;
}

inline uint8_t regIndex(X86Reg reg) {
	return static_cast<uint8_t>(reg) & 0x7;
}

//==============================================================================
// x86-64 Condition Codes
//==============================================================================

enum class X86Cond : uint8_t {
	O = 0x0,  // Overflow
	NO = 0x1, // Not overflow
	B = 0x2,  // Below (unsigned <)
	AE = 0x3, // Above or equal (unsigned >=)
	E = 0x4,  // Equal
	NE = 0x5, // Not equal
	BE = 0x6, // Below or equal (unsigned <=)
	A = 0x7,  // Above (unsigned >)
	S = 0x8,  // Sign (negative)
	NS = 0x9, // Not sign (non-negative)
	P = 0xA,  // Parity even
	NP = 0xB, // Parity odd
	L = 0xC,  // Less (signed <)
	GE = 0xD, // Greater or equal (signed >=)
	LE = 0xE, // Less or equal (signed <=)
	G = 0xF,  // Greater (signed >)
};

//==============================================================================
// Memory Operand
//==============================================================================

struct X86Mem {
	X86Reg base = X86Reg::None;
	X86Reg index = X86Reg::None;
	uint8_t scale = 1; // 1, 2, 4, or 8
	int32_t disp = 0;

	// RIP-relative addressing
	bool ripRelative = false;

	X86Mem() = default;
	X86Mem(X86Reg b, int32_t d = 0) : base(b), disp(d) {}
	X86Mem(X86Reg b, X86Reg i, uint8_t s, int32_t d = 0)
		: base(b), index(i), scale(s), disp(d) {}

	static X86Mem RipRel(int32_t offset) {
		X86Mem m;
		m.ripRelative = true;
		m.disp = offset;
		return m;
	}
};

//==============================================================================
// x86-64 Instruction Encoder
//==============================================================================

class X86Encoder {
  private:
	std::vector<uint8_t> code;

	// Current position for fixups
	size_t currentOffset() const { return code.size(); }

  public:
	X86Encoder() = default;

	// Get generated code
	const std::vector<uint8_t> &getCode() const { return code; }
	std::vector<uint8_t> &getCode() { return code; }
	void clear() { code.clear(); }
	size_t size() const { return code.size(); }

	//--------------------------------------------------------------------------
	// Raw byte emission
	//--------------------------------------------------------------------------

	void emit8(uint8_t byte) { code.push_back(byte); }
	void emit16(uint16_t val);
	void emit32(uint32_t val);
	void emit64(uint64_t val);
	void emitBytes(const uint8_t *data, size_t len);

	//--------------------------------------------------------------------------
	// Encoding helpers
	//--------------------------------------------------------------------------

	// REX prefix: W=64-bit, R=ModRM.reg extension, X=SIB.index extension, B=ModRM.rm/SIB.base extension
	void emitREX(bool w, bool r, bool x, bool b);
	void emitREX(bool w, X86Reg reg, X86Reg rm);
	void emitREXOpt(bool w, X86Reg reg, X86Reg rm); // Emit only if needed

	// ModR/M byte
	void emitModRM(uint8_t mod, uint8_t reg, uint8_t rm);
	void emitModRM(uint8_t mod, X86Reg reg, X86Reg rm);

	// SIB byte
	void emitSIB(uint8_t scale, uint8_t index, uint8_t base);

	// ModR/M + optional SIB + displacement for memory operand
	void emitModRMMem(X86Reg reg, const X86Mem &mem);

	//--------------------------------------------------------------------------
	// Data movement
	//--------------------------------------------------------------------------

	// MOV reg, reg (64-bit)
	void movRR64(X86Reg dst, X86Reg src);
	// MOV reg, reg (32-bit)
	void movRR32(X86Reg dst, X86Reg src);
	// MOV reg, imm64
	void movRI64(X86Reg dst, uint64_t imm);
	// MOV reg, imm32
	void movRI32(X86Reg dst, uint32_t imm);
	// MOV reg, [mem]
	void movRM64(X86Reg dst, const X86Mem &src);
	void movRM32(X86Reg dst, const X86Mem &src);
	void movRM8(X86Reg dst, const X86Mem &src);
	// MOV [mem], reg
	void movMR64(const X86Mem &dst, X86Reg src);
	void movMR32(const X86Mem &dst, X86Reg src);
	void movMR8(const X86Mem &dst, X86Reg src);
	// MOV [mem], imm32 (sign-extended for 64-bit stores)
	void movMI32(const X86Mem &dst, int32_t imm);

	// LEA reg, [mem]
	void lea64(X86Reg dst, const X86Mem &src);

	// MOVZX (zero-extend)
	void movzxRR32_8(X86Reg dst, X86Reg src); // movzx r32, r8
	void movzxRM32_8(X86Reg dst, const X86Mem &src);

	// MOVSX (sign-extend)
	void movsxRR64_32(X86Reg dst, X86Reg src); // movsx r64, r32
	void movsxRR32_8(X86Reg dst, X86Reg src);  // movsx r32, r8

	//--------------------------------------------------------------------------
	// Arithmetic (integer)
	//--------------------------------------------------------------------------

	// ADD
	void addRR64(X86Reg dst, X86Reg src);
	void addRR32(X86Reg dst, X86Reg src);
	void addRI64(X86Reg dst, int32_t imm);
	void addRI32(X86Reg dst, int32_t imm);
	void addRM64(X86Reg dst, const X86Mem &src);
	void addMR64(const X86Mem &dst, X86Reg src);

	// SUB
	void subRR64(X86Reg dst, X86Reg src);
	void subRR32(X86Reg dst, X86Reg src);
	void subRI64(X86Reg dst, int32_t imm);
	void subRI32(X86Reg dst, int32_t imm);

	// IMUL (signed multiply)
	void imulRR64(X86Reg dst, X86Reg src);
	void imulRR32(X86Reg dst, X86Reg src);
	void imulRRI64(X86Reg dst, X86Reg src, int32_t imm);
	void imulRRI32(X86Reg dst, X86Reg src, int32_t imm);

	// IDIV (signed divide) - uses RDX:RAX
	void idivR64(X86Reg divisor);
	void idivR32(X86Reg divisor);

	// DIV (unsigned divide) - uses RDX:RAX
	void divR64(X86Reg divisor);
	void divR32(X86Reg divisor);

	// NEG (two's complement negation)
	void negR64(X86Reg reg);
	void negR32(X86Reg reg);

	// CQO/CDQ - sign extend RAX into RDX:RAX
	void cqo(); // 64-bit
	void cdq(); // 32-bit

	//--------------------------------------------------------------------------
	// Bitwise operations
	//--------------------------------------------------------------------------

	void andRR64(X86Reg dst, X86Reg src);
	void andRR32(X86Reg dst, X86Reg src);
	void andRI64(X86Reg dst, int32_t imm);
	void andRI32(X86Reg dst, int32_t imm);

	void orRR64(X86Reg dst, X86Reg src);
	void orRR32(X86Reg dst, X86Reg src);
	void orRI64(X86Reg dst, int32_t imm);
	void orRI32(X86Reg dst, int32_t imm);

	void xorRR64(X86Reg dst, X86Reg src);
	void xorRR32(X86Reg dst, X86Reg src);
	void xorRI64(X86Reg dst, int32_t imm);
	void xorRI32(X86Reg dst, int32_t imm);

	void notR64(X86Reg reg);
	void notR32(X86Reg reg);

	// Shifts (shift amount in CL or immediate)
	void shlR64_CL(X86Reg dst);
	void shlR64_imm(X86Reg dst, uint8_t imm);
	void shlR32_CL(X86Reg dst);
	void shlR32_imm(X86Reg dst, uint8_t imm);

	void shrR64_CL(X86Reg dst); // Logical shift right
	void shrR64_imm(X86Reg dst, uint8_t imm);
	void shrR32_CL(X86Reg dst);
	void shrR32_imm(X86Reg dst, uint8_t imm);

	void sarR64_CL(X86Reg dst); // Arithmetic shift right
	void sarR64_imm(X86Reg dst, uint8_t imm);
	void sarR32_CL(X86Reg dst);
	void sarR32_imm(X86Reg dst, uint8_t imm);

	//--------------------------------------------------------------------------
	// Comparisons
	//--------------------------------------------------------------------------

	void cmpRR64(X86Reg lhs, X86Reg rhs);
	void cmpRR32(X86Reg lhs, X86Reg rhs);
	void cmpRI64(X86Reg lhs, int32_t imm);
	void cmpRI32(X86Reg lhs, int32_t imm);
	void cmpRM64(X86Reg lhs, const X86Mem &rhs);

	void testRR64(X86Reg lhs, X86Reg rhs);
	void testRR32(X86Reg lhs, X86Reg rhs);
	void testRI32(X86Reg lhs, int32_t imm);

	// SETcc - set byte based on condition
	void setcc(X86Cond cond, X86Reg dst);

	//--------------------------------------------------------------------------
	// Control flow
	//--------------------------------------------------------------------------

	// JMP rel32
	void jmpRel32(int32_t offset);
	// JMP reg
	void jmpR(X86Reg reg);
	// Jcc rel32
	void jccRel32(X86Cond cond, int32_t offset);
	// Jcc rel8
	void jccRel8(X86Cond cond, int8_t offset);

	// CALL rel32
	void callRel32(int32_t offset);
	// CALL reg
	void callR(X86Reg reg);
	// CALL [mem]
	void callM(const X86Mem &mem);

	// RET
	void ret();

	//--------------------------------------------------------------------------
	// Stack operations
	//--------------------------------------------------------------------------

	void pushR(X86Reg reg);
	void popR(X86Reg reg);
	void pushI32(int32_t imm);

	//--------------------------------------------------------------------------
	// SSE2 floating-point (scalar double)
	//--------------------------------------------------------------------------

	// MOVSD xmm, xmm
	void movsdRR(X86Reg dst, X86Reg src);
	// MOVSD xmm, [mem]
	void movsdRM(X86Reg dst, const X86Mem &src);
	// MOVSD [mem], xmm
	void movsdMR(const X86Mem &dst, X86Reg src);

	// ADDSD
	void addsdRR(X86Reg dst, X86Reg src);
	void addsdRM(X86Reg dst, const X86Mem &src);

	// SUBSD
	void subsdRR(X86Reg dst, X86Reg src);
	void subsdRM(X86Reg dst, const X86Mem &src);

	// MULSD
	void mulsdRR(X86Reg dst, X86Reg src);
	void mulsdRM(X86Reg dst, const X86Mem &src);

	// DIVSD
	void divsdRR(X86Reg dst, X86Reg src);
	void divsdRM(X86Reg dst, const X86Mem &src);

	// UCOMISD (compare for flags)
	void ucomisdRR(X86Reg lhs, X86Reg rhs);

	// XORPD (for zeroing)
	void xorpdRR(X86Reg dst, X86Reg src);

	// CVTSI2SD (int to double)
	void cvtsi2sdRR64(X86Reg dst, X86Reg src); // 64-bit int
	void cvtsi2sdRR32(X86Reg dst, X86Reg src); // 32-bit int

	// CVTTSD2SI (double to int, truncate)
	void cvttsd2siRR64(X86Reg dst, X86Reg src);
	void cvttsd2siRR32(X86Reg dst, X86Reg src);

	// MOVQ (move quadword between XMM and GPR - for bitcast)
	void movqXmmToGpr(X86Reg gpr, X86Reg xmm); // MOVQ r64, xmm
	void movqGprToXmm(X86Reg xmm, X86Reg gpr); // MOVQ xmm, r64

	//--------------------------------------------------------------------------
	// Fixup support
	//--------------------------------------------------------------------------

	// Return current code offset (for calculating branch targets)
	size_t getOffset() const { return code.size(); }

	// Patch a 32-bit relative offset at the given position
	void patchRel32(size_t offset, int32_t value);

	// Calculate relative offset from `from` to `to`
	static int32_t calcRel32(size_t from, size_t to);
};

} // namespace backend
