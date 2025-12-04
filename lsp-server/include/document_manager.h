#ifndef DOCUMENT_MANAGER_H
#define DOCUMENT_MANAGER_H

#include "lsp_types.h"
#include <string>
#include <map>
#include <memory>

struct Document {
    std::string uri;
    std::string text;
    int version;

    Document(const std::string& u, const std::string& t, int v)
        : uri(u), text(t), version(v) {}

    std::string getLine(int line) const;
    int getLineCount() const;

    // Get the byte offset for a given line and character position
    size_t getOffset(int line, int character) const;
};

class DocumentManager {
public:
    DocumentManager();

    void openDocument(const std::string& uri, const std::string& text, int version);
    void closeDocument(const std::string& uri);
    void updateDocument(const std::string& uri, const std::string& text, int version);

    // Apply an incremental change to a document
    // Returns true if the change was applied successfully
    bool applyIncrementalChange(const std::string& uri, const lsp::Range& range,
                                const std::string& newText, int version);

    std::shared_ptr<Document> getDocument(const std::string& uri);
    bool hasDocument(const std::string& uri) const;

    // Get all open document URIs
    std::vector<std::string> getAllDocumentUris() const;

private:
    std::map<std::string, std::shared_ptr<Document>> documents;
};

#endif // DOCUMENT_MANAGER_H
