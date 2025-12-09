#include "type_conversion.h"
#include <algorithm>
#include <vector>

namespace maxon {

//==============================================================================
// Conversion Table Definition
//
// This table is the single source of truth for type conversions.
// Rows are source types, columns are target types.
// Each cell indicates what kind of conversion is allowed.
//
// Legend:
//   N = None (same type, no conversion)
//   I = Implicit (allowed without cast)
//   E = Explicit (requires 'as' cast)
//   P = Prohibited (not allowed, use specific function like trunc())
//   X = Incompatible (types cannot be converted)
//==============================================================================

// Shorthand for readability
constexpr ConversionKind N = ConversionKind::None;
constexpr ConversionKind I = ConversionKind::Implicit;
constexpr ConversionKind E = ConversionKind::Explicit;
constexpr ConversionKind P = ConversionKind::Prohibited;
constexpr ConversionKind X = ConversionKind::Incompatible;

//                            Void Bool Byte Int  Flt  Str  Char Ptr  Err
const ConversionKind TypeConversion::conversionTable
	[static_cast<int>(TypeKind::_Count)]
	[static_cast<int>(TypeKind::_Count)] = {
		// From Void:
		{N, X, X, X, X, X, X, X, N},
		// From Bool:
		{X, N, X, E, X, X, X, X, N},
		// From Byte:
		{X, X, N, I, I, X, X, X, N}, // byte -> int: implicit (widening)
									 // byte -> float: implicit (widening)
									 // From Int:
		{X, X, E, N, I, X, X, E, N}, // int -> byte: explicit (narrowing)
									 // int -> float: implicit (widening)
									 // int -> ptr: explicit (low-level)
									 // From Float:
		{X, X, P, P, N, X, X, X, N}, // float -> byte: prohibited (use trunc/round/floor/ceil)
									 // float -> int: prohibited (use trunc/round/floor/ceil)
									 // From String:
		{X, X, X, X, X, N, X, X, N},
		// From Character:
		{X, X, X, X, X, X, N, X, N},
		// From Ptr:
		{X, X, X, E, X, X, X, N, N}, // ptr -> int: explicit (low-level)
									 // From Error:
		{N, N, N, N, N, N, N, N, N}, // error matches everything (avoid cascading)
};

//==============================================================================
// Type Kind Utilities
//==============================================================================

TypeKind TypeConversion::getTypeKind(const std::string &typeStr) {
	if (typeStr.empty())
		return TypeKind::Error;
	if (typeStr == "void")
		return TypeKind::Void;
	if (typeStr == "bool")
		return TypeKind::Bool;
	if (typeStr == "byte")
		return TypeKind::Byte;
	if (typeStr == "int")
		return TypeKind::Int;
	if (typeStr == "float")
		return TypeKind::Float;
	if (typeStr == "string")
		return TypeKind::String;
	if (typeStr == "character")
		return TypeKind::Character;
	if (typeStr == "ptr" || typeStr == "cstring")
		return TypeKind::Ptr;
	if (typeStr == "error" || typeStr == "nil")
		return TypeKind::Error;

	// Optional types: "T or nil"
	if (typeStr.find(" or nil") != std::string::npos) {
		return TypeKind::Optional;
	}

	// Array types: _ManagedArray<T>, _StaticArray<N, T>
	if (typeStr.rfind("_ManagedArray<", 0) == 0 || typeStr.rfind("_StaticArray<", 0) == 0) {
		return TypeKind::Array;
	}

	// Assume anything else is a struct type
	return TypeKind::Struct;
}

std::string TypeConversion::getTypeString(TypeKind kind) {
	switch (kind) {
	case TypeKind::Void:
		return "void";
	case TypeKind::Bool:
		return "bool";
	case TypeKind::Byte:
		return "byte";
	case TypeKind::Int:
		return "int";
	case TypeKind::Float:
		return "float";
	case TypeKind::String:
		return "string";
	case TypeKind::Character:
		return "character";
	case TypeKind::Ptr:
		return "ptr";
	case TypeKind::Error:
		return "error";
	case TypeKind::Struct:
		return "struct";
	case TypeKind::Array:
		return "array";
	case TypeKind::Optional:
		return "optional";
	default:
		return "unknown";
	}
}

bool TypeConversion::isNumeric(TypeKind kind) {
	return kind == TypeKind::Int || kind == TypeKind::Float || kind == TypeKind::Byte;
}

bool TypeConversion::isInteger(TypeKind kind) {
	return kind == TypeKind::Int || kind == TypeKind::Byte;
}

//==============================================================================
// Conversion Queries
//==============================================================================

ConversionKind TypeConversion::getConversionKind(TypeKind source, TypeKind target) {
	// Same type: no conversion needed
	if (source == target) {
		return ConversionKind::None;
	}

	// Handle non-primitive types
	if (source == TypeKind::Struct || source == TypeKind::Array || source == TypeKind::Optional ||
		target == TypeKind::Struct || target == TypeKind::Array || target == TypeKind::Optional) {
		// Struct/Array/Optional conversions are handled separately
		// (e.g., optional wrapping, array element access)
		return ConversionKind::Incompatible;
	}

	// Look up in table
	int srcIdx = static_cast<int>(source);
	int tgtIdx = static_cast<int>(target);

	if (srcIdx >= 0 && srcIdx < static_cast<int>(TypeKind::_Count) &&
		tgtIdx >= 0 && tgtIdx < static_cast<int>(TypeKind::_Count)) {
		return conversionTable[srcIdx][tgtIdx];
	}

	return ConversionKind::Incompatible;
}

ConversionKind TypeConversion::getConversionKind(const std::string &source, const std::string &target) {
	return getConversionKind(getTypeKind(source), getTypeKind(target));
}

bool TypeConversion::canConvertImplicitly(TypeKind source, TypeKind target) {
	ConversionKind kind = getConversionKind(source, target);
	return kind == ConversionKind::None || kind == ConversionKind::Implicit;
}

bool TypeConversion::canConvertImplicitly(const std::string &source, const std::string &target) {
	return canConvertImplicitly(getTypeKind(source), getTypeKind(target));
}

bool TypeConversion::canCast(TypeKind source, TypeKind target) {
	ConversionKind kind = getConversionKind(source, target);
	return kind == ConversionKind::None ||
		   kind == ConversionKind::Implicit ||
		   kind == ConversionKind::Explicit;
}

bool TypeConversion::canCast(const std::string &source, const std::string &target) {
	return canCast(getTypeKind(source), getTypeKind(target));
}

bool TypeConversion::typesMatch(const std::string &type1, const std::string &type2) {
	// Error type matches anything (to avoid cascading errors)
	if (type1 == "error" || type2 == "error") {
		return true;
	}

	// Nil matches any optional type
	if (type1 == "nil" && isOptionalType(type2)) {
		return true;
	}
	if (type2 == "nil" && isOptionalType(type1)) {
		return true;
	}

	// Check if both are array types and extract element types
	if (isArrayType(type1) && isArrayType(type2)) {
		std::string elem1 = getArrayElementType(type1);
		std::string elem2 = getArrayElementType(type2);
		// Array types match if element types match, regardless of size
		return typesMatch(elem1, elem2);
	}

	// Check optional types
	if (isOptionalType(type1) && isOptionalType(type2)) {
		// Both optional: base types must match
		return typesMatch(unwrapOptionalType(type1), unwrapOptionalType(type2));
	}

	// Check function types
	if (isFunctionType(type1) && isFunctionType(type2)) {
		std::vector<std::string> params1, params2;
		std::string ret1, ret2;
		if (!parseFunctionType(type1, params1, ret1) ||
			!parseFunctionType(type2, params2, ret2)) {
			return false;
		}
		// Parameter count must match
		if (params1.size() != params2.size()) {
			return false;
		}
		// All parameter types must match
		for (size_t i = 0; i < params1.size(); i++) {
			if (!typesMatch(params1[i], params2[i])) {
				return false;
			}
		}
		// Return types must match
		return typesMatch(ret1, ret2);
	}

	// Exact match
	return type1 == type2;
}

//==============================================================================
// Binary Expression Type Rules
//==============================================================================

BinaryOpCategory TypeConversion::getBinaryOpCategory(char op) {
	switch (op) {
	case '+':
	case '-':
	case '*':
		return BinaryOpCategory::Arithmetic;
	case '/':
		return BinaryOpCategory::Division;
	case '%':
		return BinaryOpCategory::Modulo;
	case '<':
	case '>':
	case 'L': // <=
	case 'G': // >=
	case 'E': // ==
	case 'N': // !=
		return BinaryOpCategory::Comparison;
	case 'A': // and
	case 'O': // or
		return BinaryOpCategory::Logical;
	case '&':
	case '|':
	case '^':
		return BinaryOpCategory::Bitwise;
	case 'S': // <<
	case 'H': // >>
		return BinaryOpCategory::Shift;
	default:
		return BinaryOpCategory::Arithmetic;
	}
}

bool TypeConversion::needsFloatPromotion(const std::string &leftType,
										 const std::string &rightType,
										 char op) {
	// Division always produces float
	if (op == '/') {
		return true;
	}

	// Float promotion if either operand is float
	TypeKind leftKind = getTypeKind(leftType);
	TypeKind rightKind = getTypeKind(rightType);

	return leftKind == TypeKind::Float || rightKind == TypeKind::Float;
}

std::string TypeConversion::getBinaryResultType(const std::string &leftType,
												const std::string &rightType,
												char op) {
	BinaryOpCategory category = getBinaryOpCategory(op);

	switch (category) {
	case BinaryOpCategory::Division:
		// Division always returns float
		return "float";

	case BinaryOpCategory::Arithmetic: {
		// String concatenation
		if (op == '+' && leftType == "string" && rightType == "string") {
			return "string";
		}
		// Float if either operand is float, otherwise int
		if (needsFloatPromotion(leftType, rightType, op)) {
			return "float";
		}
		return "int";
	}

	case BinaryOpCategory::Modulo:
		// Modulo only works on integers, returns int
		return "int";

	case BinaryOpCategory::Comparison:
		// Comparisons return bool
		return "bool";

	case BinaryOpCategory::Logical:
		// Logical operators return bool
		return "bool";

	case BinaryOpCategory::Bitwise:
	case BinaryOpCategory::Shift:
		// Bitwise/shift operations return int
		return "int";

	default:
		return "int";
	}
}

//==============================================================================
// Special Cases
//==============================================================================

bool TypeConversion::isLiteralByteCompatible(int literalValue) {
	return literalValue >= 0 && literalValue <= 255;
}

bool TypeConversion::isOptionalType(const std::string &type) {
	return type.find(" or nil") != std::string::npos;
}

std::string TypeConversion::unwrapOptionalType(const std::string &type) {
	size_t pos = type.find(" or nil");
	if (pos == std::string::npos) {
		return type; // Not optional
	}
	return type.substr(0, pos);
}

std::string TypeConversion::makeOptionalType(const std::string &type) {
	if (isOptionalType(type)) {
		return type; // Already optional
	}
	return type + " or nil";
}

bool TypeConversion::isArrayType(const std::string &type) {
	// Internal format: _ManagedArray<T> or _StaticArray<N, T>
	return type.rfind("_ManagedArray<", 0) == 0 || type.rfind("_StaticArray<", 0) == 0;
}

bool TypeConversion::isManagedArrayType(const std::string &type) {
	// Internal format: _ManagedArray<T>
	return type.rfind("_ManagedArray<", 0) == 0;
}

bool TypeConversion::isManagedArrayOpaqueType(const std::string &type) {
	// Only the new format: _ManagedArray<T>
	return type.rfind("_ManagedArray<", 0) == 0;
}

bool TypeConversion::isStaticArrayType(const std::string &type) {
	// Internal format: _StaticArray<N, T>
	return type.rfind("_StaticArray<", 0) == 0;
}

std::string TypeConversion::getArrayElementType(const std::string &arrayType) {
	if (!isArrayType(arrayType)) {
		return arrayType;
	}

	// Internal format: _ManagedArray<T>
	if (arrayType.rfind("_ManagedArray<", 0) == 0) {
		// Extract T from _ManagedArray<T>
		size_t start = 14; // length of "_ManagedArray<"
		size_t end = arrayType.rfind('>');
		if (end != std::string::npos && end > start) {
			return arrayType.substr(start, end - start);
		}
	}

	// Internal format: _StaticArray<N, T>
	if (arrayType.rfind("_StaticArray<", 0) == 0) {
		// Extract T from _StaticArray<N, T>
		size_t commaPos = arrayType.find(", ");
		size_t end = arrayType.rfind('>');
		if (commaPos != std::string::npos && end != std::string::npos && end > commaPos + 2) {
			return arrayType.substr(commaPos + 2, end - commaPos - 2);
		}
	}

	return "int"; // Fallback
}

int TypeConversion::getStaticArraySize(const std::string &arrayType) {
	// Internal format: _StaticArray<N, T>
	if (arrayType.rfind("_StaticArray<", 0) == 0) {
		size_t start = 13; // length of "_StaticArray<"
		size_t commaPos = arrayType.find(", ");
		if (commaPos != std::string::npos && commaPos > start) {
			std::string sizeStr = arrayType.substr(start, commaPos - start);
			return std::stoi(sizeStr);
		}
	}

	return 0; // Not a static array or no size
}

std::string TypeConversion::makeManagedArrayType(const std::string &elementType) {
	return "_ManagedArray<" + elementType + ">";
}

std::string TypeConversion::makeStaticArrayType(int size, const std::string &elementType) {
	return "_StaticArray<" + std::to_string(size) + ", " + elementType + ">";
}

std::string TypeConversion::makeArrayStructType(const std::string &elementType) {
	return "array<" + elementType + ">";
}

bool TypeConversion::isArrayStructType(const std::string &type) {
	return type.rfind("array<", 0) == 0 && type.back() == '>';
}

std::string TypeConversion::getArrayStructElementType(const std::string &type) {
	if (!isArrayStructType(type)) {
		return type;
	}
	// Extract T from array<T>
	size_t start = 6; // length of "array<"
	size_t end = type.rfind('>');
	if (end != std::string::npos && end > start) {
		return type.substr(start, end - start);
	}
	return type;
}

std::string TypeConversion::arrayTypeToDisplayString(const std::string &arrayType) {
	// Handle array<T> struct type (stdlib array)
	if (isArrayStructType(arrayType)) {
		std::string elemType = getArrayStructElementType(arrayType);
		// Recursively convert nested array types
		std::string elemDisplay = arrayTypeToDisplayString(elemType);
		return "array of " + elemDisplay;
	}

	if (!isArrayType(arrayType)) {
		return arrayType;
	}

	// Internal format: _ManagedArray<T> -> array of T
	if (arrayType.rfind("_ManagedArray<", 0) == 0) {
		std::string elemType = getArrayElementType(arrayType);
		// Recursively convert nested array types
		std::string elemDisplay = arrayTypeToDisplayString(elemType);
		return "array of " + elemDisplay;
	}

	// Internal format: _StaticArray<N, T> -> array of N T
	if (arrayType.rfind("_StaticArray<", 0) == 0) {
		int size = getStaticArraySize(arrayType);
		std::string elemType = getArrayElementType(arrayType);
		// Recursively convert nested array types
		std::string elemDisplay = arrayTypeToDisplayString(elemType);
		return "array of " + std::to_string(size) + " " + elemDisplay;
	}

	return arrayType;
}

bool TypeConversion::isFunctionType(const std::string &type) {
	return type.rfind("fn(", 0) == 0;
}

bool TypeConversion::parseFunctionType(const std::string &funcType,
									   std::vector<std::string> &paramTypes,
									   std::string &returnType) {
	// Function type format: fn(T1,T2,...)->R
	if (!isFunctionType(funcType)) {
		return false;
	}

	// Find the closing paren and arrow
	size_t parenStart = 3; // After "fn("
	size_t parenEnd = funcType.find(")->");
	if (parenEnd == std::string::npos) {
		return false;
	}

	// Extract parameter types (comma-separated, handling nested types)
	paramTypes.clear();
	std::string paramStr = funcType.substr(parenStart, parenEnd - parenStart);

	if (!paramStr.empty()) {
		size_t start = 0;
		int parenDepth = 0;
		for (size_t i = 0; i <= paramStr.size(); i++) {
			if (i == paramStr.size() || (paramStr[i] == ',' && parenDepth == 0)) {
				std::string param = paramStr.substr(start, i - start);
				// Trim whitespace
				size_t first = param.find_first_not_of(' ');
				size_t last = param.find_last_not_of(' ');
				if (first != std::string::npos) {
					paramTypes.push_back(param.substr(first, last - first + 1));
				}
				start = i + 1;
			} else if (paramStr[i] == '(' || paramStr[i] == '<') {
				parenDepth++;
			} else if (paramStr[i] == ')' || paramStr[i] == '>') {
				parenDepth--;
			}
		}
	}

	// Extract return type
	returnType = funcType.substr(parenEnd + 3); // After ")->"

	return true;
}

} // namespace maxon
