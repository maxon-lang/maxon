#include "document_manager.h"
#include <sstream>
#include <algorithm>

DocumentManager::DocumentManager() {}

void DocumentManager::openDocument(const std::string& uri, const std::string& text, int version) {
    documents[uri] = std::make_shared<Document>(uri, text, version);
}

void DocumentManager::closeDocument(const std::string& uri) {
    documents.erase(uri);
}

void DocumentManager::updateDocument(const std::string& uri, const std::string& text, int version) {
    auto it = documents.find(uri);
    if (it != documents.end()) {
        it->second->text = text;
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
