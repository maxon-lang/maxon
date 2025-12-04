#include "linked_editing.h"
#include <regex>

namespace maxon_lsp {

std::optional<LinkedEditingRanges> LinkedEditingProvider::getLinkedEditingRanges(
    const Document& document,
    const Position& position,
    const AnalysisCache* cache
) {
    // First, check if we're on a label
    auto labelInfo = getLabelAtPosition(document, position);
    if (!labelInfo.has_value()) {
        return std::nullopt;
    }

    const std::string& label = labelInfo->first;
    const Range& startRange = labelInfo->second;

    // Find matching labels in the document
    std::vector<Range> ranges = findMatchingLabels(document, label, startRange);

    if (ranges.size() < 2) {
        // Need at least 2 occurrences for linked editing
        return std::nullopt;
    }

    LinkedEditingRanges result;
    result.ranges = std::move(ranges);
    // Word pattern for labels: alphanumeric and underscore
    result.wordPattern = "[a-zA-Z_][a-zA-Z0-9_]*";

    return result;
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
                // Range includes the quotes
                range.start.line = position.line;
                range.start.character = start;
                range.end.line = position.line;
                range.end.character = end + 1;

                return std::make_pair(label, range);
            }
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
            range.start.character = static_cast<int>(pos);
            range.end.line = lineNum;
            range.end.character = static_cast<int>(pos + pattern.size());

            ranges.push_back(range);
            pos += pattern.size();
        }
    }

    return ranges;
}

} // namespace maxon_lsp
