/**
 * MIR Code Generator - Main Implementation
 *
 * This file contains the main CodeGenerator class implementation that
 * generates MIR from the Maxon AST.
 */

#include "codegen_mir.h"
#include "lexer.h"
#include "types/type_conversion.h"
#include <stdexcept>

MIRCodeGenerator::MIRCodeGenerator(const std::string &moduleName, bool debugInfo, int verboseLevel,
								   bool trackAllocs)
	: generateDebugInfo(debugInfo), trackAllocs(trackAllocs), verboseLevel(verboseLevel),
	  sourceFileName(moduleName), logger_(verboseLevel) {
	module = std::make_unique<mir::MIRModule>(moduleName);

	// Set target triple
#ifdef _WIN32
	module->targetTriple = "x86_64-pc-windows-msvc";
#else
	module->targetTriple = "x86_64-pc-linux-gnu";
#endif

	builder = std::make_unique<mir::MIRBuilder>(module.get());

	logDetail("MIR CodeGenerator initialized for module: " + moduleName);
	logTrace("Target triple: " + module->targetTriple);
}

MIRCodeGenerator::~MIRCodeGenerator() = default;

// Logging helpers
void MIRCodeGenerator::logProgress(const std::string &msg) {
	logger_.progress(LogPhase::MIR, msg);
}

void MIRCodeGenerator::logDetail(const std::string &msg) {
	logger_.detail(LogPhase::MIR, msg);
}

void MIRCodeGenerator::logTrace(const std::string &msg) {
	logger_.trace(LogPhase::MIR, msg);
}

void MIRCodeGenerator::reportError(const std::string &message, int line, int column) {
	throw std::runtime_error(message + " at line " + std::to_string(line) +
							 ", column " + std::to_string(column));
}

//==============================================================================
// Type Conversion Helpers
//==============================================================================

// Infer the type of an expression (for struct field default values with type inference)
std::string MIRCodeGenerator::inferExprType(ExprAST *expr) {
	if (dynamic_cast<NumberExprAST *>(expr)) {
		return "int";
	}
	if (dynamic_cast<FloatExprAST *>(expr)) {
		return "float";
	}
	if (dynamic_cast<BooleanExprAST *>(expr)) {
		return "bool";
	}
	if (dynamic_cast<CharacterExprAST *>(expr)) {
		return "character";
	}
	if (dynamic_cast<StringLiteralExprAST *>(expr)) {
		return "string";
	}
	// For more complex expressions, default to int (semantic analyzer should have caught errors)
	return "int";
}

// Mark a struct type and all its field types as used (for lazy type emission)
void MIRCodeGenerator::markFieldTypesUsed(mir::MIRType *type) {
	if (!type || type->kind != mir::MIRTypeKind::Struct)
		return;
	for (mir::MIRType *fieldType : type->fieldTypes) {
		if (fieldType->kind == mir::MIRTypeKind::Struct && !fieldType->used) {
			fieldType->used = true;
			markFieldTypesUsed(fieldType); // Recursively mark nested struct types
		}
	}
}

// Get type without marking it as used (for struct field definitions)
mir::MIRType *MIRCodeGenerator::getTypeFromStringNoMark(const std::string &typeStr) {
	// Check for generic type parameter substitution first
	if (!currentTypeBindings.empty()) {
		auto bindingIt = currentTypeBindings.find(typeStr);
		if (bindingIt != currentTypeBindings.end()) {
			return getTypeFromStringNoMark(bindingIt->second);
		}
		// Handle array types with generic element type: _ManagedArray<KeyType> -> _ManagedArray<string>
		if (maxon::TypeConversion::isManagedArrayType(typeStr)) {
			std::string elemType = maxon::TypeConversion::getArrayElementType(typeStr);
			auto elemBindingIt = currentTypeBindings.find(elemType);
			if (elemBindingIt != currentTypeBindings.end()) {
				return getTypeFromStringNoMark(maxon::TypeConversion::makeManagedArrayType(elemBindingIt->second));
			}
		}
	}

	// _ManagedString is an opaque pointer type - not a struct
	if (typeStr == "_ManagedString") {
		return mir::MIRType::getPtr();
	}

	// Check for optional type: T or nil
	size_t orNilPos = typeStr.find(" or nil");
	if (orNilPos != std::string::npos) {
		std::string wrappedTypeStr = typeStr.substr(0, orNilPos);
		mir::MIRType *wrappedType = getTypeFromStringNoMark(wrappedTypeStr);
		return mir::MIRType::getOptional(wrappedType);
	}

	// Check for user-defined struct types first
	auto it = structTypes.find(typeStr);
	if (it != structTypes.end()) {
		return it->second;
	}

	// Check for enum types
	auto enumIt = enumTypes.find(typeStr);
	if (enumIt != enumTypes.end()) {
		return enumIt->second.mirType;
	}

	// For primitive types, delegate to regular getTypeFromString (they don't have used flag)
	return getTypeFromString(typeStr);
}

mir::MIRType *MIRCodeGenerator::getTypeFromString(const std::string &typeStr) {
	// Check for generic type parameter substitution
	if (!currentTypeBindings.empty()) {
		auto bindingIt = currentTypeBindings.find(typeStr);
		if (bindingIt != currentTypeBindings.end()) {
			return getTypeFromString(bindingIt->second);
		}
		// Handle array types with generic element type: _ManagedArray<KeyType> -> _ManagedArray<string>
		if (maxon::TypeConversion::isManagedArrayType(typeStr)) {
			std::string elemType = maxon::TypeConversion::getArrayElementType(typeStr);
			auto elemBindingIt = currentTypeBindings.find(elemType);
			if (elemBindingIt != currentTypeBindings.end()) {
				return getTypeFromString(maxon::TypeConversion::makeManagedArrayType(elemBindingIt->second));
			}
		}
	}

	// _ManagedString is an opaque pointer type - not a struct
	if (typeStr == "_ManagedString") {
		return mir::MIRType::getPtr();
	}

	// Check for user-defined struct types first (allows stdlib to define types like 'string')
	auto it = structTypes.find(typeStr);
	if (it != structTypes.end()) {
		// Mark this type as used when it's actually referenced
		it->second->used = true;
		// Also mark field types as used (for transitive dependencies)
		markFieldTypesUsed(it->second);
		return it->second;
	}

	// Check for enum types
	auto enumIt = enumTypes.find(typeStr);
	if (enumIt != enumTypes.end()) {
		return enumIt->second.mirType;
	}

	if (typeStr == "int") {
		return mir::MIRType::getInt32();
	} else if (typeStr == "float") {
		return mir::MIRType::getFloat64();
	} else if (typeStr == "bool") {
		return mir::MIRType::getInt1();
	} else if (typeStr == "character") {
		// Character type: 8-bit signed integer (for compatibility with existing stdlib)
		// Note: Future EGC (Extended Grapheme Cluster) iteration will use a different approach
		return mir::MIRType::getInt8();
	} else if (typeStr == "byte") {
		return mir::MIRType::getInt8(); // byte is 8-bit unsigned
	} else if (typeStr == "cstring") {
		// cstring is a zero-copy reference to a string's buffer
		// Layout: { ptr data, i32 len, ptr managed } - managed ptr for refcount
		mir::MIRType *cstringType = structTypes["cstring"];
		if (!cstringType) {
			cstringType = module->getOrCreateStructType(
				"cstring",
				{mir::MIRType::getPtr(), mir::MIRType::getInt32(), mir::MIRType::getPtr()});
			structTypes["cstring"] = cstringType;
		}
		return cstringType;
	} else if (typeStr == "ptr") {
		return mir::MIRType::getPtr();
	} else if (typeStr == "void") {
		return mir::MIRType::getVoid();
	} else if (typeStr.empty()) {
		return mir::MIRType::getInt32(); // Default type
	}

	// Check for optional type: T or nil
	size_t orNilPos = typeStr.find(" or nil");
	if (orNilPos != std::string::npos) {
		std::string wrappedTypeStr = typeStr.substr(0, orNilPos);
		mir::MIRType *wrappedType = getTypeFromString(wrappedTypeStr);
		return mir::MIRType::getOptional(wrappedType);
	}

	// Check for new array type formats: _ManagedArray<T> or _StaticArray<N, T>
	if (typeStr.rfind("_ManagedArray<", 0) == 0) {
		// Phase 2: _ManagedArray<T> uses struct layout { _buffer ptr, _len i32, _capacity i32 }
		std::string elemType = maxon::TypeConversion::getArrayElementType(typeStr);
		return getOrCreateManagedArrayDataType(elemType);
	}

	if (typeStr.rfind("_StaticArray<", 0) == 0) {
		int size = maxon::TypeConversion::getStaticArraySize(typeStr);
		std::string elemType = maxon::TypeConversion::getArrayElementType(typeStr);
		mir::MIRType *elementType = getTypeFromString(elemType);
		return mir::MIRType::getArray(elementType, size);
	}

	// Legacy array type format: [size]elementType or []elementType (unsized)
	if (typeStr.size() > 2 && typeStr[0] == '[') {
		size_t closeBracket = typeStr.find(']');
		if (closeBracket != std::string::npos) {
			std::string sizeStr = typeStr.substr(1, closeBracket - 1);
			std::string elemType = typeStr.substr(closeBracket + 1);

			// Unsized array []type - Phase 2: use struct layout { _buffer ptr, _len i32, _capacity i32 }
			if (sizeStr.empty()) {
				return getOrCreateManagedArrayDataType(elemType);
			}

			// Sized array [N]type
			int size = std::stoi(sizeStr);
			mir::MIRType *elementType = getTypeFromString(elemType);
			return mir::MIRType::getArray(elementType, size);
		}
	}

	throw std::runtime_error("Unknown type: " + typeStr);
}

mir::MIRType *MIRCodeGenerator::getParamTypeFromString(const std::string &typeStr) {
	// Check for generic type parameter substitution first
	std::string resolvedType = typeStr;
	if (!currentTypeBindings.empty()) {
		auto bindingIt = currentTypeBindings.find(typeStr);
		if (bindingIt != currentTypeBindings.end()) {
			resolvedType = bindingIt->second;
		}
		// Handle array types with generic element type: _ManagedArray<KeyType> -> _ManagedArray<string>
		else if (maxon::TypeConversion::isManagedArrayType(typeStr)) {
			std::string elemType = maxon::TypeConversion::getArrayElementType(typeStr);
			auto elemBindingIt = currentTypeBindings.find(elemType);
			if (elemBindingIt != currentTypeBindings.end()) {
				resolvedType = maxon::TypeConversion::makeManagedArrayType(elemBindingIt->second);
			}
		}
	}

	// Array parameters are passed as pointers
	if (isArrayParam(resolvedType)) {
		return mir::MIRType::getPtr();
	}

	// Struct parameters are passed by pointer
	if (structTypes.find(resolvedType) != structTypes.end()) {
		return mir::MIRType::getPtr();
	}

	// Use no-mark variant - types are marked when actually used in code generation
	return getTypeFromStringNoMark(resolvedType);
}

std::string MIRCodeGenerator::getMaxonTypeFromMIRType(mir::MIRType *type) {
	if (!type)
		return "int";
	switch (type->kind) {
	case mir::MIRTypeKind::Int32:
		return "int";
	case mir::MIRTypeKind::Float64:
		return "float";
	case mir::MIRTypeKind::Int1:
		return "bool";
	case mir::MIRTypeKind::Int8:
		return "character"; // Could also be byte, but character is the common case
	case mir::MIRTypeKind::Int64:
		return "int"; // Map i64 to int for now
	case mir::MIRTypeKind::Ptr:
		return "ptr";
	case mir::MIRTypeKind::Void:
		return "void";
	case mir::MIRTypeKind::Struct:
		// Handle built-in string type
		if (type->structName == "__string")
			return "string";
		if (type->structName == "cstring")
			return "cstring";
		return type->structName;
	case mir::MIRTypeKind::Array:
		// Format: _StaticArray<size, elementType>
		return maxon::TypeConversion::makeStaticArrayType(
			type->arraySize,
			getMaxonTypeFromMIRType(type->elementType));
	case mir::MIRTypeKind::Optional:
		// Format: T or nil
		return getMaxonTypeFromMIRType(type->wrappedType) + " or nil";
	}
	return "int";
}

bool MIRCodeGenerator::isArrayParam(const std::string &typeStr) {
	// Check for array types (both new and legacy formats)
	return maxon::TypeConversion::isArrayType(typeStr);
}

bool MIRCodeGenerator::isStructParameter(const std::string &varName) {
	return structParameters.find(varName) != structParameters.end();
}

// Check if a binary expression is a string concatenation (including nested ones)
bool MIRCodeGenerator::isStringConcatExpr(BinaryExprAST *binExpr) {
	if (binExpr->op != '+') {
		return false;
	}

	// Helper lambda to get type of an expression
	auto getExprType = [this](ExprAST *expr) -> std::string {
		if (auto *varExpr = dynamic_cast<VariableExprAST *>(expr)) {
			auto it = variableTypes.find(varExpr->name);
			return (it != variableTypes.end()) ? it->second : "";
		}
		if (dynamic_cast<StringLiteralExprAST *>(expr)) {
			return "string";
		}
		if (auto *nestedBin = dynamic_cast<BinaryExprAST *>(expr)) {
			return isStringConcatExpr(nestedBin) ? "string" : "";
		}
		return "";
	};

	std::string leftType = getExprType(binExpr->left.get());
	std::string rightType = getExprType(binExpr->right.get());

	return leftType == "string" && rightType == "string";
}

//==============================================================================
// Scope Management
//==============================================================================

void MIRCodeGenerator::pushScope() {
	scopeStack.push_back(ScopeInfo{});
}

void MIRCodeGenerator::popScope(mir::MIRFunction *function) {
	if (scopeStack.empty()) {
		return;
	}

	generateScopeCleanup(function);
	scopeStack.pop_back();
}

void MIRCodeGenerator::generateScopeCleanup(mir::MIRFunction *function) {
	if (scopeStack.empty()) {
		return;
	}

	// Free heap-allocated arrays in this scope
	for (const auto &[name, allocaVal] : scopeStack.back().heapAllocatedArrays) {
		// Load the pointer
		mir::MIRValue *ptr = builder->createLoad(mir::MIRType::getPtr(), allocaVal, name + ".ptr");

		// Call free
		mir::MIRFunction *freeFunc = getOrDeclareFunction(
			"free",
			mir::MIRType::getVoid(),
			{mir::MIRType::getPtr()});
		builder->createCall(freeFunc, {ptr});
	}

	// Free heap-allocated strings in this scope using _managed_string_release
	// _managed_string_release handles the refcount header and frees only when refcount reaches 0
	for (const auto &[name, dataPtrAlloca] : scopeStack.back().heapAllocatedStrings) {
		// Load the data pointer
		mir::MIRValue *dataPtr = builder->createLoad(mir::MIRType::getPtr(), dataPtrAlloca, name + ".data.ptr");

		// Create tag for tracking based on allocation name
		std::string tagStr = (name.find("concat") != std::string::npos) ? "string concat" : "string literal";
		mir::MIRValue *tag = module->createGlobalString(".__tag.free." + name, tagStr);

		// Call _managed_string_release on the data pointer (handles refcount and free)
		mir::MIRFunction *stringReleaseFunc = getOrDeclareFunction(
			"_managed_string_release",
			mir::MIRType::getVoid(),
			{mir::MIRType::getPtr(), mir::MIRType::getPtr()});
		builder->createCall(stringReleaseFunc, {dataPtr, tag});
	}

	// Release parent references for substrings in this scope
	// Substrings hold a reference to their parent's buffer if the parent was heap-allocated
	for (const auto &[name, substringAlloca] : scopeStack.back().substringAllocas) {
		// Get or create substring type
		mir::MIRType *substringType = structTypes["substring"];
		if (!substringType) {
			substringType = module->getOrCreateStructType(
				"substring",
				{mir::MIRType::getPtr(), mir::MIRType::getPtr(), mir::MIRType::getInt32(), mir::MIRType::getInt32()});
			structTypes["substring"] = substringType;
		}

		// Get __ManagedStringData type
		mir::MIRType *managedStringType = structTypes["__ManagedStringData"];
		if (!managedStringType) {
			mir::MIRType *unsizedArrayType = structTypes["_ManagedArray_byte"];
			if (!unsizedArrayType) {
				unsizedArrayType = module->getOrCreateStructType(
					"_ManagedArray_byte",
					{mir::MIRType::getPtr(), mir::MIRType::getInt32()});
				structTypes["_ManagedArray_byte"] = unsizedArrayType;
			}
			managedStringType = module->getOrCreateStructType(
				"__ManagedStringData",
				{unsizedArrayType, mir::MIRType::getInt32(), mir::MIRType::getInt32()});
			structTypes["__ManagedStringData"] = managedStringType;
		}
		mir::MIRType *unsizedArrayType = structTypes["_ManagedArray_byte"];

		// Load _parentManaged pointer from substring
		mir::MIRValue *parentPtrPtr = builder->createStructGEP(substringType, substringAlloca, 0, name + "._parentManaged.ptr");
		mir::MIRValue *parentPtr = builder->createLoad(mir::MIRType::getPtr(), parentPtrPtr, name + "._parentManaged");

		// Check if parent is heap-allocated (capacity > 0)
		mir::MIRValue *parentCapPtr = builder->createStructGEP(managedStringType, parentPtr, 2, name + ".parent._capacity.ptr");
		mir::MIRValue *parentCap = builder->createLoad(mir::MIRType::getInt32(), parentCapPtr, name + ".parent._capacity");
		mir::MIRValue *isHeap = builder->createICmpSGT(parentCap, builder->getInt32(0), name + ".isHeap");

		// Conditional release: only release if parent was heap-allocated
		mir::MIRBasicBlock *releaseBlock = builder->createBasicBlock(name + ".release");
		mir::MIRBasicBlock *continueBlock = builder->createBasicBlock(name + ".continue");
		builder->createCondBr(isHeap, releaseBlock, continueBlock);

		builder->setInsertPoint(releaseBlock);
		// Get the buffer pointer from parent's _buffer field
		mir::MIRValue *parentBufferPtr = builder->createStructGEP(managedStringType, parentPtr, 0, name + ".parent._buffer");
		mir::MIRValue *parentDataPtrPtr = builder->createStructGEP(unsizedArrayType, parentBufferPtr, 0, name + ".parent.data.ptr.ptr");
		mir::MIRValue *parentDataPtr = builder->createLoad(mir::MIRType::getPtr(), parentDataPtrPtr, name + ".parent.data.ptr");

		// Create tag for tracking
		mir::MIRValue *tag = module->createGlobalString(".__tag.free." + name, "substring parent");

		// Call _managed_string_release on the data pointer
		mir::MIRFunction *stringReleaseFunc = getOrDeclareFunction(
			"_managed_string_release",
			mir::MIRType::getVoid(),
			{mir::MIRType::getPtr(), mir::MIRType::getPtr()});
		builder->createCall(stringReleaseFunc, {parentDataPtr, tag});
		builder->createBr(continueBlock);

		builder->setInsertPoint(continueBlock);
	}

	// Release cstring references in this scope
	// Cstrings hold a reference to the underlying managed string
	for (const auto &[name, cstringAlloca] : scopeStack.back().cstringAllocas) {
		// Get or create cstring type
		mir::MIRType *cstringType = structTypes["cstring"];
		if (!cstringType) {
			cstringType = module->getOrCreateStructType(
				"cstring",
				{mir::MIRType::getPtr(), mir::MIRType::getInt32(), mir::MIRType::getPtr()});
			structTypes["cstring"] = cstringType;
		}

		// Get __ManagedStringData type
		mir::MIRType *managedStringType = structTypes["__ManagedStringData"];
		if (!managedStringType) {
			mir::MIRType *unsizedArrayType = structTypes["_ManagedArray_byte"];
			if (!unsizedArrayType) {
				unsizedArrayType = module->getOrCreateStructType(
					"_ManagedArray_byte",
					{mir::MIRType::getPtr(), mir::MIRType::getInt32()});
				structTypes["_ManagedArray_byte"] = unsizedArrayType;
			}
			managedStringType = module->getOrCreateStructType(
				"__ManagedStringData",
				{unsizedArrayType, mir::MIRType::getInt32(), mir::MIRType::getInt32()});
			structTypes["__ManagedStringData"] = managedStringType;
		}
		mir::MIRType *unsizedArrayType = structTypes["_ManagedArray_byte"];

		// Load managed pointer from cstring field 2
		mir::MIRValue *managedPtrPtr = builder->createStructGEP(cstringType, cstringAlloca, 2, name + ".managed.ptr");
		mir::MIRValue *managedPtr = builder->createLoad(mir::MIRType::getPtr(), managedPtrPtr, name + ".managed");

		// Check if the underlying string is heap-allocated (capacity > 0)
		mir::MIRValue *capPtr = builder->createStructGEP(managedStringType, managedPtr, 2, name + "._capacity.ptr");
		mir::MIRValue *cap = builder->createLoad(mir::MIRType::getInt32(), capPtr, name + "._capacity");
		mir::MIRValue *isHeap = builder->createICmpSGT(cap, builder->getInt32(0), name + ".isHeap");

		// Conditional release: only release if string was heap-allocated
		mir::MIRBasicBlock *releaseBlock = builder->createBasicBlock(name + ".cstring.release");
		mir::MIRBasicBlock *continueBlock = builder->createBasicBlock(name + ".cstring.continue");
		builder->createCondBr(isHeap, releaseBlock, continueBlock);

		builder->setInsertPoint(releaseBlock);
		// Get the buffer pointer from managed string's _buffer field
		mir::MIRValue *bufferPtr = builder->createStructGEP(managedStringType, managedPtr, 0, name + "._buffer");
		mir::MIRValue *dataPtrPtr = builder->createStructGEP(unsizedArrayType, bufferPtr, 0, name + ".data.ptr.ptr");
		mir::MIRValue *dataPtr = builder->createLoad(mir::MIRType::getPtr(), dataPtrPtr, name + ".data.ptr");

		// Create tag for tracking
		mir::MIRValue *tag = module->createGlobalString(".__tag.free." + name, "cstring release");

		// Call _managed_string_release on the data pointer
		mir::MIRFunction *stringReleaseFunc = getOrDeclareFunction(
			"_managed_string_release",
			mir::MIRType::getVoid(),
			{mir::MIRType::getPtr(), mir::MIRType::getPtr()});
		builder->createCall(stringReleaseFunc, {dataPtr, tag});
		builder->createBr(continueBlock);

		builder->setInsertPoint(continueBlock);
	}
}

//==============================================================================
// Alloca Helper
//==============================================================================

mir::MIRValue *MIRCodeGenerator::createEntryBlockAlloca(mir::MIRFunction *function,
														const std::string &varName,
														mir::MIRType *type) {
	// In MIR, we create alloca at the current position
	// A more sophisticated implementation would move allocas to entry block
	if (!type) {
		type = mir::MIRType::getInt32();
	}
	return builder->createAlloca(type, varName);
}

//==============================================================================
// Runtime Function Helpers
//==============================================================================

mir::MIRFunction *MIRCodeGenerator::getOrDeclareFunction(const std::string &name,
														 mir::MIRType *returnType,
														 const std::vector<mir::MIRType *> &paramTypes) {
	mir::MIRFunction *func = module->getFunction(name);
	if (func) {
		return func;
	}

	return builder->declareFunction(name, returnType, paramTypes);
}

void MIRCodeGenerator::initHeapManagement() {
	// Declare malloc
	getOrDeclareFunction("malloc", mir::MIRType::getPtr(), {mir::MIRType::getInt64()});

	// Declare free
	getOrDeclareFunction("free", mir::MIRType::getVoid(), {mir::MIRType::getPtr()});

	// Declare memset
	getOrDeclareFunction("memset",
						 mir::MIRType::getPtr(),
						 {mir::MIRType::getPtr(), mir::MIRType::getInt32(), mir::MIRType::getInt64()});

	// Declare memcpy
	getOrDeclareFunction("memcpy",
						 mir::MIRType::getPtr(),
						 {mir::MIRType::getPtr(), mir::MIRType::getPtr(), mir::MIRType::getInt64()});

	// Declare refcount_inc (for COW strings)
	getOrDeclareFunction("refcount_inc", mir::MIRType::getInt32(), {mir::MIRType::getPtr()});

	// Declare refcount_dec (for COW strings)
	getOrDeclareFunction("refcount_dec", mir::MIRType::getInt32(), {mir::MIRType::getPtr()});
}

//==============================================================================
// Entry Point Creation
//==============================================================================

void MIRCodeGenerator::createMinimalEntryPoint() {
	// Determine target platform
	bool isWindows = (module->targetTriple.find("windows") != std::string::npos);

	// Declare exit - runtime provides implementation
	getOrDeclareFunction("exit", mir::MIRType::getVoid(), {mir::MIRType::getInt32()});

	// Find main function
	mir::MIRFunction *mainFunc = module->getFunction("main");
	if (!mainFunc) {
		// Look for namespaced main (e.g., examples.main)
		for (auto &func : module->functions) {
			const std::string &funcName = func->name;
			if (funcName == "main" ||
				(funcName.size() > 5 && funcName.substr(funcName.size() - 5) == ".main")) {
				mainFunc = func.get();
				break;
			}
		}
	}

	if (!mainFunc) {
		throw std::runtime_error("main function not found");
	}

	// Check if main takes arguments
	bool mainTakesArgs = (mainFunc->parameters.size() == 2);

	// Create _start function
	std::vector<std::pair<mir::MIRType *, std::string>> startParams;
	(void)builder->createFunction("_start", mir::MIRType::getVoid(), startParams);
	mir::MIRBasicBlock *entry = builder->createBasicBlock("entry");
	builder->setInsertPoint(entry);

	// If we have any extern declarations, check if we're running as a worker
	// Note: __ffi_worker_main is provided by the runtime, so we declare it here
	// and it will be linked when the runtime MIR is merged
	if (hasExternCalls) {
		// Declare runtime functions (these are defined in runtime_windows.mir)
		mir::MIRFunction *isWorkerFunc = getOrDeclareFunction("ffi_is_worker_mode",
															  mir::MIRType::getInt32(), {});
		mir::MIRFunction *workerMain = getOrDeclareFunction("__ffi_worker_main",
															mir::MIRType::getInt32(), {});

		// Check if we're running as a worker (via environment variable)
		mir::MIRBasicBlock *checkWorker = builder->createBasicBlock("check_worker");
		mir::MIRBasicBlock *runMain = builder->createBasicBlock("run_main");
		mir::MIRBasicBlock *runWorker = builder->createBasicBlock("run_worker");

		builder->createBr(checkWorker);
		builder->setInsertPoint(checkWorker);

		// Call ffi_is_worker_mode to check environment variable
		mir::MIRValue *isWorkerVal = builder->createCall(isWorkerFunc, {});
		mir::MIRValue *zero = builder->getInt32(0);
		mir::MIRValue *isWorker = builder->createICmpNe(isWorkerVal, zero, "check_is_worker");
		builder->createCondBr(isWorker, runWorker, runMain);

		// Run as worker - call worker main and exit with its return code
		builder->setInsertPoint(runWorker);
		mir::MIRValue *workerResult = builder->createCall(workerMain, {});
		mir::MIRFunction *exitFuncW = module->getFunction("exit");
		builder->createCall(exitFuncW, {workerResult});
		builder->createRetVoid();

		// Run as main
		builder->setInsertPoint(runMain);
	}

	mir::MIRValue *mainRetVal = nullptr;

	// Enable allocation tracking if requested (before calling main)
	if (trackAllocs) {
		mir::MIRFunction *trackEnableFunc = getOrDeclareFunction("__enable_alloc_tracking",
																 mir::MIRType::getVoid(), {});
		builder->createCall(trackEnableFunc, {});
	}

	if (mainTakesArgs) {
		if (isWindows) {
			// Windows command line argument handling
			// Call __get_command_args() which returns a pointer to { ptr data, i32 length }
			mir::MIRFunction *getArgsFunc = getOrDeclareFunction(
				"__get_command_args", mir::MIRType::getPtr(), {});
			mir::MIRValue *argsStructPtr = builder->createCall(getArgsFunc, {});

			// Load the data pointer (offset 0)
			mir::MIRValue *argsDataPtr = builder->createLoad(
				mir::MIRType::getPtr(), argsStructPtr, "args.data");

			// Load the length (offset 8)
			// Use byte-level GEP to get to offset 8
			mir::MIRValue *argsLenPtr = builder->createGEP(
				mir::MIRType::getInt8(), argsStructPtr,
				{builder->getInt64(8)}, "args.len.ptr");
			mir::MIRValue *argsLen = builder->createLoad(
				mir::MIRType::getInt32(), argsLenPtr, "args.len");

			// Call main with args (ptr, i32)
			mainRetVal = builder->createCall(mainFunc, {argsDataPtr, argsLen});
		} else {
			// Linux: TODO - for now call with empty args
			mir::MIRValue *nullPtr = mir::MIRValue::createConstantNull();
			mir::MIRValue *zero = builder->getInt32(0);
			mainRetVal = builder->createCall(mainFunc, {nullPtr, zero});
		}
	} else {
		// Call main without arguments
		mainRetVal = builder->createCall(mainFunc, {});
	}

	// Print allocation stats if tracking was enabled (before cleanup/exit)
	if (trackAllocs) {
		mir::MIRFunction *printStatsFunc = getOrDeclareFunction("__print_alloc_summary",
																mir::MIRType::getVoid(), {});
		builder->createCall(printStatsFunc, {});
	}

	// Clean up FFI resources before exiting (if any extern calls were made)
	if (hasExternCalls) {
		mir::MIRFunction *cleanupFunc = getOrDeclareFunction("__ffi_parent_cleanup",
															 mir::MIRType::getVoid(), {});
		builder->createCall(cleanupFunc, {});
	}

	// Call exit with main's return value
	mir::MIRFunction *exitFunc = module->getFunction("exit");
	builder->createCall(exitFunc, {mainRetVal});

	// Return void (exit never returns, but we need a terminator)
	builder->createRetVoid();
}

//==============================================================================
// Generic Struct Instantiation
//==============================================================================

std::string MIRCodeGenerator::instantiateGenericStruct(const std::string &templateName,
													   const std::map<std::string, std::string> &typeBindings) {
	// Build specialized name: map<string,int>
	std::string specializedName = templateName + "<";
	bool first = true;
	for (const auto &[param, concreteType] : typeBindings) {
		if (!first)
			specializedName += ",";
		specializedName += concreteType;
		first = false;
	}
	specializedName += ">";

	// Check if already instantiated
	if (instantiatedGenericStructs.count(specializedName) > 0) {
		return specializedName;
	}

	// Look up the template
	auto defIt = structDefinitions.find(templateName);
	if (defIt == structDefinitions.end()) {
		throw std::runtime_error("Generic struct template not found: " + templateName);
	}
	StructDefAST *templateDef = defIt->second;

	logDetail("Instantiating generic struct: " + specializedName);

	// Helper to substitute type parameters
	auto substituteType = [&](const std::string &type) -> std::string {
		auto it = typeBindings.find(type);
		if (it != typeBindings.end()) {
			return it->second;
		}
		// Handle array types: _ManagedArray<KeyType> -> _ManagedArray<string>
		if (maxon::TypeConversion::isManagedArrayType(type)) {
			std::string elemType = maxon::TypeConversion::getArrayElementType(type);
			auto elemIt = typeBindings.find(elemType);
			if (elemIt != typeBindings.end()) {
				return maxon::TypeConversion::makeManagedArrayType(elemIt->second);
			}
		}
		return type;
	};

	// Create field types with substitution
	std::vector<mir::MIRType *> fieldTypes;
	std::vector<std::pair<std::string, std::string>> fields;

	for (const auto &field : templateDef->fields) {
		std::string substitutedType = substituteType(field.type);
		fieldTypes.push_back(getTypeFromStringNoMark(substitutedType));
		fields.push_back({field.name, substitutedType});
	}

	// Create the specialized struct type
	mir::MIRType *structType = module->getOrCreateStructType(specializedName, fieldTypes);
	structTypes[specializedName] = structType;
	structFields[specializedName] = fields;
	structConformsTo[specializedName] = templateDef->conformsTo;
	structTypeAssignments[specializedName] = typeBindings;

	// Mark as instantiated
	instantiatedGenericStructs.insert(specializedName);

	// Now instantiate methods
	for (const auto &method : templateDef->methods) {
		// Build specialized method name: map<string,int>.insert
		std::string specializedMethodName = specializedName + "." + method->name;

		// Skip if already declared (shouldn't happen, but be safe)
		if (module->getFunction(specializedMethodName) != nullptr) {
			continue;
		}

		// Substitute types in parameters
		std::vector<FunctionParameter> substitutedParams;
		for (const auto &param : method->parameters) {
			std::string substitutedType = substituteType(param.type);
			substitutedParams.push_back(FunctionParameter(param.name, substitutedType, param.line, param.column));
		}

		// Substitute return type
		std::string substitutedReturnType = substituteType(method->returnType);

		// Create MIR function declaration
		mir::MIRType *retType = getTypeFromStringNoMark(substitutedReturnType);
		mir::MIRFunction *mirFunc = module->createFunction(specializedMethodName, retType);

		// Add parameters - first is always self (pointer to struct)
		mirFunc->addParameter(mir::MIRType::getPtr(), "self");
		for (const auto &param : substitutedParams) {
			if (param.name == "self")
				continue; // Skip self, already added
			mir::MIRType *paramType = getParamTypeFromString(param.type);
			mirFunc->addParameter(paramType, param.name);
		}

		logTrace("Declared specialized method: " + specializedMethodName);
	}

	// Generate method bodies
	for (const auto &method : templateDef->methods) {
		generateFunctionWithTypeBindings(method.get(), templateDef->namespaceName, typeBindings, specializedName);
	}

	return specializedName;
}

//==============================================================================
// Main Generation
//==============================================================================

void MIRCodeGenerator::generate(ProgramAST *program, bool needsEntryPoint,
								const std::map<std::string, size_t> *functionIndices,
								const std::map<std::string, std::string> *functionReturnTypesIn) {
	logProgress("Starting MIR generation");

	// Clear function ID map for new generation
	functionIdToMIR.clear();

	// Copy function return types from semantic analyzer if provided
	if (functionReturnTypesIn) {
		functionReturnTypes = *functionReturnTypesIn;
	}

	// First pass: Forward-declare all struct types (to handle circular/forward references)
	logDetail("Pass 1a: Forward-declaring struct types (" + std::to_string(program->structs.size()) + " structs)");
	for (const auto &structDef : program->structs) {
		// Store AST pointer for generic instantiation
		structDefinitions[structDef->name] = structDef.get();

		// Skip generic template structs - they'll be instantiated on demand
		if (!structDef->associatedTypeParams.empty()) {
			logTrace("Skipping generic template struct: " + structDef->name);
			continue;
		}

		// Create struct with empty fields first
		mir::MIRType *structType = module->getOrCreateStructType(structDef->name, {});
		structTypes[structDef->name] = structType;
		structConformsTo[structDef->name] = structDef->conformsTo;
		structTypeAssignments[structDef->name] = structDef->typeAssignments;
	}

	// Second pass: Fill in struct fields now that all types are declared
	// Use getTypeFromStringNoMark to avoid marking field types as used prematurely
	logDetail("Pass 1b: Filling in struct fields");
	for (const auto &structDef : program->structs) {
		// Skip generic template structs - they'll be instantiated on demand
		if (!structDef->associatedTypeParams.empty()) {
			continue;
		}

		std::vector<mir::MIRType *> fieldTypes;
		std::vector<std::pair<std::string, std::string>> fields;
		std::map<std::string, ExprAST *> defaults;

		for (const auto &field : structDef->fields) {
			std::string fieldType = field.type;

			// Type inference from default value (mirrors semantic analyzer logic)
			if (fieldType.empty() && field.defaultValue != nullptr) {
				fieldType = inferExprType(field.defaultValue.get());
			}

			fieldTypes.push_back(getTypeFromStringNoMark(fieldType));
			fields.push_back({field.name, fieldType});
			// Store pointer to default value expression (if any)
			if (field.defaultValue != nullptr) {
				defaults[field.name] = field.defaultValue.get();
			}
		}

		logTrace("Defining struct type: " + structDef->name + " (" + std::to_string(fieldTypes.size()) + " fields)");

		// Update the existing struct type with actual fields
		mir::MIRType *structType = structTypes[structDef->name];
		structType->fieldTypes = fieldTypes;
		structType->recomputeSize();
		structFields[structDef->name] = fields;
		structFieldDefaults[structDef->name] = defaults;
	}

	// Pass 1c: Register enum types
	logDetail("Pass 1c: Registering enum types (" + std::to_string(program->enums.size()) + " enums)");
	for (const auto &enumDef : program->enums) {
		EnumCodegenInfo enumInfo;
		enumInfo.name = enumDef->name;
		enumInfo.rawValueType = enumDef->rawValueType;
		enumInfo.hasAssociatedValues = enumDef->hasAssociatedValues();

		// Calculate the size needed for the largest associated value payload
		size_t maxPayloadSize = 0;
		std::vector<mir::MIRType *> allPayloadTypes;

		for (size_t i = 0; i < enumDef->cases.size(); i++) {
			const auto &enumCase = enumDef->cases[i];
			enumInfo.caseNames.push_back(enumCase.name);
			enumInfo.caseTags[enumCase.name] = static_cast<int>(i);

			// Store associated values
			if (!enumCase.associatedValues.empty()) {
				std::vector<std::pair<std::string, std::string>> assocValues;
				size_t payloadSize = 0;
				for (const auto &av : enumCase.associatedValues) {
					assocValues.push_back({av.name, av.type});
					mir::MIRType *avType = getTypeFromStringNoMark(av.type);
					payloadSize += avType->sizeInBytes;
					allPayloadTypes.push_back(avType);
				}
				enumInfo.caseAssocValues[enumCase.name] = assocValues;
				maxPayloadSize = std::max(maxPayloadSize, payloadSize);
			}

			// Store raw values
			if (enumCase.rawValue) {
				if (auto *numExpr = dynamic_cast<NumberExprAST *>(enumCase.rawValue.get())) {
					enumInfo.caseRawIntValues[enumCase.name] = numExpr->value;
				} else if (auto *strExpr = dynamic_cast<StringLiteralExprAST *>(enumCase.rawValue.get())) {
					enumInfo.caseRawStrValues[enumCase.name] = strExpr->value;
				}
			}
		}

		// Create MIR type for the enum
		// Simple enums: just an i8 tag
		// Enums with associated values: struct { i8 tag, [payload] }
		if (enumInfo.hasAssociatedValues) {
			// Tagged union: tag byte + padding + max payload
			// For simplicity, use a struct with tag and an array of bytes for payload
			std::vector<mir::MIRType *> fieldTypes;
			fieldTypes.push_back(mir::MIRType::getInt8()); // tag
			if (maxPayloadSize > 0) {
				// Add payload as array of bytes
				mir::MIRType *payloadType = mir::MIRType::getArray(mir::MIRType::getInt8(), static_cast<int>(maxPayloadSize));
				fieldTypes.push_back(payloadType);
			}
			enumInfo.mirType = module->getOrCreateStructType("__enum_" + enumDef->name, fieldTypes);
		} else {
			// Simple enum: just i8 for tag
			enumInfo.mirType = mir::MIRType::getInt8();
		}

		enumTypes[enumDef->name] = enumInfo;
		logTrace("Registered enum: " + enumDef->name + " (" + std::to_string(enumDef->cases.size()) + " cases)");
	}

	// Second pass: Create all function declarations and register extern functions
	// This includes both top-level functions and methods declared inside structs
	logDetail("Pass 2: Creating function declarations");

	// Helper lambda to declare a function/method
	// Uses no-mark variants - types are marked when actually used in code generation
	auto declareFunction = [&](FunctionAST *func, const std::string &namespaceName) {
		// Build function name
		std::string functionName;
		if (!func->receiverType.empty()) {
			// This is a method
			functionName = func->receiverType + "." + func->name;
		} else if (!namespaceName.empty()) {
			functionName = namespaceName + "." + func->name;
		} else {
			functionName = func->name;
		}

		mir::MIRType *returnType = getTypeFromStringNoMark(func->returnType);

		std::vector<mir::MIRType *> paramTypes;
		for (const auto &param : func->parameters) {
			paramTypes.push_back(getParamTypeFromString(param.type));
			// Add hidden length parameter for arrays
			if (isArrayParam(param.type)) {
				paramTypes.push_back(mir::MIRType::getInt32());
			}
		}

		logTrace("Declaring function: " + functionName + " -> " + func->returnType);

		if (func->isExtern) {
			// External declaration - register for Safe FFI
			builder->declareFunction(functionName, returnType, paramTypes);
			registerExternFunction(func);
		} else {
			// Will be defined later - create declaration first
			mir::MIRFunction *mirFunc = module->createFunction(functionName, returnType);
			mirFunc->isExternal = false; // Will have body

			// Add parameters
			for (const auto &param : func->parameters) {
				mir::MIRType *paramType = getParamTypeFromString(param.type);
				mirFunc->addParameter(paramType, param.name);

				if (isArrayParam(param.type)) {
					mirFunc->addParameter(mir::MIRType::getInt32(), param.name + ".__length");
				}
			}

			// Store function ID mapping for fast lookups during codegen
			if (functionIndices) {
				auto it = functionIndices->find(functionName);
				if (it != functionIndices->end()) {
					functionIdToMIR[it->second] = mirFunc;
				}
			}
		}
	};

	// First, declare methods from struct definitions
	// Skip generic template structs - their methods will be instantiated on demand
	for (auto &structDef : program->structs) {
		if (!structDef->associatedTypeParams.empty()) {
			continue;
		}
		for (auto &method : structDef->methods) {
			declareFunction(method.get(), structDef->namespaceName);
		}
	}

	// Declare methods from enum definitions
	for (auto &enumDef : program->enums) {
		for (auto &method : enumDef->methods) {
			declareFunction(method.get(), enumDef->namespaceName);
		}
	}

	// Then declare top-level functions
	for (auto &func : program->functions) {
		declareFunction(func.get(), func->namespaceName);
	}

	// Generate Safe FFI infrastructure (only if there are extern functions)
	if (!externFunctions.empty()) {
		logDetail("Generating Safe FFI infrastructure for " + std::to_string(externFunctions.size()) + " extern functions");
		generateFFIGlobals();
		generateFFIInitFunction();
		generateFFICleanup();
		generateFFIDispatch();	 // Dispatch function for worker
		generateFFIWorkerMain(); // Worker subprocess main loop (provided by runtime)
	}

	// Third pass: Generate function bodies (both methods inside structs and top-level functions)
	logDetail("Pass 3: Generating function bodies");

	// First, generate method bodies from structs
	// Skip generic template structs - their methods will be instantiated on demand
	for (auto &structDef : program->structs) {
		if (!structDef->associatedTypeParams.empty()) {
			continue;
		}
		for (auto &method : structDef->methods) {
			generateFunction(method.get(), structDef->namespaceName);
		}
	}

	// Generate method bodies from enum definitions
	for (auto &enumDef : program->enums) {
		for (auto &method : enumDef->methods) {
			generateFunction(method.get(), enumDef->namespaceName);
		}
	}

	// Then generate top-level function bodies
	for (auto &func : program->functions) {
		if (!func->isExtern) {
			generateFunction(func.get(), func->namespaceName);
		}
	}

	// Create entry point if needed
	if (needsEntryPoint) {
		logDetail("Creating entry point (_start)");
		createMinimalEntryPoint();
	}

	logProgress("MIR generation complete: " + std::to_string(module->functions.size()) + " functions, " +
				std::to_string(module->globals.size()) + " globals");
}
