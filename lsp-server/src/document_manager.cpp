#include "document_manager.h"
#include <sstream>
#include <algorithm>
#include <vector>

DocumentManager::DocumentManager() {}

void DocumentManager::openDocument(const std::string& uri, const std::string& text, int version) {
    std::string processedText = text;

    // For .test files in language-tests/fragments/, extract only the Maxon code before the --- separator
    // This handles test fragment files while leaving other .test files intact
    // Check for both forward slashes (URI) and backslashes (Windows paths)
    bool isTestFile = uri.find(".test") != std::string::npos;
    bool isFragmentPath = uri.find("language-tests/fragments/") != std::string::npos ||
                          uri.find("language-tests\\fragments\\") != std::string::npos;

    if (isTestFile && isFragmentPath) {
        size_t separatorPos = text.find("\n---");
        if (separatorPos != std::string::npos) {
            processedText = text.substr(0, separatorPos);
        }
    }

    documents[uri] = std::make_shared<Document>(uri, processedText, version);
}

void DocumentManager::closeDocument(const std::string& uri) {
    documents.erase(uri);
}

void DocumentManager::updateDocument(const std::string& uri, const std::string& text, int version) {
    auto it = documents.find(uri);
    if (it != documents.end()) {
        std::string processedText = text;

        // For .test files in language-tests/fragments/, extract only the Maxon code before the --- separator
        // This handles test fragment files while leaving other .test files intact
        // Check for both forward slashes (URI) and backslashes (Windows paths)
        bool isTestFile = uri.find(".test") != std::string::npos;
        bool isFragmentPath = uri.find("language-tests/fragments/") != std::string::npos ||
                              uri.find("language-tests\\fragments\\") != std::string::npos;

        if (isTestFile && isFragmentPath) {
            size_t separatorPos = text.find("\n---");
            if (separatorPos != std::string::npos) {
                processedText = text.substr(0, separatorPos);
            }
        }

        it->second->text = processedText;
        it->second->version = version;
    }
}

std::shared_ptr<Document> DocumentManager::getDocument(const std::string& uri) {
    auto it = documents.find(uri);
    if (it != documents.end()) {
        return it->second;
    }
    return nullptr;
}

bool DocumentManager::hasDocument(const std::string& uri) const {
    return documents.find(uri) != documents.end();
}

std::string Document::getLine(int line) const {
    std::istringstream stream(text);
    std::string currentLine;
    int currentLineNum = 0;
    
    while (std::getline(stream, currentLine)) {
        if (currentLineNum == line) {
            return currentLine;
        }
        currentLineNum++;
    }
    
    return "";
}

int Document::getLineCount() const {
    return std::count(text.begin(), text.end(), '\n') + 1;
}

size_t Document::getOffset(int line, int character) const {
    size_t offset = 0;
    int currentLine = 0;

    // Navigate to the correct line
    while (currentLine < line && offset < text.size()) {
        if (text[offset] == '\n') {
            currentLine++;
        }
        offset++;
    }

    // Add the character offset within the line
    // Be careful not to go past line boundaries or text end
    int charCount = 0;
    while (charCount < character && offset < text.size() && text[offset] != '\n') {
        offset++;
        charCount++;
    }

    return offset;
}

bool DocumentManager::applyIncrementalChange(const std::string& uri, const lsp::Range& range,
                                              const std::string& newText, int version) {
    auto it = documents.find(uri);
    if (it == documents.end()) {
        return false;
    }

    auto& doc = it->second;

    // Calculate start and end offsets
    size_t startOffset = doc->getOffset(range.start.line, range.start.character);
    size_t endOffset = doc->getOffset(range.end.line, range.end.character);

    // Validate offsets
    if (startOffset > doc->text.size()) {
        startOffset = doc->text.size();
    }
    if (endOffset > doc->text.size()) {
        endOffset = doc->text.size();
    }
    if (startOffset > endOffset) {
        // Invalid range, fall back to full replacement by doing nothing
        return false;
    }

    // Apply the change: replace text from startOffset to endOffset with newText
    std::string updatedText = doc->text.substr(0, startOffset) +
                              newText +
                              doc->text.substr(endOffset);

    // Handle .test files in language-tests/fragments/ specially
    bool isTestFile = uri.find(".test") != std::string::npos;
    bool isFragmentPath = uri.find("language-tests/fragments/") != std::string::npos ||
                          uri.find("language-tests\\fragments\\") != std::string::npos;

    if (isTestFile && isFragmentPath) {
        size_t separatorPos = updatedText.find("\n---");
        if (separatorPos != std::string::npos) {
            updatedText = updatedText.substr(0, separatorPos);
        }
    }

    doc->text = updatedText;
    doc->version = version;

    return true;
}

std::vector<std::string> DocumentManager::getAllDocumentUris() const {
    std::vector<std::string> uris;
    uris.reserve(documents.size());
    for (const auto& pair : documents) {
        uris.push_back(pair.first);
    }
    return uris;
}
