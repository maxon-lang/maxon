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

#include <filesystem>
#include <fstream>
#include <sstream>

// ============================================================================
// Documentation Extraction
// ============================================================================

std::string extractDocComment(const std::string &source, int declarationLine) {
	if (declarationLine <= 1 || source.empty()) {
		return "";
	}

	// Split source into lines
	std::vector<std::string> lines;
	std::istringstream stream(source);
	std::string line;
	while (std::getline(stream, line)) {
		lines.push_back(line);
	}

	// declarationLine is 1-based, convert to 0-based index
	int declIndex = declarationLine - 1;
	if (declIndex >= static_cast<int>(lines.size())) {
		return "";
	}

	// Look backwards from declaration line for /// comments
	std::vector<std::string> docLines;
	int currentLine = declIndex - 1;

	while (currentLine >= 0) {
		const std::string &currentLineStr = lines[currentLine];

		// Find first non-whitespace
		size_t firstNonSpace = currentLineStr.find_first_not_of(" \t");
		if (firstNonSpace == std::string::npos) {
			// Empty line - stop looking
			break;
		}

		// Check for /// doc comment
		if (currentLineStr.length() >= firstNonSpace + 3 &&
			currentLineStr.substr(firstNonSpace, 3) == "///") {
			// Extract content after ///
			std::string content;
			if (currentLineStr.length() > firstNonSpace + 3) {
				content = currentLineStr.substr(firstNonSpace + 3);
				// Strip leading space if present
				if (!content.empty() && content[0] == ' ') {
					content = content.substr(1);
				}
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
// Symbol Extraction
// ============================================================================

std::vector<LSPSymbolInfo> extractSymbolsFromAST(const ProgramAST *program,
												 const std::string &source,
												 bool exportedOnly) {
	std::vector<LSPSymbolInfo> symbols;

	if (!program)
		return symbols;

	// Extract functions
	for (const auto &func : program->functions) {
		if (exportedOnly && !func->isExported)
			continue;

		std::string kind = func->isMethod() ? "method" : "function";
		std::string signature = buildFunctionSignature(func.get());
		std::string doc = extractDocComment(source, func->line);

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

		symbols.push_back(std::move(sym));
	}

	// Extract structs and their fields/methods
	for (const auto &structDef : program->structs) {
		if (exportedOnly && !structDef->isExported)
			continue;

		std::string signature = buildStructSignature(structDef.get());
		std::string doc = extractDocComment(source, structDef->line);

		LSPSymbolInfo structSym(
			structDef->name,
			"struct",
			signature,
			doc,
			structDef->getSourceRange());
		structSym.conformsTo = structDef->conformsTo;

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
			std::string methodDoc = extractDocComment(source, method->line);

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

			symbols.push_back(std::move(methodSym));
		}
	}

	// Extract enums and their cases
	for (const auto &enumDef : program->enums) {
		if (exportedOnly && !enumDef->isExported)
			continue;

		std::string signature = buildEnumSignature(enumDef.get());
		std::string doc = extractDocComment(source, enumDef->line);

		symbols.emplace_back(
			enumDef->name,
			"enum",
			signature,
			doc,
			enumDef->getSourceRange());

		// Extract enum cases
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
			std::string methodDoc = extractDocComment(source, method->line);

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
		std::string doc = extractDocComment(source, interfaceDef->line);

		symbols.emplace_back(
			interfaceDef->name,
			"interface",
			signature,
			doc,
			interfaceDef->getSourceRange());
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
	}

	return result;
}

LSPAnalysisResult analyzeForLSP(const std::string &source, const std::string &filename,
								const StdlibSymbols &stdlib) {
	LSPAnalysisResult result;

	// Get the absolute path of the current file for comparison
	// This is used to skip stdlib symbols from the current file to avoid duplicate registration
	std::string currentFilePath;
	try {
		currentFilePath = std::filesystem::absolute(filename).string();
	} catch (...) {
		currentFilePath = filename;
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

		// Register stdlib functions with full signatures
		// Skip symbols from the current file to avoid duplicate registration
		for (const auto &func : stdlib.functions) {
			if (func.filePath == currentFilePath) {
				continue; // Skip symbols from current file
			}
			std::vector<FunctionParameter> params;
			for (const auto &p : func.parameters) {
				params.push_back({p.name, p.type});
			}
			analyzer.registerExternalFunction(func.name, func.returnType, params);
		}

		// Register stdlib structs with their fields and interface conformance
		for (const auto &structSym : stdlib.structs) {
			if (structSym.filePath == currentFilePath) {
				continue; // Skip symbols from current file
			}
			// Convert LSPFieldInfo to StructFieldInfo
			std::vector<StructFieldInfo> fields;
			for (const auto &f : structSym.fields) {
				fields.emplace_back(f.name, f.type, f.isImmutable);
			}
			analyzer.registerExternalStruct(structSym.name, fields, structSym.conformsTo);
		}

		// Register stdlib interfaces
		for (const auto &ifaceSym : stdlib.interfaces) {
			if (ifaceSym.filePath == currentFilePath) {
				continue; // Skip symbols from current file
			}
			analyzer.registerExternalInterface(ifaceSym.name);
		}

		// Register stdlib enums
		for (const auto &enumSym : stdlib.enums) {
			if (enumSym.filePath == currentFilePath) {
				continue; // Skip symbols from current file
			}
			analyzer.registerExternalEnum(enumSym.name);
		}

		// Set source context for doc comment checking
		analyzer.setSourceContext(source, filename);

		result.semanticErrors = analyzer.analyze(result.ast.get());

		// Extract semantic info for LSP features
		result.variables = analyzer.getAllVariables();
		result.functions = analyzer.getFunctions();
		result.structs = analyzer.getStructs();
		result.interfaces = analyzer.getInterfaces();
	}

	return result;
}

StdlibSymbols loadStdlib(const std::string &stdlibPath) {
	StdlibSymbols result;

	if (!std::filesystem::exists(stdlibPath)) {
		return result;
	}

	// Find all .maxon files in stdlib
	std::vector<std::string> stdlibFiles = findMaxonFiles(stdlibPath);

	for (const auto &filePath : stdlibFiles) {
		try {
			// Read file content
			std::ifstream file(filePath);
			if (!file)
				continue;

			std::stringstream buffer;
			buffer << file.rdbuf();
			std::string source = buffer.str();

			// Parse the file
			Lexer lexer(source);
			TokenStream stream = lexer.tokenize_stream();

			Parser parser(std::move(stream));
			std::string fileNamespace = deriveNamespace(filePath);
			parser.setDefaultNamespace(fileNamespace);

			auto program = parser.parse();

			// Extract exported symbols only
			auto symbols = extractSymbolsFromAST(program.get(), source, true);

			// Get the absolute file path for go-to-definition support
			std::string absolutePath = std::filesystem::absolute(filePath).string();

			// Categorize symbols and set file path
			for (auto &sym : symbols) {
				sym.filePath = absolutePath;

				if (sym.kind == "function" || sym.kind == "method") {
					result.functions.push_back(std::move(sym));
				} else if (sym.kind == "struct") {
					result.structs.push_back(std::move(sym));
				} else if (sym.kind == "enum") {
					result.enums.push_back(std::move(sym));
				} else if (sym.kind == "interface") {
					result.interfaces.push_back(std::move(sym));
				}
			}

		} catch (const std::exception &e) {
			// Skip files that fail to parse
			// In a real implementation, you might want to log this
			continue;
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
