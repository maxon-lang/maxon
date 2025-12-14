#pragma once

#include <cstdint>
#include <memory>
#include <string>
#include <unordered_map>
#include <vector>

namespace mir {

// Forward declarations
class MIRBasicBlock;
class MIRFunction;
class MIRModule;
class MIRInstruction;

//==============================================================================
// Type System
//==============================================================================

enum class MIRTypeKind {
	Void,
	Int1,	  // bool
	Int8,	  // char/byte
	Int32,	  // 32-bit integer (for internal use, FFI)
	Int64,	  // int (default integer type in Maxon)
	Float64,  // double
	Ptr,	  // pointer (opaque, 64-bit on x64)
	Array,	  // fixed-size array
	Struct,	  // user-defined struct
	Optional, // discriminated union with tag (T or nil)
};

class MIRType {
  public:
	MIRTypeKind kind;

	// For arrays: element type and count
	MIRType *elementType = nullptr;
	uint64_t arraySize = 0;

	// For structs: name and field types
	std::string structName;
	std::vector<MIRType *> fieldTypes;

	// For optionals: wrapped type
	MIRType *wrappedType = nullptr;

	// Size in bytes (computed lazily)
	uint64_t sizeInBytes = 0;
	uint64_t alignmentInBytes = 0;

	// Track if this type is actually used in generated code (for filtering IR output)
	bool used = false;

	MIRType(MIRTypeKind k) : kind(k) { computeSize(); }

	static MIRType *getVoid();
	static MIRType *getInt1();
	static MIRType *getInt8();
	static MIRType *getInt32();
	static MIRType *getInt64();
	static MIRType *getFloat64();
	static MIRType *getPtr();
	static MIRType *getArray(MIRType *elem, uint64_t size);
	static MIRType *getStruct(const std::string &name, const std::vector<MIRType *> &fields);
	static MIRType *getOptional(MIRType *wrapped);

	bool isInteger() const { return kind == MIRTypeKind::Int1 || kind == MIRTypeKind::Int8 ||
									kind == MIRTypeKind::Int32 || kind == MIRTypeKind::Int64; }
	bool isFloat() const { return kind == MIRTypeKind::Float64; }
	bool isPointer() const { return kind == MIRTypeKind::Ptr; }
	bool isVoid() const { return kind == MIRTypeKind::Void; }
	bool isArray() const { return kind == MIRTypeKind::Array; }
	bool isStruct() const { return kind == MIRTypeKind::Struct; }
	bool isOptional() const { return kind == MIRTypeKind::Optional; }

	std::string toString() const;

	// Allow recomputation of size (needed when building struct types)
	void recomputeSize() { computeSize(); }

	// Recompute sizes of all cached Optional types (call after filling in struct fields)
	static void recomputeAllOptionalSizes();

  private:
	void computeSize();
};

//==============================================================================
// Values (SSA Virtual Registers, Constants, Globals)
//==============================================================================

enum class MIRValueKind {
	VirtualReg,	   // SSA virtual register (result of an instruction)
	ConstantInt,   // Integer constant
	ConstantFloat, // Floating-point constant
	ConstantNull,  // Null pointer
	Global,		   // Global variable reference
	FunctionRef,   // Function address reference (function pointer)
	Parameter,	   // Function parameter
	BasicBlockRef, // Reference to a basic block (for branches)
};

class MIRValue {
  public:
	MIRValueKind kind;
	MIRType *type;

	// Virtual register ID (for VirtualReg)
	uint32_t regId = 0;

	// Constant values
	int64_t intValue = 0;
	double floatValue = 0.0;

	// For globals and parameters
	std::string name;

	// True if this is a reference to a global variable
	bool isGlobalRef = false;

	// For basic block references
	MIRBasicBlock *blockRef = nullptr;

	// The instruction that defines this value (for virtual regs)
	MIRInstruction *definingInst = nullptr;

	MIRValue(MIRValueKind k, MIRType *t) : kind(k), type(t) {}

	// Constructor for creating global references by name
	MIRValue(MIRType *t, const std::string &n) : kind(MIRValueKind::Global), type(t), name(n), isGlobalRef(true) {}

	static MIRValue *createVirtualReg(MIRType *type, uint32_t id);
	static MIRValue *createConstantInt(MIRType *type, int64_t value);
	static MIRValue *createConstantFloat(double value);
	static MIRValue *createConstantNull();
	static MIRValue *createGlobal(MIRType *type, const std::string &name);
	static MIRValue *createFunctionRef(const std::string &funcName);
	static MIRValue *createParameter(MIRType *type, const std::string &name, uint32_t index);
	static MIRValue *createBlockRef(MIRBasicBlock *block);

	bool isConstant() const {
		return kind == MIRValueKind::ConstantInt ||
			   kind == MIRValueKind::ConstantFloat ||
			   kind == MIRValueKind::ConstantNull;
	}

	std::string toString() const;
};

//==============================================================================
// Instructions
//==============================================================================

enum class MIROpcode {
	// Arithmetic (integer)
	Add,
	Sub,
	Mul,
	SDiv,
	SRem,
	// Arithmetic (unsigned)
	UDiv,
	URem,
	// Bitwise
	And,
	Or,
	Xor,
	Shl,
	AShr,
	LShr,
	// Arithmetic (floating-point)
	FAdd,
	FSub,
	FMul,
	FDiv,
	FRem,
	// Negation
	Neg,
	FNeg,

	// Comparisons (integer)
	ICmpEq,
	ICmpNe,
	ICmpSLT,
	ICmpSLE,
	ICmpSGT,
	ICmpSGE,
	ICmpULT,
	ICmpULE,
	ICmpUGT,
	ICmpUGE,
	// Comparisons (floating-point)
	FCmpEq,
	FCmpNe,
	FCmpLT,
	FCmpLE,
	FCmpGT,
	FCmpGE,

	// Memory
	Alloca,		   // Stack allocation
	Load,		   // Load from memory
	Store,		   // Store to memory
	GetElementPtr, // Address calculation for arrays/structs

	// Conversions
	Trunc,	  // Truncate integer
	ZExt,	  // Zero-extend integer
	SExt,	  // Sign-extend integer
	FPToSI,	  // Float to signed int
	SIToFP,	  // Signed int to float
	PtrToInt, // Pointer to integer
	IntToPtr, // Integer to pointer
	Bitcast,  // Bitwise reinterpretation

	// Control flow
	Br,		 // Unconditional branch
	CondBr,	 // Conditional branch
	Ret,	 // Return
	RetVoid, // Return void

	// Function calls
	Call,		  // Function call (direct)
	CallIndirect, // Indirect call through function pointer

	// SSA
	Phi, // Phi node for SSA

	// Special
	Copy, // Copy value (for register allocation)
};

class MIRInstruction {
  public:
	MIROpcode opcode;
	MIRValue *result = nullptr;		  // Destination (nullptr for Store, Br, Ret, etc.)
	std::vector<MIRValue *> operands; // Source operands

	// For function calls
	std::string calleeName;
	MIRFunction *calleeFunc = nullptr;

	// For indirect calls: the return type and parameter types (since we don't have calleeFunc)
	MIRType *indirectReturnType = nullptr;
	std::vector<MIRType *> indirectParamTypes;

	// For Phi nodes: incoming values paired with basic blocks
	std::vector<std::pair<MIRValue *, MIRBasicBlock *>> phiIncoming;

	// For GEP instructions: the element type being indexed into
	MIRType *elementType = nullptr;

	// For Alloca instructions: the type being allocated
	MIRType *allocatedType = nullptr;

	// Source location for debug info
	int sourceLine = 0;
	int sourceColumn = 0;

	// Memory attributes for Call/CallIndirect (override callee's attributes)
	bool callDoesNotReadMemory = false;
	bool callDoesNotWriteMemory = false;
	bool callOnlyAccessesArgMemory = false;

	// Parent basic block
	MIRBasicBlock *parent = nullptr;

	MIRInstruction(MIROpcode op) : opcode(op) {}

	bool isTerminator() const {
		return opcode == MIROpcode::Br || opcode == MIROpcode::CondBr ||
			   opcode == MIROpcode::Ret || opcode == MIROpcode::RetVoid;
	}

	bool hasResult() const {
		return opcode != MIROpcode::Store && opcode != MIROpcode::Br &&
			   opcode != MIROpcode::CondBr && opcode != MIROpcode::Ret &&
			   opcode != MIROpcode::RetVoid;
	}

	bool isBranch() const {
		return opcode == MIROpcode::Br || opcode == MIROpcode::CondBr;
	}

	bool isComparison() const {
		return opcode == MIROpcode::ICmpEq || opcode == MIROpcode::ICmpNe ||
			   opcode == MIROpcode::ICmpSLT || opcode == MIROpcode::ICmpSLE ||
			   opcode == MIROpcode::ICmpSGT || opcode == MIROpcode::ICmpSGE ||
			   opcode == MIROpcode::ICmpULT || opcode == MIROpcode::ICmpULE ||
			   opcode == MIROpcode::ICmpUGT || opcode == MIROpcode::ICmpUGE ||
			   opcode == MIROpcode::FCmpEq || opcode == MIROpcode::FCmpNe ||
			   opcode == MIROpcode::FCmpLT || opcode == MIROpcode::FCmpLE ||
			   opcode == MIROpcode::FCmpGT || opcode == MIROpcode::FCmpGE;
	}

	std::string toString() const;
	static const char *opcodeToString(MIROpcode op);
};

//==============================================================================
// Basic Block
//==============================================================================

class MIRBasicBlock {
  public:
	std::string name;
	std::vector<std::unique_ptr<MIRInstruction>> instructions;

	// CFG edges
	std::vector<MIRBasicBlock *> predecessors;
	std::vector<MIRBasicBlock *> successors;

	// Parent function
	MIRFunction *parent = nullptr;

	// Unique ID within function
	uint32_t id = 0;

	MIRBasicBlock(const std::string &n) : name(n) {}

	void addInstruction(std::unique_ptr<MIRInstruction> inst);
	MIRInstruction *getTerminator();
	bool hasTerminator() const;

	std::string toString() const;
};

//==============================================================================
// Function
//==============================================================================

class MIRFunction {
  public:
	std::string name;
	MIRType *returnType;
	std::vector<MIRValue *> parameters;
	std::vector<std::unique_ptr<MIRBasicBlock>> basicBlocks;

	// For virtual register allocation
	uint32_t nextRegId = 0;
	uint32_t nextBlockId = 0;

	// Stack frame info (filled during register allocation)
	uint32_t stackFrameSize = 0;
	std::vector<std::pair<uint32_t, int32_t>> spillSlots; // regId -> stack offset

	// Parent module
	MIRModule *parent = nullptr;

	// Source location
	int sourceLine = 0;

	// Linkage
	bool isExternal = false; // Declaration only (no body)

	// Memory attributes (for optimization)
	bool doesNotReadMemory = false;		// Function does not read from memory (writeonly)
	bool doesNotWriteMemory = false;	// Function does not write to memory (readonly)
	bool onlyAccessesArgMemory = false; // Function only accesses memory through pointer args

	MIRFunction(const std::string &n, MIRType *retType) : name(n), returnType(retType) {}

	MIRBasicBlock *createBasicBlock(const std::string &name);
	MIRBasicBlock *getEntryBlock();
	MIRValue *createVirtualReg(MIRType *type);
	MIRValue *addParameter(MIRType *type, const std::string &name);

	std::string toString() const;
};

//==============================================================================
// Global Variable
//==============================================================================

class MIRGlobal {
  public:
	std::string name;
	MIRType *type;

	// Initial value (for initialized globals)
	std::vector<uint8_t> initializer;
	bool hasInitializer = false;

	// String constant (convenience for string literals)
	std::string stringValue;
	bool isStringConstant = false;

	// Linkage
	bool isConstant = false;
	bool isExternal = false;

	MIRGlobal(const std::string &n, MIRType *t) : name(n), type(t) {}

	void setInitializer(const std::vector<uint8_t> &data);
	void setStringInitializer(const std::string &str);

	std::string toString() const;
};

//==============================================================================
// Module (Top-level container)
//==============================================================================

class MIRModule {
  public:
	std::string name;
	std::string targetTriple; // e.g., "x86_64-pc-windows-msvc"

	std::vector<std::unique_ptr<MIRFunction>> functions;
	std::vector<std::unique_ptr<MIRGlobal>> globals;

	// Type cache (to ensure type uniqueness)
	std::unordered_map<std::string, std::unique_ptr<MIRType>> structTypes;

	MIRModule(const std::string &n) : name(n) {}

	MIRFunction *createFunction(const std::string &name, MIRType *returnType);
	MIRFunction *getFunction(const std::string &name);
	MIRGlobal *createGlobal(const std::string &name, MIRType *type);
	MIRGlobal *getGlobal(const std::string &name);

	// Create a global string constant and return a pointer to it
	MIRValue *createGlobalString(const std::string &name, const std::string &value);

	// Create or get struct type
	MIRType *getOrCreateStructType(const std::string &name, const std::vector<MIRType *> &fields);

	// Count total instructions across all functions
	size_t countInstructions() const;

	std::string toString() const;
	void print() const;
};

} // namespace mir
