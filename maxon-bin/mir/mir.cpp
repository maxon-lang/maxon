#include "mir.h"
#include <iostream>
#include <sstream>

namespace mir {

//==============================================================================
// MIRType Implementation
//==============================================================================

// Static type singletons
static MIRType voidType(MIRTypeKind::Void);
static MIRType int1Type(MIRTypeKind::Int1);
static MIRType int8Type(MIRTypeKind::Int8);
static MIRType int32Type(MIRTypeKind::Int32);
static MIRType int64Type(MIRTypeKind::Int64);
static MIRType float64Type(MIRTypeKind::Float64);
static MIRType ptrType(MIRTypeKind::Ptr);

// Cache for array types
static std::vector<std::unique_ptr<MIRType>> arrayTypeCache;

MIRType *MIRType::getVoid() { return &voidType; }
MIRType *MIRType::getInt1() { return &int1Type; }
MIRType *MIRType::getInt8() { return &int8Type; }
MIRType *MIRType::getInt32() { return &int32Type; }
MIRType *MIRType::getInt64() { return &int64Type; }
MIRType *MIRType::getFloat64() { return &float64Type; }
MIRType *MIRType::getPtr() { return &ptrType; }

MIRType *MIRType::getArray(MIRType *elem, uint64_t size) {
	auto type = std::make_unique<MIRType>(MIRTypeKind::Array);
	type->elementType = elem;
	type->arraySize = size;
	type->computeSize();
	MIRType *ptr = type.get();
	arrayTypeCache.push_back(std::move(type));
	return ptr;
}

MIRType *MIRType::getStruct(const std::string &name, const std::vector<MIRType *> &fields) {
	auto type = std::make_unique<MIRType>(MIRTypeKind::Struct);
	type->structName = name;
	type->fieldTypes = fields;
	type->computeSize();
	MIRType *ptr = type.get();
	arrayTypeCache.push_back(std::move(type)); // Reuse cache
	return ptr;
}

void MIRType::computeSize() {
	switch (kind) {
	case MIRTypeKind::Void:
		sizeInBytes = 0;
		alignmentInBytes = 1;
		break;
	case MIRTypeKind::Int1:
		sizeInBytes = 1;
		alignmentInBytes = 1;
		break;
	case MIRTypeKind::Int8:
		sizeInBytes = 1;
		alignmentInBytes = 1;
		break;
	case MIRTypeKind::Int32:
		sizeInBytes = 4;
		alignmentInBytes = 4;
		break;
	case MIRTypeKind::Int64:
		sizeInBytes = 8;
		alignmentInBytes = 8;
		break;
	case MIRTypeKind::Float64:
		sizeInBytes = 8;
		alignmentInBytes = 8;
		break;
	case MIRTypeKind::Ptr:
		sizeInBytes = 8; // x64
		alignmentInBytes = 8;
		break;
	case MIRTypeKind::Array:
		if (elementType) {
			sizeInBytes = elementType->sizeInBytes * arraySize;
			alignmentInBytes = elementType->alignmentInBytes;
		}
		break;
	case MIRTypeKind::Struct:
		// Compute struct size with proper alignment
		sizeInBytes = 0;
		alignmentInBytes = 1;
		for (auto *field : fieldTypes) {
			// Align current offset
			uint64_t fieldAlign = field->alignmentInBytes;
			if (fieldAlign > alignmentInBytes)
				alignmentInBytes = fieldAlign;
			uint64_t padding = (fieldAlign - (sizeInBytes % fieldAlign)) % fieldAlign;
			sizeInBytes += padding + field->sizeInBytes;
		}
		// Final padding to alignment
		uint64_t finalPad = (alignmentInBytes - (sizeInBytes % alignmentInBytes)) % alignmentInBytes;
		sizeInBytes += finalPad;
		break;
	}
}

std::string MIRType::toString() const {
	switch (kind) {
	case MIRTypeKind::Void:
		return "void";
	case MIRTypeKind::Int1:
		return "i1";
	case MIRTypeKind::Int8:
		return "i8";
	case MIRTypeKind::Int32:
		return "i32";
	case MIRTypeKind::Int64:
		return "i64";
	case MIRTypeKind::Float64:
		return "f64";
	case MIRTypeKind::Ptr:
		return "ptr";
	case MIRTypeKind::Array:
		return "[" + std::to_string(arraySize) + " x " + elementType->toString() + "]";
	case MIRTypeKind::Struct:
		return "%" + structName;
	}
	return "unknown";
}

//==============================================================================
// MIRValue Implementation
//==============================================================================

// Value caches
static std::vector<std::unique_ptr<MIRValue>> valueCache;

MIRValue *MIRValue::createVirtualReg(MIRType *type, uint32_t id) {
	auto val = std::make_unique<MIRValue>(MIRValueKind::VirtualReg, type);
	val->regId = id;
	MIRValue *ptr = val.get();
	valueCache.push_back(std::move(val));
	return ptr;
}

MIRValue *MIRValue::createConstantInt(MIRType *type, int64_t value) {
	auto val = std::make_unique<MIRValue>(MIRValueKind::ConstantInt, type);
	val->intValue = value;
	MIRValue *ptr = val.get();
	valueCache.push_back(std::move(val));
	return ptr;
}

MIRValue *MIRValue::createConstantFloat(double value) {
	auto val = std::make_unique<MIRValue>(MIRValueKind::ConstantFloat, MIRType::getFloat64());
	val->floatValue = value;
	MIRValue *ptr = val.get();
	valueCache.push_back(std::move(val));
	return ptr;
}

MIRValue *MIRValue::createConstantNull() {
	auto val = std::make_unique<MIRValue>(MIRValueKind::ConstantNull, MIRType::getPtr());
	MIRValue *ptr = val.get();
	valueCache.push_back(std::move(val));
	return ptr;
}

MIRValue *MIRValue::createGlobal(MIRType *type, const std::string &name) {
	auto val = std::make_unique<MIRValue>(MIRValueKind::Global, type);
	val->name = name;
	MIRValue *ptr = val.get();
	valueCache.push_back(std::move(val));
	return ptr;
}

MIRValue *MIRValue::createParameter(MIRType *type, const std::string &name, uint32_t index) {
	auto val = std::make_unique<MIRValue>(MIRValueKind::Parameter, type);
	val->name = name;
	val->regId = index;
	MIRValue *ptr = val.get();
	valueCache.push_back(std::move(val));
	return ptr;
}

MIRValue *MIRValue::createBlockRef(MIRBasicBlock *block) {
	auto val = std::make_unique<MIRValue>(MIRValueKind::BasicBlockRef, MIRType::getVoid());
	val->blockRef = block;
	MIRValue *ptr = val.get();
	valueCache.push_back(std::move(val));
	return ptr;
}

std::string MIRValue::toString() const {
	std::ostringstream ss;
	switch (kind) {
	case MIRValueKind::VirtualReg:
		ss << "%" << regId;
		break;
	case MIRValueKind::ConstantInt:
		ss << intValue;
		break;
	case MIRValueKind::ConstantFloat:
		ss << floatValue;
		break;
	case MIRValueKind::ConstantNull:
		ss << "null";
		break;
	case MIRValueKind::Global:
		ss << "@" << name;
		break;
	case MIRValueKind::Parameter:
		ss << "%" << name;
		break;
	case MIRValueKind::BasicBlockRef:
		ss << "label %" << (blockRef ? blockRef->name : "null");
		break;
	}
	return ss.str();
}

//==============================================================================
// MIRInstruction Implementation
//==============================================================================

const char *MIRInstruction::opcodeToString(MIROpcode op) {
	switch (op) {
	case MIROpcode::Add:
		return "add";
	case MIROpcode::Sub:
		return "sub";
	case MIROpcode::Mul:
		return "mul";
	case MIROpcode::SDiv:
		return "sdiv";
	case MIROpcode::SRem:
		return "srem";
	case MIROpcode::UDiv:
		return "udiv";
	case MIROpcode::URem:
		return "urem";
	case MIROpcode::And:
		return "and";
	case MIROpcode::Or:
		return "or";
	case MIROpcode::Xor:
		return "xor";
	case MIROpcode::Shl:
		return "shl";
	case MIROpcode::AShr:
		return "ashr";
	case MIROpcode::LShr:
		return "lshr";
	case MIROpcode::FAdd:
		return "fadd";
	case MIROpcode::FSub:
		return "fsub";
	case MIROpcode::FMul:
		return "fmul";
	case MIROpcode::FDiv:
		return "fdiv";
	case MIROpcode::FRem:
		return "frem";
	case MIROpcode::Neg:
		return "neg";
	case MIROpcode::FNeg:
		return "fneg";
	case MIROpcode::ICmpEq:
		return "icmp eq";
	case MIROpcode::ICmpNe:
		return "icmp ne";
	case MIROpcode::ICmpSLT:
		return "icmp slt";
	case MIROpcode::ICmpSLE:
		return "icmp sle";
	case MIROpcode::ICmpSGT:
		return "icmp sgt";
	case MIROpcode::ICmpSGE:
		return "icmp sge";
	case MIROpcode::ICmpULT:
		return "icmp ult";
	case MIROpcode::ICmpULE:
		return "icmp ule";
	case MIROpcode::ICmpUGT:
		return "icmp ugt";
	case MIROpcode::ICmpUGE:
		return "icmp uge";
	case MIROpcode::FCmpEq:
		return "fcmp oeq";
	case MIROpcode::FCmpNe:
		return "fcmp one";
	case MIROpcode::FCmpLT:
		return "fcmp olt";
	case MIROpcode::FCmpLE:
		return "fcmp ole";
	case MIROpcode::FCmpGT:
		return "fcmp ogt";
	case MIROpcode::FCmpGE:
		return "fcmp oge";
	case MIROpcode::Alloca:
		return "alloca";
	case MIROpcode::Load:
		return "load";
	case MIROpcode::Store:
		return "store";
	case MIROpcode::GetElementPtr:
		return "getelementptr";
	case MIROpcode::Trunc:
		return "trunc";
	case MIROpcode::ZExt:
		return "zext";
	case MIROpcode::SExt:
		return "sext";
	case MIROpcode::FPToSI:
		return "fptosi";
	case MIROpcode::SIToFP:
		return "sitofp";
	case MIROpcode::PtrToInt:
		return "ptrtoint";
	case MIROpcode::IntToPtr:
		return "inttoptr";
	case MIROpcode::Bitcast:
		return "bitcast";
	case MIROpcode::Br:
		return "br";
	case MIROpcode::CondBr:
		return "br";
	case MIROpcode::Ret:
		return "ret";
	case MIROpcode::RetVoid:
		return "ret void";
	case MIROpcode::Call:
		return "call";
	case MIROpcode::Phi:
		return "phi";
	case MIROpcode::Copy:
		return "copy";
	}
	return "unknown";
}

std::string MIRInstruction::toString() const {
	std::ostringstream ss;

	if (result) {
		ss << result->toString() << " = ";
	}

	ss << opcodeToString(opcode);

	// Handle special instructions
	if (opcode == MIROpcode::Call) {
		ss << " " << (result ? result->type->toString() : "void") << " @" << calleeName << "(";
		for (size_t i = 0; i < operands.size(); ++i) {
			if (i > 0)
				ss << ", ";
			ss << operands[i]->type->toString() << " " << operands[i]->toString();
		}
		ss << ")";
	} else if (opcode == MIROpcode::Phi) {
		ss << " " << result->type->toString() << " ";
		for (size_t i = 0; i < phiIncoming.size(); ++i) {
			if (i > 0)
				ss << ", ";
			ss << "[ " << phiIncoming[i].first->toString() << ", %"
			   << phiIncoming[i].second->name << " ]";
		}
	} else if (opcode == MIROpcode::Alloca) {
		ss << " " << result->type->toString();
	} else if (opcode == MIROpcode::CondBr) {
		ss << " i1 " << operands[0]->toString() << ", label %"
		   << operands[1]->blockRef->name << ", label %" << operands[2]->blockRef->name;
	} else if (opcode == MIROpcode::Br) {
		ss << " label %" << operands[0]->blockRef->name;
	} else if (opcode == MIROpcode::Ret) {
		ss << " " << operands[0]->type->toString() << " " << operands[0]->toString();
	} else if (opcode == MIROpcode::Store) {
		ss << " " << operands[0]->type->toString() << " " << operands[0]->toString()
		   << ", ptr " << operands[1]->toString();
	} else if (opcode == MIROpcode::Load) {
		ss << " " << result->type->toString() << ", ptr " << operands[0]->toString();
	} else {
		// Generic binary/unary operations
		if (result) {
			ss << " " << result->type->toString();
		}
		for (size_t i = 0; i < operands.size(); ++i) {
			ss << " " << operands[i]->toString();
			if (i < operands.size() - 1)
				ss << ",";
		}
	}

	return ss.str();
}

//==============================================================================
// MIRBasicBlock Implementation
//==============================================================================

void MIRBasicBlock::addInstruction(std::unique_ptr<MIRInstruction> inst) {
	inst->parent = this;
	instructions.push_back(std::move(inst));
}

MIRInstruction *MIRBasicBlock::getTerminator() {
	if (instructions.empty())
		return nullptr;
	auto *last = instructions.back().get();
	return last->isTerminator() ? last : nullptr;
}

bool MIRBasicBlock::hasTerminator() const {
	if (instructions.empty())
		return false;
	return instructions.back()->isTerminator();
}

std::string MIRBasicBlock::toString() const {
	std::ostringstream ss;
	ss << name << ":\n";
	for (const auto &inst : instructions) {
		ss << "  " << inst->toString() << "\n";
	}
	return ss.str();
}

//==============================================================================
// MIRFunction Implementation
//==============================================================================

MIRBasicBlock *MIRFunction::createBasicBlock(const std::string &blockName) {
	auto block = std::make_unique<MIRBasicBlock>(blockName);
	block->parent = this;
	block->id = nextBlockId++;
	MIRBasicBlock *ptr = block.get();
	basicBlocks.push_back(std::move(block));
	return ptr;
}

MIRBasicBlock *MIRFunction::getEntryBlock() {
	return basicBlocks.empty() ? nullptr : basicBlocks[0].get();
}

MIRValue *MIRFunction::createVirtualReg(MIRType *type) {
	return MIRValue::createVirtualReg(type, nextRegId++);
}

MIRValue *MIRFunction::addParameter(MIRType *type, const std::string &paramName) {
	auto *param = MIRValue::createParameter(type, paramName, parameters.size());
	parameters.push_back(param);
	return param;
}

std::string MIRFunction::toString() const {
	std::ostringstream ss;

	ss << (isExternal ? "declare " : "define ") << returnType->toString() << " @" << name << "(";
	for (size_t i = 0; i < parameters.size(); ++i) {
		if (i > 0)
			ss << ", ";
		ss << parameters[i]->type->toString() << " " << parameters[i]->toString();
	}
	ss << ")";

	if (isExternal) {
		ss << "\n";
	} else {
		ss << " {\n";
		for (const auto &bb : basicBlocks) {
			ss << bb->toString();
		}
		ss << "}\n";
	}

	return ss.str();
}

//==============================================================================
// MIRGlobal Implementation
//==============================================================================

void MIRGlobal::setInitializer(const std::vector<uint8_t> &data) {
	initializer = data;
	hasInitializer = true;
}

void MIRGlobal::setStringInitializer(const std::string &str) {
	stringValue = str;
	isStringConstant = true;
	hasInitializer = true;
	// Also set raw bytes
	initializer.assign(str.begin(), str.end());
	initializer.push_back(0); // null terminator
}

std::string MIRGlobal::toString() const {
	std::ostringstream ss;
	ss << "@" << name << " = ";
	if (isExternal) {
		ss << "external ";
	}
	if (isConstant) {
		ss << "constant ";
	} else {
		ss << "global ";
	}
	ss << type->toString();
	if (isStringConstant) {
		ss << " c\"" << stringValue << "\\00\"";
	} else if (hasInitializer) {
		ss << " zeroinitializer"; // Simplified
	}
	ss << "\n";
	return ss.str();
}

//==============================================================================
// MIRModule Implementation
//==============================================================================

MIRFunction *MIRModule::createFunction(const std::string &funcName, MIRType *returnType) {
	auto func = std::make_unique<MIRFunction>(funcName, returnType);
	func->parent = this;
	MIRFunction *ptr = func.get();
	functions.push_back(std::move(func));
	return ptr;
}

MIRFunction *MIRModule::getFunction(const std::string &funcName) {
	for (auto &func : functions) {
		if (func->name == funcName)
			return func.get();
	}
	return nullptr;
}

MIRGlobal *MIRModule::createGlobal(const std::string &globalName, MIRType *type) {
	auto global = std::make_unique<MIRGlobal>(globalName, type);
	MIRGlobal *ptr = global.get();
	globals.push_back(std::move(global));
	return ptr;
}

MIRGlobal *MIRModule::getGlobal(const std::string &globalName) {
	for (auto &global : globals) {
		if (global->name == globalName)
			return global.get();
	}
	return nullptr;
}

MIRType *MIRModule::getOrCreateStructType(const std::string &structName, const std::vector<MIRType *> &fields) {
	auto it = structTypes.find(structName);
	if (it != structTypes.end()) {
		return it->second.get();
	}

	auto type = std::make_unique<MIRType>(MIRTypeKind::Struct);
	type->structName = structName;
	type->fieldTypes = fields;
	type->computeSize();
	MIRType *ptr = type.get();
	structTypes[structName] = std::move(type);
	return ptr;
}

std::string MIRModule::toString() const {
	std::ostringstream ss;

	ss << "; Module: " << name << "\n";
	if (!targetTriple.empty()) {
		ss << "target triple = \"" << targetTriple << "\"\n";
	}
	ss << "\n";

	// Globals
	for (const auto &global : globals) {
		ss << global->toString();
	}
	if (!globals.empty())
		ss << "\n";

	// Functions
	for (const auto &func : functions) {
		ss << func->toString() << "\n";
	}

	return ss.str();
}

void MIRModule::print() const {
	std::cout << toString();
}

} // namespace mir
