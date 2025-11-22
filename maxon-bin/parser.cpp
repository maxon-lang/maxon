#include "parser.h"
#include <stdexcept>

Parser::Parser(const std::vector<Token>& toks)
    : tokens(toks), position(0), defaultNamespace("") {}

void Parser::setDefaultNamespace(const std::string& ns) {
    defaultNamespace = ns;
}

std::unique_ptr<ProgramAST> Parser::parse() {
    std::vector<std::unique_ptr<FunctionAST>> functions;
    std::vector<std::unique_ptr<StructDefAST>> structs;
    
    while (!check(TokenType::END_OF_FILE)) {
        if (check(TokenType::END_OF_FILE)) {
            break;
        }
        
        // Check for struct, export, extern, or function keyword
        if (check(TokenType::KEYWORD) && currentToken().value == "struct") {
            structs.push_back(parseStruct());
        } else if (check(TokenType::KEYWORD) && currentToken().value == "export") {
            // Check what comes after export
            Token exportToken = currentToken();
            advance(); // consume 'export'
            if (check(TokenType::KEYWORD) && currentToken().value == "struct") {
                structs.push_back(parseStruct());
            } else if ((check(TokenType::KEYWORD) && currentToken().value == "extern") ||
                       (check(TokenType::KEYWORD) && currentToken().value == "function")) {
                functions.push_back(parseFunction());
            } else {
                throw std::runtime_error("Expected 'struct', 'extern function', or 'function' after 'export'\n  Location: line " + 
                                       std::to_string(currentToken().line) + ", column " + 
                                       std::to_string(currentToken().column));
            }
        } else if ((check(TokenType::KEYWORD) && currentToken().value == "extern") || 
                   (check(TokenType::KEYWORD) && currentToken().value == "function")) {
            functions.push_back(parseFunction());
        } else {
            throw std::runtime_error("Expected 'struct', 'export', 'function', or 'extern function' at top level\n  Location: line " + 
                                   std::to_string(currentToken().line) + ", column " + 
                                   std::to_string(currentToken().column));
        }
    }
    
    return std::make_unique<ProgramAST>(std::move(functions), std::move(structs));
}
