/**
 * Compiler API for LSP Integration
 *
 * Implementation of clean API for LSP server to access compiler functionality.
 * The compiler serves as the single source of truth for all language information.
 */

#include "compiler_api.h"
#include "file_utils.h"
#include "lexer.h"
#include "parser.h"
#include "semantic_analyzer.h"
#include "token_stream.h"

// IR generation requires the full code generator (not available in LSP-only builds)
#ifdef MAXON_HAS_CODEGEN
#include "backend/x86_codegen.h"
#include "backend/x86_disassembler.h"
#include "codegen_mir.h"
#include "mir/optimizer.h"
#endif

#include <algorithm>
#include <cctype>
#include <filesystem>
#include <fstream>
#include <sstream>

// ============================================================================
// Path Normalization Helper
// ============================================================================

// Normalizes a file path for consistent comparison across different sources
// (filesystem iteration vs URI conversion). Converts to lowercase on Windows,
// normalizes slashes, and ensures consistent absolute path format.
static std::string normalizeFilePath(const std::string &path) {
	std::string result = path;

#ifdef _WIN32
	// Convert to lowercase for case-insensitive comparison on Windows
	std::transform(result.begin(), result.end(), result.begin(),
				   [](unsigned char c) { return std::tolower(c); });

	// Normalize slashes to backslashes
	std::replace(result.begin(), result.end(), '/', '\\');
#endif

	return result;
}

// ============================================================================
// Expression to String Helper
// ============================================================================

// Convert a simple expression to a string representation for display
static std::string exprToDisplayString(ExprAST *expr) {
	if (!expr)
		return "";

	if (auto *num = dynamic_cast<NumberExprAST *>(expr)) {
		return std::to_string(num->value);
	}
	if (auto *flt = dynamic_cast<FloatExprAST *>(expr)) {
		std::ostringstream ss;
		ss << flt->value;
		return ss.str();
	}
	if (auto *boolExpr = dynamic_cast<BooleanExprAST *>(expr)) {
		return boolExpr->value ? "true" : "false";
	}
	if (auto *ch = dynamic_cast<CharacterExprAST *>(expr)) {
		return std::string("'") + ch->value + "'";
	}
	if (auto *str = dynamic_cast<StringLiteralExprAST *>(expr)) {
		return "\"" + str->value + "\"";
	}
	if (auto *byteExpr = dynamic_cast<ByteExprAST *>(expr)) {
		return std::to_string(byteExpr->value) + "b";
	}
	if (auto *varExpr = dynamic_cast<VariableExprAST *>(expr)) {
		return varExpr->name;
	}
	if (auto *binExpr = dynamic_cast<BinaryExprAST *>(expr)) {
		std::string left = exprToDisplayString(binExpr->left.get());
		std::string right = exprToDisplayString(binExpr->right.get());
		std::string op;
		switch (binExpr->op) {
		case '+':
			op = " + ";
			break;
		case '-':
			op = " - ";
			break;
		case '*':
			op = " * ";
			break;
		case '/':
			op = " / ";
			break;
		case '%':
			op = " mod ";
			break;
		case '<':
			op = " < ";
			break;
		case '>':
			op = " > ";
			break;
		case 'L':
			op = " <= ";
			break;
		case 'G':
			op = " >= ";
			break;
		case '=':
			op = " == ";
			break;
		case '!':
			op = " != ";
			break;
		case 'A':
			op = " and ";
			break;
		case 'O':
			op = " or ";
			break;
		default:
			op = " ? ";
			break;
		}
		return left + op + right;
	}
	if (auto *unaryExpr = dynamic_cast<UnaryExprAST *>(expr)) {
		std::string operand = exprToDisplayString(unaryExpr->operand.get());
		if (unaryExpr->op == '-')
			return "-" + operand;
		if (unaryExpr->op == '!')
			return "not " + operand;
		return operand;
	}
	// For complex expressions, just indicate there's a value
	return "<expr>";
}

// ============================================================================
// Documentation Extraction
// ============================================================================

// Helper: build a vector of line start offsets for efficient line lookup
static std::vector<size_t> buildLineOffsets(const std::string &source) {
	std::vector<size_t> offsets;
	offsets.push_back(0); // Line 1 starts at offset 0
	for (size_t i = 0; i < source.size(); ++i) {
		if (source[i] == '\n') {
			offsets.push_back(i + 1);
		}
	}
	return offsets;
}

// Helper: get a line from source using pre-computed offsets (1-based line number)
static std::string_view getLine(const std::string &source, const std::vector<size_t> &lineOffsets, int lineNum) {
	if (lineNum < 1 || lineNum > static_cast<int>(lineOffsets.size())) {
		return {};
	}
	size_t start = lineOffsets[lineNum - 1];
	size_t end;
	if (lineNum < static_cast<int>(lineOffsets.size())) {
		end = lineOffsets[lineNum] - 1; // Exclude the newline
		if (end > start && end < source.size() && source[end - 1] == '\r') {
			end--; // Handle CRLF
		}
	} else {
		end = source.size();
	}
	return std::string_view(source).substr(start, end - start);
}

// Optimized version that uses pre-computed line offsets
static std::string extractDocCommentWithOffsets(const std::string &source,
												const std::vector<size_t> &lineOffsets,
												int declarationLine) {
	if (declarationLine <= 1 || source.empty()) {
		return "";
	}

	if (declarationLine > static_cast<int>(lineOffsets.size())) {
		return "";
	}

	// Look backwards from declaration line for /// comments
	std::vector<std::string> docLines;
	int currentLine = declarationLine - 1;

	while (currentLine >= 1) {
		std::string_view lineView = getLine(source, lineOffsets, currentLine);

		// Find first non-whitespace
		size_t firstNonSpace = lineView.find_first_not_of(" \t");
		if (firstNonSpace == std::string_view::npos) {
			// Empty line - stop looking
			break;
		}

		// Check for /// doc comment
		if (lineView.length() >= firstNonSpace + 3 &&
			lineView.substr(firstNonSpace, 3) == "///") {
			// Extract content after ///
			std::string content;
			if (lineView.length() > firstNonSpace + 3) {
				std::string_view contentView = lineView.substr(firstNonSpace + 3);
				// Strip leading space if present
				if (!contentView.empty() && contentView[0] == ' ') {
					contentView = contentView.substr(1);
				}
				content = std::string(contentView);
			}
			docLines.insert(docLines.begin(), content);
			currentLine--;
		} else {
			// Not a doc comment line - stop
			break;
		}
	}

	// Join doc lines
	std::string result;
	for (size_t i = 0; i < docLines.size(); ++i) {
		if (i > 0) {
			result += "\n";
		}
		result += docLines[i];
	}

	return result;
}

std::string extractDocComment(const std::string &source, int declarationLine) {
	if (declarationLine <= 1 || source.empty()) {
		return "";
	}

	// Build line offsets for efficient lookup
	auto lineOffsets = buildLineOffsets(source);
	return extractDocCommentWithOffsets(source, lineOffsets, declarationLine);
}

// ============================================================================
// Signature Building Helpers
// ============================================================================

std::string buildFunctionSignature(const FunctionAST *func) {
	if (!func)
		return "";

	std::ostringstream sig;

	if (func->isMethod()) {
		sig << "function " << func->receiverType << "." << func->name << "(";
	} else {
		sig << "function " << func->name << "(";
	}

	bool first = true;
	for (const auto &param : func->parameters) {
		if (!first)
			sig << ", ";
		first = false;
		sig << param.name << " " << param.type;
	}

	sig << ")";

	if (!func->returnType.empty() && func->returnType != "void") {
		sig << " " << func->returnType;
	}

	return sig.str();
}

std::string buildStructSignature(const StructDefAST *structDef) {
	if (!structDef)
		return "";

	std::ostringstream sig;
	sig << "struct " << structDef->name;

	if (!structDef->conformsTo.empty()) {
		sig << " is ";
		bool first = true;
		for (const auto &iface : structDef->conformsTo) {
			if (!first)
				sig << ", ";
			first = false;
			sig << iface;
		}
	}

	sig << " { ";
	bool first = true;
	for (const auto &field : structDef->fields) {
		if (!first)
			sig << ", ";
		first = false;
		sig << (field.isImmutable ? "let " : "var ") << field.name << " " << field.type;
	}
	sig << " }";

	return sig.str();
}

std::string buildEnumSignature(const EnumDefAST *enumDef) {
	if (!enumDef)
		return "";

	std::ostringstream sig;
	sig << "enum " << enumDef->name;

	if (!enumDef->rawValueType.empty()) {
		sig << " : " << enumDef->rawValueType;
	}

	sig << " { ";
	bool first = true;
	for (const auto &c : enumDef->cases) {
		if (!first)
			sig << ", ";
		first = false;
		sig << "case " << c.name;
		if (!c.associatedValues.empty()) {
			sig << "(";
			bool firstAssoc = true;
			for (const auto &av : c.associatedValues) {
				if (!firstAssoc)
					sig << ", ";
				firstAssoc = false;
				sig << av.name << " " << av.type;
			}
			sig << ")";
		}
	}
	sig << " }";

	return sig.str();
}

std::string buildInterfaceSignature(const InterfaceDefAST *interfaceDef) {
	if (!interfaceDef)
		return "";

	std::ostringstream sig;
	sig << "interface " << interfaceDef->name;

	if (!interfaceDef->associatedTypes.empty()) {
		sig << " uses ";
		bool first = true;
		for (const auto &at : interfaceDef->associatedTypes) {
			if (!first)
				sig << ", ";
			first = false;
			sig << at;
		}
	}

	sig << " { ";
	bool first = true;
	for (const auto &method : interfaceDef->methods) {
		if (!first)
			sig << "; ";
		first = false;
		sig << "function " << method.name << "(";
		bool firstParam = true;
		for (const auto &param : method.parameters) {
			if (!firstParam)
				sig << ", ";
			firstParam = false;
			sig << param.name << " " << param.type;
		}
		sig << ")";
		if (!method.returnType.empty() && method.returnType != "void") {
			sig << " " << method.returnType;
		}
	}
	sig << " }";

	return sig.str();
}

// ============================================================================
// Stdlib Registration Helper
// ============================================================================

// Register all stdlib symbols on an analyzer, optionally skipping symbols from a specific file
static void registerStdlibSymbols(SemanticAnalyzer &analyzer, const StdlibSymbols &stdlib,
								  const std::string &skipFilePath = "") {
	// Register stdlib functions with full signatures
	for (const auto &func : stdlib.functions) {
		if (!skipFilePath.empty() && normalizeFilePath(func.filePath) == skipFilePath) {
			continue;
		}
		std::vector<FunctionParameter> params;
		for (const auto &p : func.parameters) {
			params.push_back({p.name, p.type});
		}
		analyzer.registerExternalFunction(func.name, func.returnType, params, func.isStaticMethod);
	}

	// Register stdlib structs with their fields, interface conformance, and type assignments
	for (const auto &structSym : stdlib.structs) {
		if (!skipFilePath.empty() && normalizeFilePath(structSym.filePath) == skipFilePath) {
			continue;
		}
		std::vector<StructFieldInfo> fields;
		for (const auto &f : structSym.fields) {
			fields.emplace_back(f.name, f.type, f.isImmutable);
		}
		analyzer.registerExternalStruct(structSym.name, fields, structSym.conformsTo, structSym.typeAssignments);
	}

	// Register stdlib interfaces
	for (const auto &ifaceSym : stdlib.interfaces) {
		if (!skipFilePath.empty() && normalizeFilePath(ifaceSym.filePath) == skipFilePath) {
			continue;
		}
		std::vector<InterfaceMethodInfo> methods;
		for (const auto &m : ifaceSym.interfaceMethods) {
			std::vector<FunctionParameter> params;
			for (const auto &p : m.parameters) {
				params.emplace_back(p.name, p.type, 0, 0);
			}
			methods.emplace_back(m.name, m.returnType, std::move(params), m.hasDefaultImplementation,
								 nullptr, m.isStatic);
		}
		analyzer.registerExternalInterface(ifaceSym.name, methods, ifaceSym.associatedTypes, ifaceSym.extendsInterface);
	}

	// Register stdlib enums with their cases
	for (const auto &enumSym : stdlib.enums) {
		if (!skipFilePath.empty() && normalizeFilePath(enumSym.filePath) == skipFilePath) {
			continue;
		}
		std::vector<EnumCaseInfo> cases;
		int tagValue = 0;
		for (const auto &c : enumSym.enumCases) {
			EnumCaseInfo caseInfo(c.name, tagValue++);
			caseInfo.hasRawValue = !c.rawStringValue.empty() || c.rawIntValue != 0;
			caseInfo.rawIntValue = c.rawIntValue;
			caseInfo.rawStringValue = c.rawStringValue;
			for (const auto &av : c.associatedValues) {
				caseInfo.associatedValues.emplace_back(av.first, av.second);
			}
			cases.push_back(std::move(caseInfo));
		}
		analyzer.registerExternalEnum(enumSym.name, enumSym.rawValueType, cases);
	}
}

// ============================================================================
// Symbol Extraction
// ============================================================================

std::vector<LSPSymbolInfo> extractSymbolsFromAST(const ProgramAST *program,
												 const std::string &source,
												 bool exportedOnly) {
	std::vector<LSPSymbolInfo> symbols;

	if (!program)
		return symbols;

	// Pre-compute line offsets once for efficient doc comment extraction
	auto lineOffsets = buildLineOffsets(source);

	// Build interface map for resolving type bindings
	// Maps interface name -> list of associated type names
	std::map<std::string, std::vector<std::string>> interfaceAssociatedTypes;
	for (const auto &iface : program->interfaces) {
		interfaceAssociatedTypes[iface->name] = iface->associatedTypes;
	}

	// Extract functions
	for (const auto &func : program->functions) {
		if (exportedOnly && !func->isExported)
			continue;

		std::string kind = func->isMethod() ? "method" : "function";
		std::string signature = buildFunctionSignature(func.get());
		std::string doc = extractDocCommentWithOffsets(source, lineOffsets, func->line);

		LSPSymbolInfo sym(
			func->name,
			kind,
			signature,
			doc,
			func->getSourceRange());

		// Populate parameters (include "self" for methods - needed for semantic analysis)
		for (const auto &param : func->parameters) {
			sym.parameters.emplace_back(param.name, param.type);
		}

		// Populate return type
		sym.returnType = func->returnType;

		// Track static method status
		sym.isStaticMethod = func->isStaticMethod;

		symbols.push_back(std::move(sym));
	}

	// Extract structs and their fields/methods
	for (const auto &structDef : program->structs) {
		if (exportedOnly && !structDef->isExported)
			continue;

		std::string signature = buildStructSignature(structDef.get());
		std::string doc = extractDocCommentWithOffsets(source, lineOffsets, structDef->line);

		LSPSymbolInfo structSym(
			structDef->name,
			"struct",
			signature,
			doc,
			structDef->getSourceRange());
		structSym.conformsTo = structDef->conformsTo;

		// Resolve interfaceTypeBindings to typeAssignments
		// Start with any explicit typeAssignments from the AST
		structSym.typeAssignments = structDef->typeAssignments;
		// Then resolve bindings from "is Interface with Type" clauses
		for (const auto &binding : structDef->interfaceTypeBindings) {
			const std::string &interfaceName = binding.first;
			const std::vector<std::string> &withTypes = binding.second;

			// Look up interface's associated types
			auto ifaceIt = interfaceAssociatedTypes.find(interfaceName);
			if (ifaceIt != interfaceAssociatedTypes.end()) {
				const auto &assocTypes = ifaceIt->second;
				// Map positionally: withTypes[i] -> assocTypes[i]
				for (size_t i = 0; i < withTypes.size() && i < assocTypes.size(); i++) {
					structSym.typeAssignments[assocTypes[i]] = withTypes[i];
				}
			}
		}

		// Populate struct fields for semantic analysis registration
		for (const auto &field : structDef->fields) {
			structSym.fields.emplace_back(field.name, field.type, field.isImmutable);
		}

		symbols.push_back(std::move(structSym));

		// Also extract fields as separate symbols for document symbols view
		for (const auto &field : structDef->fields) {
			std::string fieldSig = (field.isImmutable ? "let " : "var ") +
								   field.name + " " + field.type;
			symbols.emplace_back(
				field.name,
				"field",
				fieldSig,
				"", // Fields don't typically have individual doc comments
				SourceRange(field.line, field.column, field.line, field.column));
		}

		// Extract methods defined inside the struct
		for (const auto &method : structDef->methods) {
			std::string methodSig = buildFunctionSignature(method.get());
			std::string methodDoc = extractDocCommentWithOffsets(source, lineOffsets, method->line);

			// Qualify method name with struct name for completion lookup
			std::string qualifiedName = structDef->name + "." + method->name;

			LSPSymbolInfo methodSym(
				qualifiedName,
				"method",
				methodSig,
				methodDoc,
				method->getSourceRange());

			// Populate parameters (include "self" for methods - needed for semantic analysis)
			for (const auto &param : method->parameters) {
				methodSym.parameters.emplace_back(param.name, param.type);
			}

			// Populate return type
			methodSym.returnType = method->returnType;

			// Track static method status
			methodSym.isStaticMethod = method->isStaticMethod;

			symbols.push_back(std::move(methodSym));
		}
	}

	// Extract enums and their cases
	for (const auto &enumDef : program->enums) {
		if (exportedOnly && !enumDef->isExported)
			continue;

		std::string signature = buildEnumSignature(enumDef.get());
		std::string doc = extractDocCommentWithOffsets(source, lineOffsets, enumDef->line);

		LSPSymbolInfo enumSym(
			enumDef->name,
			"enum",
			signature,
			doc,
			enumDef->getSourceRange());

		// Store raw value type for external enum registration
		enumSym.rawValueType = enumDef->rawValueType;

		// Populate enum cases for external enum registration
		for (const auto &c : enumDef->cases) {
			LSPEnumCaseInfo caseInfo;
			caseInfo.name = c.name;
			// Extract raw value from the AST expression if present
			if (c.rawValue) {
				if (enumDef->rawValueType == "int") {
					if (auto *numExpr = dynamic_cast<NumberExprAST *>(c.rawValue.get())) {
						caseInfo.rawIntValue = numExpr->value;
					}
				} else if (enumDef->rawValueType == "string") {
					if (auto *strExpr = dynamic_cast<StringLiteralExprAST *>(c.rawValue.get())) {
						caseInfo.rawStringValue = strExpr->value;
					}
				}
			}
			for (const auto &av : c.associatedValues) {
				caseInfo.associatedValues.emplace_back(av.name, av.type);
			}
			enumSym.enumCases.push_back(std::move(caseInfo));
		}

		symbols.push_back(std::move(enumSym));

		// Extract enum cases as separate symbols for document symbols view
		for (const auto &c : enumDef->cases) {
			std::ostringstream caseSig;
			caseSig << enumDef->name << "." << c.name;
			if (!c.associatedValues.empty()) {
				caseSig << "(";
				bool first = true;
				for (const auto &av : c.associatedValues) {
					if (!first)
						caseSig << ", ";
					first = false;
					caseSig << av.name << " " << av.type;
				}
				caseSig << ")";
			}

			symbols.emplace_back(
				c.name,
				"enum_case",
				caseSig.str(),
				"",
				SourceRange(c.line, c.column, c.line, c.column));
		}

		// Extract methods defined inside the enum
		for (const auto &method : enumDef->methods) {
			std::string methodSig = buildFunctionSignature(method.get());
			std::string methodDoc = extractDocCommentWithOffsets(source, lineOffsets, method->line);

			// Qualify method name with enum name for completion lookup
			std::string qualifiedName = enumDef->name + "." + method->name;

			LSPSymbolInfo methodSym(
				qualifiedName,
				"method",
				methodSig,
				methodDoc,
				method->getSourceRange());

			// Populate parameters (include "self" for methods - needed for semantic analysis)
			for (const auto &param : method->parameters) {
				methodSym.parameters.emplace_back(param.name, param.type);
			}

			// Populate return type
			methodSym.returnType = method->returnType;

			symbols.push_back(std::move(methodSym));
		}
	}

	// Extract interfaces (but NOT their method signatures as standalone functions)
	// Interface methods are abstract signatures, not callable functions.
	// They get registered in the interfaces map, not the functions map.
	for (const auto &interfaceDef : program->interfaces) {
		if (exportedOnly && !interfaceDef->isExported)
			continue;

		std::string signature = buildInterfaceSignature(interfaceDef.get());
		std::string doc = extractDocCommentWithOffsets(source, lineOffsets, interfaceDef->line);

		LSPSymbolInfo ifaceSym(
			interfaceDef->name,
			"interface",
			signature,
			doc,
			interfaceDef->getSourceRange());
		ifaceSym.extendsInterface = interfaceDef->extendsInterface;
		ifaceSym.associatedTypes = interfaceDef->associatedTypes;

		// Extract interface method signatures
		for (const auto &method : interfaceDef->methods) {
			LSPInterfaceMethodInfo methodInfo;
			methodInfo.name = method.name;
			methodInfo.returnType = method.returnType;
			methodInfo.hasDefaultImplementation = method.hasDefaultImplementation;
			methodInfo.isStatic = method.isStatic;
			for (const auto &param : method.parameters) {
				methodInfo.parameters.emplace_back(param.name, param.type);
			}
			ifaceSym.interfaceMethods.push_back(std::move(methodInfo));
		}

		symbols.push_back(std::move(ifaceSym));
	}

	return symbols;
}

// ============================================================================
// Analysis Functions
// ============================================================================

LSPAnalysisResult analyzeForLSP(const std::string &source, const std::string &filename) {
	LSPAnalysisResult result;

	// Try to parse the source
	try {
		Lexer lexer(source);
		TokenStream stream = lexer.tokenize_stream();

		Parser parser(std::move(stream));
		std::string fileNamespace = deriveNamespace(filename);
		parser.setDefaultNamespace(fileNamespace);

		result.ast = parser.parse();
	} catch (const std::runtime_error &e) {
		// Parse error - extract location info if possible
		std::string errorMsg = e.what();

		// Try to extract line/column from error message
		// Format: "message\n  Location: line X, column Y"
		int line = 1, column = 1;
		size_t locPos = errorMsg.find("Location: line ");
		if (locPos != std::string::npos) {
			size_t lineStart = locPos + 15; // Skip "Location: line "
			size_t lineEnd = errorMsg.find(',', lineStart);
			if (lineEnd != std::string::npos) {
				line = std::stoi(errorMsg.substr(lineStart, lineEnd - lineStart));
				size_t colStart = errorMsg.find("column ", lineEnd);
				if (colStart != std::string::npos) {
					colStart += 7; // Skip "column "
					column = std::stoi(errorMsg.substr(colStart));
				}
			}
		}

		// Extract just the error message (first line)
		size_t newlinePos = errorMsg.find('\n');
		std::string msg = (newlinePos != std::string::npos)
							  ? errorMsg.substr(0, newlinePos)
							  : errorMsg;

		result.parseErrors.emplace_back(msg, line, column);
		return result;
	}

	// Copy parse errors from AST
	if (result.ast) {
		for (const auto &err : result.ast->parseErrors) {
			result.parseErrors.emplace_back(err.message, err.line, err.column);
		}
	}

	// Extract symbols from AST
	if (result.ast) {
		result.symbols = extractSymbolsFromAST(result.ast.get(), source, false);
	}

	// Run semantic analysis (collect errors, don't fail)
	if (result.ast) {
		SemanticAnalyzer analyzer;
		analyzer.registerBuiltinFunctions();
		result.semanticErrors = analyzer.analyze(result.ast.get());

		// Extract semantic info for LSP features
		result.variables = analyzer.getAllVariables();
		result.functions = analyzer.getFunctions();
		result.structs = analyzer.getStructs();
		result.interfaces = analyzer.getInterfaces();
		result.enums = analyzer.getEnums();

		// Add global constants to variables for hover support
		for (const auto &[name, info] : analyzer.getGlobalConstants()) {
			VariableInfo varInfo;
			varInfo.name = name;
			varInfo.type = info.type;
			varInfo.isImmutable = true; // Global constants are always immutable
			varInfo.isParameter = false;
			varInfo.line = info.line;
			varInfo.column = info.column;

			// Find initializer value from AST
			for (const auto &global : result.ast->globals) {
				if (global->name == name) {
					varInfo.initialValue = exprToDisplayString(global->initializer.get());
					break;
				}
			}

			result.variables[name] = varInfo;
		}
	}

	return result;
}

LSPAnalysisResult analyzeForLSP(const std::string &source, const std::string &filename,
								const StdlibSymbols &stdlib) {
	LSPAnalysisResult result;

	// Get the absolute path of the current file for comparison
	// This is used to skip stdlib symbols from the current file to avoid duplicate registration
	// Normalize the path for consistent comparison across different sources
	std::string currentFilePath;
	try {
		currentFilePath = normalizeFilePath(std::filesystem::absolute(filename).string());
	} catch (...) {
		currentFilePath = normalizeFilePath(filename);
	}

	// Try to parse the source
	try {
		Lexer lexer(source);
		TokenStream stream = lexer.tokenize_stream();

		Parser parser(std::move(stream));
		std::string fileNamespace = deriveNamespace(filename);
		parser.setDefaultNamespace(fileNamespace);

		result.ast = parser.parse();
	} catch (const std::runtime_error &e) {
		// Parse error - extract location info if possible
		std::string errorMsg = e.what();

		// Try to extract line/column from error message
		int line = 1, column = 1;
		size_t locPos = errorMsg.find("Location: line ");
		if (locPos != std::string::npos) {
			size_t lineStart = locPos + 15;
			size_t lineEnd = errorMsg.find(',', lineStart);
			if (lineEnd != std::string::npos) {
				line = std::stoi(errorMsg.substr(lineStart, lineEnd - lineStart));
				size_t colStart = errorMsg.find("column ", lineEnd);
				if (colStart != std::string::npos) {
					colStart += 7;
					column = std::stoi(errorMsg.substr(colStart));
				}
			}
		}

		size_t newlinePos = errorMsg.find('\n');
		std::string msg = (newlinePos != std::string::npos)
							  ? errorMsg.substr(0, newlinePos)
							  : errorMsg;

		result.parseErrors.emplace_back(msg, line, column);
		return result;
	}

	// Copy parse errors from AST
	if (result.ast) {
		for (const auto &err : result.ast->parseErrors) {
			result.parseErrors.emplace_back(err.message, err.line, err.column);
		}
	}

	// Extract symbols from AST
	if (result.ast) {
		result.symbols = extractSymbolsFromAST(result.ast.get(), source, false);
	}

	// Run semantic analysis with stdlib symbols registered
	if (result.ast) {
		SemanticAnalyzer analyzer;
		analyzer.registerBuiltinFunctions();
		registerStdlibSymbols(analyzer, stdlib, currentFilePath);

		// Set source context for doc comment checking
		analyzer.setSourceContext(source, filename);

		result.semanticErrors = analyzer.analyze(result.ast.get());

		// Extract semantic info for LSP features
		result.variables = analyzer.getAllVariables();
		result.functions = analyzer.getFunctions();
		result.structs = analyzer.getStructs();
		result.interfaces = analyzer.getInterfaces();
		result.enums = analyzer.getEnums();

		// Add global constants to variables for hover support
		for (const auto &[name, info] : analyzer.getGlobalConstants()) {
			VariableInfo varInfo;
			varInfo.name = name;
			varInfo.type = info.type;
			varInfo.isImmutable = true; // Global constants are always immutable
			varInfo.isParameter = false;
			varInfo.line = info.line;
			varInfo.column = info.column;

			// Find initializer value from AST
			for (const auto &global : result.ast->globals) {
				if (global->name == name) {
					varInfo.initialValue = exprToDisplayString(global->initializer.get());
					break;
				}
			}

			result.variables[name] = varInfo;
		}
	}

	return result;
}

// Static cache for stdlib symbols loaded from disk (no content provider)
static std::string cachedStdlibPath;
static StdlibSymbols cachedStdlibSymbols;

StdlibSymbols loadStdlib(const std::string &stdlibPath) {
	// Check if we have a cached version for this path
	std::string normalizedPath = std::filesystem::absolute(stdlibPath).string();
	if (!cachedStdlibPath.empty() && cachedStdlibPath == normalizedPath) {
		return cachedStdlibSymbols;
	}

	// Load fresh and cache for future calls
	cachedStdlibSymbols = loadStdlibWithContentProvider(stdlibPath, nullptr);
	cachedStdlibPath = normalizedPath;
	return cachedStdlibSymbols;
}

StdlibSymbols loadStdlibWithContentProvider(
	const std::string &stdlibPath,
	std::function<std::optional<std::string>(const std::string &)> contentProvider) {
	StdlibSymbols result;

	if (!std::filesystem::exists(stdlibPath)) {
		return result;
	}

	// Find all .maxon files in stdlib
	std::vector<std::string> stdlibFiles = findMaxonFiles(stdlibPath);

	// First pass: collect all interface definitions for type binding resolution
	std::map<std::string, std::vector<std::string>> interfaceAssociatedTypes;

	// Store parsed programs for second pass
	struct ParsedFile {
		std::string absolutePath;
		std::string source;
		std::unique_ptr<ProgramAST> program;
	};
	std::vector<ParsedFile> parsedFiles;

	for (const auto &filePath : stdlibFiles) {
		try {
			std::string source;
			std::string absolutePath = std::filesystem::absolute(filePath).string();

			// Try content provider first, fall back to disk
			std::optional<std::string> providedContent;
			if (contentProvider) {
				providedContent = contentProvider(absolutePath);
			}

			if (providedContent.has_value()) {
				source = providedContent.value();
			} else {
				// Read file content from disk
				std::ifstream file(filePath);
				if (!file)
					continue;

				std::stringstream buffer;
				buffer << file.rdbuf();
				source = buffer.str();
			}

			// Parse the file
			Lexer lexer(source);
			TokenStream stream = lexer.tokenize_stream();

			Parser parser(std::move(stream));
			std::string fileNamespace = deriveNamespace(filePath);
			parser.setDefaultNamespace(fileNamespace);

			auto program = parser.parse();

			// Collect interface associated types
			for (const auto &iface : program->interfaces) {
				if (iface->isExported) {
					interfaceAssociatedTypes[iface->name] = iface->associatedTypes;
				}
			}

			// Store for second pass
			parsedFiles.push_back({absolutePath, std::move(source), std::move(program)});

		} catch (const std::exception &e) {
			// Skip files that fail to parse
			continue;
		}
	}

	// Second pass: extract symbols with interface type bindings resolved
	for (auto &pf : parsedFiles) {
		// Pre-compute line offsets
		auto lineOffsets = buildLineOffsets(pf.source);

		// Extract exported symbols
		for (const auto &func : pf.program->functions) {
			if (!func->isExported)
				continue;

			std::string kind = func->isMethod() ? "method" : "function";
			std::string signature = buildFunctionSignature(func.get());
			std::string doc = extractDocCommentWithOffsets(pf.source, lineOffsets, func->line);

			LSPSymbolInfo sym(func->name, kind, signature, doc, func->getSourceRange());
			for (const auto &param : func->parameters) {
				sym.parameters.emplace_back(param.name, param.type);
			}
			sym.returnType = func->returnType;
			sym.isStaticMethod = func->isStaticMethod;
			sym.filePath = pf.absolutePath;
			result.functions.push_back(std::move(sym));
		}

		for (const auto &structDef : pf.program->structs) {
			if (!structDef->isExported)
				continue;

			std::string signature = buildStructSignature(structDef.get());
			std::string doc = extractDocCommentWithOffsets(pf.source, lineOffsets, structDef->line);

			LSPSymbolInfo structSym(structDef->name, "struct", signature, doc, structDef->getSourceRange());
			structSym.conformsTo = structDef->conformsTo;
			structSym.filePath = pf.absolutePath;

			// Resolve interfaceTypeBindings to typeAssignments
			structSym.typeAssignments = structDef->typeAssignments;
			for (const auto &binding : structDef->interfaceTypeBindings) {
				const std::string &interfaceName = binding.first;
				const std::vector<std::string> &withTypes = binding.second;

				auto ifaceIt = interfaceAssociatedTypes.find(interfaceName);
				if (ifaceIt != interfaceAssociatedTypes.end()) {
					const auto &assocTypes = ifaceIt->second;
					for (size_t i = 0; i < withTypes.size() && i < assocTypes.size(); i++) {
						structSym.typeAssignments[assocTypes[i]] = withTypes[i];
					}
				}
			}

			for (const auto &field : structDef->fields) {
				structSym.fields.emplace_back(field.name, field.type, field.isImmutable);
			}
			result.structs.push_back(std::move(structSym));

			// Extract methods
			for (const auto &method : structDef->methods) {
				std::string methodSig = buildFunctionSignature(method.get());
				std::string methodDoc = extractDocCommentWithOffsets(pf.source, lineOffsets, method->line);
				std::string qualifiedName = structDef->name + "." + method->name;

				LSPSymbolInfo methodSym(qualifiedName, "method", methodSig, methodDoc, method->getSourceRange());
				for (const auto &param : method->parameters) {
					methodSym.parameters.emplace_back(param.name, param.type);
				}
				methodSym.returnType = method->returnType;
				methodSym.isStaticMethod = method->isStaticMethod;
				methodSym.filePath = pf.absolutePath;
				result.functions.push_back(std::move(methodSym));
			}
		}

		for (const auto &enumDef : pf.program->enums) {
			if (!enumDef->isExported)
				continue;

			std::string signature = buildEnumSignature(enumDef.get());
			std::string doc = extractDocCommentWithOffsets(pf.source, lineOffsets, enumDef->line);

			LSPSymbolInfo enumSym(enumDef->name, "enum", signature, doc, enumDef->getSourceRange());
			enumSym.rawValueType = enumDef->rawValueType;
			enumSym.filePath = pf.absolutePath;

			for (const auto &c : enumDef->cases) {
				LSPEnumCaseInfo caseInfo;
				caseInfo.name = c.name;
				// Raw values are computed during semantic analysis, not available at parse time
				for (const auto &av : c.associatedValues) {
					caseInfo.associatedValues.emplace_back(av.name, av.type);
				}
				enumSym.enumCases.push_back(std::move(caseInfo));
			}
			result.enums.push_back(std::move(enumSym));
		}

		for (const auto &iface : pf.program->interfaces) {
			if (!iface->isExported)
				continue;

			std::string signature = buildInterfaceSignature(iface.get());
			std::string doc = extractDocCommentWithOffsets(pf.source, lineOffsets, iface->line);

			LSPSymbolInfo ifaceSym(iface->name, "interface", signature, doc, iface->getSourceRange());
			ifaceSym.extendsInterface = iface->extendsInterface;
			ifaceSym.associatedTypes = iface->associatedTypes;
			ifaceSym.filePath = pf.absolutePath;

			for (const auto &method : iface->methods) {
				LSPInterfaceMethodInfo methodInfo;
				methodInfo.name = method.name;
				methodInfo.returnType = method.returnType;
				methodInfo.hasDefaultImplementation = method.hasDefaultImplementation;
				methodInfo.isStatic = method.isStatic;
				for (const auto &param : method.parameters) {
					methodInfo.parameters.emplace_back(param.name, param.type);
				}
				ifaceSym.interfaceMethods.push_back(std::move(methodInfo));
			}
			result.interfaces.push_back(std::move(ifaceSym));
		}
	}

	return result;
}

std::vector<KeywordLSPInfo> getKeywordInfo() {
	return KeywordMatcher::getLSPKeywordInfo();
}

std::vector<KeywordLSPInfo> getKeywordsForCompletion(const std::string &prefix) {
	return KeywordMatcher::getKeywordsForCompletion(prefix);
}

// ============================================================================
// IR Generation for Compiler Explorer
// ============================================================================

#ifdef MAXON_HAS_CODEGEN

IRGenerationResult generateIRForLSP(const std::string &source, const std::string &filename,
									bool optimize, const StdlibSymbols &stdlib) {
	IRGenerationResult result;
	// MARKER_generateIRForLSP

	// Parse the source
	std::unique_ptr<ProgramAST> ast;
	try {
		Lexer lexer(source);
		TokenStream stream = lexer.tokenize_stream();

		Parser parser(std::move(stream));
		std::string fileNamespace = deriveNamespace(filename);
		parser.setDefaultNamespace(fileNamespace);

		ast = parser.parse();
	} catch (const std::runtime_error &e) {
		// Parse error - extract location info
		std::string errorMsg = e.what();
		int line = 1, column = 1;
		size_t locPos = errorMsg.find("Location: line ");
		if (locPos != std::string::npos) {
			size_t lineStart = locPos + 15;
			size_t lineEnd = errorMsg.find(',', lineStart);
			if (lineEnd != std::string::npos) {
				line = std::stoi(errorMsg.substr(lineStart, lineEnd - lineStart));
				size_t colStart = errorMsg.find("column ", lineEnd);
				if (colStart != std::string::npos) {
					colStart += 7;
					column = std::stoi(errorMsg.substr(colStart));
				}
			}
		}

		size_t newlinePos = errorMsg.find('\n');
		std::string msg = (newlinePos != std::string::npos)
							  ? errorMsg.substr(0, newlinePos)
							  : errorMsg;

		result.parseErrors.emplace_back(msg, line, column);
		return result;
	}

	// Copy parse errors from AST
	if (ast) {
		for (const auto &err : ast->parseErrors) {
			result.parseErrors.emplace_back(err.message, err.line, err.column);
		}
	}

	// If there are parse errors, return early
	if (!result.parseErrors.empty()) {
		return result;
	}

	// Run semantic analysis with stdlib symbols registered
	SemanticAnalyzer analyzer;
	analyzer.registerBuiltinFunctions();
	registerStdlibSymbols(analyzer, stdlib);

	result.semanticErrors = analyzer.analyze(ast.get());

	// If there are semantic errors, return early
	if (!result.semanticErrors.empty()) {
		return result;
	}

	// Generate MIR
	try {
		std::string moduleName = "compiler_explorer";
		MIRCodeGenerator codegen(moduleName, false, 0, false);

		// Get function indices and return types from analyzer
		auto functionIndices = analyzer.getFunctionIndices();
		std::map<std::string, std::string> functionReturnTypes;
		for (const auto &[name, info] : analyzer.getFunctions()) {
			functionReturnTypes[name] = info.returnType;
		}

		// Generate code (no entry point needed for explorer view)
		codegen.generate(ast.get(), false, &functionIndices, &functionReturnTypes);

		// Note: We intentionally skip dead code elimination for the compiler explorer
		// because we want to show IR for all user-defined functions, even if they
		// aren't reachable from main (which may not even exist in explorer snippets)

		// Optionally run optimization passes (using explorer pipeline which skips DCE)
		if (optimize) {
			codegen.optimizeForExplorer();
		}

		// Get IR as string
		result.ir = codegen.getModule()->toString();

	} catch (const std::exception &e) {
		// Code generation error
		result.semanticErrors.emplace_back(std::string("Code generation error: ") + e.what(), 1, 1);
	}

	return result;
}

AsmGenerationResult generateAsmForLSP(const std::string &source, const std::string &filename,
									  bool optimize, const StdlibSymbols &stdlib) {
	AsmGenerationResult result;

	// Parse the source
	std::unique_ptr<ProgramAST> ast;
	try {
		Lexer lexer(source);
		TokenStream stream = lexer.tokenize_stream();

		Parser parser(std::move(stream));
		std::string fileNamespace = deriveNamespace(filename);
		parser.setDefaultNamespace(fileNamespace);

		ast = parser.parse();
	} catch (const std::runtime_error &e) {
		// Parse error - extract location info
		std::string errorMsg = e.what();
		int line = 1, column = 1;
		size_t locPos = errorMsg.find("Location: line ");
		if (locPos != std::string::npos) {
			size_t lineStart = locPos + 15;
			size_t lineEnd = errorMsg.find(',', lineStart);
			if (lineEnd != std::string::npos) {
				line = std::stoi(errorMsg.substr(lineStart, lineEnd - lineStart));
				size_t colStart = errorMsg.find("column ", lineEnd);
				if (colStart != std::string::npos) {
					colStart += 7;
					column = std::stoi(errorMsg.substr(colStart));
				}
			}
		}

		size_t newlinePos = errorMsg.find('\n');
		std::string msg = (newlinePos != std::string::npos)
							  ? errorMsg.substr(0, newlinePos)
							  : errorMsg;

		result.parseErrors.emplace_back(msg, line, column);
		return result;
	}

	// Copy parse errors from AST
	if (ast) {
		for (const auto &err : ast->parseErrors) {
			result.parseErrors.emplace_back(err.message, err.line, err.column);
		}
	}

	// If there are parse errors, return early
	if (!result.parseErrors.empty()) {
		return result;
	}

	// Run semantic analysis with stdlib symbols registered
	SemanticAnalyzer analyzer;
	analyzer.registerBuiltinFunctions();
	registerStdlibSymbols(analyzer, stdlib);

	result.semanticErrors = analyzer.analyze(ast.get());

	// If there are semantic errors, return early
	if (!result.semanticErrors.empty()) {
		return result;
	}

	// Generate MIR
	try {
		std::string moduleName = "compiler_explorer";
		MIRCodeGenerator codegen(moduleName, false, 0, false);

		// Get function indices and return types from analyzer
		auto functionIndices = analyzer.getFunctionIndices();
		std::map<std::string, std::string> functionReturnTypes;
		for (const auto &[name, info] : analyzer.getFunctions()) {
			functionReturnTypes[name] = info.returnType;
		}

		// Generate code (no entry point needed for explorer view)
		codegen.generate(ast.get(), false, &functionIndices, &functionReturnTypes);

		// Optionally run optimization passes on MIR (using explorer pipeline which skips DCE)
		if (optimize) {
			codegen.optimizeForExplorer();
		}

		// Run PHI elimination (required before x86 codegen)
		mir::PhiEliminationPass phiElim;
		phiElim.run(*codegen.getModule());

		// Generate x86-64 machine code
#ifdef _WIN32
		backend::X86CodeGen x86gen(backend::CallingConv::Win64);
#else
		backend::X86CodeGen x86gen(backend::CallingConv::SysV64);
#endif
		x86gen.generate(codegen.getModule());

		// Disassemble to Intel syntax assembly
		backend::X86Disassembler disasm;
		result.assembly = disasm.disassembleWithSymbols(x86gen.getFunctionCodes());

	} catch (const std::exception &e) {
		// Code generation error
		result.semanticErrors.emplace_back(std::string("Code generation error: ") + e.what(), 1, 1);
	}

	return result;
}

#else

// Stub implementation when code generator is not available (e.g., LSP test builds)
IRGenerationResult generateIRForLSP(const std::string &source, const std::string &filename,
									bool optimize, const StdlibSymbols &stdlib) {
	IRGenerationResult result;
	result.semanticErrors.emplace_back("IR generation not available in this build", 1, 1);
	return result;
}

AsmGenerationResult generateAsmForLSP(const std::string &source, const std::string &filename,
									  bool optimize, const StdlibSymbols &stdlib) {
	AsmGenerationResult result;
	result.semanticErrors.emplace_back("Assembly generation not available in this build", 1, 1);
	return result;
}

#endif // MAXON_HAS_CODEGEN
