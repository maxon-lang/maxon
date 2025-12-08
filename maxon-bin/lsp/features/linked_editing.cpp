#include "linked_editing.h"
#include "../../lexer/lexer_keyword_matcher.h"
#include <regex>

namespace maxon_lsp {

std::optional<LinkedEditingRanges> LinkedEditingProvider::getLinkedEditingRanges(
    const Document& document,
    const Position& position,
    const AnalysisCache* cache
) {
    // First, check if we're on a quoted label (e.g., 'loop')
    auto labelInfo = getLabelAtPosition(document, position);
    if (labelInfo.has_value()) {
        const std::string& label = labelInfo->first;
        const Range& startRange = labelInfo->second;

        // Find matching labels in the document
        std::vector<Range> ranges = findMatchingLabels(document, label, startRange);

        if (ranges.size() >= 2) {
            LinkedEditingRanges result;
            result.ranges = std::move(ranges);
            result.wordPattern = "[a-zA-Z_][a-zA-Z0-9_]*";
            return result;
        }
    }

    // Check if we're on a block keyword name (function, struct, etc.)
    auto blockInfo = getBlockNameAtPosition(document, position);
    if (blockInfo.has_value()) {
        const std::string& name = blockInfo->first;
        const Range& nameRange = blockInfo->second;

        // Find the corresponding end 'name' label
        std::vector<Range> ranges;
        ranges.push_back(nameRange);

        // Search for end 'name' in the document
        std::string endPattern = "'" + name + "'";
        for (int lineNum = 0; lineNum < static_cast<int>(document.lines.size()); ++lineNum) {
            const std::string& line = document.lines[lineNum];
            size_t pos = line.find(endPattern);
            if (pos != std::string::npos) {
                // Verify this is an end label (line starts with "end" before the pattern)
                size_t endPos = line.find("end");
                if (endPos != std::string::npos && endPos < pos) {
                    Range range;
                    range.start.line = lineNum;
                    range.start.character = static_cast<int>(pos + 1);  // After opening quote
                    range.end.line = lineNum;
                    range.end.character = static_cast<int>(pos + endPattern.size() - 1);  // Before closing quote
                    ranges.push_back(range);
                }
            }
        }

        if (ranges.size() >= 2) {
            LinkedEditingRanges result;
            result.ranges = std::move(ranges);
            result.wordPattern = "[a-zA-Z_][a-zA-Z0-9_]*";
            return result;
        }
    }

    return std::nullopt;
}

std::optional<std::pair<std::string, Range>> LinkedEditingProvider::getLabelAtPosition(
    const Document& document,
    const Position& position
) {
    if (position.line >= static_cast<int>(document.lines.size())) {
        return std::nullopt;
    }

    const std::string& line = document.lines[position.line];
    int col = position.character;

    if (col >= static_cast<int>(line.size())) {
        return std::nullopt;
    }

    // Look for a quoted label pattern: 'labelname'
    // Search backwards to find the opening quote
    int start = col;
    while (start > 0 && line[start] != '\'') {
        start--;
    }

    if (start >= 0 && line[start] == '\'') {
        // Found opening quote, now find closing quote
        int end = col;
        while (end < static_cast<int>(line.size()) && line[end] != '\'') {
            end++;
        }

        if (end < static_cast<int>(line.size()) && line[end] == '\'') {
            // Extract the label (between quotes)
            std::string label = line.substr(start + 1, end - start - 1);

            // Validate it's a proper label (alphanumeric and underscore)
            bool validLabel = !label.empty();
            for (char c : label) {
                if (!std::isalnum(c) && c != '_') {
                    validLabel = false;
                    break;
                }
            }

            if (validLabel) {
                Range range;
                // Range excludes the quotes - only the label text
                range.start.line = position.line;
                range.start.character = start + 1;  // After opening quote
                range.end.line = position.line;
                range.end.character = end;  // Before closing quote

                return std::make_pair(label, range);
            }
        }
    }

    return std::nullopt;
}

std::optional<std::pair<std::string, Range>> LinkedEditingProvider::getBlockNameAtPosition(
    const Document& document,
    const Position& position
) {
    if (position.line >= static_cast<int>(document.lines.size())) {
        return std::nullopt;
    }

    const std::string& line = document.lines[position.line];
    int col = position.character;

    if (col >= static_cast<int>(line.size())) {
        return std::nullopt;
    }

    // Block keywords that have names followed by end 'name'
    static const std::vector<std::string> blockKeywords = KeywordMatcher::getNamedBlockKeywords();

    for (const auto& keyword : blockKeywords) {
        size_t kwPos = line.find(keyword);
        if (kwPos == std::string::npos) continue;

        // Check it's not part of a larger word
        if (kwPos > 0 && std::isalnum(line[kwPos - 1])) continue;

        size_t afterKw = kwPos + keyword.size();
        if (afterKw < line.size() && std::isalnum(line[afterKw])) continue;

        // Find the name after the keyword (skip whitespace)
        size_t nameStart = afterKw;
        while (nameStart < line.size() && std::isspace(line[nameStart])) {
            nameStart++;
        }

        if (nameStart >= line.size()) continue;

        // The name should start with a letter or underscore
        if (!std::isalpha(line[nameStart]) && line[nameStart] != '_') continue;

        // Find the end of the full name (may include InterfaceName.methodName)
        size_t nameEnd = nameStart;
        while (nameEnd < line.size() && (std::isalnum(line[nameEnd]) || line[nameEnd] == '_' || line[nameEnd] == '.')) {
            nameEnd++;
        }

        std::string fullName = line.substr(nameStart, nameEnd - nameStart);

        // Check for interface method pattern: InterfaceName.methodName
        // The end label uses just the method name (after the dot)
        size_t dotPos = fullName.find('.');
        if (dotPos != std::string::npos) {
            // This is an interface method like "Countable.count"
            std::string methodName = fullName.substr(dotPos + 1);
            size_t methodStart = nameStart + dotPos + 1;
            size_t methodEnd = nameEnd;

            // Check if position is within the method name (after the dot)
            if (col >= static_cast<int>(methodStart) && col < static_cast<int>(methodEnd)) {
                Range range;
                range.start.line = position.line;
                range.start.character = static_cast<int>(methodStart);
                range.end.line = position.line;
                range.end.character = static_cast<int>(methodEnd);

                return std::make_pair(methodName, range);
            }
        }

        // Check if position is within the regular name (no dot, or before the dot)
        if (col >= static_cast<int>(nameStart) && col < static_cast<int>(nameEnd)) {
            // For interface methods, use the method name for the end label
            std::string name = (dotPos != std::string::npos) ? fullName.substr(dotPos + 1) : fullName;

            Range range;
            range.start.line = position.line;
            range.start.character = static_cast<int>(nameStart);
            range.end.line = position.line;
            range.end.character = static_cast<int>(nameEnd);

            return std::make_pair(name, range);
        }
    }

    return std::nullopt;
}

std::vector<Range> LinkedEditingProvider::findMatchingLabels(
    const Document& document,
    const std::string& label,
    const Range& startRange
) {
    std::vector<Range> ranges;

    // Search for all occurrences of 'label' in the document
    std::string pattern = "'" + label + "'";

    for (int lineNum = 0; lineNum < static_cast<int>(document.lines.size()); ++lineNum) {
        const std::string& line = document.lines[lineNum];
        size_t pos = 0;

        while ((pos = line.find(pattern, pos)) != std::string::npos) {
            Range range;
            range.start.line = lineNum;
            range.start.character = static_cast<int>(pos + 1);  // After opening quote
            range.end.line = lineNum;
            range.end.character = static_cast<int>(pos + pattern.size() - 1);  // Before closing quote

            ranges.push_back(range);
            pos += pattern.size();
        }
    }

    return ranges;
}

} // namespace maxon_lsp
