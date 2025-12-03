#include "type_conversion.h"
#include <algorithm>

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

	// Array types: [N]T or []T
	if (!typeStr.empty() && typeStr[0] == '[') {
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
	return !type.empty() && type[0] == '[';
}

std::string TypeConversion::getArrayElementType(const std::string &arrayType) {
	if (!isArrayType(arrayType)) {
		return arrayType;
	}

	size_t closeBracket = arrayType.find(']');
	if (closeBracket != std::string::npos && closeBracket + 1 < arrayType.size()) {
		return arrayType.substr(closeBracket + 1);
	}

	return "int"; // Fallback
}

} // namespace maxon
