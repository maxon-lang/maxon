#ifndef MAXON_LSP_LINKED_EDITING_H
#define MAXON_LSP_LINKED_EDITING_H

#include "../lsp_types.h"
#include "../document_manager.h"
#include "../../compiler_api.h"
#include <optional>

namespace maxon_lsp {

// Namespace alias for easier access to LSP types
using LinkedEditingRanges = maxon::lsp::LinkedEditingRanges;
using Position = maxon::lsp::Position;
using Range = maxon::lsp::Range;

/**
 * Provides linked editing ranges for block labels in Maxon source code.
 *
 * When the cursor is on a block label (e.g., 'loop' in for/while/if statements),
 * returns the ranges of both the start label and the end label so they can be
 * edited together.
 */
class LinkedEditingProvider {
public:
    /**
     * Get linked editing ranges at a position in a document.
     *
     * @param document The document to search in
     * @param position The cursor position
     * @param cache Analysis cache containing AST
     * @return Linked editing ranges if on a block label, nullopt otherwise
     */
    std::optional<LinkedEditingRanges> getLinkedEditingRanges(
        const Document& document,
        const Position& position,
        const AnalysisCache* cache
    );

private:
    /**
     * Check if position is within a quoted label (e.g., 'label')
     * and return the label content and its range.
     *
     * @param document The document
     * @param position The cursor position
     * @return Pair of label string and range, or nullopt if not on a label
     */
    std::optional<std::pair<std::string, Range>> getLabelAtPosition(
        const Document& document,
        const Position& position
    );

    /**
     * Find matching block label pairs in the document.
     * Returns ranges for both occurrences of a label if found.
     *
     * @param document The document
     * @param label The label to find matches for
     * @param startRange The range of the label we started from
     * @return Vector of ranges for matching labels
     */
    std::vector<Range> findMatchingLabels(
        const Document& document,
        const std::string& label,
        const Range& startRange
    );
};

} // namespace maxon_lsp

#endif // MAXON_LSP_LINKED_EDITING_H
