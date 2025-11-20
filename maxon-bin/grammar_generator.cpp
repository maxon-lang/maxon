#include "lexer.h"
#include <iostream>
#include <fstream>
#include <string>
#include <vector>
#include <algorithm>

int main(int argc, char* argv[]) {
    if (argc != 2) {
        std::cerr << "Usage: " << argv[0] << " <output_file>" << std::endl;
        return 1;
    }

    std::string outputFile = argv[1];

    // Get keywords by category from the lexer
    auto controlFlowKeywords = Lexer::getKeywordsByCategory(KeywordCategory::ControlFlow);
    auto declarationKeywords = Lexer::getKeywordsByCategory(KeywordCategory::Declaration);
    auto literalKeywords = Lexer::getKeywordsByCategory(KeywordCategory::Literal);
    auto mathIntrinsicKeywords = Lexer::getKeywordsByCategory(KeywordCategory::MathIntrinsic);
    auto typeKeywords = Lexer::getKeywordsByCategory(KeywordCategory::Type);

    // Remove unimplemented keywords
    mathIntrinsicKeywords.erase(
        std::remove_if(mathIntrinsicKeywords.begin(), mathIntrinsicKeywords.end(),
            [](const std::string& kw) {
                return kw == "pow" || kw == "tan" || kw == "log" || kw == "exp";
            }),
        mathIntrinsicKeywords.end());

    // Combine control flow and declaration keywords for block identifiers
    std::vector<std::string> blockKeywords;
    blockKeywords.insert(blockKeywords.end(), controlFlowKeywords.begin(), controlFlowKeywords.end());
    blockKeywords.insert(blockKeywords.end(), declarationKeywords.begin(), declarationKeywords.end());

    // Helper function to create regex pattern from keyword list
    auto makePattern = [](const std::vector<std::string>& keywords) -> std::string {
        if (keywords.empty()) return "";
        std::string pattern = "\\b(";
        for (size_t i = 0; i < keywords.size(); ++i) {
            if (i > 0) pattern += "|";
            pattern += keywords[i];
        }
        pattern += ")\\b";
        return pattern;
    };

    // Helper function to create alternation pattern (without word boundaries)
    auto makeAlternation = [](const std::vector<std::string>& keywords) -> std::string {
        if (keywords.empty()) return "";
        std::string pattern = "(";
        for (size_t i = 0; i < keywords.size(); ++i) {
            if (i > 0) pattern += "|";
            pattern += keywords[i];
        }
        pattern += ")";
        return pattern;
    };

    // Generate the TextMate grammar JSON
    std::string json = "{\n";
    json += "    \"$schema\": \"https://raw.githubusercontent.com/martinring/tmlanguage/master/tmlanguage.json\",\n";
    json += "    \"name\": \"Maxon\",\n";
    json += "    \"patterns\": [\n";
    json += "        { \"include\": \"#comments\" },\n";
    json += "        { \"include\": \"#keywords\" },\n";
    json += "        { \"include\": \"#block-identifiers\" },\n";
    json += "        { \"include\": \"#strings\" },\n";
    json += "        { \"include\": \"#numbers\" },\n";
    json += "        { \"include\": \"#operators\" },\n";
    json += "        { \"include\": \"#functions\" },\n";
    json += "        { \"include\": \"#types\" }\n";
    json += "    ],\n";
    json += "    \"repository\": {\n";
    json += "        \"comments\": {\n";
    json += "            \"patterns\": [\n";
    json += "                { \"name\": \"comment.line.double-slash.maxon\", \"match\": \"//.*$\" },\n";
    json += "                { \"name\": \"comment.block.maxon\", \"begin\": \"/\\\\*\", \"end\": \"\\\\*/\" }\n";
    json += "            ]\n";
    json += "        },\n";
    json += "        \"keywords\": {\n";
    json += "            \"patterns\": [\n";
    json += "                { \"name\": \"keyword.control.maxon\", \"match\": \"" + makePattern(controlFlowKeywords) + "\" },\n";
    json += "                { \"name\": \"keyword.other.maxon\", \"match\": \"" + makePattern(declarationKeywords) + "\" },\n";
    json += "                { \"name\": \"constant.language.boolean.maxon\", \"match\": \"" + makePattern(literalKeywords) + "\" },\n";
    json += "                { \"name\": \"support.function.math.maxon\", \"match\": \"" + makePattern(mathIntrinsicKeywords) + "\" }\n";
    json += "            ]\n";
    json += "        },\n";
    json += "        \"block-identifiers\": {\n";
    json += "            \"patterns\": [\n";
    json += "                { \"name\": \"entity.name.label.maxon\", \"match\": \"" + std::string("(?<=\\\\b") + makeAlternation(blockKeywords) + "\\\\s+[^'\\\"]*?)('[^']*'|\\\"[^\\\"]*\\\")" + "\" }\n";
    json += "            ]\n";
    json += "        },\n";
    json += "        \"strings\": {\n";
    json += "            \"patterns\": [\n";
    json += "                { \"name\": \"string.quoted.double.maxon\", \"begin\": \"\\\"\", \"end\": \"\\\"\", \"patterns\": [{ \"name\": \"constant.character.escape.maxon\", \"match\": \"\\\\\\\\.\" }] },\n";
    json += "                { \"name\": \"string.quoted.single.maxon\", \"begin\": \"'\", \"end\": \"'\", \"patterns\": [{ \"name\": \"constant.character.escape.maxon\", \"match\": \"\\\\\\\\.\" }] }\n";
    json += "            ]\n";
    json += "        },\n";
    json += "        \"numbers\": {\n";
    json += "            \"patterns\": [\n";
    json += "                { \"name\": \"constant.numeric.float.maxon\", \"match\": \"\\\\b[0-9]+\\\\.[0-9]+([eE][+-]?[0-9]+)?\\\\b\" },\n";
    json += "                { \"name\": \"constant.numeric.integer.maxon\", \"match\": \"\\\\b[0-9]+\\\\b\" }\n";
    json += "            ]\n";
    json += "        },\n";
    json += "        \"operators\": {\n";
    json += "            \"patterns\": [\n";
    json += "                { \"name\": \"keyword.operator.arithmetic.maxon\", \"match\": \"[+\\\\-*/]\" },\n";
    json += "                { \"name\": \"keyword.operator.comparison.maxon\", \"match\": \"(>=?|<=?|=)\" },\n";
    json += "                { \"name\": \"keyword.operator.assignment.maxon\", \"match\": \"=\" }\n";
    json += "            ]\n";
    json += "        },\n";
    json += "        \"functions\": {\n";
    json += "            \"patterns\": [\n";
    json += "                { \"name\": \"entity.name.function.maxon\", \"match\": \"\\\\b([a-zA-Z_][a-zA-Z0-9_]*)\\\\s*(?=\\\\()\" }\n";
    json += "            ]\n";
    json += "        },\n";
    json += "        \"types\": {\n";
    json += "            \"patterns\": [\n";
    json += "                { \"name\": \"storage.type.maxon\", \"match\": \"" + makePattern(typeKeywords) + "\" }\n";
    json += "            ]\n";
    json += "        }\n";
    json += "    },\n";
    json += "    \"scopeName\": \"source.maxon\"\n";
    json += "}\n";

    // Write to output file
    std::ofstream outFile(outputFile);
    if (!outFile) {
        std::cerr << "Error: Cannot open output file " << outputFile << std::endl;
        return 1;
    }

    outFile << json;
    outFile.close();

    std::cout << "Generated TextMate grammar in " << outputFile << std::endl;
    return 0;
}
