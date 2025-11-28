#include "document_manager.h"
#include <sstream>
#include <algorithm>

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
