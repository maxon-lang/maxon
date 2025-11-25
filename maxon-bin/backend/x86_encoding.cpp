#include "x86_encoding.h"
#include <cstring>
#include <stdexcept>

namespace backend {

//==============================================================================
// Raw byte emission
//==============================================================================

void X86Encoder::emit16(uint16_t val) {
	code.push_back(val & 0xFF);
	code.push_back((val >> 8) & 0xFF);
}

void X86Encoder::emit32(uint32_t val) {
	code.push_back(val & 0xFF);
	code.push_back((val >> 8) & 0xFF);
	code.push_back((val >> 16) & 0xFF);
	code.push_back((val >> 24) & 0xFF);
}

void X86Encoder::emit64(uint64_t val) {
	emit32(val & 0xFFFFFFFF);
	emit32((val >> 32) & 0xFFFFFFFF);
}

void X86Encoder::emitBytes(const uint8_t *data, size_t len) {
	code.insert(code.end(), data, data + len);
}

//==============================================================================
// Encoding helpers
//==============================================================================

void X86Encoder::emitREX(bool w, bool r, bool x, bool b) {
	uint8_t rex = 0x40;
	if (w)
		rex |= 0x08;
	if (r)
		rex |= 0x04;
	if (x)
		rex |= 0x02;
	if (b)
		rex |= 0x01;
	emit8(rex);
}

void X86Encoder::emitREX(bool w, X86Reg reg, X86Reg rm) {
	emitREX(w, isExtendedReg(reg), false, isExtendedReg(rm));
}

void X86Encoder::emitREXOpt(bool w, X86Reg reg, X86Reg rm) {
	// Only emit REX if needed
	if (w || isExtendedReg(reg) || isExtendedReg(rm)) {
		emitREX(w, reg, rm);
	}
}

void X86Encoder::emitModRM(uint8_t mod, uint8_t reg, uint8_t rm) {
	emit8((mod << 6) | ((reg & 0x7) << 3) | (rm & 0x7));
}

void X86Encoder::emitModRM(uint8_t mod, X86Reg reg, X86Reg rm) {
	emitModRM(mod, regIndex(reg), regIndex(rm));
}

void X86Encoder::emitSIB(uint8_t scale, uint8_t index, uint8_t base) {
	uint8_t scaleVal = 0;
	switch (scale) {
	case 1:
		scaleVal = 0;
		break;
	case 2:
		scaleVal = 1;
		break;
	case 4:
		scaleVal = 2;
		break;
	case 8:
		scaleVal = 3;
		break;
	default:
		throw std::runtime_error("Invalid SIB scale");
	}
	emit8((scaleVal << 6) | ((index & 0x7) << 3) | (base & 0x7));
}

void X86Encoder::emitModRMMem(X86Reg reg, const X86Mem &mem) {
	uint8_t regBits = regIndex(reg);

	if (mem.ripRelative) {
		// RIP-relative: ModRM mod=00, rm=101
		emitModRM(0x00, regBits, 0x05);
		emit32(static_cast<uint32_t>(mem.disp));
		return;
	}

	bool needsSIB = (mem.index != X86Reg::None) ||
					(mem.base == X86Reg::RSP) || (mem.base == X86Reg::R12);

	uint8_t baseBits = (mem.base != X86Reg::None) ? regIndex(mem.base) : 0x05;

	// Determine mod based on displacement size
	uint8_t mod;
	if (mem.base == X86Reg::None && !needsSIB) {
		mod = 0x00; // Absolute address (with SIB) or RIP-relative
	} else if (mem.disp == 0 && baseBits != 0x05) {
		mod = 0x00; // No displacement (except RBP/R13 which needs disp8)
	} else if (mem.disp >= -128 && mem.disp <= 127) {
		mod = 0x01; // 8-bit displacement
	} else {
		mod = 0x02; // 32-bit displacement
	}

	if (needsSIB) {
		emitModRM(mod, regBits, 0x04); // SIB follows
		uint8_t indexBits = (mem.index != X86Reg::None) ? regIndex(mem.index) : 0x04;
		uint8_t sibBase = (mem.base != X86Reg::None) ? baseBits : 0x05;
		emitSIB(mem.scale, indexBits, sibBase);
	} else {
		emitModRM(mod, regBits, baseBits);
	}

	// Emit displacement
	if (mod == 0x00 && (mem.base == X86Reg::None || baseBits == 0x05)) {
		emit32(static_cast<uint32_t>(mem.disp));
	} else if (mod == 0x01) {
		emit8(static_cast<uint8_t>(mem.disp));
	} else if (mod == 0x02) {
		emit32(static_cast<uint32_t>(mem.disp));
	}
}

//==============================================================================
// Data movement
//==============================================================================

void X86Encoder::movRR64(X86Reg dst, X86Reg src) {
	emitREX(true, src, dst);
	emit8(0x89); // MOV r/m64, r64
	emitModRM(0x03, src, dst);
}

void X86Encoder::movRR32(X86Reg dst, X86Reg src) {
	emitREXOpt(false, src, dst);
	emit8(0x89); // MOV r/m32, r32
	emitModRM(0x03, src, dst);
}

void X86Encoder::movRI64(X86Reg dst, uint64_t imm) {
	// Check if we can use 32-bit sign-extended form
	if (imm <= 0x7FFFFFFF || imm >= 0xFFFFFFFF80000000ULL) {
		// MOV r64, imm32 (sign-extended)
		emitREX(true, X86Reg::RAX, dst);
		emit8(0xC7);
		emitModRM(0x03, 0, regIndex(dst));
		emit32(static_cast<uint32_t>(imm));
	} else {
		// MOV r64, imm64
		emitREX(true, X86Reg::RAX, dst);
		emit8(0xB8 + regIndex(dst));
		emit64(imm);
	}
}

void X86Encoder::movRI32(X86Reg dst, uint32_t imm) {
	if (isExtendedReg(dst)) {
		emitREX(false, X86Reg::RAX, dst);
	}
	emit8(0xB8 + regIndex(dst));
	emit32(imm);
}

void X86Encoder::movRM64(X86Reg dst, const X86Mem &src) {
	emitREX(true, dst, src.base);
	emit8(0x8B); // MOV r64, r/m64
	emitModRMMem(dst, src);
}

void X86Encoder::movRM32(X86Reg dst, const X86Mem &src) {
	if (isExtendedReg(dst) || (src.base != X86Reg::None && isExtendedReg(src.base))) {
		emitREXOpt(false, dst, src.base);
	}
	emit8(0x8B); // MOV r32, r/m32
	emitModRMMem(dst, src);
}

void X86Encoder::movRM8(X86Reg dst, const X86Mem &src) {
	// Need REX for SPL, BPL, SIL, DIL
	if (isExtendedReg(dst) || (src.base != X86Reg::None && isExtendedReg(src.base)) ||
		static_cast<uint8_t>(dst) >= 4) {
		emitREXOpt(false, dst, src.base);
	}
	emit8(0x8A); // MOV r8, r/m8
	emitModRMMem(dst, src);
}

void X86Encoder::movMR64(const X86Mem &dst, X86Reg src) {
	emitREX(true, src, dst.base);
	emit8(0x89); // MOV r/m64, r64
	emitModRMMem(src, dst);
}

void X86Encoder::movMR32(const X86Mem &dst, X86Reg src) {
	if (isExtendedReg(src) || (dst.base != X86Reg::None && isExtendedReg(dst.base))) {
		emitREXOpt(false, src, dst.base);
	}
	emit8(0x89); // MOV r/m32, r32
	emitModRMMem(src, dst);
}

void X86Encoder::movMR8(const X86Mem &dst, X86Reg src) {
	if (isExtendedReg(src) || (dst.base != X86Reg::None && isExtendedReg(dst.base)) ||
		static_cast<uint8_t>(src) >= 4) {
		emitREXOpt(false, src, dst.base);
	}
	emit8(0x88); // MOV r/m8, r8
	emitModRMMem(src, dst);
}

void X86Encoder::movMI32(const X86Mem &dst, int32_t imm) {
	emitREX(true, X86Reg::RAX, dst.base);
	emit8(0xC7); // MOV r/m64, imm32 (sign-extended)
	emitModRMMem(X86Reg::RAX, dst);
	emit32(static_cast<uint32_t>(imm));
}

void X86Encoder::lea64(X86Reg dst, const X86Mem &src) {
	emitREX(true, dst, src.base);
	emit8(0x8D); // LEA r64, m
	emitModRMMem(dst, src);
}

void X86Encoder::movzxRR32_8(X86Reg dst, X86Reg src) {
	if (isExtendedReg(dst) || isExtendedReg(src) || static_cast<uint8_t>(src) >= 4) {
		emitREXOpt(false, dst, src);
	}
	emit8(0x0F);
	emit8(0xB6); // MOVZX r32, r/m8
	emitModRM(0x03, dst, src);
}

void X86Encoder::movzxRM32_8(X86Reg dst, const X86Mem &src) {
	if (isExtendedReg(dst) || (src.base != X86Reg::None && isExtendedReg(src.base))) {
		emitREXOpt(false, dst, src.base);
	}
	emit8(0x0F);
	emit8(0xB6); // MOVZX r32, r/m8
	emitModRMMem(dst, src);
}

void X86Encoder::movsxRR64_32(X86Reg dst, X86Reg src) {
	emitREX(true, dst, src);
	emit8(0x63); // MOVSXD r64, r/m32
	emitModRM(0x03, dst, src);
}

void X86Encoder::movsxRR32_8(X86Reg dst, X86Reg src) {
	if (isExtendedReg(dst) || isExtendedReg(src) || static_cast<uint8_t>(src) >= 4) {
		emitREXOpt(false, dst, src);
	}
	emit8(0x0F);
	emit8(0xBE); // MOVSX r32, r/m8
	emitModRM(0x03, dst, src);
}

//==============================================================================
// Arithmetic (integer)
//==============================================================================

void X86Encoder::addRR64(X86Reg dst, X86Reg src) {
	emitREX(true, src, dst);
	emit8(0x01); // ADD r/m64, r64
	emitModRM(0x03, src, dst);
}

void X86Encoder::addRR32(X86Reg dst, X86Reg src) {
	emitREXOpt(false, src, dst);
	emit8(0x01); // ADD r/m32, r32
	emitModRM(0x03, src, dst);
}

void X86Encoder::addRI64(X86Reg dst, int32_t imm) {
	emitREX(true, X86Reg::RAX, dst);
	if (imm >= -128 && imm <= 127) {
		emit8(0x83); // ADD r/m64, imm8
		emitModRM(0x03, 0, regIndex(dst));
		emit8(static_cast<uint8_t>(imm));
	} else {
		emit8(0x81); // ADD r/m64, imm32
		emitModRM(0x03, 0, regIndex(dst));
		emit32(static_cast<uint32_t>(imm));
	}
}

void X86Encoder::addRI32(X86Reg dst, int32_t imm) {
	if (isExtendedReg(dst)) {
		emitREX(false, X86Reg::RAX, dst);
	}
	if (imm >= -128 && imm <= 127) {
		emit8(0x83); // ADD r/m32, imm8
		emitModRM(0x03, 0, regIndex(dst));
		emit8(static_cast<uint8_t>(imm));
	} else {
		emit8(0x81); // ADD r/m32, imm32
		emitModRM(0x03, 0, regIndex(dst));
		emit32(static_cast<uint32_t>(imm));
	}
}

void X86Encoder::addRM64(X86Reg dst, const X86Mem &src) {
	emitREX(true, dst, src.base);
	emit8(0x03); // ADD r64, r/m64
	emitModRMMem(dst, src);
}

void X86Encoder::addMR64(const X86Mem &dst, X86Reg src) {
	emitREX(true, src, dst.base);
	emit8(0x01); // ADD r/m64, r64
	emitModRMMem(src, dst);
}

void X86Encoder::subRR64(X86Reg dst, X86Reg src) {
	emitREX(true, src, dst);
	emit8(0x29); // SUB r/m64, r64
	emitModRM(0x03, src, dst);
}

void X86Encoder::subRR32(X86Reg dst, X86Reg src) {
	emitREXOpt(false, src, dst);
	emit8(0x29); // SUB r/m32, r32
	emitModRM(0x03, src, dst);
}

void X86Encoder::subRI64(X86Reg dst, int32_t imm) {
	emitREX(true, X86Reg::RAX, dst);
	if (imm >= -128 && imm <= 127) {
		emit8(0x83); // SUB r/m64, imm8
		emitModRM(0x03, 5, regIndex(dst));
		emit8(static_cast<uint8_t>(imm));
	} else {
		emit8(0x81); // SUB r/m64, imm32
		emitModRM(0x03, 5, regIndex(dst));
		emit32(static_cast<uint32_t>(imm));
	}
}

void X86Encoder::subRI32(X86Reg dst, int32_t imm) {
	if (isExtendedReg(dst)) {
		emitREX(false, X86Reg::RAX, dst);
	}
	if (imm >= -128 && imm <= 127) {
		emit8(0x83); // SUB r/m32, imm8
		emitModRM(0x03, 5, regIndex(dst));
		emit8(static_cast<uint8_t>(imm));
	} else {
		emit8(0x81); // SUB r/m32, imm32
		emitModRM(0x03, 5, regIndex(dst));
		emit32(static_cast<uint32_t>(imm));
	}
}

void X86Encoder::imulRR64(X86Reg dst, X86Reg src) {
	emitREX(true, dst, src);
	emit8(0x0F);
	emit8(0xAF); // IMUL r64, r/m64
	emitModRM(0x03, dst, src);
}

void X86Encoder::imulRR32(X86Reg dst, X86Reg src) {
	emitREXOpt(false, dst, src);
	emit8(0x0F);
	emit8(0xAF); // IMUL r32, r/m32
	emitModRM(0x03, dst, src);
}

void X86Encoder::imulRRI64(X86Reg dst, X86Reg src, int32_t imm) {
	emitREX(true, dst, src);
	if (imm >= -128 && imm <= 127) {
		emit8(0x6B); // IMUL r64, r/m64, imm8
		emitModRM(0x03, dst, src);
		emit8(static_cast<uint8_t>(imm));
	} else {
		emit8(0x69); // IMUL r64, r/m64, imm32
		emitModRM(0x03, dst, src);
		emit32(static_cast<uint32_t>(imm));
	}
}

void X86Encoder::imulRRI32(X86Reg dst, X86Reg src, int32_t imm) {
	emitREXOpt(false, dst, src);
	if (imm >= -128 && imm <= 127) {
		emit8(0x6B); // IMUL r32, r/m32, imm8
		emitModRM(0x03, dst, src);
		emit8(static_cast<uint8_t>(imm));
	} else {
		emit8(0x69); // IMUL r32, r/m32, imm32
		emitModRM(0x03, dst, src);
		emit32(static_cast<uint32_t>(imm));
	}
}

void X86Encoder::idivR64(X86Reg divisor) {
	emitREX(true, X86Reg::RAX, divisor);
	emit8(0xF7); // IDIV r/m64
	emitModRM(0x03, 7, regIndex(divisor));
}

void X86Encoder::idivR32(X86Reg divisor) {
	if (isExtendedReg(divisor)) {
		emitREX(false, X86Reg::RAX, divisor);
	}
	emit8(0xF7); // IDIV r/m32
	emitModRM(0x03, 7, regIndex(divisor));
}

void X86Encoder::divR64(X86Reg divisor) {
	emitREX(true, X86Reg::RAX, divisor);
	emit8(0xF7); // DIV r/m64
	emitModRM(0x03, 6, regIndex(divisor));
}

void X86Encoder::divR32(X86Reg divisor) {
	if (isExtendedReg(divisor)) {
		emitREX(false, X86Reg::RAX, divisor);
	}
	emit8(0xF7); // DIV r/m32
	emitModRM(0x03, 6, regIndex(divisor));
}

void X86Encoder::negR64(X86Reg reg) {
	emitREX(true, X86Reg::RAX, reg);
	emit8(0xF7); // NEG r/m64
	emitModRM(0x03, 3, regIndex(reg));
}

void X86Encoder::negR32(X86Reg reg) {
	if (isExtendedReg(reg)) {
		emitREX(false, X86Reg::RAX, reg);
	}
	emit8(0xF7); // NEG r/m32
	emitModRM(0x03, 3, regIndex(reg));
}

void X86Encoder::cqo() {
	emitREX(true, X86Reg::RAX, X86Reg::RAX);
	emit8(0x99); // CQO
}

void X86Encoder::cdq() {
	emit8(0x99); // CDQ
}

//==============================================================================
// Bitwise operations
//==============================================================================

void X86Encoder::andRR64(X86Reg dst, X86Reg src) {
	emitREX(true, src, dst);
	emit8(0x21); // AND r/m64, r64
	emitModRM(0x03, src, dst);
}

void X86Encoder::andRR32(X86Reg dst, X86Reg src) {
	emitREXOpt(false, src, dst);
	emit8(0x21); // AND r/m32, r32
	emitModRM(0x03, src, dst);
}

void X86Encoder::andRI64(X86Reg dst, int32_t imm) {
	emitREX(true, X86Reg::RAX, dst);
	if (imm >= -128 && imm <= 127) {
		emit8(0x83);
		emitModRM(0x03, 4, regIndex(dst));
		emit8(static_cast<uint8_t>(imm));
	} else {
		emit8(0x81);
		emitModRM(0x03, 4, regIndex(dst));
		emit32(static_cast<uint32_t>(imm));
	}
}

void X86Encoder::andRI32(X86Reg dst, int32_t imm) {
	if (isExtendedReg(dst)) {
		emitREX(false, X86Reg::RAX, dst);
	}
	if (imm >= -128 && imm <= 127) {
		emit8(0x83);
		emitModRM(0x03, 4, regIndex(dst));
		emit8(static_cast<uint8_t>(imm));
	} else {
		emit8(0x81);
		emitModRM(0x03, 4, regIndex(dst));
		emit32(static_cast<uint32_t>(imm));
	}
}

void X86Encoder::orRR64(X86Reg dst, X86Reg src) {
	emitREX(true, src, dst);
	emit8(0x09); // OR r/m64, r64
	emitModRM(0x03, src, dst);
}

void X86Encoder::orRR32(X86Reg dst, X86Reg src) {
	emitREXOpt(false, src, dst);
	emit8(0x09); // OR r/m32, r32
	emitModRM(0x03, src, dst);
}

void X86Encoder::orRI64(X86Reg dst, int32_t imm) {
	emitREX(true, X86Reg::RAX, dst);
	if (imm >= -128 && imm <= 127) {
		emit8(0x83);
		emitModRM(0x03, 1, regIndex(dst));
		emit8(static_cast<uint8_t>(imm));
	} else {
		emit8(0x81);
		emitModRM(0x03, 1, regIndex(dst));
		emit32(static_cast<uint32_t>(imm));
	}
}

void X86Encoder::orRI32(X86Reg dst, int32_t imm) {
	if (isExtendedReg(dst)) {
		emitREX(false, X86Reg::RAX, dst);
	}
	if (imm >= -128 && imm <= 127) {
		emit8(0x83);
		emitModRM(0x03, 1, regIndex(dst));
		emit8(static_cast<uint8_t>(imm));
	} else {
		emit8(0x81);
		emitModRM(0x03, 1, regIndex(dst));
		emit32(static_cast<uint32_t>(imm));
	}
}

void X86Encoder::xorRR64(X86Reg dst, X86Reg src) {
	emitREX(true, src, dst);
	emit8(0x31); // XOR r/m64, r64
	emitModRM(0x03, src, dst);
}

void X86Encoder::xorRR32(X86Reg dst, X86Reg src) {
	emitREXOpt(false, src, dst);
	emit8(0x31); // XOR r/m32, r32
	emitModRM(0x03, src, dst);
}

void X86Encoder::xorRI64(X86Reg dst, int32_t imm) {
	emitREX(true, X86Reg::RAX, dst);
	if (imm >= -128 && imm <= 127) {
		emit8(0x83);
		emitModRM(0x03, 6, regIndex(dst));
		emit8(static_cast<uint8_t>(imm));
	} else {
		emit8(0x81);
		emitModRM(0x03, 6, regIndex(dst));
		emit32(static_cast<uint32_t>(imm));
	}
}

void X86Encoder::xorRI32(X86Reg dst, int32_t imm) {
	if (isExtendedReg(dst)) {
		emitREX(false, X86Reg::RAX, dst);
	}
	if (imm >= -128 && imm <= 127) {
		emit8(0x83);
		emitModRM(0x03, 6, regIndex(dst));
		emit8(static_cast<uint8_t>(imm));
	} else {
		emit8(0x81);
		emitModRM(0x03, 6, regIndex(dst));
		emit32(static_cast<uint32_t>(imm));
	}
}

void X86Encoder::notR64(X86Reg reg) {
	emitREX(true, X86Reg::RAX, reg);
	emit8(0xF7);
	emitModRM(0x03, 2, regIndex(reg));
}

void X86Encoder::notR32(X86Reg reg) {
	if (isExtendedReg(reg)) {
		emitREX(false, X86Reg::RAX, reg);
	}
	emit8(0xF7);
	emitModRM(0x03, 2, regIndex(reg));
}

// Shifts
void X86Encoder::shlR64_CL(X86Reg dst) {
	emitREX(true, X86Reg::RAX, dst);
	emit8(0xD3);
	emitModRM(0x03, 4, regIndex(dst));
}

void X86Encoder::shlR64_imm(X86Reg dst, uint8_t imm) {
	emitREX(true, X86Reg::RAX, dst);
	if (imm == 1) {
		emit8(0xD1);
		emitModRM(0x03, 4, regIndex(dst));
	} else {
		emit8(0xC1);
		emitModRM(0x03, 4, regIndex(dst));
		emit8(imm);
	}
}

void X86Encoder::shlR32_CL(X86Reg dst) {
	if (isExtendedReg(dst)) {
		emitREX(false, X86Reg::RAX, dst);
	}
	emit8(0xD3);
	emitModRM(0x03, 4, regIndex(dst));
}

void X86Encoder::shlR32_imm(X86Reg dst, uint8_t imm) {
	if (isExtendedReg(dst)) {
		emitREX(false, X86Reg::RAX, dst);
	}
	if (imm == 1) {
		emit8(0xD1);
		emitModRM(0x03, 4, regIndex(dst));
	} else {
		emit8(0xC1);
		emitModRM(0x03, 4, regIndex(dst));
		emit8(imm);
	}
}

void X86Encoder::shrR64_CL(X86Reg dst) {
	emitREX(true, X86Reg::RAX, dst);
	emit8(0xD3);
	emitModRM(0x03, 5, regIndex(dst));
}

void X86Encoder::shrR64_imm(X86Reg dst, uint8_t imm) {
	emitREX(true, X86Reg::RAX, dst);
	if (imm == 1) {
		emit8(0xD1);
		emitModRM(0x03, 5, regIndex(dst));
	} else {
		emit8(0xC1);
		emitModRM(0x03, 5, regIndex(dst));
		emit8(imm);
	}
}

void X86Encoder::shrR32_CL(X86Reg dst) {
	if (isExtendedReg(dst)) {
		emitREX(false, X86Reg::RAX, dst);
	}
	emit8(0xD3);
	emitModRM(0x03, 5, regIndex(dst));
}

void X86Encoder::shrR32_imm(X86Reg dst, uint8_t imm) {
	if (isExtendedReg(dst)) {
		emitREX(false, X86Reg::RAX, dst);
	}
	if (imm == 1) {
		emit8(0xD1);
		emitModRM(0x03, 5, regIndex(dst));
	} else {
		emit8(0xC1);
		emitModRM(0x03, 5, regIndex(dst));
		emit8(imm);
	}
}

void X86Encoder::sarR64_CL(X86Reg dst) {
	emitREX(true, X86Reg::RAX, dst);
	emit8(0xD3);
	emitModRM(0x03, 7, regIndex(dst));
}

void X86Encoder::sarR64_imm(X86Reg dst, uint8_t imm) {
	emitREX(true, X86Reg::RAX, dst);
	if (imm == 1) {
		emit8(0xD1);
		emitModRM(0x03, 7, regIndex(dst));
	} else {
		emit8(0xC1);
		emitModRM(0x03, 7, regIndex(dst));
		emit8(imm);
	}
}

void X86Encoder::sarR32_CL(X86Reg dst) {
	if (isExtendedReg(dst)) {
		emitREX(false, X86Reg::RAX, dst);
	}
	emit8(0xD3);
	emitModRM(0x03, 7, regIndex(dst));
}

void X86Encoder::sarR32_imm(X86Reg dst, uint8_t imm) {
	if (isExtendedReg(dst)) {
		emitREX(false, X86Reg::RAX, dst);
	}
	if (imm == 1) {
		emit8(0xD1);
		emitModRM(0x03, 7, regIndex(dst));
	} else {
		emit8(0xC1);
		emitModRM(0x03, 7, regIndex(dst));
		emit8(imm);
	}
}

//==============================================================================
// Comparisons
//==============================================================================

void X86Encoder::cmpRR64(X86Reg lhs, X86Reg rhs) {
	emitREX(true, rhs, lhs);
	emit8(0x39); // CMP r/m64, r64
	emitModRM(0x03, rhs, lhs);
}

void X86Encoder::cmpRR32(X86Reg lhs, X86Reg rhs) {
	emitREXOpt(false, rhs, lhs);
	emit8(0x39); // CMP r/m32, r32
	emitModRM(0x03, rhs, lhs);
}

void X86Encoder::cmpRI64(X86Reg lhs, int32_t imm) {
	emitREX(true, X86Reg::RAX, lhs);
	if (imm >= -128 && imm <= 127) {
		emit8(0x83);
		emitModRM(0x03, 7, regIndex(lhs));
		emit8(static_cast<uint8_t>(imm));
	} else {
		emit8(0x81);
		emitModRM(0x03, 7, regIndex(lhs));
		emit32(static_cast<uint32_t>(imm));
	}
}

void X86Encoder::cmpRI32(X86Reg lhs, int32_t imm) {
	if (isExtendedReg(lhs)) {
		emitREX(false, X86Reg::RAX, lhs);
	}
	if (imm >= -128 && imm <= 127) {
		emit8(0x83);
		emitModRM(0x03, 7, regIndex(lhs));
		emit8(static_cast<uint8_t>(imm));
	} else {
		emit8(0x81);
		emitModRM(0x03, 7, regIndex(lhs));
		emit32(static_cast<uint32_t>(imm));
	}
}

void X86Encoder::cmpRM64(X86Reg lhs, const X86Mem &rhs) {
	emitREX(true, lhs, rhs.base);
	emit8(0x3B); // CMP r64, r/m64
	emitModRMMem(lhs, rhs);
}

void X86Encoder::testRR64(X86Reg lhs, X86Reg rhs) {
	emitREX(true, rhs, lhs);
	emit8(0x85); // TEST r/m64, r64
	emitModRM(0x03, rhs, lhs);
}

void X86Encoder::testRR32(X86Reg lhs, X86Reg rhs) {
	emitREXOpt(false, rhs, lhs);
	emit8(0x85); // TEST r/m32, r32
	emitModRM(0x03, rhs, lhs);
}

void X86Encoder::testRI32(X86Reg lhs, int32_t imm) {
	if (isExtendedReg(lhs)) {
		emitREX(false, X86Reg::RAX, lhs);
	}
	emit8(0xF7);
	emitModRM(0x03, 0, regIndex(lhs));
	emit32(static_cast<uint32_t>(imm));
}

void X86Encoder::setcc(X86Cond cond, X86Reg dst) {
	if (isExtendedReg(dst) || static_cast<uint8_t>(dst) >= 4) {
		emitREX(false, X86Reg::RAX, dst);
	}
	emit8(0x0F);
	emit8(0x90 + static_cast<uint8_t>(cond));
	emitModRM(0x03, 0, regIndex(dst));
}

//==============================================================================
// Control flow
//==============================================================================

void X86Encoder::jmpRel32(int32_t offset) {
	emit8(0xE9); // JMP rel32
	emit32(static_cast<uint32_t>(offset));
}

void X86Encoder::jmpR(X86Reg reg) {
	if (isExtendedReg(reg)) {
		emitREX(false, X86Reg::RAX, reg);
	}
	emit8(0xFF);
	emitModRM(0x03, 4, regIndex(reg));
}

void X86Encoder::jccRel32(X86Cond cond, int32_t offset) {
	emit8(0x0F);
	emit8(0x80 + static_cast<uint8_t>(cond));
	emit32(static_cast<uint32_t>(offset));
}

void X86Encoder::jccRel8(X86Cond cond, int8_t offset) {
	emit8(0x70 + static_cast<uint8_t>(cond));
	emit8(static_cast<uint8_t>(offset));
}

void X86Encoder::callRel32(int32_t offset) {
	emit8(0xE8); // CALL rel32
	emit32(static_cast<uint32_t>(offset));
}

void X86Encoder::callR(X86Reg reg) {
	if (isExtendedReg(reg)) {
		emitREX(false, X86Reg::RAX, reg);
	}
	emit8(0xFF);
	emitModRM(0x03, 2, regIndex(reg));
}

void X86Encoder::callM(const X86Mem &mem) {
	if (mem.base != X86Reg::None && isExtendedReg(mem.base)) {
		emitREX(false, X86Reg::RAX, mem.base);
	}
	emit8(0xFF);
	emitModRMMem(static_cast<X86Reg>(2), mem);
}

void X86Encoder::ret() {
	emit8(0xC3);
}

//==============================================================================
// Stack operations
//==============================================================================

void X86Encoder::pushR(X86Reg reg) {
	if (isExtendedReg(reg)) {
		emitREX(false, X86Reg::RAX, reg);
	}
	emit8(0x50 + regIndex(reg));
}

void X86Encoder::popR(X86Reg reg) {
	if (isExtendedReg(reg)) {
		emitREX(false, X86Reg::RAX, reg);
	}
	emit8(0x58 + regIndex(reg));
}

void X86Encoder::pushI32(int32_t imm) {
	if (imm >= -128 && imm <= 127) {
		emit8(0x6A);
		emit8(static_cast<uint8_t>(imm));
	} else {
		emit8(0x68);
		emit32(static_cast<uint32_t>(imm));
	}
}

//==============================================================================
// SSE2 floating-point
//==============================================================================

void X86Encoder::movsdRR(X86Reg dst, X86Reg src) {
	emit8(0xF2);
	if (isExtendedReg(dst) || isExtendedReg(src)) {
		emitREX(false, dst, src);
	}
	emit8(0x0F);
	emit8(0x10); // MOVSD xmm1, xmm2
	emitModRM(0x03, dst, src);
}

void X86Encoder::movsdRM(X86Reg dst, const X86Mem &src) {
	emit8(0xF2);
	if (isExtendedReg(dst) || (src.base != X86Reg::None && isExtendedReg(src.base))) {
		emitREXOpt(false, dst, src.base);
	}
	emit8(0x0F);
	emit8(0x10); // MOVSD xmm, m64
	emitModRMMem(dst, src);
}

void X86Encoder::movsdMR(const X86Mem &dst, X86Reg src) {
	emit8(0xF2);
	if (isExtendedReg(src) || (dst.base != X86Reg::None && isExtendedReg(dst.base))) {
		emitREXOpt(false, src, dst.base);
	}
	emit8(0x0F);
	emit8(0x11); // MOVSD m64, xmm
	emitModRMMem(src, dst);
}

void X86Encoder::addsdRR(X86Reg dst, X86Reg src) {
	emit8(0xF2);
	if (isExtendedReg(dst) || isExtendedReg(src)) {
		emitREXOpt(false, dst, src);
	}
	emit8(0x0F);
	emit8(0x58);
	emitModRM(0x03, dst, src);
}

void X86Encoder::addsdRM(X86Reg dst, const X86Mem &src) {
	emit8(0xF2);
	if (isExtendedReg(dst) || (src.base != X86Reg::None && isExtendedReg(src.base))) {
		emitREXOpt(false, dst, src.base);
	}
	emit8(0x0F);
	emit8(0x58);
	emitModRMMem(dst, src);
}

void X86Encoder::subsdRR(X86Reg dst, X86Reg src) {
	emit8(0xF2);
	if (isExtendedReg(dst) || isExtendedReg(src)) {
		emitREXOpt(false, dst, src);
	}
	emit8(0x0F);
	emit8(0x5C);
	emitModRM(0x03, dst, src);
}

void X86Encoder::subsdRM(X86Reg dst, const X86Mem &src) {
	emit8(0xF2);
	if (isExtendedReg(dst) || (src.base != X86Reg::None && isExtendedReg(src.base))) {
		emitREXOpt(false, dst, src.base);
	}
	emit8(0x0F);
	emit8(0x5C);
	emitModRMMem(dst, src);
}

void X86Encoder::mulsdRR(X86Reg dst, X86Reg src) {
	emit8(0xF2);
	if (isExtendedReg(dst) || isExtendedReg(src)) {
		emitREXOpt(false, dst, src);
	}
	emit8(0x0F);
	emit8(0x59);
	emitModRM(0x03, dst, src);
}

void X86Encoder::mulsdRM(X86Reg dst, const X86Mem &src) {
	emit8(0xF2);
	if (isExtendedReg(dst) || (src.base != X86Reg::None && isExtendedReg(src.base))) {
		emitREXOpt(false, dst, src.base);
	}
	emit8(0x0F);
	emit8(0x59);
	emitModRMMem(dst, src);
}

void X86Encoder::divsdRR(X86Reg dst, X86Reg src) {
	emit8(0xF2);
	if (isExtendedReg(dst) || isExtendedReg(src)) {
		emitREXOpt(false, dst, src);
	}
	emit8(0x0F);
	emit8(0x5E);
	emitModRM(0x03, dst, src);
}

void X86Encoder::divsdRM(X86Reg dst, const X86Mem &src) {
	emit8(0xF2);
	if (isExtendedReg(dst) || (src.base != X86Reg::None && isExtendedReg(src.base))) {
		emitREXOpt(false, dst, src.base);
	}
	emit8(0x0F);
	emit8(0x5E);
	emitModRMMem(dst, src);
}

void X86Encoder::ucomisdRR(X86Reg lhs, X86Reg rhs) {
	emit8(0x66);
	if (isExtendedReg(lhs) || isExtendedReg(rhs)) {
		emitREXOpt(false, lhs, rhs);
	}
	emit8(0x0F);
	emit8(0x2E);
	emitModRM(0x03, lhs, rhs);
}

void X86Encoder::xorpdRR(X86Reg dst, X86Reg src) {
	emit8(0x66);
	if (isExtendedReg(dst) || isExtendedReg(src)) {
		emitREXOpt(false, dst, src);
	}
	emit8(0x0F);
	emit8(0x57);
	emitModRM(0x03, dst, src);
}

void X86Encoder::cvtsi2sdRR64(X86Reg dst, X86Reg src) {
	emit8(0xF2);
	emitREX(true, dst, src);
	emit8(0x0F);
	emit8(0x2A);
	emitModRM(0x03, dst, src);
}

void X86Encoder::cvtsi2sdRR32(X86Reg dst, X86Reg src) {
	emit8(0xF2);
	if (isExtendedReg(dst) || isExtendedReg(src)) {
		emitREXOpt(false, dst, src);
	}
	emit8(0x0F);
	emit8(0x2A);
	emitModRM(0x03, dst, src);
}

void X86Encoder::cvttsd2siRR64(X86Reg dst, X86Reg src) {
	emit8(0xF2);
	emitREX(true, dst, src);
	emit8(0x0F);
	emit8(0x2C);
	emitModRM(0x03, dst, src);
}

void X86Encoder::cvttsd2siRR32(X86Reg dst, X86Reg src) {
	emit8(0xF2);
	if (isExtendedReg(dst) || isExtendedReg(src)) {
		emitREXOpt(false, dst, src);
	}
	emit8(0x0F);
	emit8(0x2C);
	emitModRM(0x03, dst, src);
}

//==============================================================================
// Fixup support
//==============================================================================

void X86Encoder::patchRel32(size_t offset, int32_t value) {
	code[offset] = value & 0xFF;
	code[offset + 1] = (value >> 8) & 0xFF;
	code[offset + 2] = (value >> 16) & 0xFF;
	code[offset + 3] = (value >> 24) & 0xFF;
}

int32_t X86Encoder::calcRel32(size_t from, size_t to) {
	// Relative offset is calculated from the end of the instruction
	return static_cast<int32_t>(to) - static_cast<int32_t>(from);
}

} // namespace backend
