/**
 * MIR Code Generator - Main Implementation
 *
 * This file contains the main CodeGenerator class implementation that
 * generates MIR from the Maxon AST.
 */

#include "codegen_mir.h"
#include "const_eval.h"
#include "lexer.h"
#include "types/type_conversion.h"
#include <cstring>
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
	// Handle closure expressions - build the function type string
	if (auto *closure = dynamic_cast<ClosureExprAST *>(expr)) {
		std::string funcType = "fn(";
		for (size_t i = 0; i < closure->parameters.size(); i++) {
			if (i > 0)
				funcType += ",";
			funcType += closure->parameters[i].type;
		}
		funcType += ")->";
		// Infer return type from body
		if (closure->isSingleExpression && closure->singleExpr) {
			// For closures, we need parameter types in scope to infer expression type
			// Since semantic analyzer already validated types, we can use parameter type
			// for arithmetic ops like x * 2 where x is the parameter
			if (!closure->parameters.empty()) {
				// For simple closures, return type is usually the first parameter type
				// when doing arithmetic operations
				funcType += closure->parameters[0].type;
			} else {
				funcType += inferExprType(closure->singleExpr.get());
			}
		} else if (!closure->returnType.empty()) {
			funcType += closure->returnType;
		} else {
			funcType += "void";
		}
		return funcType;
	}
	// Handle variable expressions - look up their type
	if (auto *varExpr = dynamic_cast<VariableExprAST *>(expr)) {
		// Check if this is a function reference
		if (varExpr->isFunctionReference && !varExpr->resolvedFunctionName.empty()) {
			// Look up function info to build function type
			mir::MIRFunction *func = module->getFunction(varExpr->resolvedFunctionName);
			if (func) {
				std::string funcType = "fn(";
				for (size_t i = 0; i < func->parameters.size(); i++) {
					if (i > 0)
						funcType += ",";
					mir::MIRType *paramType = func->parameters[i]->type;
					if (paramType->isInteger())
						funcType += "int";
					else if (paramType->isFloat())
						funcType += "float";
					else if (paramType->isPointer())
						funcType += "ptr";
					else
						funcType += "unknown";
				}
				funcType += ")->";
				mir::MIRType *retType = func->returnType;
				if (retType->isInteger())
					funcType += "int";
				else if (retType->isFloat())
					funcType += "float";
				else if (retType->isVoid())
					funcType += "void";
				else if (retType->isPointer())
					funcType += "ptr";
				else
					funcType += "unknown";
				return funcType;
			}
		}
		auto it = variableTypes.find(varExpr->name);
		if (it != variableTypes.end()) {
			return it->second;
		}
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

	// Check for array<T> struct type (stdlib array struct)
	if (maxon::TypeConversion::isArrayStructType(typeStr)) {
		// array<T> is a struct with fields: { managed _ManagedArray, iterIndex int }
		// The _ManagedArray is stored as a struct { ptr, i32, i32 }
		std::string elemType = maxon::TypeConversion::getArrayStructElementType(typeStr);
		return getOrCreateArrayStructType(elemType);
	}

	// Check for new array type formats: _ManagedArray<T> or _StaticArray<N, T>
	if (typeStr.rfind("_ManagedArray<", 0) == 0) {
		// Phase 2: _ManagedArray<T> uses struct layout { _buffer ptr, _len i32, _capacity i32 }
		std::string elemType = maxon::TypeConversion::getArrayElementType(typeStr);
		return getOrCreateManagedArrayDataType(elemType);
	}

	// Handle bare _ManagedArray (used in ExpressibleByArrayLiteral interface)
	// This is an opaque pointer type
	if (typeStr == "_ManagedArray") {
		return mir::MIRType::getPtr();
	}

	if (typeStr.rfind("_StaticArray<", 0) == 0) {
		int size = maxon::TypeConversion::getStaticArraySize(typeStr);
		std::string elemType = maxon::TypeConversion::getArrayElementType(typeStr);
		mir::MIRType *elementType = getTypeFromString(elemType);
		return mir::MIRType::getArray(elementType, size);
	}

	// Function pointer types: fn(T1,T2)->R
	// At the MIR level, these are just opaque pointers
	if (typeStr.rfind("fn(", 0) == 0) {
		return mir::MIRType::getPtr();
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

	// Array parameters are passed as pointers (old-style fixed arrays with hidden length)
	if (isArrayParam(resolvedType)) {
		return mir::MIRType::getPtr();
	}

	// _ManagedArray<T> parameters are passed as pointers (no hidden length)
	// The struct is __ManagedArrayData<T> = { ptr, i32, i32 }
	if (maxon::TypeConversion::isManagedArrayType(resolvedType)) {
		std::string elemType = maxon::TypeConversion::getArrayElementType(resolvedType);
		getOrCreateManagedArrayDataType(elemType);
		return mir::MIRType::getPtr();
	}

	// Handle array<T> struct types - ensure they're instantiated
	if (maxon::TypeConversion::isArrayStructType(resolvedType)) {
		std::string elemType = maxon::TypeConversion::getArrayStructElementType(resolvedType);
		getOrCreateArrayStructType(elemType);
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
	// Check for old-style array types that use hidden length parameter
	// Note: array<T> struct types do NOT use hidden length - they store length in the struct
	// Note: _ManagedArray<T> is an opaque struct type (like _ManagedString), not legacy arrays
	if (maxon::TypeConversion::isManagedArrayType(typeStr)) {
		return false;
	}
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

	// Free heap-allocated arrays in this scope using _managed_array_release
	// _managed_array_release handles the refcount header and frees only when refcount reaches 0
	for (const auto &info : scopeStack.back().heapAllocatedArrays) {
		mir::MIRValue *ptr = nullptr;

		// Create tag for tracking
		mir::MIRValue *tag = module->createGlobalString(".__tag.free." + info.name, "array cleanup");

		// Get release function
		mir::MIRFunction *arrayReleaseFunc = getOrDeclareFunction(
			"_managed_array_release",
			mir::MIRType::getVoid(),
			{mir::MIRType::getPtr(), mir::MIRType::getPtr()});

		if (info.isDynamic) {
			// Dynamic array - read buffer pointer from struct at cleanup time
			// This handles the case where realloc may have changed the buffer pointer
			mir::MIRType *arrayStructType = getOrCreateArrayStructType(info.elementType);
			mir::MIRType *managedArrayType = getOrCreateManagedArrayDataType(info.elementType);

			// GEP to managed field (field 0), then buffer field (field 0)
			mir::MIRValue *managedField = builder->createStructGEP(arrayStructType, info.alloca, 0, info.name + ".managed");
			mir::MIRValue *bufferField = builder->createStructGEP(managedArrayType, managedField, 0, info.name + ".buffer.ptr");
			ptr = builder->createLoad(mir::MIRType::getPtr(), bufferField, info.name + ".buffer");

			// Check capacity - only release if capacity > 0 (heap-allocated)
			// capacity == 0 means the buffer is still stack-allocated
			mir::MIRValue *capField = builder->createStructGEP(managedArrayType, managedField, 2, info.name + ".cap.ptr");
			mir::MIRValue *capacity = builder->createLoad(mir::MIRType::getInt32(), capField, info.name + ".cap");

			mir::MIRFunction *currentFunc = builder->getFunction();
			mir::MIRBasicBlock *releaseBlock = currentFunc->createBasicBlock(info.name + ".release");
			mir::MIRBasicBlock *skipBlock = currentFunc->createBasicBlock(info.name + ".skip");

			mir::MIRValue *needsRelease = builder->createICmpSGT(capacity, builder->getInt32(0), info.name + ".needs.release");
			builder->createCondBr(needsRelease, releaseBlock, skipBlock);

			builder->setInsertPoint(releaseBlock);
			builder->createCall(arrayReleaseFunc, {ptr, tag});
			builder->createBr(skipBlock);

			builder->setInsertPoint(skipBlock);
		} else {
			// Static allocation - load from tracking alloca
			ptr = builder->createLoad(mir::MIRType::getPtr(), info.alloca, info.name + ".ptr");

			// Call _managed_array_release unconditionally (these are always heap-allocated)
			builder->createCall(arrayReleaseFunc, {ptr, tag});
		}
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

	// Release cstring data in this scope
	// Cstrings store their data pointer in field 0, which was either:
	// - Allocated via _managed_string_alloc (for SSO-to-cstring conversion)
	// - Retained from parent (for heap-to-cstring conversion)
	// In both cases, we call _managed_string_release on the data pointer
	for (const auto &[name, cstringAlloca] : scopeStack.back().cstringAllocas) {
		// Get or create cstring type
		mir::MIRType *cstringType = structTypes["cstring"];
		if (!cstringType) {
			cstringType = module->getOrCreateStructType(
				"cstring",
				{mir::MIRType::getPtr(), mir::MIRType::getInt32(), mir::MIRType::getPtr()});
			structTypes["cstring"] = cstringType;
		}

		// Load data pointer from cstring field 0
		mir::MIRValue *dataPtrPtr = builder->createStructGEP(cstringType, cstringAlloca, 0, name + ".data.ptr");
		mir::MIRValue *dataPtr = builder->createLoad(mir::MIRType::getPtr(), dataPtrPtr, name + ".data");

		// Create tag for tracking
		mir::MIRValue *tag = module->createGlobalString(".__tag.free." + name, "cstring release");

		// Call _managed_string_release on the data pointer
		// This works for both cases:
		// - SSO: data was allocated with _managed_string_alloc, will be freed
		// - Heap: data was retained, refcount will be decremented
		mir::MIRFunction *stringReleaseFunc = getOrDeclareFunction(
			"_managed_string_release",
			mir::MIRType::getVoid(),
			{mir::MIRType::getPtr(), mir::MIRType::getPtr()});
		builder->createCall(stringReleaseFunc, {dataPtr, tag});
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
	// Old-style: args passed as (ptr, i32) = 2 params
	// New-style: args passed as ptr to array<string> = 1 param
	bool mainTakesArgs = (mainFunc->parameters.size() >= 1);

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

			// Load the data pointer (offset 0) - ptr to array of string structs
			mir::MIRValue *argsDataPtr = builder->createLoad(
				mir::MIRType::getPtr(), argsStructPtr, "args.data");

			// Load the length (offset 8)
			mir::MIRValue *argsLenPtr = builder->createGEP(
				mir::MIRType::getInt8(), argsStructPtr,
				{builder->getInt64(8)}, "args.len.ptr");
			mir::MIRValue *argsLen = builder->createLoad(
				mir::MIRType::getInt32(), argsLenPtr, "args.len");

			// Build array<string> struct on stack
			// Layout: { __ManagedArrayData_string { ptr, i32, i32 }, i32 iterIndex }
			mir::MIRType *managedArrayDataType = getOrCreateManagedArrayDataType("string");
			mir::MIRType *arrayStructType = getOrCreateArrayStructType("string");
			mir::MIRValue *argsAlloca = builder->createAlloca(arrayStructType, "args.struct");

			// Set managed.data (field 0 of field 0)
			mir::MIRValue *managedField = builder->createStructGEP(arrayStructType, argsAlloca, 0, "args.managed");
			mir::MIRValue *bufferField = builder->createStructGEP(managedArrayDataType, managedField, 0, "args.buffer");
			builder->createStore(argsDataPtr, bufferField);

			// Set managed.length (field 1 of field 0)
			mir::MIRValue *lenField = builder->createStructGEP(managedArrayDataType, managedField, 1, "args.len.field");
			builder->createStore(argsLen, lenField);

			// Set managed.capacity (field 2 of field 0) - same as length for read-only args
			mir::MIRValue *capField = builder->createStructGEP(managedArrayDataType, managedField, 2, "args.cap.field");
			builder->createStore(argsLen, capField);

			// Set iterIndex (field 1) to 0
			mir::MIRValue *iterField = builder->createStructGEP(arrayStructType, argsAlloca, 1, "args.iter");
			builder->createStore(builder->getInt32(0), iterField);

			// Call main with pointer to array<string> struct
			mainRetVal = builder->createCall(mainFunc, {argsAlloca});
		} else {
			// Linux: TODO - for now call with empty args
			mir::MIRType *arrayStructType = getOrCreateArrayStructType("string");
			mir::MIRType *managedArrayDataType = getOrCreateManagedArrayDataType("string");
			mir::MIRValue *argsAlloca = builder->createAlloca(arrayStructType, "args.struct");

			// Initialize with empty array
			mir::MIRValue *managedField = builder->createStructGEP(arrayStructType, argsAlloca, 0, "args.managed");
			mir::MIRValue *bufferField = builder->createStructGEP(managedArrayDataType, managedField, 0, "args.buffer");
			builder->createStore(mir::MIRValue::createConstantNull(), bufferField);

			mir::MIRValue *lenField = builder->createStructGEP(managedArrayDataType, managedField, 1, "args.len.field");
			builder->createStore(builder->getInt32(0), lenField);

			mir::MIRValue *capField = builder->createStructGEP(managedArrayDataType, managedField, 2, "args.cap.field");
			builder->createStore(builder->getInt32(0), capField);

			mir::MIRValue *iterField = builder->createStructGEP(arrayStructType, argsAlloca, 1, "args.iter");
			builder->createStore(builder->getInt32(0), iterField);

			mainRetVal = builder->createCall(mainFunc, {argsAlloca});
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
	if (!mainRetVal) {
		mainRetVal = builder->getInt32(0);
	}
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
		// Handle Self type - substitute with the specialized name
		if (type == "Self") {
			return specializedName;
		}
		auto it = typeBindings.find(type);
		if (it != typeBindings.end()) {
			return it->second;
		}
		// Handle optional types: "Element or nil" -> "int or nil"
		if (maxon::TypeConversion::isOptionalType(type)) {
			std::string baseType = maxon::TypeConversion::unwrapOptionalType(type);
			auto baseIt = typeBindings.find(baseType);
			if (baseIt != typeBindings.end()) {
				return maxon::TypeConversion::makeOptionalType(baseIt->second);
			}
			// Check if Self is the base type
			if (baseType == "Self") {
				return maxon::TypeConversion::makeOptionalType(specializedName);
			}
		}
		// Handle opaque _ManagedArray type (without angle brackets)
		// When the array struct is instantiated, _ManagedArray becomes _ManagedArray<Element>
		if (type == "_ManagedArray") {
			auto elemIt = typeBindings.find("Element");
			if (elemIt != typeBindings.end()) {
				return maxon::TypeConversion::makeManagedArrayType(elemIt->second);
			}
		}
		// Handle array types: _ManagedArray<KeyType> -> _ManagedArray<string>
		if (maxon::TypeConversion::isManagedArrayType(type)) {
			std::string elemType = maxon::TypeConversion::getArrayElementType(type);
			auto elemIt = typeBindings.find(elemType);
			if (elemIt != typeBindings.end()) {
				return maxon::TypeConversion::makeManagedArrayType(elemIt->second);
			}
		}
		// Handle array<T> struct types: array<Element> -> array<int>
		if (maxon::TypeConversion::isArrayStructType(type)) {
			std::string elemType = maxon::TypeConversion::getArrayStructElementType(type);
			auto elemIt = typeBindings.find(elemType);
			if (elemIt != typeBindings.end()) {
				return maxon::TypeConversion::makeArrayStructType(elemIt->second);
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
	logDetail("Instantiating " + std::to_string(templateDef->methods.size()) + " methods for " + specializedName);
	for (const auto &method : templateDef->methods) {
		// Build specialized method name: map<string,int>.insert
		std::string specializedMethodName = specializedName + "." + method->name;
		logDetail("  Instantiating method: " + specializedMethodName);

		// Skip if already declared (shouldn't happen, but be safe)
		if (module->getFunction(specializedMethodName) != nullptr) {
			logDetail("    Already declared, skipping");
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
		logDetail("    Created MIR function: " + specializedMethodName);

		// Add parameters - first is always self (pointer to struct)
		mirFunc->addParameter(mir::MIRType::getPtr(), "self");
		for (const auto &param : substitutedParams) {
			if (param.name == "self")
				continue; // Skip self, already added
			mir::MIRType *paramType = getParamTypeFromString(param.type);
			mirFunc->addParameter(paramType, param.name);
			// Add hidden length parameter for array parameters (matches generateFunctionWithTypeBindings)
			if (isArrayParam(param.type)) {
				mirFunc->addParameter(mir::MIRType::getInt32(), param.name + ".__length");
			}
		}

		logTrace("Declared specialized method: " + specializedMethodName);
	}

	// Generate method bodies
	for (const auto &method : templateDef->methods) {
		generateFunctionWithTypeBindings(method.get(), templateDef->namespaceName, typeBindings, specializedName);
	}

	return specializedName;
}

void MIRCodeGenerator::ensureStructMethodDeclared(const std::string &structType, const std::string &methodName) {
	// Build the full method name
	std::string fullMethodName = structType + "." + methodName;

	// If already declared, nothing to do
	if (module->getFunction(fullMethodName)) {
		return;
	}

	logDetail("Ensuring method is declared: " + fullMethodName);

	// Search through ALL struct definitions to find the method
	// The method might be in a different struct definition than structDefinitions[structType]
	// because partial structs are created during incremental imports
	for (const auto &pair : structDefinitions) {
		StructDefAST *structDef = pair.second;

		// Only search in structs with matching name
		if (structDef->name != structType) {
			continue;
		}

		// Find the method in this struct
		// Method names in the AST may include interface prefix (e.g., "Hashable.hash")
		// Strip prefix if present when comparing
		for (const auto &method : structDef->methods) {
			std::string bareMethodName = method->name;
			size_t dotPos = bareMethodName.find('.');
			if (dotPos != std::string::npos) {
				bareMethodName = bareMethodName.substr(dotPos + 1);
			}
			if (bareMethodName == methodName) {
				// Declare the function
				mir::MIRType *returnType = getTypeFromStringNoMark(method->returnType);
				mir::MIRFunction *mirFunc = module->createFunction(fullMethodName, returnType);
				mirFunc->isExternal = false;

				// Add parameters - first is always self (pointer to struct)
				mirFunc->addParameter(mir::MIRType::getPtr(), "self");
				for (const auto &param : method->parameters) {
					if (param.name == "self")
						continue;
					mir::MIRType *paramType = getParamTypeFromString(param.type);
					mirFunc->addParameter(paramType, param.name);
					if (isArrayParam(param.type)) {
						mirFunc->addParameter(mir::MIRType::getInt32(), param.name + ".__length");
					}
				}

				logDetail("  Declared method: " + fullMethodName);

				// Also generate the method body
				generateFunction(method.get(), structDef->namespaceName);
				logDetail("  Generated method body: " + fullMethodName);
				return;
			}
		}
	}

	// Method not found in structDefinitions - try searching in program->structs directly
	// (the struct might not have been stored in structDefinitions yet)
	if (currentProgram) {
		logDetail("  Searching in currentProgram->structs (" + std::to_string(currentProgram->structs.size()) + " structs)");
		for (const auto &structDef : currentProgram->structs) {
			if (structDef->name != structType) {
				continue;
			}
			logDetail("  Found struct " + structDef->name + " with " + std::to_string(structDef->methods.size()) + " methods");

			for (const auto &method : structDef->methods) {
				std::string bareMethodName = method->name;
				size_t dotPos = bareMethodName.find('.');
				if (dotPos != std::string::npos) {
					bareMethodName = bareMethodName.substr(dotPos + 1);
				}
				if (bareMethodName == methodName) {
					// Declare the function
					mir::MIRType *returnType = getTypeFromStringNoMark(method->returnType);
					mir::MIRFunction *mirFunc = module->createFunction(fullMethodName, returnType);
					mirFunc->isExternal = false;

					// Add parameters - first is always self (pointer to struct)
					mirFunc->addParameter(mir::MIRType::getPtr(), "self");
					for (const auto &param : method->parameters) {
						if (param.name == "self")
							continue;
						mir::MIRType *paramType = getParamTypeFromString(param.type);
						mirFunc->addParameter(paramType, param.name);
						if (isArrayParam(param.type)) {
							mirFunc->addParameter(mir::MIRType::getInt32(), param.name + ".__length");
						}
					}

					logDetail("  Declared method from program: " + fullMethodName);

					// Also generate the method body
					generateFunction(method.get(), structDef->namespaceName);
					logDetail("  Generated method body: " + fullMethodName);
					return;
				}
			}
		}
	}

	// Try searching in program->functions for the method
	// (some methods are registered as top-level functions with receiver type set)
	if (currentProgram) {
		logDetail("  Searching in currentProgram->functions (" + std::to_string(currentProgram->functions.size()) + " functions)");
		for (const auto &func : currentProgram->functions) {
			if (func->receiverType == structType) {
				std::string bareMethodName = func->name;
				size_t dotPos = bareMethodName.find('.');
				if (dotPos != std::string::npos) {
					bareMethodName = bareMethodName.substr(dotPos + 1);
				}
				if (bareMethodName == methodName) {
					// Declare the function
					mir::MIRType *returnType = getTypeFromStringNoMark(func->returnType);
					mir::MIRFunction *mirFunc = module->createFunction(fullMethodName, returnType);
					mirFunc->isExternal = false;

					// Add parameters - first is always self (pointer to struct)
					mirFunc->addParameter(mir::MIRType::getPtr(), "self");
					for (const auto &param : func->parameters) {
						if (param.name == "self")
							continue;
						mir::MIRType *paramType = getParamTypeFromString(param.type);
						mirFunc->addParameter(paramType, param.name);
						if (isArrayParam(param.type)) {
							mirFunc->addParameter(mir::MIRType::getInt32(), param.name + ".__length");
						}
					}

					logDetail("  Declared method from functions: " + fullMethodName);

					// Also generate the method body
					generateFunction(func.get(), func->namespaceName);
					logDetail("  Generated method body: " + fullMethodName);
					return;
				}
			}
		}
	}

	logDetail("  Method not found: " + methodName + " in struct " + structType);
}

//==============================================================================
// Main Generation
//==============================================================================

void MIRCodeGenerator::generate(ProgramAST *program, bool needsEntryPoint,
								const std::map<std::string, size_t> *functionIndices,
								const std::map<std::string, std::string> *functionReturnTypesIn) {
	logProgress("Starting MIR generation");

	// Store program pointer for method lookup during codegen
	currentProgram = program;

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
		logTrace("  Storing structDefinitions['" + structDef->name + "'] with " + std::to_string(structDef->methods.size()) + " methods");

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

	// Pass 1d: Generate global constants
	logDetail("Pass 1d: Generating global constants (" + std::to_string(program->globals.size()) + " globals)");
	if (!program->globals.empty()) {
		// Use ConstExprEvaluator to evaluate all global initializers
		ConstExprEvaluator evaluator;

		// Register all globals
		for (const auto &global : program->globals) {
			evaluator.registerGlobal(global->name, global.get());
		}

		// Evaluate all globals (handles dependency ordering)
		std::vector<std::string> evalErrors;
		if (!evaluator.evaluateAll(evalErrors)) {
			for (const auto &err : evalErrors) {
				throw std::runtime_error(err);
			}
		}

		// Create MIR globals from evaluated values
		for (const auto &global : program->globals) {
			auto valueOpt = evaluator.getGlobalValue(global->name);
			auto typeOpt = evaluator.getGlobalType(global->name);

			if (!valueOpt || !typeOpt) {
				throw std::runtime_error("Failed to evaluate global constant '" + global->name + "'");
			}

			const ConstValue &value = *valueOpt;
			const std::string &type = *typeOpt;

			logTrace("Generating global: " + global->name + " : " + type);

			if (type == "int") {
				// Create global integer constant
				mir::MIRGlobal *mirGlobal = module->createGlobal(global->name, mir::MIRType::getInt32());
				mirGlobal->isConstant = true;
				int32_t intVal = static_cast<int32_t>(std::get<int64_t>(value));
				std::vector<uint8_t> data(4);
				std::memcpy(data.data(), &intVal, 4);
				mirGlobal->setInitializer(data);
			} else if (type == "float") {
				// Create global float constant
				mir::MIRGlobal *mirGlobal = module->createGlobal(global->name, mir::MIRType::getFloat64());
				mirGlobal->isConstant = true;
				double floatVal = std::get<double>(value);
				std::vector<uint8_t> data(8);
				std::memcpy(data.data(), &floatVal, 8);
				mirGlobal->setInitializer(data);
			} else if (type == "bool") {
				// Create global bool constant
				mir::MIRGlobal *mirGlobal = module->createGlobal(global->name, mir::MIRType::getInt8());
				mirGlobal->isConstant = true;
				uint8_t boolVal = std::get<bool>(value) ? 1 : 0;
				std::vector<uint8_t> data(1);
				data[0] = boolVal;
				mirGlobal->setInitializer(data);
			} else if (type == "string") {
				// Create global string constant (as raw string data)
				// For strings, we store the data and length separately
				const std::string &strVal = std::get<std::string>(value);
				mir::MIRValue *strData = module->createGlobalString(global->name, strVal);
				// Store reference in namedValues for lookup during codegen
				// Note: String globals need special handling since they're struct-like
				(void)strData; // For now, string globals use the existing string literal mechanism
			}
		}
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

	// Declare synthesized default methods from interfaces
	// Skip methods that have unresolved type parameters (Self, Element, etc.)
	// These will be instantiated when a concrete type is used
	auto hasUnresolvedTypeParams = [](const std::string &type) {
		return type == "Self" || type == "Element" ||
			   type.find("Element ") != std::string::npos ||
			   type.find("(Element)") != std::string::npos;
	};
	for (const auto &funcInfo : synthesizedMethods) {
		// Skip methods with unresolved type parameters
		if (hasUnresolvedTypeParams(funcInfo.returnType)) {
			logTrace("Skipping synthesized method with unresolved types: " + funcInfo.name);
			continue;
		}
		bool hasUnresolvedParam = false;
		for (const auto &param : funcInfo.parameters) {
			if (hasUnresolvedTypeParams(param.type)) {
				hasUnresolvedParam = true;
				break;
			}
		}
		if (hasUnresolvedParam) {
			logTrace("Skipping synthesized method with unresolved param types: " + funcInfo.name);
			continue;
		}

		logTrace("Declaring synthesized method: " + funcInfo.name);
		mir::MIRType *returnType = getTypeFromString(funcInfo.returnType);
		mir::MIRFunction *mirFunc = module->createFunction(funcInfo.name, returnType);
		mirFunc->isExternal = false;

		// Add parameters
		for (const auto &param : funcInfo.parameters) {
			mir::MIRType *paramType = getParamTypeFromString(param.type);
			mirFunc->addParameter(paramType, param.name);

			if (isArrayParam(param.type)) {
				mirFunc->addParameter(mir::MIRType::getInt32(), param.name + ".__length");
			}
		}
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

	// Generate synthesized default method bodies
	// Skip methods with unresolved type parameters (same filter as declaration phase)
	for (const auto &funcInfo : synthesizedMethods) {
		// Skip methods with unresolved type parameters
		if (hasUnresolvedTypeParams(funcInfo.returnType)) {
			continue;
		}
		bool hasUnresolvedParam = false;
		for (const auto &param : funcInfo.parameters) {
			if (hasUnresolvedTypeParams(param.type)) {
				hasUnresolvedParam = true;
				break;
			}
		}
		if (hasUnresolvedParam) {
			continue;
		}
		generateSynthesizedMethod(funcInfo);
	}

	// Create entry point if needed
	if (needsEntryPoint) {
		logDetail("Creating entry point (_start)");
		createMinimalEntryPoint();
	}

	logProgress("MIR generation complete: " + std::to_string(module->functions.size()) + " functions, " +
				std::to_string(module->globals.size()) + " globals");
}
