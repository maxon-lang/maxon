#pragma once

#include <string>

/**
 * Type Conversion Rules - Single Source of Truth
 *
 * This module centralizes all type conversion, promotion, and coercion logic
 * for the Maxon compiler. Both semantic analysis and code generation query
 * this module to ensure consistent behavior.
 *
 * Key concepts:
 * - TypeKind: Enumeration of all primitive/builtin types
 * - ConversionKind: What kind of conversion is needed between two types
 * - Promotion: Widening conversions that preserve value (int -> float)
 * - Truncation: Narrowing conversions that may lose precision (float -> int)
 */

namespace maxon {

/// Enumeration of Maxon primitive types for table indexing
enum class TypeKind {
	Void = 0,
	Bool,
	Byte,  // 8-bit unsigned
	Int,   // 32-bit signed
	Float, // 64-bit floating point
	String,
	Character,
	Ptr,
	Error,	  // Sentinel for error types
	Struct,	  // User-defined struct (not in conversion table)
	Array,	  // Array types (not in conversion table)
	Optional, // T or nil (not in conversion table)

	_Count // Number of primitive types in table
};

/// What kind of conversion is allowed/required between two types
enum class ConversionKind {
	None,		 // Types are identical, no conversion needed
	Implicit,	 // Allowed implicitly (e.g., int -> float in mixed arithmetic)
	Explicit,	 // Requires explicit cast (e.g., byte -> int)
	Prohibited,	 // Not allowed (e.g., float -> int without trunc/round/floor/ceil)
	Incompatible // Types cannot be converted at all
};

/// Binary operator categories that affect result type determination
enum class BinaryOpCategory {
	Arithmetic, // +, -, *, /
	Modulo,		// % (requires int operands)
	Division,	// / (special: always returns float)
	Comparison, // <, >, <=, >=, ==, !=
	Logical,	// and, or
	Bitwise,	// &, |, ^
	Shift		// <<, >>
};

/**
 * Central type conversion utilities.
 *
 * All methods are static - this is a stateless utility class.
 */
class TypeConversion {
  public:
	//==========================================================================
	// Type Kind Utilities
	//==========================================================================

	/// Convert a Maxon type string to TypeKind enum
	static TypeKind getTypeKind(const std::string &typeStr);

	/// Convert TypeKind back to Maxon type string
	static std::string getTypeString(TypeKind kind);

	/// Check if a TypeKind is numeric (supports arithmetic)
	static bool isNumeric(TypeKind kind);

	/// Check if a TypeKind is an integer type (int or byte)
	static bool isInteger(TypeKind kind);

	//==========================================================================
	// Conversion Queries
	//==========================================================================

	/// Get the conversion kind from source to target type.
	/// This is the core lookup into the conversion table.
	static ConversionKind getConversionKind(TypeKind source, TypeKind target);

	/// Convenience overload using type strings
	static ConversionKind getConversionKind(const std::string &source, const std::string &target);

	/// Can source be implicitly converted to target?
	/// Returns true for None (same type) or Implicit conversions.
	static bool canConvertImplicitly(TypeKind source, TypeKind target);
	static bool canConvertImplicitly(const std::string &source, const std::string &target);

	/// Can source be explicitly cast to target?
	/// Returns true for None, Implicit, or Explicit conversions.
	static bool canCast(TypeKind source, TypeKind target);
	static bool canCast(const std::string &source, const std::string &target);

	/// Are two types compatible for assignment/comparison?
	/// This handles the "error matches anything" rule and optional types.
	static bool typesMatch(const std::string &type1, const std::string &type2);

	//==========================================================================
	// Binary Expression Type Rules
	//==========================================================================

	/// Get the result type of a binary operation.
	/// Returns the Maxon type string for the result (e.g., "float" for int / int).
	static std::string getBinaryResultType(const std::string &leftType,
										   const std::string &rightType,
										   char op);

	/// Categorize a binary operator
	static BinaryOpCategory getBinaryOpCategory(char op);

	/// Does this binary operation require float promotion?
	/// True for division or when either operand is float.
	static bool needsFloatPromotion(const std::string &leftType,
									const std::string &rightType,
									char op);

	//==========================================================================
	// Special Cases
	//==========================================================================

	/// Check if an integer literal in range 0-255 can be treated as byte
	/// for comparison with a byte variable (contextual literal typing).
	static bool isLiteralByteCompatible(int literalValue);

	/// Check if a type is an optional type (contains " or nil")
	static bool isOptionalType(const std::string &type);

	/// Extract base type from optional (e.g., "int or nil" -> "int")
	static std::string unwrapOptionalType(const std::string &type);

	/// Make a type optional (e.g., "int" -> "int or nil")
	static std::string makeOptionalType(const std::string &type);

	/// Check if a type is an array type (starts with '[')
	static bool isArrayType(const std::string &type);

	/// Extract element type from array (e.g., "[5]int" -> "int", "[]byte" -> "byte")
	static std::string getArrayElementType(const std::string &arrayType);

  private:
	/// The conversion table: [source][target] -> ConversionKind
	/// Indexed by TypeKind ordinal values.
	static const ConversionKind conversionTable[static_cast<int>(TypeKind::_Count)]
											   [static_cast<int>(TypeKind::_Count)];
};

} // namespace maxon
