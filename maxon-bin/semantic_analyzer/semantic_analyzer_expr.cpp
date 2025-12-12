#include "../intrinsics.h"
#include "../lexer.h"
#include "../semantic_analyzer.h"
#include "../type_members.h"
#include "../types/type_conversion.h"
#include <algorithm>
#include <set>

// Expression analysis implementation
std::string SemanticAnalyzer::analyzeExpression(ExprAST *expr) {
	if (dynamic_cast<NumberExprAST *>(expr)) {
		return "int";

	} else if (dynamic_cast<ByteExprAST *>(expr)) {
		return "byte";

	} else if (dynamic_cast<FloatExprAST *>(expr)) {
		return "float";

	} else if (dynamic_cast<BooleanExprAST *>(expr)) {
		return "bool";

	} else if (dynamic_cast<NilExprAST *>(expr)) {
		return "nil";

	} else if (dynamic_cast<CharacterExprAST *>(expr)) {
		// Character literals require the stdlib 'character' struct (grapheme cluster)
		// Track as undefined so it gets auto-discovered
		if (lookupStruct("character") == nullptr) {
			undefinedStructs.insert("character");
		}
		return "character";

	} else if (dynamic_cast<StringLiteralExprAST *>(expr)) {
		// String literals require the stdlib 'string' struct
		// Track as undefined so it gets auto-discovered
		if (lookupStruct("string") == nullptr) {
			undefinedStructs.insert("string");
		}
		return "string";

	} else if (auto arrayLiteral = dynamic_cast<ArrayLiteralExprAST *>(expr)) {
		// Array literal: [val1, val2, ...] form - value-initialized array
		// Returns array<T> (the stdlib struct type)
		if (arrayLiteral->values.empty()) {
			addError("Array literal cannot be empty",
					 expr->line, expr->column);
			return "error";
		}

		// Infer element type from first element
		std::string elemType = analyzeExpression(arrayLiteral->values[0].get());

		// Validate all elements have same type
		for (size_t i = 1; i < arrayLiteral->values.size(); i++) {
			std::string valueType = analyzeExpression(arrayLiteral->values[i].get());
			if (!typesMatch(elemType, valueType)) {
				addError("Array element type mismatch: expected '" + elemType + "', got '" + valueType + "'" +
							 std::string("\n  Note: All array elements must have the same type"),
						 expr->line, expr->column);
			}
		}

		// Track the base array struct as undefined for auto-import
		// This triggers loading array.maxon so the generic template is available for monomorphization
		if (structs.find("array") == structs.end()) {
			undefinedStructs.insert("array");
		}

		// Return array<T> struct type - methods will be resolved through the stdlib array struct
		return maxon::TypeConversion::makeArrayStructType(elemType);

	} else if (auto sizedArray = dynamic_cast<SizedArrayExprAST *>(expr)) {
		// Sized array: array of N T or array of T or array of expr T
		// Returns array<T> (the stdlib struct type)

		// If there's a size expression, analyze it to mark variables as used
		if (sizedArray->hasVariableSize()) {
			analyzeExpression(sizedArray->sizeExpr.get());
		}

		// Track the base array struct as undefined for auto-import
		// This triggers loading array.maxon so the generic template is available for monomorphization
		if (structs.find("array") == structs.end()) {
			undefinedStructs.insert("array");
		}

		// Return array<T> struct type - methods will be resolved through the stdlib array struct
		return maxon::TypeConversion::makeArrayStructType(sizedArray->elementType);

	} else if (auto mapLiteral = dynamic_cast<MapLiteralExprAST *>(expr)) {
		// Map literal: map from K to V
		// Validate key type implements Hashable
		const std::string &keyType = mapLiteral->keyType;

		if (!typeIsHashable(keyType)) {
			addError("Map key type '" + keyType + "' must be Hashable (provide hash() int method)",
					 expr->line, expr->column);
			return "error";
		}

		// Validate value type exists
		const std::string &valueType = mapLiteral->valueType;
		bool isBuiltinValueType = (valueType == "int" || valueType == "float" ||
								   valueType == "bool" || valueType == "character" ||
								   valueType == "byte" ||
								   valueType.substr(0, 1) == "["); // Array types

		if (!isBuiltinValueType && structs.find(valueType) == structs.end()) {
			// Track for auto-import
			undefinedStructs.insert(valueType);
		}

		// Register map methods for this specific key/value type pair
		// Use the dictionary type name from the AST (e.g., "map", "HashMap", etc.)
		std::string mapType = mapLiteral->dictType + "<" + keyType + "," + valueType + ">";

		// Instantiate map methods for this key/value type using generic mechanism
		std::map<std::string, std::string> typeBindings = {{"Key", keyType}, {"Value", valueType}};
		instantiateGenericStructMethods(mapLiteral->dictType, mapType, typeBindings);

		// Track the base dictionary struct (e.g., "map") as undefined for auto-import
		// This triggers loading map.maxon so the generic template is available for monomorphization
		if (structs.find(mapLiteral->dictType) == structs.end()) {
			undefinedStructs.insert(mapLiteral->dictType);
		}

		// Return the dictionary type string: dictType<keyType,valueType>
		return mapType;

	} else if (auto mapWithEntries = dynamic_cast<MapLiteralWithEntriesExprAST *>(expr)) {
		// Map literal with entries: ["key1": val1, "key2": val2]
		if (mapWithEntries->entries.empty()) {
			addError("Map literal cannot be empty\n"
					 "  Use: map from K to V  (for empty map)",
					 expr->line, expr->column);
			return "error";
		}

		// Analyze all key and value expressions to determine types
		std::string keyType;
		std::string valueType;

		for (const auto &entry : mapWithEntries->entries) {
			std::string entryKeyType = analyzeExpression(entry.key.get());
			std::string entryValueType = analyzeExpression(entry.value.get());

			if (keyType.empty()) {
				keyType = entryKeyType;
				valueType = entryValueType;
			} else {
				// Ensure consistent types
				if (entryKeyType != keyType) {
					addError("Inconsistent key types in map literal\n"
							 "  First key type: " +
								 keyType + "\n"
										   "  This key type: " +
								 entryKeyType,
							 entry.key->line, entry.key->column);
					return "error";
				}
				if (entryValueType != valueType) {
					addError("Inconsistent value types in map literal\n"
							 "  First value type: " +
								 valueType + "\n"
											 "  This value type: " +
								 entryValueType,
							 entry.value->line, entry.value->column);
					return "error";
				}
			}
		}

		// Store inferred types for codegen
		mapWithEntries->inferredKeyType = keyType;
		mapWithEntries->inferredValueType = valueType;

		// Validate key type implements Hashable
		if (!typeIsHashable(keyType)) {
			addError("Map key type '" + keyType + "' must be Hashable (provide hash() int method)",
					 expr->line, expr->column);
			return "error";
		}

		// Instantiate map methods for this key/value type using generic mechanism
		std::string mapType = "map<" + keyType + "," + valueType + ">";
		std::map<std::string, std::string> typeBindings = {{"Key", keyType}, {"Value", valueType}};
		instantiateGenericStructMethods("map", mapType, typeBindings);

		// Track map struct as undefined for auto-import
		if (structs.find("map") == structs.end()) {
			undefinedStructs.insert("map");
		}

		// Return the map type string: map<keyType,valueType>
		return mapType;

	} else if (auto setFromExpr = dynamic_cast<SetFromExprAST *>(expr)) {
		// Set from array: set from [1, 2, 3]
		// Analyze the array expression to get element type
		std::string arrayType = analyzeExpression(setFromExpr->arrayExpr.get());

		// Extract element type from array type using TypeConversion utility
		std::string elemType;
		if (maxon::TypeConversion::isArrayStructType(arrayType)) {
			// array<T> struct type
			elemType = maxon::TypeConversion::getArrayStructElementType(arrayType);
		} else if (maxon::TypeConversion::isManagedArrayType(arrayType)) {
			// Managed array type: _ManagedArray<T>
			elemType = maxon::TypeConversion::getArrayElementType(arrayType);
		}

		if (elemType.empty()) {
			addError("Cannot determine element type from array expression in 'set from'" +
						 std::string("\n  Expected: set from [values] or set from arrayVariable") +
						 "\n  Got type: " + arrayType,
					 expr->line, expr->column);
			return "error";
		}

		// Store the inferred element type for codegen
		setFromExpr->inferredElementType = elemType;

		// Validate element type implements Hashable (sets require hashable elements)
		if (!typeIsHashable(elemType)) {
			addError("Set element type '" + elemType + "' must be Hashable (provide hash() int method)",
					 expr->line, expr->column);
			return "error";
		}

		// Instantiate set methods for this element type using generic mechanism
		std::string setType = setFromExpr->setType + "<" + elemType + ">";
		std::map<std::string, std::string> typeBindings = {{"Element", elemType}};
		instantiateGenericStructMethods(setFromExpr->setType, setType, typeBindings);

		// Track the base set struct for auto-import
		if (structs.find(setFromExpr->setType) == structs.end()) {
			undefinedStructs.insert(setFromExpr->setType);
		}

		return setType;

	} else if (auto castExpr = dynamic_cast<CastExprAST *>(expr)) {
		// Analyze the expression being cast
		std::string sourceType = analyzeExpression(castExpr->expr.get());

		// Use centralized type conversion rules to validate cast
		auto convKind = maxon::TypeConversion::getConversionKind(sourceType, castExpr->targetType);
		if (convKind == maxon::ConversionKind::Prohibited) {
			addError("Cannot cast " + sourceType + " to " + castExpr->targetType + "; use trunc(), round(), floor(), or ceil() instead" +
						 std::string("\n  Hint: trunc() truncates toward zero"),
					 expr->line, expr->column);
			return "error";
		}

		// Check if target type is a struct - if so, track as undefined for auto-import
		const std::string &targetType = castExpr->targetType;
		bool isBuiltinType = (targetType == "int" || targetType == "float" ||
							  targetType == "bool" || targetType == "character" ||
							  targetType == "string" || targetType == "void" ||
							  targetType.substr(0, 1) == "["); // Array types

		if (!isBuiltinType && structs.find(targetType) == structs.end()) {
			// Target type is a struct that isn't defined yet - track for auto-import
			undefinedStructs.insert(targetType);
		}

		// Valid casts: int <-> char, char <-> int, or ExpressibleByStringLiteral types
		return castExpr->targetType;

	} else if (auto coalesceExpr = dynamic_cast<OrCoalesceExprAST *>(expr)) {
		// Nil coalescing: optionalExpr or defaultExpr
		// Left must be optional (T or nil), right must be T, result is T
		std::string leftType = analyzeExpression(coalesceExpr->optionalExpr.get());
		std::string rightType = analyzeExpression(coalesceExpr->defaultExpr.get());

		// Verify left operand is optional type
		if (!isOptionalType(leftType)) {
			addError("Nil coalescing 'or' requires optional type on the left" +
						 std::string("\n  Got: ") + leftType +
						 "\n  Note: Use 'or' for nil coalescing: optionalValue or defaultValue",
					 coalesceExpr->optionalExpr->line, coalesceExpr->optionalExpr->column);
			return "error";
		}

		// Prevent chaining: right operand cannot be optional
		if (isOptionalType(rightType)) {
			addError("Cannot chain nil coalescing operators" +
						 std::string("\n  Right operand type: ") + rightType +
						 "\n  Note: The result of 'or' is already unwrapped, so chaining is not allowed",
					 coalesceExpr->defaultExpr->line, coalesceExpr->defaultExpr->column);
			return "error";
		}

		// Unwrap the optional type and verify the default matches
		std::string unwrappedType = unwrapOptionalType(leftType);
		if (!typesMatch(unwrappedType, rightType)) {
			// Allow nil literal as default for optional result
			if (rightType == "nil") {
				addError("Nil coalescing default cannot be nil" +
							 std::string("\n  Note: The default value provides the fallback when the optional is nil"),
						 coalesceExpr->defaultExpr->line, coalesceExpr->defaultExpr->column);
				return "error";
			}
			addError("Nil coalescing type mismatch" +
						 std::string("\n  Optional unwraps to: ") + unwrappedType +
						 "\n  Default value type: " + rightType +
						 "\n  Note: The default must match the unwrapped optional type",
					 expr->line, expr->column);
			return "error";
		}

		// Result is the unwrapped type
		return unwrappedType;

	} else if (auto varExpr = dynamic_cast<VariableExprAST *>(expr)) {
		auto varInfo = lookupVariable(varExpr->name);
		if (!varInfo.has_value()) {
			// Check if this is a function reference (function name used as value)
			auto funcIt = functions.find(varExpr->name);
			if (funcIt == functions.end()) {
				// Try suffix matching for unqualified function names
				std::string searchSuffix = "." + varExpr->name;
				for (const auto &pair : functions) {
					const std::string &funcName = pair.first;
					if (funcName.size() > searchSuffix.size() &&
						funcName.substr(funcName.size() - searchSuffix.size()) == searchSuffix) {
						funcIt = functions.find(funcName);
						break;
					}
				}
			}

			if (funcIt != functions.end()) {
				// This is a function reference - build the function type
				const FunctionInfo &funcInfo = funcIt->second;
				std::string funcType = "fn(";
				for (size_t i = 0; i < funcInfo.parameters.size(); i++) {
					if (i > 0)
						funcType += ",";
					funcType += funcInfo.parameters[i].type;
				}
				funcType += ")->" + funcInfo.returnType;

				// Mark this variable expression as a function reference
				varExpr->isFunctionReference = true;
				varExpr->resolvedFunctionName = funcIt->first;

				return funcType;
			}

			addError("Undefined variable: '" + varExpr->name + "'" +
						 std::string("\n  Note: Variable must be declared with 'var' or 'let' before use"),
					 expr->line, expr->column);
			return "error";
		}
		// Mark variable as used when it's referenced
		markVariableAsUsed(varExpr->name);
		return varInfo->type;

	} else if (auto binExpr = dynamic_cast<BinaryExprAST *>(expr)) {
		std::string leftType = analyzeExpression(binExpr->left.get());
		std::string rightType = analyzeExpression(binExpr->right.get());

		// Special case: 'or' with optional left operand is nil coalescing
		// This handles cases where the parser couldn't distinguish nil coalescing from logical or
		if (binExpr->op == 'O' && isOptionalType(leftType)) {
			// Treat as nil coalescing: optionalExpr or defaultExpr
			// Right operand cannot be optional (no chaining)
			if (isOptionalType(rightType)) {
				addError("Cannot chain nil coalescing operators" +
							 std::string("\n  Right operand type: ") + rightType +
							 "\n  Note: The result of 'or' is already unwrapped, so chaining is not allowed",
						 binExpr->right->line, binExpr->right->column);
				return "error";
			}

			// Unwrap the optional type and verify the default matches
			std::string unwrappedType = unwrapOptionalType(leftType);
			if (!typesMatch(unwrappedType, rightType)) {
				if (rightType == "nil") {
					addError("Nil coalescing default cannot be nil" +
								 std::string("\n  Note: The default value provides the fallback when the optional is nil"),
							 binExpr->right->line, binExpr->right->column);
					return "error";
				}
				addError("Nil coalescing type mismatch" +
							 std::string("\n  Optional unwraps to: ") + unwrappedType +
							 "\n  Default value type: " + rightType +
							 "\n  Note: The default must match the unwrapped optional type",
						 expr->line, expr->column);
				return "error";
			}

			// Return the unwrapped type (nil coalescing always produces non-optional)
			return unwrappedType;
		}

		// Prevent using optional types without unwrapping (except for nil coalescing handled above)
		if (isOptionalType(leftType)) {
			addError("Cannot use optional type '" + leftType + "' without unwrapping" +
						 std::string("\n  Note: Use 'if let' to safely unwrap optional values before using them"),
					 binExpr->left->line, binExpr->left->column);
			return "error";
		}
		if (isOptionalType(rightType)) {
			addError("Cannot use optional type '" + rightType + "' without unwrapping" +
						 std::string("\n  Note: Use 'if let' to safely unwrap optional values before using them"),
					 binExpr->right->line, binExpr->right->column);
			return "error";
		}

		// Arithmetic operators: +, -, *, /, %
		if (binExpr->op == '+' || binExpr->op == '-' || binExpr->op == '*' ||
			binExpr->op == '/' || binExpr->op == '%') {
			// Special case: string + string = string concatenation
			if (binExpr->op == '+' && leftType == "string" && rightType == "string") {
				return "string";
			}

			// Special handling for modulo: requires both operands to be int
			if (binExpr->op == '%') {
				if (leftType != "int" || rightType != "int") {
					addError("Modulo operator (%) requires integer operands" +
								 std::string("\n  Left operand type: ") + leftType +
								 "\n  Right operand type: " + rightType,
							 expr->line, expr->column);
					return "error";
				}
				return "int";
			}

			// For other arithmetic operators: allow int or float
			// Result is float if either operand is float (implicit promotion)
			if ((leftType == "int" || leftType == "float") &&
				(rightType == "int" || rightType == "float")) {
				// Division always returns float (optimization pass handles trunc(int/int))
				if (binExpr->op == '/') {
					return "float";
				}
				// Other operators: return float if either operand is float
				if (leftType == "float" || rightType == "float") {
					return "float";
				}
				return "int";
			}

			std::string opName;
			if (binExpr->op == '+')
				opName = "addition (+)";
			else if (binExpr->op == '-')
				opName = "subtraction (-)";
			else if (binExpr->op == '*')
				opName = "multiplication (*)";
			else
				opName = "division (/";

			addError("Arithmetic operator " + opName + " requires numeric operands (int or float)" +
						 std::string("\n  Left operand type: ") + leftType +
						 "\n  Right operand type: " + rightType,
					 expr->line, expr->column);
			return "error";
		}

		// Comparison operators: <, >, L (<=), G (>=), E (==), N (!=)
		if (binExpr->op == '<' || binExpr->op == '>' || binExpr->op == 'L' ||
			binExpr->op == 'G' || binExpr->op == 'E' || binExpr->op == 'N') {
			// Exact type matching for int and float (no implicit promotion)
			if (leftType == "int" && rightType == "int") {
				return "bool";
			}
			if (leftType == "float" && rightType == "float") {
				return "bool";
			}

			// Contextual literal typing for byte ↔ int:
			// - int literal (0-255) compared to byte variable: treat literal as byte
			// - byte literal compared to int variable: treat literal as int
			if (leftType == "byte" && rightType == "int") {
				if (auto *numExpr = dynamic_cast<NumberExprAST *>(binExpr->right.get())) {
					if (numExpr->value >= 0 && numExpr->value <= 255) {
						return "bool";
					}
				}
			}
			if (leftType == "int" && rightType == "byte") {
				if (auto *numExpr = dynamic_cast<NumberExprAST *>(binExpr->left.get())) {
					if (numExpr->value >= 0 && numExpr->value <= 255) {
						return "bool";
					}
				}
			}
			// byte literal compared to int variable
			if (leftType == "int" && rightType == "byte") {
				if (dynamic_cast<ByteExprAST *>(binExpr->right.get())) {
					return "bool";
				}
			}
			if (leftType == "byte" && rightType == "int") {
				if (dynamic_cast<ByteExprAST *>(binExpr->left.get())) {
					return "bool";
				}
			}

			// byte == byte is always valid
			if (leftType == "byte" && rightType == "byte") {
				return "bool";
			}

			// For == and != on struct/enum types
			if (binExpr->op == 'E' || binExpr->op == 'N') {
				// Check if comparing enum types
				const EnumInfo *leftEnum = lookupEnum(leftType);
				const EnumInfo *rightEnum = lookupEnum(rightType);

				if (leftEnum != nullptr || rightEnum != nullptr) {
					// At least one operand is an enum type
					if (leftType != rightType) {
						addError("Cannot compare different enum types with == or !=" +
									 std::string("\n  Left operand type: ") + leftType +
									 "\n  Right operand type: " + rightType,
								 expr->line, expr->column);
						return "error";
					}
					// Same enum type - comparison is valid
					return "bool";
				}

				// Check if comparing struct types
				const StructInfo *leftStruct = lookupStruct(leftType);
				const StructInfo *rightStruct = lookupStruct(rightType);

				if (leftStruct != nullptr || rightStruct != nullptr) {
					// At least one operand is a struct type
					if (leftType != rightType) {
						addError("Cannot compare different types with == or !=" +
									 std::string("\n  Left operand type: ") + leftType +
									 "\n  Right operand type: " + rightType,
								 expr->line, expr->column);
						return "error";
					}

					// Both are the same struct type, check Equatable conformance
					bool conformsToEquatable = typeIsEquatable(leftType);

					if (!conformsToEquatable) {
						addError("Cannot use == or != on type '" + leftType +
									 "' because it does not implement the Equatable interface",
								 expr->line, expr->column);
						return "error";
					}

					return "bool";
				}
			}

			if (leftType != rightType) {
				addError("Comparison operators require operands of compatible types" +
							 std::string("\n  Left operand type: ") + leftType +
							 "\n  Right operand type: " + rightType,
						 expr->line, expr->column);
				return "error";
			}
			return "bool";
		}

		// Logical operators: A (and), O (or)
		if (binExpr->op == 'A' || binExpr->op == 'O') {
			// Logical operators work on bool operands or int (truthy/falsy)
			if ((leftType == "bool" || leftType == "int") &&
				(rightType == "bool" || rightType == "int")) {
				return "bool";
			}
			std::string opName = binExpr->op == 'A' ? "and" : "or";
			addError("Logical operator '" + opName + "' requires boolean or integer operands" +
						 std::string("\n  Left operand type: ") + leftType +
						 "\n  Right operand type: " + rightType,
					 expr->line, expr->column);
			return "error";
		}

		// Bitwise operators: & (AND), | (OR), ^ (XOR)
		if (binExpr->op == '&' || binExpr->op == '|' || binExpr->op == '^') {
			// Bitwise operators require integer operands
			if (leftType == "int" && rightType == "int") {
				return "int";
			}
			std::string opName;
			if (binExpr->op == '&')
				opName = "bitwise AND (&)";
			else if (binExpr->op == '|')
				opName = "bitwise OR (|)";
			else
				opName = "bitwise XOR (^)";
			addError("Operator " + opName + " requires integer operands" +
						 std::string("\n  Left operand type: ") + leftType +
						 "\n  Right operand type: " + rightType,
					 expr->line, expr->column);
			return "error";
		}

		// Shift operators: S (<<), H (>>)
		if (binExpr->op == 'S' || binExpr->op == 'H') {
			// Shift operators require integer operands
			if (leftType == "int" && rightType == "int") {
				return "int";
			}
			std::string opName = binExpr->op == 'S' ? "left shift (<<)" : "right shift (>>)";
			addError("Operator " + opName + " requires integer operands" +
						 std::string("\n  Left operand type: ") + leftType +
						 "\n  Right operand type: " + rightType,
					 expr->line, expr->column);
			return "error";
		}

		addError("Unknown binary operator", expr->line, expr->column);
		return "error";

	} else if (auto unaryExpr = dynamic_cast<UnaryExprAST *>(expr)) {
		std::string operandType = analyzeExpression(unaryExpr->operand.get());

		// Unary + and - work on numeric types
		if (unaryExpr->op == '+' || unaryExpr->op == '-') {
			if (operandType == "int" || operandType == "float") {
				return operandType; // Result type is same as operand type
			}

			std::string opName = (unaryExpr->op == '+') ? "unary plus (+)" : "unary minus (-)";
			addError("Operator " + opName + " requires numeric operand (int or float)" +
						 std::string("\n  Operand type: ") + operandType,
					 expr->line, expr->column);
			return "error";
		}

		// Logical not: works on bool or int
		if (unaryExpr->op == '!') {
			if (operandType == "bool" || operandType == "int") {
				return "bool";
			}
			addError("Operator 'not' requires boolean or integer operand" +
						 std::string("\n  Operand type: ") + operandType,
					 expr->line, expr->column);
			return "error";
		}

		addError("Unknown unary operator", expr->line, expr->column);
		return "error";

	} else if (auto callExpr = dynamic_cast<CallExprAST *>(expr)) {
		// Check if this is a built-in math function (intrinsics only)
		// NOTE: The list of math intrinsics is defined in lexer.cpp's keywords map
		if (Lexer::isMathIntrinsic(callExpr->callee)) {
			// Validate argument count (all intrinsics take 1 argument)
			if (callExpr->args.size() != 1) {
				addError("Function '" + callExpr->callee + "' expects exactly 1 argument",
						 expr->line, expr->column);
				return "float";
			}

			// Analyze arguments - allow int, byte, or float (int/byte are implicitly promoted)
			for (auto &arg : callExpr->args) {
				std::string argType = analyzeExpression(arg.value.get());
				if (argType != "int" && argType != "byte" && argType != "float" && argType != "error") {
					addError("Function '" + callExpr->callee + "' requires numeric argument, got " + argType,
							 expr->line, expr->column);
				}
			}

			// Return type from metadata
			const MathIntrinsicInfo *info = Lexer::getMathIntrinsicInfo(callExpr->callee);
			return info ? info->returnType : "float";
		}

		// Handle compiler intrinsics using the registry
		const IntrinsicInfo *intrinsic = IntrinsicRegistry::instance().lookup(callExpr->callee);
		if (intrinsic) {
			// Validate argument count
			if (callExpr->args.size() != intrinsic->params.size()) {
				addError(intrinsic->name + " requires exactly " + std::to_string(intrinsic->params.size()) +
							 " argument(s)",
						 expr->line, expr->column);
				return "error";
			}

			// Validate each argument type
			for (size_t i = 0; i < intrinsic->params.size(); ++i) {
				std::string argType = analyzeExpression(callExpr->args[i].value.get());
				std::string error = IntrinsicRegistry::instance().validateArgType(intrinsic->params[i], argType);
				if (!error.empty()) {
					addError(intrinsic->name + " argument " + std::to_string(i + 1) + ": " + error,
							 expr->line, expr->column);
					return "error";
				}
			}

			return intrinsic->returnType;
		}

		// Check for unknown intrinsics (functions starting with __)
		if (callExpr->callee.rfind("__", 0) == 0) {
			addError("Unknown intrinsic: " + callExpr->callee, expr->line, expr->column);
			return "error";
		}

		// Check if this is an enum case construction with associated values (e.g., Result.success(42))
		size_t dotPos = callExpr->callee.find(".");
		if (dotPos != std::string::npos) {
			std::string potentialEnumName = callExpr->callee.substr(0, dotPos);
			std::string potentialCaseName = callExpr->callee.substr(dotPos + 1);

			const EnumInfo *enumInfo = lookupEnum(potentialEnumName);
			if (enumInfo != nullptr) {
				const EnumCaseInfo *caseInfo = enumInfo->findCase(potentialCaseName);
				if (caseInfo != nullptr) {
					// This is an enum case construction
					// Validate associated value arguments
					if (callExpr->args.size() != caseInfo->associatedValues.size()) {
						if (caseInfo->associatedValues.empty()) {
							addError("Enum case '" + potentialCaseName + "' does not have associated values\n"
																		 "  Use: " +
										 potentialEnumName + "." + potentialCaseName,
									 expr->line, expr->column);
						} else {
							addError("Wrong number of associated values for case '" + potentialCaseName + "': expected " +
										 std::to_string(caseInfo->associatedValues.size()) + ", got " +
										 std::to_string(callExpr->args.size()),
									 expr->line, expr->column);
						}
						return potentialEnumName; // Still return the enum type
					}

					// Type check each associated value argument
					for (size_t i = 0; i < callExpr->args.size(); i++) {
						std::string argType = analyzeExpression(callExpr->args[i].value.get());
						const std::string &expectedType = caseInfo->associatedValues[i].type;

						if (!typesMatch(expectedType, argType)) {
							addError("Type mismatch for associated value '" + caseInfo->associatedValues[i].name +
										 "': expected '" + expectedType + "', got '" + argType + "'",
									 callExpr->args[i].line, callExpr->args[i].column);
						}
					}

					// Store resolved enum info in the AST for codegen
					callExpr->resolvedEnumName = potentialEnumName;
					callExpr->resolvedEnumCaseName = potentialCaseName;

					return potentialEnumName;
				}
			}
		}

		// Try to find the function - first exact match, then unqualified lookup
		// First, if we're inside a method, check for a sibling method call
		// This handles the case where find(needle) inside string.contains() should resolve to string.find
		auto funcIt = functions.end();
		if (!currentReceiverType.empty() && callExpr->callee.find(".") == std::string::npos) {
			// Try qualified name: currentReceiverType.callee (e.g., "string.find")
			std::string qualifiedName = currentReceiverType + "." + callExpr->callee;
			funcIt = functions.find(qualifiedName);
		}

		// If not found as sibling method, try exact match with the callee name
		if (funcIt == functions.end()) {
			funcIt = functions.find(callExpr->callee);
		}

		// Check if this is a qualified call (contains .)
		bool isQualifiedCall = callExpr->callee.find(".") != std::string::npos;

		// If the call is qualified, check if it's unnecessary
		// But skip this check for method calls (Type.method) since qualification is required
		if (isQualifiedCall && funcIt != functions.end()) {
			// Check if this is a method call (qualifier is a type name)
			size_t lastDotPos = callExpr->callee.rfind(".");
			std::string qualifier = callExpr->callee.substr(0, lastDotPos);
			std::string unqualifiedName = callExpr->callee.substr(lastDotPos + 1);

			// Check if qualifier is a type (struct) name - if so, this is a method call
			bool isMethodCall = (structs.find(qualifier) != structs.end());

			// Disallow qualified method calls - use instance.method() syntax instead
			if (isMethodCall) {
				addError("Qualified method call not allowed: '" + callExpr->callee + "'" +
							 std::string("\n  Use method call syntax instead: instance.") + unqualifiedName + "()",
						 expr->line, expr->column);
				// Still analyze arguments to mark variables as used
				for (auto &arg : callExpr->args) {
					analyzeExpression(arg.value.get());
				}
				return "error";
			}

			// Only warn about unnecessary qualification for namespace-qualified calls
			// Check how many functions match the unqualified name
			std::string searchSuffix = "." + unqualifiedName;
			std::vector<std::string> matches;

			// Check for exact match with unqualified name (global function)
			if (functions.find(unqualifiedName) != functions.end()) {
				matches.push_back(unqualifiedName);
			}

			// Check for qualified matches (but exclude method-style qualifications)
			for (const auto &pair : functions) {
				const std::string &funcName = pair.first;
				if (funcName.size() > searchSuffix.size() &&
					funcName.substr(funcName.size() - searchSuffix.size()) == searchSuffix) {
					// Check if this is a method (qualifier is a type)
					size_t dotPos = funcName.rfind(".");
					if (dotPos != std::string::npos) {
						std::string funcQualifier = funcName.substr(0, dotPos);
						if (structs.find(funcQualifier) != structs.end()) {
							// This is a method, don't include in matches for unqualified resolution
							continue;
						}
					}
					matches.push_back(funcName);
				}
			}

			// If there's only one match, the qualified name is unnecessary
			if (matches.size() == 1) {
				addWarning("Unnecessary qualified name: '" + callExpr->callee + "'" +
							   std::string("\n  The unqualified name '") + unqualifiedName + "' is unambiguous" +
							   "\n  Consider using '" + unqualifiedName + "' instead",
						   expr->line, expr->column, "unnecessary-qualified-name");
			}
		}

		// If not found and the name is unqualified (no .), try suffix matching
		if (funcIt == functions.end() && callExpr->callee.find(".") == std::string::npos) {
			std::string searchSuffix = "." + callExpr->callee;
			std::vector<std::string> matches;

			for (const auto &pair : functions) {
				const std::string &funcName = pair.first;
				if (funcName.size() > searchSuffix.size() &&
					funcName.substr(funcName.size() - searchSuffix.size()) == searchSuffix) {
					matches.push_back(funcName);
				}
			}

			if (matches.empty()) {
				// Check if callee is a variable with a function type
				auto varInfo = lookupVariable(callExpr->callee);
				if (varInfo.has_value() && maxon::TypeConversion::isFunctionType(varInfo->type)) {
					// Mark variable as used
					markVariableAsUsed(callExpr->callee);

					// Parse the function type to get parameter types and return type
					std::vector<std::string> expectedParamTypes;
					std::string returnType;
					if (!maxon::TypeConversion::parseFunctionType(varInfo->type, expectedParamTypes, returnType)) {
						addError("Invalid function type: '" + varInfo->type + "'",
								 expr->line, expr->column);
						return "error";
					}

					// Check argument count
					if (callExpr->args.size() != expectedParamTypes.size()) {
						addError("Function variable '" + callExpr->callee + "' argument count mismatch" +
									 std::string("\n  Expected: ") + std::to_string(expectedParamTypes.size()) + " argument" +
									 (expectedParamTypes.size() == 1 ? "" : "s") +
									 "\n  Found: " + std::to_string(callExpr->args.size()) + " argument" +
									 (callExpr->args.size() == 1 ? "" : "s"),
								 expr->line, expr->column);
						return returnType;
					}

					// Check argument types
					for (size_t i = 0; i < callExpr->args.size(); i++) {
						std::string argType = analyzeExpression(callExpr->args[i].value.get());
						std::string expectedType = expectedParamTypes[i];

						bool isValidType = typesMatch(expectedType, argType);
						if (!isValidType) {
							isValidType = maxon::TypeConversion::canConvertImplicitly(argType, expectedType);
						}

						if (!isValidType) {
							addError("Function variable '" + callExpr->callee + "' argument type mismatch" +
										 std::string("\n  Parameter ") + std::to_string(i + 1) +
										 "\n  Expected type: " + expectedType +
										 "\n  Found type: " + argType,
									 expr->line, expr->column);
						}
					}

					// Mark this as a function variable call for codegen
					callExpr->isFunctionVariableCall = true;

					return returnType;
				}

				// Track this as an undefined function for potential auto-discovery
				undefinedFunctions.insert(callExpr->callee);

				addError("Undefined function: '" + callExpr->callee + "'" +
							 std::string("\n  Note: Function must be defined before it can be called"),
						 expr->line, expr->column);
				// Still analyze arguments to mark variables as used
				for (auto &arg : callExpr->args) {
					analyzeExpression(arg.value.get());
				}
				return "error";
			} else if (matches.size() > 1) {
				// Multiple matches found - try to disambiguate using first argument type
				// This handles method calls where obj.method() becomes method(obj)
				// and the first argument's type can resolve which Type.method to call
				std::string resolvedMatch;

				if (!callExpr->args.empty()) {
					// Get the type of the first argument
					// NOTE: This analysis may trigger instantiateGenericStructMethods for struct field
					// array types, adding new specialized methods to the functions map
					std::string firstArgType = analyzeExpression(callExpr->args[0].value.get());
					logTrace("Method disambiguation: callee='" + callExpr->callee + "' firstArgType='" + firstArgType + "'");

					// After analyzing the first argument, check if an exact match was added
					// (e.g., array<string>.count may have been instantiated when accessing a struct field)
					std::string exactMethodName = firstArgType + "." + callExpr->callee;
					auto exactIt = functions.find(exactMethodName);
					if (exactIt != functions.end()) {
						logTrace("  Found newly instantiated method: " + exactMethodName);
						resolvedMatch = exactMethodName;
					}

					// If no exact match from instantiation, look through original matches
					if (resolvedMatch.empty()) {
						// Look for a method with matching qualifier (Type.method where Type == firstArgType)
						// Prefer exact matches over generic base type matches
						std::string exactMatch;
						std::string baseTypeMatch;

						for (const auto &match : matches) {
							logTrace("  Checking match: " + match);
							size_t dotPos = match.rfind(".");
							if (dotPos != std::string::npos) {
								std::string qualifier = match.substr(0, dotPos);
								logTrace("    qualifier='" + qualifier + "' vs firstArgType='" + firstArgType + "'");

								// Check for exact match first
								if (qualifier == firstArgType) {
									logTrace("    -> EXACT MATCH!");
									if (exactMatch.empty()) {
										exactMatch = match;
									} else {
										// Multiple exact matches - ambiguous
										exactMatch.clear();
										break;
									}
								}
								// Check if qualifier is the base type of a generic (e.g., "array" matches "array<character>")
								else {
									size_t genericPos = firstArgType.find('<');
									if (genericPos != std::string::npos) {
										std::string baseType = firstArgType.substr(0, genericPos);
										if (qualifier == baseType) {
											logTrace("    -> BASE TYPE MATCH!");
											if (baseTypeMatch.empty()) {
												baseTypeMatch = match;
											}
											// Don't break for multiple base type matches - we'll prefer exact match anyway
										}
									}
								}
							}
						}

						// Prefer exact match, fall back to base type match
						resolvedMatch = !exactMatch.empty() ? exactMatch : baseTypeMatch;
					}
				}

				if (!resolvedMatch.empty()) {
					// Found exactly one match based on receiver type
					logTrace("Resolved method: " + resolvedMatch);
					funcIt = functions.find(resolvedMatch);
					if (funcIt == functions.end()) {
						logTrace("  ERROR: functions.find failed for " + resolvedMatch);
					} else {
						logTrace("  Found with " + std::to_string(funcIt->second.parameters.size()) + " params");
					}
					auto idIt = functionIndices.find(resolvedMatch);
					callExpr->resolvedCallee = resolvedMatch; // Store resolved name for codegen
					if (idIt != functionIndices.end()) {
						callExpr->functionId = idIt->second;
					}
				} else {
					// Still ambiguous - report error
					std::string errorMsg = "Ambiguous function call: '" + callExpr->callee + "'" +
										   std::string("\n  Multiple definitions found:");
					for (const auto &match : matches) {
						errorMsg += "\n    - " + match;
					}
					errorMsg += "\n  Use a qualified name to disambiguate (e.g., namespace.function)";
					addError(errorMsg, expr->line, expr->column);
					// Still analyze remaining arguments to mark variables as used (first already analyzed)
					for (size_t i = 1; i < callExpr->args.size(); ++i) {
						analyzeExpression(callExpr->args[i].value.get());
					}
					return "error";
				}
			} else {
				// Exactly one match - use it for validation and set function ID
				// Note: We found the function, so it's not undefined
				funcIt = functions.find(matches[0]);
				// Store the resolved function ID in the AST for fast codegen lookup
				callExpr->resolvedCallee = matches[0]; // Store resolved name for codegen
				auto idIt = functionIndices.find(matches[0]);
				if (idIt != functionIndices.end()) {
					callExpr->functionId = idIt->second;
				}
			}
		}

		if (funcIt == functions.end()) {
			// Track this as an undefined function for potential auto-discovery
			undefinedFunctions.insert(callExpr->callee);

			addError("Undefined function: '" + callExpr->callee + "'" +
						 std::string("\n  Note: Function must be defined before it can be called"),
					 expr->line, expr->column);
			// Still analyze arguments to mark variables as used
			for (auto &arg : callExpr->args) {
				analyzeExpression(arg.value.get());
			}
			return "error";
		}

		const FunctionInfo &funcInfo = funcIt->second;

		// Check for mutating method calls on immutable variables
		// Mutating methods like push, pop, clear cannot be called on let-declared arrays
		std::string resolvedName = funcIt->first;
		size_t methodDotPos = resolvedName.rfind(".");
		if (methodDotPos != std::string::npos) {
			std::string methodName = resolvedName.substr(methodDotPos + 1);
			// List of known mutating methods
			static const std::set<std::string> mutatingMethods = {
				"push", "pop", "clear", "remove", "insert", "append", "removeAt"};
			if (mutatingMethods.count(methodName) > 0 && !callExpr->args.empty()) {
				// Check if the first argument (receiver) is an immutable variable
				if (auto *varExpr = dynamic_cast<VariableExprAST *>(callExpr->args[0].value.get())) {
					auto varInfo = lookupVariable(varExpr->name);
					if (varInfo.has_value() && varInfo->isImmutable) {
						addError("Cannot call mutating method '" + methodName + "' on immutable variable '" +
									 varExpr->name + "'" +
									 std::string("\n  Variable declared with 'let' at line ") +
									 std::to_string(varInfo->line) + ", column " + std::to_string(varInfo->column) +
									 "\n  Note: Use 'var' instead of 'let' if you need to modify the array",
								 expr->line, expr->column);
						return "error";
					}
				}
			}
		}

		// Store the resolved function ID in the AST for fast codegen lookup
		auto idIt = functionIndices.find(funcIt->first);
		if (idIt != functionIndices.end()) {
			callExpr->functionId = idIt->second;
		}

		// Check if this is a sibling method call (calling another method of the same type from within a method)
		// In this case, the caller passes N args but the method expects N+1 (with implicit self)
		// NOTE: This is NOT a sibling method call if args[0] already provides a value for self
		// (e.g., when calling other.method() where 'other' is a parameter of the same type)
		bool isSiblingMethodCall = false;

		if (!currentReceiverType.empty() && funcInfo.parameters.size() > 0) {
			// We're inside a method - check if the called function is a method of the same type
			std::string resolvedName = funcIt->first;
			size_t dotPos = resolvedName.rfind(".");
			if (dotPos != std::string::npos) {
				std::string methodType = resolvedName.substr(0, dotPos);
				// Check if the first parameter is 'self' of our receiver type
				if (methodType == currentReceiverType &&
					funcInfo.parameters[0].name == "self" &&
					funcInfo.parameters[0].type == currentReceiverType) {
					// Only treat as sibling call if the caller hasn't provided a receiver
					// If args.size() >= funcInfo.parameters.size(), caller provided all args including self
					if (callExpr->args.size() < funcInfo.parameters.size()) {
						isSiblingMethodCall = true;
						callExpr->isSiblingMethodCall = true; // Mark for codegen
					}
				}
			}
		}

		// For sibling method calls, args[i] corresponds to funcInfo.parameters[i+1] (skip self)
		size_t paramOffset = isSiblingMethodCall ? 1 : 0;

		// named arguments validation:
		// - Positional args come first, fill required params in order
		// - Named args can appear in any order after positional args
		// - Params with defaults can ONLY be provided via named arguments
		// - Once a named arg is seen, all remaining must be named

		// Count required parameters (those without defaults, excluding self)
		size_t requiredParamCount = 0;
		for (size_t i = paramOffset; i < funcInfo.parameters.size(); i++) {
			if (!funcInfo.parameters[i].hasDefault()) {
				requiredParamCount++;
			}
		}

		// Track which parameters have been filled
		std::vector<bool> paramFilled(funcInfo.parameters.size(), false);
		if (paramOffset > 0) {
			paramFilled[0] = true; // self is always filled implicitly
		}

		// Track argument-to-parameter mapping for codegen reordering
		std::vector<size_t> argToParamMapping(callExpr->args.size());

		// For generic methods, we need to infer concrete element type from first array arg
		std::string concreteElementType;

		bool seenNamedArg = false;
		size_t positionalParamIdx = paramOffset; // Next param to fill positionally

		for (size_t argIdx = 0; argIdx < callExpr->args.size(); argIdx++) {
			const CallArgument &arg = callExpr->args[argIdx];

			if (arg.isNamed()) {
				// Named argument
				seenNamedArg = true;

				// Find matching parameter by name
				size_t matchedParamIdx = SIZE_MAX;
				for (size_t i = paramOffset; i < funcInfo.parameters.size(); i++) {
					if (funcInfo.parameters[i].name == arg.name) {
						matchedParamIdx = i;
						break;
					}
				}

				if (matchedParamIdx == SIZE_MAX) {
					addError("Unknown parameter name '" + arg.name + "'" +
								 std::string("\n  Function '") + callExpr->callee + "' has no parameter with this name",
							 arg.line, arg.column);
					continue;
				}

				if (paramFilled[matchedParamIdx]) {
					addError("Duplicate argument for parameter '" + arg.name + "'" +
								 std::string("\n  Parameter already provided"),
							 arg.line, arg.column);
					continue;
				}

				paramFilled[matchedParamIdx] = true;
				argToParamMapping[argIdx] = matchedParamIdx;

				// Type check the argument
				const FunctionParameter &param = funcInfo.parameters[matchedParamIdx];
				std::string argType = analyzeExpression(arg.value.get());
				std::string expectedType = param.type;

				// Cache array element type for generic type inference
				if (concreteElementType.empty() &&
					(maxon::TypeConversion::isArrayStructType(argType) ||
					 maxon::TypeConversion::isManagedArrayType(argType))) {
					concreteElementType = maxon::TypeConversion::getArrayElementType(argType);
				}

				// Substitute generic type parameter
				if (!concreteElementType.empty() && expectedType == "Element") {
					expectedType = concreteElementType;
				}

				bool isValidType = typesMatch(expectedType, argType);
				if (!isValidType) {
					isValidType = maxon::TypeConversion::canConvertImplicitly(argType, expectedType);
				}
				if (!isValidType && isOptionalType(expectedType)) {
					std::string unwrapped = unwrapOptionalType(expectedType);
					if (argType == "nil" || typesMatch(unwrapped, argType)) {
						isValidType = true;
					}
				}

				if (!isValidType) {
					addError("Function '" + callExpr->callee + "' argument type mismatch" +
								 std::string("\n  Parameter '") + param.name + "'" +
								 "\n  Expected type: " + expectedType +
								 "\n  Found type: " + argType,
							 arg.line, arg.column);
				}
			} else {
				// Positional argument
				if (seenNamedArg) {
					addError("Positional argument after named argument" +
								 std::string("\n  All positional arguments must come before named arguments"),
							 arg.line, arg.column);
					continue;
				}

				// Skip params that have defaults - they can only be filled by named args
				while (positionalParamIdx < funcInfo.parameters.size() &&
					   funcInfo.parameters[positionalParamIdx].hasDefault()) {
					positionalParamIdx++;
				}

				if (positionalParamIdx >= funcInfo.parameters.size()) {
					addError("Too many positional arguments" +
								 std::string("\n  Function '") + callExpr->callee + "' has " +
								 std::to_string(requiredParamCount) + " required parameter" +
								 (requiredParamCount == 1 ? "" : "s"),
							 arg.line, arg.column);
					continue;
				}

				const FunctionParameter &param = funcInfo.parameters[positionalParamIdx];
				paramFilled[positionalParamIdx] = true;
				argToParamMapping[argIdx] = positionalParamIdx;

				// Type check
				std::string argType = analyzeExpression(arg.value.get());
				std::string expectedType = param.type;

				// Cache array element type for generic type inference
				if (concreteElementType.empty() &&
					(maxon::TypeConversion::isArrayStructType(argType) ||
					 maxon::TypeConversion::isManagedArrayType(argType))) {
					concreteElementType = maxon::TypeConversion::getArrayElementType(argType);
				}

				if (!concreteElementType.empty() && expectedType == "Element") {
					expectedType = concreteElementType;
				}

				bool isValidType = typesMatch(expectedType, argType);
				if (!isValidType) {
					isValidType = maxon::TypeConversion::canConvertImplicitly(argType, expectedType);
				}
				if (!isValidType && isOptionalType(expectedType)) {
					std::string unwrapped = unwrapOptionalType(expectedType);
					if (argType == "nil" || typesMatch(unwrapped, argType)) {
						isValidType = true;
					}
				}

				if (!isValidType) {
					addError("Function '" + callExpr->callee + "' argument type mismatch" +
								 std::string("\n  Parameter ") + std::to_string(positionalParamIdx + 1 - paramOffset) +
								 " ('" + param.name + "')" +
								 "\n  Expected type: " + expectedType +
								 "\n  Found type: " + argType,
							 arg.line, arg.column);
				}

				positionalParamIdx++;
			}
		}

		// Check for missing required parameters and inject default values for omitted optional params
		for (size_t i = paramOffset; i < funcInfo.parameters.size(); i++) {
			const FunctionParameter &param = funcInfo.parameters[i];
			if (!paramFilled[i]) {
				if (!param.hasDefault()) {
					addError("Missing required argument for parameter '" + param.name + "'" +
								 std::string("\n  Add: ") + param.name + " = <value>",
							 expr->line, expr->column);
				} else {
					// Inject the default value expression as an argument
					// Clone the default expression and add it to the call arguments
					auto defaultExprCopy = param.defaultValue->clone();
					callExpr->args.push_back(CallArgument(
						std::unique_ptr<ExprAST>(defaultExprCopy),
						expr->line, expr->column, param.name));
					argToParamMapping.push_back(i);
					paramFilled[i] = true;
				}
			}
		}

		// Store the argument-to-parameter mapping for codegen
		callExpr->argToParamMapping = std::move(argToParamMapping);

		return funcInfo.returnType;

	} else if (auto sliceExpr = dynamic_cast<SliceExprAST *>(expr)) {
		// Check if object variable exists
		auto varInfo = lookupVariable(sliceExpr->objectName);
		if (!varInfo.has_value()) {
			addError("Undefined variable: '" + sliceExpr->objectName + "'" +
						 std::string("\n  Note: Variable must be declared before slicing"),
					 expr->line, expr->column);
			return "error";
		}

		// Mark variable as used
		markVariableAsUsed(sliceExpr->objectName);

		// Analyze start expression if present
		if (sliceExpr->start) {
			std::string startType = analyzeExpression(sliceExpr->start.get());
			if (startType != "int" && startType != "error") {
				addError("Slice start index must be an integer" +
							 std::string("\n  Found type: ") + startType,
						 expr->line, expr->column);
			}
		}

		// Analyze end expression if present
		if (sliceExpr->end) {
			std::string endType = analyzeExpression(sliceExpr->end.get());
			if (endType != "int" && endType != "error") {
				addError("Slice end index must be an integer" +
							 std::string("\n  Found type: ") + endType,
						 expr->line, expr->column);
			}
		}

		// Slicing a string returns a string
		if (varInfo->type == "string") {
			return "string";
		}

		// For arrays, return the same array type (slice creates a view/copy)
		return varInfo->type;

	} else if (auto arrayExpr = dynamic_cast<ArrayIndexExprAST *>(expr)) {
		// Analyze the index expression first
		std::string indexType = analyzeExpression(arrayExpr->index.get());
		if (indexType != "int" && indexType != "error") {
			addError("Array index must be an integer" +
						 std::string("\n  Found type: ") + indexType,
					 expr->line, expr->column);
		}

		std::string arrayType;

		// Handle complex array access (e.g., struct.field[i])
		if (arrayExpr->hasArrayExpr()) {
			arrayType = analyzeExpression(arrayExpr->arrayExpr.get());
		} else {
			// Simple array[i] access - check if array variable exists
			auto varInfo = lookupVariable(arrayExpr->arrayName);
			if (!varInfo.has_value()) {
				addError("Undefined variable: '" + arrayExpr->arrayName + "'" +
							 std::string("\n  Note: Array must be declared before use"),
						 expr->line, expr->column);
				return "error";
			}

			// Mark array variable as used when accessed
			markVariableAsUsed(arrayExpr->arrayName);
			arrayType = varInfo->type;
		}

		// Extract element type from array type
		if (maxon::TypeConversion::isArrayType(arrayType)) {
			return maxon::TypeConversion::getArrayElementType(arrayType);
		}
		// Handle array<T> struct type
		if (maxon::TypeConversion::isArrayStructType(arrayType)) {
			return maxon::TypeConversion::getArrayStructElementType(arrayType);
		}
		// Fallback if type parsing fails
		return "int";

	} else if (auto memberAccessExpr = dynamic_cast<MemberAccessExprAST *>(expr)) {
		std::string objectType;

		// Handle both simple variable access and complex expression access
		if (memberAccessExpr->object) {
			// Complex expression (e.g., arr[0].field)
			objectType = analyzeExpression(memberAccessExpr->object.get());
		} else {
			// First check if objectName is an enum type (for enum case expressions like Direction.north)
			const EnumInfo *enumInfo = lookupEnum(memberAccessExpr->objectName);
			if (enumInfo != nullptr) {
				// This is an enum case expression
				const EnumCaseInfo *caseInfo = enumInfo->findCase(memberAccessExpr->memberName);
				if (caseInfo == nullptr) {
					std::string availableCases;
					for (size_t i = 0; i < enumInfo->cases.size(); i++) {
						if (i > 0)
							availableCases += ", ";
						availableCases += enumInfo->cases[i].name;
					}
					addError("Unknown case '" + memberAccessExpr->memberName + "' for enum '" + memberAccessExpr->objectName + "'\n"
																															   "  Available cases: " +
								 availableCases,
							 expr->line, expr->column);
					return "error";
				}

				// Check if this case requires associated values
				if (!caseInfo->associatedValues.empty()) {
					addError("Enum case '" + memberAccessExpr->memberName + "' requires associated values\n"
																			"  Use: " +
								 memberAccessExpr->objectName + "." + memberAccessExpr->memberName + "(...)",
							 expr->line, expr->column);
					return memberAccessExpr->objectName; // Still return enum type
				}

				// Store the resolved enum info in the AST for codegen
				memberAccessExpr->resolvedEnumName = memberAccessExpr->objectName;
				memberAccessExpr->resolvedEnumCaseName = memberAccessExpr->memberName;

				return memberAccessExpr->objectName;
			}

			// Check if objectName is an enum type for .rawValue access
			auto varInfo = lookupVariable(memberAccessExpr->objectName);
			if (!varInfo.has_value()) {
				addError("Undefined variable: '" + memberAccessExpr->objectName + "'",
						 expr->line, expr->column);
				return "error";
			}

			// Mark object variable as used when its member is accessed
			markVariableAsUsed(memberAccessExpr->objectName);
			objectType = varInfo->type;
		}

		// Check if it's an enum type (for .rawValue access)
		const EnumInfo *varEnumInfo = lookupEnum(objectType);
		if (varEnumInfo != nullptr) {
			// Handle .rawValue access
			if (memberAccessExpr->memberName == "rawValue") {
				if (varEnumInfo->rawValueType.empty()) {
					addError("Cannot access 'rawValue' on enum '" + objectType + "' which has no raw value type\n"
																				 "  Declare the enum with a raw value type: enum " +
								 objectType + " int",
							 expr->line, expr->column);
					return "error";
				}
				return varEnumInfo->rawValueType;
			}

			// Check if it's a method call
			std::string methodName = objectType + "." + memberAccessExpr->memberName;
			auto methodIt = functions.find(methodName);
			if (methodIt != functions.end()) {
				return methodIt->second.returnType;
			}

			addError("Enum '" + objectType + "' has no property or method named '" + memberAccessExpr->memberName + "'",
					 expr->line, expr->column);
			return "error";
		}

		// Check if it's a struct type
		if (lookupStruct(objectType) != nullptr) {
			const auto &structInfo = *lookupStruct(objectType);

			// Find the field
			for (const auto &field : structInfo.fields) {
				if (field.name == memberAccessExpr->memberName) {
					// For array<T> struct types, ensure generic methods are instantiated
					// This is needed when accessing struct fields that contain arrays
					if (maxon::TypeConversion::isArrayStructType(field.type)) {
						std::string elemType = maxon::TypeConversion::getArrayStructElementType(field.type);
						std::map<std::string, std::string> typeBindings = {{"Element", elemType}};
						instantiateGenericStructMethods("array", field.type, typeBindings);
					}
					return field.type;
				}
			}

			// Not a field - check if it's a method call (Type.method)
			std::string methodName = objectType + "." + memberAccessExpr->memberName;
			auto methodIt = functions.find(methodName);
			if (methodIt != functions.end()) {
				// Found a method - return its return type
				return methodIt->second.returnType;
			}

			addError("Type '" + objectType + "' has no field or method named '" + memberAccessExpr->memberName + "'",
					 expr->line, expr->column);
			return "error";
		}

		// Support array member access using shared type registry
		if (maxon::TypeConversion::isArrayType(objectType) || maxon::TypeConversion::isArrayStructType(objectType)) {
			std::string memberType = getArrayMemberType(memberAccessExpr->memberName);
			if (!memberType.empty()) {
				return memberType;
			}
		}

		// Check if objectType.memberName is a known method
		std::string methodName = objectType + "." + memberAccessExpr->memberName;
		auto methodIt = functions.find(methodName);
		if (methodIt != functions.end()) {
			return methodIt->second.returnType;
		}

		addError("Unknown member: " + memberAccessExpr->memberName + " on type " + maxon::TypeConversion::arrayTypeToDisplayString(objectType),
				 expr->line, expr->column);
		return "error";
	} else if (auto structInitExpr = dynamic_cast<StructInitExprAST *>(expr)) {
		// Check if struct type exists
		if (lookupStruct(structInitExpr->structName) == nullptr) {
			// Track as undefined for auto-import from stdlib
			undefinedStructs.insert(structInitExpr->structName);
			addError("Undefined type: '" + structInitExpr->structName + "'",
					 expr->line, expr->column);
			return "error";
		}

		const auto &structInfo = *lookupStruct(structInitExpr->structName);

		// Check that all required fields are initialized
		std::set<std::string> initializedFields;
		for (const auto &initField : structInitExpr->fields) {
			initializedFields.insert(initField.name);

			// Verify field exists in struct
			bool fieldFound = false;
			std::string expectedType;
			for (const auto &structField : structInfo.fields) {
				if (structField.name == initField.name) {
					fieldFound = true;
					expectedType = structField.type;
					break;
				}
			}

			if (!fieldFound) {
				addError("Type '" + structInitExpr->structName + "' has no field named '" + initField.name + "'",
						 initField.line, initField.column);
			} else {
				// Type check the initializer value
				std::string valueType = analyzeExpression(initField.value.get());
				bool typesOk = typesMatch(expectedType, valueType);

				// Handle optional field types: if field is "T or nil"
				// Accept: (1) nil, (2) T, (3) T or nil
				if (!typesOk && isOptionalType(expectedType)) {
					std::string unwrappedExpected = unwrapOptionalType(expectedType);

					// Case 1: Assigning nil to optional field
					if (valueType == "nil") {
						typesOk = true;
					}
					// Case 2: Assigning unwrapped type T to optional field T or nil (implicit wrap)
					else if (typesMatch(unwrappedExpected, valueType)) {
						typesOk = true;
					}
					// Case 3: Already checked by typesMatch above (T or nil → T or nil)
				}

				if (!typesOk) {
					addError("Type mismatch for field '" + initField.name + "': expected '" + expectedType + "', got '" + valueType + "'",
							 initField.line, initField.column);
				}
			}
		}

		// Check for missing fields - fields with defaults are optional
		for (const auto &structField : structInfo.fields) {
			if (initializedFields.find(structField.name) == initializedFields.end()) {
				if (!structField.hasDefault) {
					addError("Missing initialization for required field '" + structField.name + "' in type '" + structInitExpr->structName + "'" +
								 "\n  Note: Fields without default values must be initialized",
							 expr->line, expr->column);
				}
				// Fields with defaults are OK to omit - codegen will use the default
			}
		}

		return structInitExpr->structName;
	} else if (auto enumCaseExpr = dynamic_cast<EnumCaseExprAST *>(expr)) {
		// Check if enum type exists
		const EnumInfo *enumInfo = lookupEnum(enumCaseExpr->enumName);
		if (enumInfo == nullptr) {
			addError("Undefined enum type: '" + enumCaseExpr->enumName + "'",
					 expr->line, expr->column);
			return "error";
		}

		// Check if case exists in the enum
		const EnumCaseInfo *caseInfo = enumInfo->findCase(enumCaseExpr->caseName);
		if (caseInfo == nullptr) {
			std::string availableCases;
			for (size_t i = 0; i < enumInfo->cases.size(); i++) {
				if (i > 0)
					availableCases += ", ";
				availableCases += enumInfo->cases[i].name;
			}
			addError("Unknown case '" + enumCaseExpr->caseName + "' for enum '" + enumCaseExpr->enumName + "'\n"
																										   "  Available cases: " +
						 availableCases,
					 expr->line, expr->column);
			return "error";
		}

		// Check associated value arguments
		if (enumCaseExpr->arguments.size() != caseInfo->associatedValues.size()) {
			if (caseInfo->associatedValues.empty()) {
				addError("Enum case '" + enumCaseExpr->caseName + "' does not have associated values\n"
																  "  Use: " +
							 enumCaseExpr->enumName + "." + enumCaseExpr->caseName,
						 expr->line, expr->column);
			} else {
				addError("Wrong number of associated values for case '" + enumCaseExpr->caseName + "': expected " +
							 std::to_string(caseInfo->associatedValues.size()) + ", got " +
							 std::to_string(enumCaseExpr->arguments.size()),
						 expr->line, expr->column);
			}
			return enumCaseExpr->enumName; // Still return the enum type
		}

		// Type check each associated value argument
		for (size_t i = 0; i < enumCaseExpr->arguments.size(); i++) {
			std::string argType = analyzeExpression(enumCaseExpr->arguments[i].get());
			const std::string &expectedType = caseInfo->associatedValues[i].type;

			if (!typesMatch(expectedType, argType)) {
				addError("Type mismatch for associated value '" + caseInfo->associatedValues[i].name +
							 "': expected '" + expectedType + "', got '" + argType + "'",
						 enumCaseExpr->arguments[i]->line, enumCaseExpr->arguments[i]->column);
			}
		}

		return enumCaseExpr->enumName;
	} else if (dynamic_cast<IfCaseStmtAST *>(expr)) {
		// Note: IfCaseStmtAST is a statement, not an expression, so this shouldn't be hit
		// But handle it gracefully if it does appear as an expression
		return "void";
	} else if (auto matchExpr = dynamic_cast<MatchExprAST *>(expr)) {
		// Analyze scrutinee expression
		std::string scrutineeType = analyzeExpression(matchExpr->scrutinee.get());

		// Track patterns for duplicate detection
		std::set<std::string> seenPatterns;
		bool hasDefault = false;
		bool defaultNotLast = false;

		// Check for exhaustiveness if matching on enum type
		const EnumInfo *enumInfo = lookupEnum(scrutineeType);
		std::set<std::string> coveredCases;

		// Track result type for type consistency
		std::string resultType;
		bool firstCase = true;

		for (size_t i = 0; i < matchExpr->cases.size(); i++) {
			const auto &matchCase = matchExpr->cases[i];

			// Check default case constraints
			if (matchCase.isDefault) {
				if (hasDefault) {
					addError("Duplicate 'default' case in match expression",
							 matchCase.line, matchCase.column);
				}
				hasDefault = true;
				if (i != matchExpr->cases.size() - 1) {
					defaultNotLast = true;
				}
			} else if (matchCase.isEnumCasePattern) {
				// Enum case pattern with bindings: case success(value) gives ...
				if (!enumInfo) {
					addError("Cannot use 'case' pattern on non-enum type '" + scrutineeType + "'",
							 matchCase.line, matchCase.column);
				} else {
					// Find the case in the enum
					const EnumCaseInfo *caseInfo = nullptr;
					for (const auto &ec : enumInfo->cases) {
						if (ec.name == matchCase.enumCaseName) {
							caseInfo = &ec;
							break;
						}
					}

					if (!caseInfo) {
						addError("Unknown case '" + matchCase.enumCaseName + "' for enum '" + scrutineeType + "'",
								 matchCase.line, matchCase.column);
					} else {
						// Validate binding count matches associated values
						if (matchCase.bindings.size() != caseInfo->associatedValues.size()) {
							addError("Wrong number of bindings for case '" + matchCase.enumCaseName +
										 "': expected " + std::to_string(caseInfo->associatedValues.size()) +
										 ", got " + std::to_string(matchCase.bindings.size()),
									 matchCase.line, matchCase.column);
						}

						// Track for exhaustiveness
						coveredCases.insert(matchCase.enumCaseName);

						// Check for duplicate patterns
						std::string patternStr = scrutineeType + "." + matchCase.enumCaseName;
						if (seenPatterns.find(patternStr) != seenPatterns.end()) {
							addError("Duplicate pattern '" + patternStr + "' in match",
									 matchCase.line, matchCase.column);
						}
						seenPatterns.insert(patternStr);

						// Analyze result expression with bindings in scope
						enterScope();

						// Declare binding variables with their types from associated values
						for (size_t j = 0; j < matchCase.bindings.size() && j < caseInfo->associatedValues.size(); j++) {
							const std::string &bindingName = matchCase.bindings[j];
							const std::string &bindingType = caseInfo->associatedValues[j].type;
							declareVariable(bindingName, bindingType, true /* immutable */,
											matchCase.line, matchCase.column);
						}

						if (matchCase.resultExpr) {
							std::string caseType = analyzeExpression(matchCase.resultExpr.get());

							if (firstCase) {
								resultType = caseType;
								firstCase = false;
							} else if (!typesMatch(resultType, caseType)) {
								addError("Match expression case types must be consistent\n  First case type: " +
											 resultType + "\n  This case type: " + caseType,
										 matchCase.line, matchCase.column);
							}
						}

						checkUnusedVariables();
						exitScope();
					}
				}
				continue; // Skip the regular analysis below
			} else {
				// Analyze each pattern
				for (const auto &pattern : matchCase.patterns) {
					std::string patternType = analyzeExpression(pattern.get());

					// Check pattern type matches scrutinee type
					if (!typesMatch(scrutineeType, patternType)) {
						addError("Pattern type '" + patternType + "' does not match scrutinee type '" + scrutineeType + "'",
								 pattern->line, pattern->column);
					}

					// Check for duplicate patterns (convert to string for comparison)
					std::string patternStr;
					if (auto numExpr = dynamic_cast<NumberExprAST *>(pattern.get())) {
						patternStr = std::to_string(numExpr->value);
					} else if (auto strExpr = dynamic_cast<StringLiteralExprAST *>(pattern.get())) {
						patternStr = "\"" + strExpr->value + "\"";
					} else if (auto memberExpr = dynamic_cast<MemberAccessExprAST *>(pattern.get())) {
						if (memberExpr->isEnumCase()) {
							patternStr = memberExpr->resolvedEnumName + "." + memberExpr->resolvedEnumCaseName;
							coveredCases.insert(memberExpr->resolvedEnumCaseName);
						} else {
							patternStr = memberExpr->objectName + "." + memberExpr->memberName;
							coveredCases.insert(memberExpr->memberName);
						}
					}

					if (!patternStr.empty() && seenPatterns.find(patternStr) != seenPatterns.end()) {
						addError("Duplicate pattern '" + patternStr + "' in match",
								 pattern->line, pattern->column);
					}
					seenPatterns.insert(patternStr);
				}
			}

			// Match expressions don't support fallthrough
			if (matchCase.hasFallthrough) {
				addError("'fallthrough' is not allowed in match expressions",
						 matchCase.line, matchCase.column);
			}

			// Analyze the result expression
			if (matchCase.resultExpr) {
				std::string caseType = analyzeExpression(matchCase.resultExpr.get());

				if (firstCase) {
					resultType = caseType;
					firstCase = false;
				} else if (!typesMatch(resultType, caseType)) {
					addError("Match expression case types must be consistent\n  First case type: " +
								 resultType + "\n  This case type: " + caseType,
							 matchCase.line, matchCase.column);
				}
			}
		}

		// Report default-not-last error
		if (defaultNotLast) {
			addError("'default' case must be the last case in match",
					 expr->line, expr->column);
		}

		// Exhaustiveness check for enum types
		if (enumInfo != nullptr && !hasDefault) {
			std::vector<std::string> missingCases;
			for (const auto &enumCase : enumInfo->cases) {
				if (coveredCases.find(enumCase.name) == coveredCases.end()) {
					missingCases.push_back(enumCase.name);
				}
			}
			if (!missingCases.empty()) {
				std::string missingList;
				for (size_t i = 0; i < missingCases.size(); i++) {
					if (i > 0)
						missingList += ", ";
					missingList += missingCases[i];
				}
				addError("Match on enum '" + scrutineeType + "' is not exhaustive\n  Missing cases: " + missingList,
						 expr->line, expr->column);
			}
		}

		return resultType.empty() ? "error" : resultType;

	} else if (auto closureExpr = dynamic_cast<ClosureExprAST *>(expr)) {
		// Analyze closure expression

		// Enter a new scope for the closure
		enterScope();

		// Declare parameters as local variables
		std::vector<std::string> paramTypes;
		for (const auto &param : closureExpr->parameters) {
			std::string paramType = param.type;
			if (paramType.empty()) {
				// Type needs to be inferred from context (for now, error)
				addError("Closure parameter '" + param.name + "' requires explicit type annotation",
						 param.line, param.column);
				paramType = "error";
			}
			declareVariable(param.name, paramType, false, param.line, param.column, true);
			paramTypes.push_back(paramType);
		}

		// Analyze body
		std::string returnType;
		if (closureExpr->isSingleExpression && closureExpr->singleExpr) {
			// Single expression closure - return type is expression type
			returnType = analyzeExpression(closureExpr->singleExpr.get());
		} else {
			// Multi-statement closure - need to analyze statements and find return type
			returnType = closureExpr->returnType.empty() ? "void" : closureExpr->returnType;
			for (const auto &stmt : closureExpr->body) {
				analyzeStatement(stmt.get(), returnType);
			}
		}

		exitScope();

		// Build function type string: fn(T1,T2)->R
		std::string funcType = "fn(";
		for (size_t i = 0; i < paramTypes.size(); i++) {
			if (i > 0)
				funcType += ",";
			funcType += paramTypes[i];
		}
		funcType += ")->" + returnType;

		return funcType;
	}

	return "error";
}
