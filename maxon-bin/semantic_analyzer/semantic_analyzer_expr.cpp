#include "../lexer.h"
#include "../semantic_analyzer.h"
#include "../type_members.h"
#include <algorithm>

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

	} else if (dynamic_cast<CharacterExprAST *>(expr)) {
		return "char";

	} else if (dynamic_cast<StringLiteralExprAST *>(expr)) {
		// String literals require the stdlib 'string' struct
		// Track as undefined so it gets auto-discovered
		if (lookupStruct("string") == nullptr) {
			undefinedStructs.insert("string");
		}
		return "string";

	} else if (auto arrayLiteral = dynamic_cast<ArrayLiteralExprAST *>(expr)) {
		// Array literal: [5]int or [1,2,3]
		if (arrayLiteral->size > 0) {
			// [size]type form - zero-initialized array
			return "[" + std::to_string(arrayLiteral->size) + "]" + arrayLiteral->elementType;
		} else {
			// [val1, val2, ...] form - value-initialized array
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

			return "[" + std::to_string(arrayLiteral->values.size()) + "]" + elemType;
		}

	} else if (auto castExpr = dynamic_cast<CastExprAST *>(expr)) {
		// Analyze the expression being cast
		std::string sourceType = analyzeExpression(castExpr->expr.get());

		// Validate that float to int casts are not allowed
		if (sourceType == "float" && castExpr->targetType == "int") {
			addError("Cannot cast float to int; use trunc(), round(), floor(), or ceil() instead" +
						 std::string("\n  Hint: trunc() truncates toward zero (equivalent to the old 'as int' behavior)"),
					 expr->line, expr->column);
			return "error";
		}

		// Check if target type is a struct - if so, track as undefined for auto-import
		const std::string &targetType = castExpr->targetType;
		bool isBuiltinType = (targetType == "int" || targetType == "float" ||
							  targetType == "bool" || targetType == "char" ||
							  targetType == "string" || targetType == "void" ||
							  targetType.substr(0, 1) == "["); // Array types

		if (!isBuiltinType && structs.find(targetType) == structs.end()) {
			// Target type is a struct that isn't defined yet - track for auto-import
			undefinedStructs.insert(targetType);
		}

		// Valid casts: int <-> char, char <-> int, or ExpressibleByStringLiteral types
		return castExpr->targetType;

	} else if (auto varExpr = dynamic_cast<VariableExprAST *>(expr)) {
		auto varInfo = lookupVariable(varExpr->name);
		if (!varInfo.has_value()) {
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
			// Allow comparison between int and float (implicit promotion)
			if ((leftType == "int" || leftType == "float") &&
				(rightType == "int" || rightType == "float")) {
				return "bool";
			}

			// For == and != on struct types, check Equatable conformance
			if (binExpr->op == 'E' || binExpr->op == 'N') {
				const StructInfo *leftStruct = lookupStruct(leftType);
				const StructInfo *rightStruct = lookupStruct(rightType);

				if (leftStruct != nullptr || rightStruct != nullptr) {
					// At least one operand is a struct type
					if (leftType != rightType) {
						addError("Cannot compare different struct types with == or !=" +
									 std::string("\n  Left operand type: ") + leftType +
									 "\n  Right operand type: " + rightType,
								 expr->line, expr->column);
						return "error";
					}

					// Both are the same struct type, check Equatable conformance
					bool conformsToEquatable = false;
					for (const auto &iface : leftStruct->conformsTo) {
						if (iface == "Equatable") {
							conformsToEquatable = true;
							break;
						}
					}

					if (!conformsToEquatable) {
						addError("Cannot use == or != on struct type '" + leftType +
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

			// Analyze arguments
			for (auto &arg : callExpr->args) {
				std::string argType = analyzeExpression(arg.get());
				// Allow both int and float (int will be promoted)
				if (argType != "int" && argType != "float" && argType != "error") {
					addError("Math function '" + callExpr->callee + "' expects numeric argument, got " + argType,
							 expr->line, expr->column);
				}
			}

			// Return type from metadata
			const MathIntrinsicInfo *info = Lexer::getMathIntrinsicInfo(callExpr->callee);
			return info ? info->returnType : "float";
		}

		// Check for array intrinsics: push(arr, val), pop(arr)
		// These are transformed from arr.push(val), arr.pop() by the parser
		if (callExpr->callee == "push") {
			if (callExpr->args.size() != 2) {
				addError("push() requires exactly 2 arguments: array and value",
						 expr->line, expr->column);
				return "void";
			}
			// First arg should be a dynamic array
			std::string arrType = analyzeExpression(callExpr->args[0].get());
			if (arrType.size() < 2 || arrType[0] != '[' || arrType[1] != ']') {
				addError("push() can only be used on dynamic arrays, not " + arrType,
						 expr->line, expr->column);
				return "void";
			}
			// Second arg should match element type
			std::string elemType = arrType.substr(2); // Skip "[]"
			std::string valType = analyzeExpression(callExpr->args[1].get());
			if (valType != elemType && valType != "error") {
				addError("push() value type " + valType + " doesn't match array element type " + elemType,
						 expr->line, expr->column);
			}
			return "void";
		}

		if (callExpr->callee == "pop") {
			if (callExpr->args.size() != 1) {
				addError("pop() requires exactly 1 argument: array",
						 expr->line, expr->column);
				return "error";
			}
			// Arg should be a dynamic array
			std::string arrType = analyzeExpression(callExpr->args[0].get());
			if (arrType.size() < 2 || arrType[0] != '[' || arrType[1] != ']') {
				addError("pop() can only be used on dynamic arrays, not " + arrType,
						 expr->line, expr->column);
				return "error";
			}
			// Return element type
			return arrType.substr(2); // Skip "[]"
		}

		// Try to find the function - first exact match, then unqualified lookup
		auto funcIt = functions.find(callExpr->callee);

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
					analyzeExpression(arg.get());
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
				// Track this as an undefined function for potential auto-discovery
				undefinedFunctions.insert(callExpr->callee);

				addError("Undefined function: '" + callExpr->callee + "'" +
							 std::string("\n  Note: Function must be defined before it can be called"),
						 expr->line, expr->column);
				// Still analyze arguments to mark variables as used
				for (auto &arg : callExpr->args) {
					analyzeExpression(arg.get());
				}
				return "error";
			} else if (matches.size() > 1) {
				// Multiple matches found - try to disambiguate using first argument type
				// This handles method calls where obj.method() becomes method(obj)
				// and the first argument's type can resolve which Type.method to call
				std::string resolvedMatch;

				if (!callExpr->args.empty()) {
					// Get the type of the first argument
					std::string firstArgType = analyzeExpression(callExpr->args[0].get());

					// Look for a method with matching qualifier (Type.method where Type == firstArgType)
					for (const auto &match : matches) {
						size_t dotPos = match.rfind(".");
						if (dotPos != std::string::npos) {
							std::string qualifier = match.substr(0, dotPos);
							// Check if qualifier matches the first argument's type
							if (qualifier == firstArgType) {
								if (resolvedMatch.empty()) {
									resolvedMatch = match;
								} else {
									// Multiple matches even with type narrowing - still ambiguous
									resolvedMatch.clear();
									break;
								}
							}
						}
					}
				}

				if (!resolvedMatch.empty()) {
					// Found exactly one match based on receiver type
					funcIt = functions.find(resolvedMatch);
					auto idIt = functionIndices.find(resolvedMatch);
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
						analyzeExpression(callExpr->args[i].get());
					}
					return "error";
				}
			} else {
				// Exactly one match - use it for validation and set function ID
				// Note: We found the function, so it's not undefined
				funcIt = functions.find(matches[0]);
				// Store the resolved function ID in the AST for fast codegen lookup
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
				analyzeExpression(arg.get());
			}
			return "error";
		}

		const FunctionInfo &funcInfo = funcIt->second;

		// Store the resolved function ID in the AST for fast codegen lookup
		auto idIt = functionIndices.find(funcIt->first);
		if (idIt != functionIndices.end()) {
			callExpr->functionId = idIt->second;
		}

		// Check if this is a sibling method call (calling another method of the same type from within a method)
		// In this case, the caller passes N args but the method expects N+1 (with implicit self)
		bool isSiblingMethodCall = false;
		size_t expectedArgCount = funcInfo.parameters.size();

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
					isSiblingMethodCall = true;
					expectedArgCount = funcInfo.parameters.size() - 1; // Don't count implicit self
					callExpr->isSiblingMethodCall = true;			   // Mark for codegen
				}
			}
		}

		// Check argument count
		if (callExpr->args.size() != expectedArgCount) {
			addError("Function '" + callExpr->callee + "' argument count mismatch" +
						 std::string("\n  Expected: ") + std::to_string(expectedArgCount) + " argument" +
						 (expectedArgCount == 1 ? "" : "s") +
						 "\n  Found: " + std::to_string(callExpr->args.size()) + " argument" +
						 (callExpr->args.size() == 1 ? "" : "s"),
					 expr->line, expr->column);
			return funcInfo.returnType;
		}

		// Check argument types
		// For sibling method calls, args[i] corresponds to funcInfo.parameters[i+1] (skip self)
		size_t paramOffset = isSiblingMethodCall ? 1 : 0;
		for (size_t i = 0; i < callExpr->args.size(); i++) {
			std::string argType = analyzeExpression(callExpr->args[i].get());
			std::string expectedType = funcInfo.parameters[i + paramOffset].type;

			// Allow implicit int-to-float conversion
			bool isValidType = typesMatch(expectedType, argType) ||
							   (expectedType == "float" && argType == "int");

			// Special case: print() accepts strings as well as ints (polymorphic built-in)
			if (!isValidType && callExpr->callee == "print" && argType == "string") {
				isValidType = true;
			}

			if (!isValidType) {
				addError("Function '" + callExpr->callee + "' argument type mismatch" +
							 std::string("\n  Parameter ") + std::to_string(i + 1) + " ('" +
							 funcInfo.parameters[i + paramOffset].name + "')" +
							 "\n  Expected type: " + expectedType +
							 "\n  Found type: " + argType,
						 expr->line, expr->column);
			}
		}

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

		// Extract element type from array type (e.g., "[]string" -> "string", "[5]int" -> "int", "[16]byte" -> "byte")
		if (arrayType.size() > 2 && arrayType[0] == '[') {
			// Find the closing bracket
			size_t closeBracket = arrayType.find(']');
			if (closeBracket != std::string::npos && closeBracket + 1 < arrayType.size()) {
				return arrayType.substr(closeBracket + 1);
			}
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
			// Simple variable access (e.g., obj.field)
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

		// Check if it's a struct type
		if (lookupStruct(objectType) != nullptr) {
			const auto &structInfo = *lookupStruct(objectType);

			// Find the field
			for (const auto &field : structInfo.fields) {
				if (field.name == memberAccessExpr->memberName) {
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

			addError("Struct '" + objectType + "' has no field or method named '" + memberAccessExpr->memberName + "'",
					 expr->line, expr->column);
			return "error";
		}

		// Support array member access using shared type registry
		if (objectType.length() > 0 && objectType[0] == '[') {
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

		addError("Unknown member: " + memberAccessExpr->memberName + " on type " + objectType,
				 expr->line, expr->column);
		return "error";
	} else if (auto structInitExpr = dynamic_cast<StructInitExprAST *>(expr)) {
		// Check if struct type exists
		if (lookupStruct(structInitExpr->structName) == nullptr) {
			// Track as undefined for auto-import from stdlib
			undefinedStructs.insert(structInitExpr->structName);
			addError("Undefined struct type: '" + structInitExpr->structName + "'",
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
				addError("Struct '" + structInitExpr->structName + "' has no field named '" + initField.name + "'",
						 initField.line, initField.column);
			} else {
				// Type check the initializer value
				std::string valueType = analyzeExpression(initField.value.get());
				if (!typesMatch(expectedType, valueType)) {
					addError("Type mismatch for field '" + initField.name + "': expected '" + expectedType + "', got '" + valueType + "'",
							 initField.line, initField.column);
				}
			}
		}

		// Check for missing fields
		for (const auto &structField : structInfo.fields) {
			if (initializedFields.find(structField.name) == initializedFields.end()) {
				addError("Missing initialization for field '" + structField.name + "' in struct '" + structInitExpr->structName + "'",
						 expr->line, expr->column);
			}
		}

		return structInitExpr->structName;
	}

	return "error";
}
