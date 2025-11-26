#pragma once

#include "../mir/mir.h"
#include "x86_encoding.h"
#include <unordered_map>
#include <unordered_set>
#include <vector>

namespace backend {

//==============================================================================
// Calling Convention
//==============================================================================

enum class CallingConv {
	Win64,	// Microsoft x64 ABI
	SysV64, // System V AMD64 ABI (Linux)
};

//==============================================================================
// Register Allocation Info
//==============================================================================

struct RegAllocInfo {
	// Map from allocation key (encodes kind + regId) to physical register or stack slot
	// Use uint64_t to encode both MIRValueKind and regId to avoid collisions
	std::unordered_map<uint64_t, X86Reg> regMap;
	std::unordered_map<uint64_t, int32_t> stackSlots; // Stack offset from RBP

	// Track which virtual registers are alloca results (their stack slots hold the allocated memory)
	std::unordered_set<uint32_t> allocaRegs;

	// Total stack frame size
	uint32_t frameSize = 0;

	// Callee-saved registers that need to be preserved
	std::vector<X86Reg> usedCalleeSaved;

	// Windows x64 ABI: For functions returning structs > 8 bytes,
	// the hidden return pointer (passed in RCX) is saved to this stack offset
	int32_t hiddenRetPtrOffset = 0;
	bool hasHiddenRetPtr = false;

	// For functions with hidden return pointer, shifted parameters need to be
	// saved to stack in prologue. Each entry is {arrivalReg, stackOffset}
	std::vector<std::pair<X86Reg, int32_t>> shiftedParamSaves;

	// Float parameters arrive in volatile XMM0-3 and need to be copied to
	// non-volatile registers in prologue. Each entry is {arrivalXMM, destXMM}
	std::vector<std::pair<X86Reg, X86Reg>> floatParamSaves;

	// Integer parameters arrive in volatile RCX/RDX/R8/R9 and need to be copied
	// to non-volatile registers in prologue. Each entry is {arrivalReg, destReg}
	std::vector<std::pair<X86Reg, X86Reg>> intParamSaves;

	// Space reserved for outgoing stack arguments (beyond register args)
	// On Win64, this is for args beyond the 4th; on SysV, beyond the 6th
	uint32_t outgoingStackArgsSize = 0;
};

//==============================================================================
// Relocation Entry
//==============================================================================

struct Relocation {
	enum class Type {
		Rel32,		  // 32-bit relative (for jumps/calls within module)
		Abs64,		  // 64-bit absolute (for data references)
		FunctionCall, // Direct call to another function (within module)
		ImportCall,	  // Indirect call to imported function (via IAT)
		GlobalRef,	  // Reference to global variable
	};

	Type type;
	size_t offset;							   // Offset in code where fixup is needed
	std::string symbolName;					   // Target symbol name (for external refs)
	mir::MIRBasicBlock *targetBlock = nullptr; // For internal branches
	int32_t addend = 0;						   // Additional offset
};

//==============================================================================
// Code Section for a Function
//==============================================================================

struct FunctionCode {
	std::string name;
	std::vector<uint8_t> code;
	std::vector<Relocation> relocations;

	// Debug info
	std::vector<std::pair<size_t, int>> lineMap; // code offset -> source line
};

//==============================================================================
// x86-64 Code Generator
//==============================================================================

class X86CodeGen {
  private:
	CallingConv callingConv;
	X86Encoder encoder;
	mir::MIRModule *module = nullptr;
	mir::MIRFunction *currentFunction = nullptr;

	// Register allocation
	RegAllocInfo regAlloc;

	// Basic block address tracking
	std::unordered_map<mir::MIRBasicBlock *, size_t> blockOffsets;

	// Pending fixups for forward branches
	std::vector<std::pair<size_t, mir::MIRBasicBlock *>> pendingBranches;

	// Generated function code
	std::vector<FunctionCode> functionCodes;

	// Global data section
	std::vector<uint8_t> dataSection;
	std::unordered_map<std::string, size_t> globalOffsets;

	// Relocations
	std::vector<Relocation> relocations;

  public:
	explicit X86CodeGen(CallingConv cc = CallingConv::Win64) : callingConv(cc) {}

	// Generate code for entire module
	void generate(mir::MIRModule *mod);

	// Get generated code
	const std::vector<FunctionCode> &getFunctionCodes() const { return functionCodes; }
	const std::vector<uint8_t> &getDataSection() const { return dataSection; }
	const std::vector<Relocation> &getRelocations() const { return relocations; }

  private:
	//--------------------------------------------------------------------------
	// Function generation
	//--------------------------------------------------------------------------

	void generateFunction(mir::MIRFunction *func);
	void generatePrologue();
	void generateEpilogue();
	void generateBasicBlock(mir::MIRBasicBlock *block);
	void generateInstruction(mir::MIRInstruction *inst);

	//--------------------------------------------------------------------------
	// Register allocation (simple linear scan for now)
	//--------------------------------------------------------------------------

	void allocateRegisters(mir::MIRFunction *func);
	X86Reg getAllocatedReg(mir::MIRValue *value);
	X86Mem getStackSlot(mir::MIRValue *value);
	void spillToStack(X86Reg reg, int32_t stackOffset);
	void loadFromStack(X86Reg reg, int32_t stackOffset);

	//--------------------------------------------------------------------------
	// Instruction generation helpers
	//--------------------------------------------------------------------------

	// Get value into a register (loading from stack if needed)
	X86Reg loadValue(mir::MIRValue *value, X86Reg hint = X86Reg::RAX);
	X86Reg loadValueFloat(mir::MIRValue *value, X86Reg hint = X86Reg::XMM0);

	// Load two operands for binary operations, handling register conflicts
	// Returns pair of (lhs register, rhs register)
	std::pair<X86Reg, X86Reg> loadBinaryOperands(mir::MIRValue *lhs, mir::MIRValue *rhs,
												 X86Reg lhsHint, X86Reg rhsHint);
	std::pair<X86Reg, X86Reg> loadBinaryOperandsFloat(mir::MIRValue *lhs, mir::MIRValue *rhs,
													  X86Reg lhsHint, X86Reg rhsHint);

	// Get the physical register a value is currently in (or None if not in a register)
	X86Reg getValueLocation(mir::MIRValue *value);

	// Store result to allocated location
	void storeResult(mir::MIRValue *result, X86Reg reg);
	void storeResultFloat(mir::MIRValue *result, X86Reg xmmReg);

	// Arithmetic
	void genAdd(mir::MIRInstruction *inst);
	void genSub(mir::MIRInstruction *inst);
	void genMul(mir::MIRInstruction *inst);
	void genDiv(mir::MIRInstruction *inst, bool isSigned);
	void genRem(mir::MIRInstruction *inst, bool isSigned);
	void genNeg(mir::MIRInstruction *inst);

	// Floating-point arithmetic
	void genFAdd(mir::MIRInstruction *inst);
	void genFSub(mir::MIRInstruction *inst);
	void genFMul(mir::MIRInstruction *inst);
	void genFDiv(mir::MIRInstruction *inst);
	void genFNeg(mir::MIRInstruction *inst);

	// Bitwise
	void genAnd(mir::MIRInstruction *inst);
	void genOr(mir::MIRInstruction *inst);
	void genXor(mir::MIRInstruction *inst);
	void genShl(mir::MIRInstruction *inst);
	void genShr(mir::MIRInstruction *inst, bool isArithmetic);

	// Comparisons
	void genICmp(mir::MIRInstruction *inst);
	void genFCmp(mir::MIRInstruction *inst);
	X86Cond getConditionCode(mir::MIROpcode op);

	// Memory
	void genAlloca(mir::MIRInstruction *inst);
	void genLoad(mir::MIRInstruction *inst);
	void genStore(mir::MIRInstruction *inst);
	void genGEP(mir::MIRInstruction *inst);

	// Conversions
	void genTrunc(mir::MIRInstruction *inst);
	void genZExt(mir::MIRInstruction *inst);
	void genSExt(mir::MIRInstruction *inst);
	void genFPToSI(mir::MIRInstruction *inst);
	void genSIToFP(mir::MIRInstruction *inst);
	void genBitcast(mir::MIRInstruction *inst);
	void genCopyConversion(mir::MIRInstruction *inst);

	// Control flow
	void genBr(mir::MIRInstruction *inst);
	void genCondBr(mir::MIRInstruction *inst);
	void genRet(mir::MIRInstruction *inst);
	void genCall(mir::MIRInstruction *inst);

	// SSA
	void genPhi(mir::MIRInstruction *inst);
	void genCopy(mir::MIRInstruction *inst);

	//--------------------------------------------------------------------------
	// Calling convention helpers
	//--------------------------------------------------------------------------

	X86Reg getArgReg(unsigned index, bool isFloat);
	X86Reg getReturnReg(bool isFloat);
	std::vector<X86Reg> getCallerSavedRegs();
	std::vector<X86Reg> getCalleeSavedRegs();
	unsigned getShadowSpaceSize(); // Windows only

	//--------------------------------------------------------------------------
	// Fixup handling
	//--------------------------------------------------------------------------

	void resolveBranchFixups();
	void addRelocation(Relocation::Type type, size_t offset, const std::string &symbol);

	//--------------------------------------------------------------------------
	// Data section
	//--------------------------------------------------------------------------

	void generateGlobals();
	size_t emitConstantFloat(double value);
	size_t emitConstantInt64(int64_t value);
	size_t emitStringConstant(const std::string &str);
};

} // namespace backend
